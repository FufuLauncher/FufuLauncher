/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed record PluginAuditEntry(
    string FolderName,
    string FileName,
    string FullPath,
    string? ConfigFilePath,
    bool IsEnabled);

public sealed class PluginAuditReporter
{
    public const string ReportStateKey = "PluginAuditReportState";

    private const string AuthToken = "c@(Kl2ygRs8n134r]h?8)45|`js>U2)7k6M&D1CY097C6J?;rwk)#CtNYAj*P$%j";

    private const string AddMethodMainPlugin = "main_plugin";

    private const string AddMethodLauncherBundled = "launcher_bundled";

    private const string AccountUid = "100000000";

    private const string UnknownValue = "unknown";

    private const int MaxFieldLength = 256;

    private const string MetaSectionName = "_meta";

    private const string GeneralSectionName = "General";

    private const string SectionNameKey = "Name";

    private static readonly TimeSpan SolveTimeout = TimeSpan.FromSeconds(60);

    private static readonly HttpClient Http = CreateHttpClient();

    private readonly ILocalSettingsService _settings;

    private readonly PluginProvenanceStore _provenance;

    private readonly SemaphoreSlim _scanGate = new(1, 1);

    private readonly object _cacheLock = new();

    private string _hwid = string.Empty;

    private string _gameVersion = string.Empty;

    private bool _hwidResolved;

    private bool _gameVersionResolved;

    public PluginAuditReporter(ILocalSettingsService settings, PluginProvenanceStore provenance)
    {
        _settings = settings;
        _provenance = provenance;
    }

    public async Task ScanAsync(IReadOnlyList<PluginAuditEntry> plugins, CancellationToken cancellationToken = default)
    {
        if (plugins == null || plugins.Count == 0) return;

        if (!await _scanGate.WaitAsync(0).ConfigureAwait(false)) return;

        try
        {
            await ScanCoreAsync(plugins, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginAuditReporter", $"[PluginAudit] Scan failed: {ex.Message}");
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private async Task ScanCoreAsync(IReadOnlyList<PluginAuditEntry> plugins, CancellationToken cancellationToken)
    {
        if (!await IsTelemetryEnabledAsync().ConfigureAwait(false))
        {
            DebugLog.Info("PluginAuditReporter", "[PluginAudit] Telemetry disabled by user, audit skipped");
            return;
        }

        await _provenance.InitializeAsync().ConfigureAwait(false);

        var state = await LoadStateAsync().ConfigureAwait(false);
        var pending = new List<PendingReport>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in plugins)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.FolderName) || !visited.Add(entry.FolderName)) continue;

            if (IsTelemetryExcluded(entry)) continue;

            var sha256 = await Task.Run(() => TryComputeSha256(entry.FullPath), cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(sha256)) continue;

            if (state.Reported.TryGetValue(entry.FolderName, out var reported) &&
                string.Equals(reported.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            pending.Add(new PendingReport(entry, sha256, ResolvePluginName(entry)));
        }

        if (pending.Count == 0) return;

        var stateChanged = false;
        foreach (var report in pending)
        {
            var submitted = await TrySubmitAsync(report, cancellationToken).ConfigureAwait(false);
            if (!submitted) continue;

            state.Reported[report.Entry.FolderName] = new ReportedPlugin
            {
                Sha256 = report.Sha256,
                Name = report.Name,
                ReportedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            stateChanged = true;

            DebugLog.Info("PluginAuditReporter",
                $"[PluginAudit] Reported plugin '{report.Name}' ({report.Entry.FolderName})");
        }

        if (stateChanged)
        {
            await SaveStateAsync(state).ConfigureAwait(false);
        }
    }

    private async Task<bool> TrySubmitAsync(PendingReport report, CancellationToken cancellationToken)
    {
        var challenge = await FetchChallengeAsync(cancellationToken).ConfigureAwait(false);
        if (challenge == null) return false;

        var nonce = await Task.Run(
            () => ProofOfWorkSolver.Solve(challenge.ChallengeId, challenge.ExpiresAt, challenge.Difficulty,
                SolveTimeout, cancellationToken), cancellationToken).ConfigureAwait(false);

        if (nonce == null)
        {
            DebugLog.Info("PluginAuditReporter",
                $"[PluginAudit] Proof of work not solved for '{report.Entry.FolderName}'");
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["token"] = AuthToken,
            ["pow"] = new Dictionary<string, object?>
            {
                ["c"] = challenge.ChallengeId,
                ["d"] = challenge.Difficulty,
                ["e"] = challenge.ExpiresAt,
                ["s"] = challenge.Signature,
                ["n"] = nonce.Value
            },
            ["telemetry"] = await BuildTelemetryAsync(report, cancellationToken).ConfigureAwait(false)
        };

        try
        {
            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(ApiEndpoints.TelemetrySubmitUrl, content, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NoContent) return true;

            DebugLog.Info("PluginAuditReporter",
                $"[PluginAudit] Submit rejected for '{report.Entry.FolderName}': {(int)response.StatusCode}");
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginAuditReporter",
                $"[PluginAudit] Submit failed for '{report.Entry.FolderName}': {ex.Message}");
            return false;
        }
    }

    private async Task<Dictionary<string, object?>> BuildTelemetryAsync(PendingReport report,
        CancellationToken cancellationToken)
    {
        var entry = report.Entry;

        return new Dictionary<string, object?>
        {
            ["hwid"] = Safe(await GetHwidAsync(cancellationToken).ConfigureAwait(false)),
            ["app_version"] = Safe(AppVersionHelper.FullVersion),
            ["game_version"] = Safe(await GetGameVersionAsync(cancellationToken).ConfigureAwait(false)),
            ["account_uid"] = AccountUid,
            ["plugin_name"] = Safe(report.Name),
            ["plugin_sha256"] = report.Sha256.ToLowerInvariant(),
            ["plugin_add_method"] = Safe(ResolveAddMethod(entry)),
            ["config"] = BuildConfigSnapshot(entry),
            ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
    }

    private static Dictionary<string, object?> BuildConfigSnapshot(PluginAuditEntry entry)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in ReadAllSections(entry.ConfigFilePath))
        {
            snapshot[section.Key] = IsGeneralSection(section.Key)
                ? section.Value
                : BuildSectionNameSnapshot(section.Value);
        }

        var fileInfo = new Dictionary<string, object?>
        {
            ["folder"] = entry.FolderName,
            ["file"] = entry.FileName,
            ["enabled"] = entry.IsEnabled
        };

        try
        {
            var info = new FileInfo(entry.FullPath);
            if (info.Exists)
            {
                fileInfo["bytes"] = info.Length;
                fileInfo["modified_utc"] = info.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginAuditReporter",
                $"[PluginAudit] Failed to inspect {entry.FullPath}: {ex.Message}");
        }

        snapshot[MetaSectionName] = fileInfo;

        return snapshot;
    }

