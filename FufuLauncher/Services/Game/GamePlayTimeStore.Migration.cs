/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed partial class GamePlayTimeStore
{
    private async Task MigrateLegacyCoreAsync(CancellationToken cancellationToken)
    {
        var legacyPath = AppPaths.LegacyFufuConfigFile;
        if (!File.Exists(legacyPath)) return;

        LegacyGamePlayTimeConfig? legacy;
        try
        {
            var json = await File.ReadAllTextAsync(legacyPath, cancellationToken);
            legacy = JsonSerializer.Deserialize<LegacyGamePlayTimeConfig>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to read legacy {Path.GetFileName(legacyPath)}: {ex.Message}");
            return;
        }

        if (legacy == null) return;

        lock (_sync)
        {
            if (legacy.GamePlayTimeData != null)
            {
                foreach (var entry in legacy.GamePlayTimeData)
                {
                    if (!IsValidDateKey(entry.Key) || entry.Value < 0) continue;
                    _legacyDailySeconds[entry.Key] = entry.Value;
                }
            }

            _lastPlayDate = string.IsNullOrWhiteSpace(legacy.LastPlayDate) ? null : legacy.LastPlayDate;
            RecalculateDailyTotalsLocked(beforeDailyFloor: null);
        }

        try
        {
            var snapshot = BuildDocumentSnapshot();
            await SaveDocumentCoreAsync(snapshot, cancellationToken);
        }
        catch (Exception ex)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to write migrated game time data; legacy config retained: {ex.Message}");
            return;
        }

        if (!await VerifyMigratedStorageAsync(cancellationToken))
        {
            DebugLog.Info("GamePlayTimeStore",
                "Migrated game time storage verification failed; legacy config retained");
            return;
        }

        TryDeleteLegacyConfig(legacyPath);
        DebugLog.Info("GamePlayTimeStore",
            $"Migrated legacy game time data to {Path.GetFileName(AppPaths.GamePlayTimeFile)}");
    }

    private async Task RetireMigratedLegacyAsync(CancellationToken cancellationToken)
    {
        var legacyPath = AppPaths.LegacyFufuConfigFile;
        if (!File.Exists(legacyPath)) return;

        LegacyGamePlayTimeConfig? legacy;
        try
        {
            var json = await File.ReadAllTextAsync(legacyPath, cancellationToken);
            legacy = JsonSerializer.Deserialize<LegacyGamePlayTimeConfig>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to read legacy {Path.GetFileName(legacyPath)}; legacy config retained: {ex.Message}");
            return;
        }

        if (legacy == null) return;

        lock (_sync)
        {
            if (legacy.GamePlayTimeData != null)
            {
                foreach (var entry in legacy.GamePlayTimeData)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key) || entry.Value <= 0) continue;
                    if (!_dailyPlayTimeSeconds.TryGetValue(entry.Key, out var migratedSeconds) ||
                        migratedSeconds < entry.Value)
                    {
                        DebugLog.Info("GamePlayTimeStore",
                            "Legacy game time config retained because the new storage does not contain all legacy data");
                        return;
                    }
                }
            }
        }

        TryDeleteLegacyConfig(legacyPath);
        DebugLog.Info("GamePlayTimeStore",
            "Retired legacy game time config after confirming the new storage contains its data");
    }

    private async Task<bool> VerifyMigratedStorageAsync(CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(AppPaths.GamePlayTimeFile, cancellationToken);
            var document = JsonSerializer.Deserialize<GamePlayTimeDocument>(json, JsonOptions);
            if (document == null) return false;
            if (!string.Equals(document.StorageKey, StorageIdentifier, StringComparison.Ordinal)) return false;
            if (document.SchemaVersion != CurrentSchemaVersion) return false;

            var persistedDailySeconds = document.DailyPlayTimeSeconds;
            if (persistedDailySeconds == null) return false;

            lock (_sync)
            {
                if (!string.Equals(document.LastPlayDate, _lastPlayDate, StringComparison.Ordinal)) return false;
                if (persistedDailySeconds.Count != _dailyPlayTimeSeconds.Count) return false;

                foreach (var entry in _dailyPlayTimeSeconds)
                {
                    if (!persistedDailySeconds.TryGetValue(entry.Key, out var persistedSeconds) ||
                        persistedSeconds != entry.Value)
                    {
                        return false;
                    }
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to verify migrated game time storage: {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteLegacyConfig(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Delete(path);
            DebugLog.Info("GamePlayTimeStore",
                $"Deleted legacy game time config after successful migration: {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("GamePlayTimeStore",
                $"Failed to delete legacy game time config; file retained: {ex.Message}");
        }
    }
}
