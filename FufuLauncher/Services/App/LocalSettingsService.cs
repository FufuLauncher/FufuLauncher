/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using Sentry;

namespace FufuLauncher.Services
{
    public class LocalSettingsService : ILocalSettingsService
    {
        private const string _defaultApplicationDataFolder = "FufuLauncher/ApplicationData";
        private const string _defaultLocalSettingsDb = "LocalSettings.db";

        private static readonly TimeSpan LoadRetryBackoff = TimeSpan.FromSeconds(30);

        private readonly LocalSettingsRepository _repository;

        private readonly ConcurrentDictionary<string, string> _settings = new();


        private readonly ConcurrentDictionary<string, byte> _missingDuringLoad = new();


        private readonly ConcurrentDictionary<string, byte> _removedDuringLoad = new();


        private readonly object _stateLock = new();


        private readonly SemaphoreSlim _writeGate = new(1, 1);


        private readonly SemaphoreSlim _fullLoadGate = new(1, 1);


        private bool _fullLoadCompleted;


        private Task? _backgroundLoadTask;

        private DateTime? _lastLoadFailureUtc;

        private bool _dirErrorNotified;

        public const string BackgroundServerKey = "BackgroundServer";
        public const string IsBackgroundEnabledKey = "IsBackgroundEnabled";
        public const string IsStartupEnabledKey = "IsStartupEnabled";
        public const string LastAnnouncedVersionKey = "LastAnnouncedVersion";

        public const string LastAnnouncedPreviewVersionKey = "LastAnnouncedPreviewVersion";

        public const string LastAnnouncementUrlKey = "LastAnnouncementUrl";

        public const string SuppressAnnouncementInGameKey = "IsSuppressAnnouncementInGameEnabled";

        public const string HasShownSecurityWarningKey = "HasShownSecurityWarning";

        public const string IsTelemetryEnabledKey = "IsTelemetryEnabled";

        public const string HasDismissedFpsWarningKey = "HasDismissedFpsWarning";

        public const string AnnouncementViewModeKey = "AnnouncementViewMode";

        public const string AnnouncementRegionKey = "AnnouncementRegion";

        private readonly JsonSerializerOptions _jsonOptions;

        public LocalSettingsService(LocalSettingsRepository repository)
        {
            _repository = repository;

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }


        public void StartBackgroundLoad()
        {
            lock (_stateLock)
            {
                if (_fullLoadCompleted)
                    return;


                if (_backgroundLoadTask is { IsCompleted: false })
                    return;


                if (_lastLoadFailureUtc is { } failedAt &&
                    DateTime.UtcNow - failedAt < LoadRetryBackoff)
                    return;

                _backgroundLoadTask = Task.Run(RunBackgroundLoadAsync);
            }
        }

        private async Task RunBackgroundLoadAsync()
        {
            try
            {
                await _fullLoadGate.WaitAsync();
                try
                {
                    lock (_stateLock)
                    {
                        if (_fullLoadCompleted)
                            return;
                    }

                    await LoadAllCoreAsync();
                }
                finally
                {
                    _fullLoadGate.Release();
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("LocalSettingsService",
                    $"LocalSettingsService: background full load error - {ex.Message}");
            }
            finally
            {
                lock (_stateLock)
                {
                    if (!_fullLoadCompleted)
                    {
                        _backgroundLoadTask = null;
                        _lastLoadFailureUtc = DateTime.UtcNow;
                    }
                }
            }
        }


        private async Task LoadAllCoreAsync()
        {
            DebugLog.Info("LocalSettingsService", "LocalSettingsService: starting full settings load");

            try
            {
                var dir = Path.GetDirectoryName(AppPaths.LocalSettingsDb);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                DebugLog.Info("LocalSettingsService",
                    $"LocalSettingsService: failed to create config directory - {ex.Message}");


                if (!_dirErrorNotified)
                {
                    _dirErrorNotified = true;
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "Settings_DirCreateFailed".GetLocalized(),
                        string.Format("Settings_DirCreateFailedMsg".GetLocalized(), ex.Message),
                        NotificationType.Error,
                        4000
                    ));
                }

                return;
            }

