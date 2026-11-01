/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed class PluginContentTranslationService
{
    public const string SettingKey = "EnablePluginContentTranslation";

    private const int MaxSingleRequestLength = 480;
    private const int MaxCacheEntries = 5000;
    private const string CacheKeyVersion = "v1";
    private const string CacheFileName = "PluginContentTranslations.json";

    private static readonly Lazy<PluginContentTranslationService> _instance =
        new(() => new PluginContentTranslationService());

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan MinRequestInterval = TimeSpan.FromMilliseconds(120);

    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly object _cacheLock = new();

    private bool _enabled = true;
    private bool _initialized;
    private bool _cacheLoaded;
    private long _lastRequestTimestamp;

    private static string CacheFilePath => Path.Combine(AppPaths.CacheDir, CacheFileName);

    private PluginContentTranslationService()
    {
    }

    public static PluginContentTranslationService Instance => _instance.Value;

    public event EventHandler? EnabledChanged;

    public bool IsEnabled
    {
        get
        {
            lock (_stateLock)
            {
                return _enabled;
            }
        }
    }

    public bool ShouldTranslate =>
        IsEnabled && TranslationService.IsSupportedTargetCulture(ResourceExtensions.CurrentCulture);

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        var enabled = true;
        try
        {
            var stored = await App.GetService<ILocalSettingsService>().ReadSettingAsync(SettingKey);
            enabled = stored == null || Convert.ToBoolean(stored);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginContentTranslation",
                $"[PluginTranslation] Failed to read the translation setting: {ex.Message}");
        }

        lock (_stateLock)
        {
            _enabled = enabled;
            _initialized = true;
        }

        EnsureCacheLoaded();
    }

    public async Task SetEnabledAsync(bool enabled)
    {
        bool changed;
        lock (_stateLock)
        {
            changed = !_initialized || _enabled != enabled;
            _enabled = enabled;
            _initialized = true;
        }

        try
        {
            await App.GetService<ILocalSettingsService>().SaveSettingAsync(SettingKey, enabled);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginContentTranslation",
                $"[PluginTranslation] Failed to save the translation setting: {ex.Message}");
        }

        if (changed)
        {
            EnabledChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool TryGetCachedTranslation(string? text, out string translated)
    {
        translated = text?.Trim() ?? string.Empty;
        if (translated.Length == 0 || !ShouldTranslate || !ContainsChinese(translated))
            return false;

        EnsureCacheLoaded();

        var culture = ResourceExtensions.CurrentCulture ?? string.Empty;
        if (!_cache.TryGetValue(BuildCacheKey(culture, translated), out var cached))
            return false;

        translated = cached;
        return true;
    }

    public async Task<string> TranslateAsync(string? text, string? fallback = null, CancellationToken ct = default)
    {
        var source = text?.Trim() ?? string.Empty;
        var resolvedFallback = string.IsNullOrWhiteSpace(fallback) ? source : fallback.Trim();

        if (source.Length == 0)
            return resolvedFallback;

        if (!ShouldTranslate || !ContainsChinese(source))
            return resolvedFallback;

        EnsureCacheLoaded();

        var culture = ResourceExtensions.CurrentCulture ?? string.Empty;
        var cacheKey = BuildCacheKey(culture, source);
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        var translated = await TranslateCoreAsync(source, culture, ct);
        if (translated == null)
            return resolvedFallback;

        if (_cache.Count < MaxCacheEntries && _cache.TryAdd(cacheKey, translated))
        {
            PersistCache();
        }

        return translated;
    }

    private static string BuildCacheKey(string culture, string source) =>
        $"{CacheKeyVersion}|{culture}|{source}";

    public static void RunOnUiThread(Action action)
    {
        var dispatcher = App.MainWindow?.DispatcherQueue;
        if (dispatcher == null || dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        dispatcher.TryEnqueue(() => action());
    }

    private void LoadPersistedCacheLocked()
    {
        try
        {
            var path = CacheFilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            var stored = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (stored == null)
            {
                return;
            }

            foreach (var entry in stored)
            {
                if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                {
                    _cache.TryAdd(entry.Key, entry.Value);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginContentTranslation",
                $"[PluginTranslation] Failed to read the translation cache: {ex.Message}");
        }
    }

    private void EnsureCacheLoaded()
    {
        lock (_cacheLock)
        {
            if (_cacheLoaded)
            {
                return;
            }

            _cacheLoaded = true;
            LoadPersistedCacheLocked();
        }
    }

    private void PersistCache()
    {
        var path = CacheFilePath;
        if (string.IsNullOrEmpty(path) || _cache.IsEmpty)
        {
            return;
        }

        try
        {
            lock (_cacheLock)
            {
                var snapshot = new Dictionary<string, string>(_cache, StringComparer.Ordinal);
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
                try
                {
                    File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot));
                    File.Move(tempPath, path, true);
                }
                finally
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginContentTranslation",
                $"[PluginTranslation] Failed to save the translation cache: {ex.Message}");
        }
    }

    private async Task<string?> TranslateCoreAsync(string text, string culture, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            await _requestGate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }

        try
        {
            await WaitForRequestSlotAsync(timeout.Token);

            var langPair = TranslationService.GetLangPair(culture);
            var translated = text.Length > MaxSingleRequestLength
                ? await TranslationService.Instance.TranslateParagraphAsync(text, langPair, timeout.Token)
                : await TranslationService.Instance.TranslateTextAsync(text, langPair, timeout.Token);

            translated = WebUtility.HtmlDecode(translated ?? string.Empty).Trim();

            if (translated.Length == 0 || string.Equals(translated, text, StringComparison.Ordinal))
                return null;

            return translated;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginContentTranslation", $"[PluginTranslation] Request failed: {ex.Message}");
            return null;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task WaitForRequestSlotAsync(CancellationToken ct)
    {
        var last = Interlocked.Read(ref _lastRequestTimestamp);
        if (last != 0)
        {
            var elapsed = Stopwatch.GetElapsedTime(last);
            if (elapsed < MinRequestInterval)
            {
                await Task.Delay(MinRequestInterval - elapsed, ct);
            }
        }

        Interlocked.Exchange(ref _lastRequestTimestamp, Stopwatch.GetTimestamp());
    }

    private static bool ContainsChinese(string text)
    {
        foreach (var c in text.AsSpan())
        {
            if (c is >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF' or >= '\uF900' and <= '\uFAFF')
            {
                return true;
            }
        }

        return false;
    }
}