    private static bool IsGeneralSection(string sectionName) =>
        string.Equals(sectionName, GeneralSectionName, StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> BuildSectionNameSnapshot(Dictionary<string, string> section)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (section.TryGetValue(SectionNameKey, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            snapshot[SectionNameKey] = Safe(name);
        }

        return snapshot;
    }

    private static async Task<TelemetryChallenge?> FetchChallengeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Http.GetAsync(ApiEndpoints.TelemetryChallengeUrl, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var challenge = JsonSerializer.Deserialize<TelemetryChallenge>(json);
            if (challenge == null) return null;
            if (string.IsNullOrWhiteSpace(challenge.ChallengeId)) return null;
            if (string.IsNullOrWhiteSpace(challenge.Signature)) return null;
            if (challenge.ExpiresAt <= 0) return null;

            return challenge;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginAuditReporter", $"[PluginAudit] Challenge request failed: {ex.Message}");
            return null;
        }
    }

    private string ResolveAddMethod(PluginAuditEntry entry)
    {
        if (IsMainPlugin(entry)) return AddMethodMainPlugin;
        if (IsLauncherBundled(entry)) return AddMethodLauncherBundled;

        if (_provenance.TryGet(entry.FolderName, out var record) && !string.IsNullOrWhiteSpace(record.Source))
        {
            return record.Source;
        }

        return PluginProvenanceStore.SourceExternalCopy;
    }

