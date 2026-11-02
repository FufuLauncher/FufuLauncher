/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed record PluginProvenanceRecord(
    string Source,
    string Sha256,
    string Version,
    long RecordedAt);

public sealed class PluginProvenanceStore
{
    public const string SettingKey = "PluginProvenanceRecords";

    public const string SourcePluginStore = "plugin_store";

    public const string SourceLauncherImport = "launcher_import";

    public const string SourceExternalCopy = "external_copy";

    private readonly ILocalSettingsService _settings;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private volatile Dictionary<string, PluginProvenanceRecord> _records =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _initialized;

    public PluginProvenanceStore(ILocalSettingsService settings)
    {
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            _records = await LoadAsync().ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool TryGet(string? folderName, out PluginProvenanceRecord entry)
    {
        entry = null!;
        if (string.IsNullOrWhiteSpace(folderName)) return false;

        if (_records.TryGetValue(folderName, out var stored) && stored != null)
        {
            entry = stored;
            return true;
        }

        return false;
    }

    public async Task RecordAsync(string? folderName, string source, string? dllPath, string? version)
    {
        if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(source)) return;

        await InitializeAsync().ConfigureAwait(false);

        var sha256 = string.Empty;
        if (!string.IsNullOrWhiteSpace(dllPath))
        {
            var target = dllPath;
            sha256 = await Task.Run(() => ComputeSha256(target)).ConfigureAwait(false);
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var updated = new Dictionary<string, PluginProvenanceRecord>(_records,
                StringComparer.OrdinalIgnoreCase)
            {
                [folderName] = new PluginProvenanceRecord(source, sha256, version ?? string.Empty,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
            };

            await SaveAsync(updated).ConfigureAwait(false);
            _records = updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string ComputeSha256(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return PluginVerifier.ComputeSha256(stream);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginProvenanceStore", $"[PluginProvenance] Failed to hash {filePath}: {ex.Message}");
            return string.Empty;
        }
    }

    private async Task<Dictionary<string, PluginProvenanceRecord>> LoadAsync()
    {
        var records = new Dictionary<string, PluginProvenanceRecord>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var raw = await _settings.ReadSettingAsync(SettingKey).ConfigureAwait(false);
            if (raw is not string text || string.IsNullOrWhiteSpace(text)) return records;

            var parsed = JsonSerializer.Deserialize<Dictionary<string, PluginProvenanceRecord>>(text);
            if (parsed == null) return records;

            foreach (var pair in parsed)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                {
                    records[pair.Key] = pair.Value;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginProvenanceStore", $"[PluginProvenance] Failed to load records: {ex.Message}");
        }

        return records;
    }

    private async Task SaveAsync(Dictionary<string, PluginProvenanceRecord> records)
    {
        try
        {
            var json = JsonSerializer.Serialize(records);
            await _settings.SaveSettingAsync(SettingKey, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginProvenanceStore", $"[PluginProvenance] Failed to save records: {ex.Message}");
        }
    }
}