            var (success, all) = await _repository.TryGetAllSettingsAsync();
            if (!success)
            {
                DebugLog.Info("LocalSettingsService",
                    "LocalSettingsService: full load failed, continuing with lazy load");
                return;
            }

            int added = 0, kept = 0, skippedRemoved = 0;
            lock (_stateLock)
            {
                foreach (var (key, value) in all)
                {
                    if (_removedDuringLoad.ContainsKey(key))
                    {
                        skippedRemoved++;
                        continue;
                    }

                    if (_settings.TryGetValue(key, out var existing))
                    {
                        if (!string.Equals(existing, value, StringComparison.Ordinal))
                        {
                            kept++;
                            SettingsLog.Write(
                                $"LocalSettingsService: in-memory value of '{key}' differs from snapshot, keeping in-memory value");
                        }

                        continue;
                    }

                    if (_settings.TryAdd(key, value))
                        added++;
                }

                _missingDuringLoad.Clear();
                _removedDuringLoad.Clear();
                _fullLoadCompleted = true;
                _lastLoadFailureUtc = null;
            }

            DebugLog.Info("LocalSettingsService",
                $"LocalSettingsService: full load complete, {all.Count} entries (added {added}, kept in-memory {kept}, skipped removed {skippedRemoved})");
        }


        public async Task<bool> InvalidateAndReloadAsync()
        {
            await _fullLoadGate.WaitAsync();
            try
            {
                await _writeGate.WaitAsync();
                try
                {
                    var (success, all) = await _repository.TryGetAllSettingsAsync();
                    if (!success)
                    {
                        DebugLog.Info("LocalSettingsService",
                            "LocalSettingsService: reload after full table replace failed, cache left unchanged");
                        return false;
                    }

                    lock (_stateLock)
                    {
                        _settings.Clear();
                        foreach (var (key, value) in all)
                            _settings[key] = value;

                        _missingDuringLoad.Clear();
                        _removedDuringLoad.Clear();
                        _fullLoadCompleted = true;
                        _lastLoadFailureUtc = null;
                    }

                    DebugLog.Info("LocalSettingsService",
                        $"LocalSettingsService: cache rebuilt from database, {all.Count} entries");
                    return true;
                }
                finally
                {
                    _writeGate.Release();
                }
            }
            finally
            {
                _fullLoadGate.Release();
            }
        }

        public async Task<object?> ReadSettingAsync(string key)
        {
            StartBackgroundLoad();

            string? fromCache;
            bool foundInCache;
            bool knownAbsent;

            lock (_stateLock)
            {
                foundInCache = _settings.TryGetValue(key, out fromCache);
                knownAbsent = !foundInCache && IsKnownAbsentLocked(key);
            }

            if (foundInCache)
            {
                SettingsLog.Write($"LocalSettingsService: read {key}");
                return Deserialize(fromCache!);
            }

            if (knownAbsent)
            {
                SettingsLog.Write($"LocalSettingsService: '{key}' not found");
                return null;
            }


            var readStarted = Environment.TickCount64;
            var (success, found, lazyValue) = await _repository.TryGetSettingAsync(key);
            var readMs = Environment.TickCount64 - readStarted;
            if (readMs >= 150)
                DebugLog.Info("LocalSettingsService", "Settings.ReadSlow", $"{key} {readMs}ms success={success}");


            lock (_stateLock)
            {
                if (_settings.TryGetValue(key, out var current))
                {
                    SettingsLog.Write($"LocalSettingsService: value updated during lazy load of {key}, using cache");
                    return Deserialize(current);
                }

                if (_removedDuringLoad.ContainsKey(key) || _fullLoadCompleted)
                {
                    SettingsLog.Write($"LocalSettingsService: '{key}' not found");
                    return null;
                }

                if (success && found)
                {
                    _settings[key] = lazyValue;
                    SettingsLog.Write($"LocalSettingsService: lazy loading {key}");
                    return Deserialize(lazyValue);
                }

                if (success)
                    _missingDuringLoad.TryAdd(key, 0);
                else
                    DebugLog.Info("LocalSettingsService", $"LocalSettingsService: lazy load query for '{key}' failed");

                SettingsLog.Write($"LocalSettingsService: '{key}' not found");
                return null;
            }
        }