    private bool IsTelemetryExcluded(PluginAuditEntry entry)
    {
        var addMethod = ResolveAddMethod(entry);

        return string.Equals(addMethod, AddMethodMainPlugin, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(addMethod, PluginProvenanceStore.SourcePluginStore,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMainPlugin(PluginAuditEntry entry) =>
        LightweightPluginService.IsMainPluginPackage(entry.FolderName, entry.FileName);

    private static bool IsLauncherBundled(PluginAuditEntry entry)
    {
        if (entry.FolderName.Contains(LightweightPluginService.LitePluginFolderName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(entry.FileName, LightweightPluginService.LitePluginDllName,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(entry.FileName, LightweightPluginService.LitePluginDllName + ".disabled",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolvePluginName(PluginAuditEntry entry)
    {
        var general = ReadGeneralSection(entry.ConfigFilePath);
        if (general.TryGetValue(SectionNameKey, out var name) && !string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        return entry.FolderName;
    }

    private static Dictionary<string, string> ReadGeneralSection(string? configPath)
    {
        var sections = ReadAllSections(configPath);

        return sections.TryGetValue(GeneralSectionName, out var general)
            ? general
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, Dictionary<string, string>> ReadAllSections(string? configPath)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return sections;
        }

        try
        {
            foreach (var section in new IniFile(configPath).ReadAll())
            {
                sections[section.Key] = new Dictionary<string, string>(section.Value,
                    StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginAuditReporter",
                $"[PluginAudit] Failed to read {configPath}: {ex.Message}");
        }

        return sections;
    }

    private async Task<string> GetHwidAsync(CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_hwidResolved) return _hwid;
        }

        var hwid = await Task.Run(SystemEnvironmentHelper.GetHwid, cancellationToken).ConfigureAwait(false);

        lock (_cacheLock)
        {
            _hwid = hwid ?? string.Empty;
            _hwidResolved = true;
            return _hwid;
        }
    }

    private async Task<string> GetGameVersionAsync(CancellationToken cancellationToken)
    {
        lock (_cacheLock)
        {
            if (_gameVersionResolved) return _gameVersion;
        }

        var version = await Task.Run(PluginGameVersionGuard.ResolveInstalledGameVersion, cancellationToken)
            .ConfigureAwait(false);

        lock (_cacheLock)
        {
            _gameVersion = version ?? string.Empty;
            _gameVersionResolved = true;
            return _gameVersion;
        }
    }

    private static string? TryComputeSha256(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return PluginVerifier.ComputeSha256(stream);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginAuditReporter", $"[PluginAudit] Failed to hash {filePath}: {ex.Message}");
            return null;
        }
    }

    private static string Safe(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0) return UnknownValue;

        return text.Length > MaxFieldLength ? text.Substring(0, MaxFieldLength) : text;
    }

    private async Task<bool> IsTelemetryEnabledAsync()
    {
        try
        {
            var raw = await _settings.ReadSettingAsync(LocalSettingsService.IsTelemetryEnabledKey)
                .ConfigureAwait(false);

            return raw != null && Convert.ToBoolean(raw);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginAuditReporter",
                $"[PluginAudit] Failed to read telemetry setting: {ex.Message}");
            return false;
        }
    }

    private async Task<ReportState> LoadStateAsync()
    {
        var state = new ReportState();

        try
        {
            var raw = await _settings.ReadSettingAsync(ReportStateKey).ConfigureAwait(false);
            if (raw is not string text || string.IsNullOrWhiteSpace(text)) return state;

            var parsed = JsonSerializer.Deserialize<ReportState>(text);
            if (parsed?.Reported == null) return state;

            foreach (var pair in parsed.Reported)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                {
                    state.Reported[pair.Key] = pair.Value;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginAuditReporter", $"[PluginAudit] Failed to load report state: {ex.Message}");
        }

        return state;
    }

    private async Task SaveStateAsync(ReportState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state);
            await _settings.SaveSettingAsync(ReportStateKey, json).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginAuditReporter", $"[PluginAudit] Failed to save report state: {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FufuLauncher");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    private sealed record PendingReport(PluginAuditEntry Entry, string Sha256, string Name);
}

internal sealed class ReportState
{
    [JsonPropertyName("reported")]
    public Dictionary<string, ReportedPlugin> Reported
    {
        get;
        set;
    } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class ReportedPlugin
{
    [JsonPropertyName("sha256")]
    public string Sha256
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("name")]
    public string Name
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("reported_at")]
    public long ReportedAt
    {
        get;
        set;
    }
}

internal sealed class TelemetryChallenge
{
    [JsonPropertyName("c")]
    public string ChallengeId
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("d")]
    public int Difficulty
    {
        get;
        set;
    }

    [JsonPropertyName("e")]
    public long ExpiresAt
    {
        get;
        set;
    }

    [JsonPropertyName("s")]
    public string Signature
    {
        get;
        set;
    } = string.Empty;
}