        private bool IsKnownAbsentLocked(string key) =>
            _fullLoadCompleted || _removedDuringLoad.ContainsKey(key) || _missingDuringLoad.ContainsKey(key);

        private object? Deserialize(string storedValue)
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<object>(storedValue, _jsonOptions);

                if (deserialized is JsonElement jsonElement)
                {
                    return jsonElement.ValueKind switch
                    {
                        JsonValueKind.String => jsonElement.GetString(),
                        JsonValueKind.Number => jsonElement.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Array => jsonElement.EnumerateArray().ToArray(),
                        JsonValueKind.Object => jsonElement,
                        _ => storedValue
                    };
                }

                return deserialized;
            }
            catch (JsonException)
            {
                return storedValue;
            }
        }

        public async Task SaveSettingAsync<T>(string key, T value)
        {
            await TrySaveSettingAsync(key, value);
        }


        public async Task<bool> TrySaveSettingAsync<T>(string key, T value)
        {
            StartBackgroundLoad();

            var json = JsonSerializer.Serialize(value, _jsonOptions);

            SettingsLog.Write($"LocalSettingsService: saving {key}");

            await _writeGate.WaitAsync();
            try
            {
                var writeStarted = Environment.TickCount64;
                await _repository.UpsertSettingAsync(key, json);
                var writeMs = Environment.TickCount64 - writeStarted;
                if (writeMs >= 150)
                    DebugLog.Info("LocalSettingsService", "Settings.WriteSlow", $"{key} {writeMs}ms");

                lock (_stateLock)
                {
                    _settings[key] = json;
                    _missingDuringLoad.TryRemove(key, out _);
                    _removedDuringLoad.TryRemove(key, out _);
                }

                return true;
            }
            catch (Exception ex)
            {
                DebugLog.Info("LocalSettingsService", $"LocalSettingsService: failed to save settings - {ex.Message}");
                SentrySdk.CaptureException(ex, scope =>
                {
                    scope.SetTag("source", "LocalSettingsService");
                    scope.SetTag("settingKey", key);
                });
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "Settings_ConfigSaveFailed".GetLocalized(),
                    string.Format("Settings_ConfigSaveFailedMsg".GetLocalized(), ex.Message)
                    + Environment.NewLine
                    + string.Format("Settings_ConfigSaveFailedPathHint".GetLocalized(), AppPaths.LocalSettingsDb),
                    NotificationType.Error,
                    5000));
                return false;
            }
            finally
            {
                _writeGate.Release();
            }
        }

        public async Task RemoveSettingAsync(string key)
        {
            StartBackgroundLoad();

            await _writeGate.WaitAsync();
            try
            {
                bool deleted = await _repository.DeleteSettingAsync(key);
                if (!deleted)
                {
                    DebugLog.Info("LocalSettingsService",
                        $"LocalSettingsService: delete of '{key}' had no effect, cache unchanged");
                    return;
                }

                lock (_stateLock)
                {
                    _settings.TryRemove(key, out _);

                    if (!_fullLoadCompleted)
                    {
                        _removedDuringLoad.TryAdd(key, 0);
                        _missingDuringLoad.TryAdd(key, 0);
                    }
                }
            }
            finally
            {
                _writeGate.Release();
            }
        }
    }
}