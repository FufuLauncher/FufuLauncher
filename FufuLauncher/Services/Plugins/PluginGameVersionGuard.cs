/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed record PluginGameVersionStatus(
    PluginGameVersionSupport Support,
    string GameVersion,
    string DeclaredRule,
    string InvalidSegment,
    bool Ignored = false)
{
    public bool IsBlocked =>
        !Ignored && (Support is PluginGameVersionSupport.Unsupported or PluginGameVersionSupport.Malformed);

    public string BlockMessage => Support switch
    {
        PluginGameVersionSupport.Unsupported => string.Format(
            "PluginGameVersion_Blocked_Format".GetLocalized(), GameVersion, DeclaredRule),
        PluginGameVersionSupport.Malformed => string.Format(
            "PluginGameVersion_Malformed_Format".GetLocalized(), DeclaredRule, InvalidSegment),
        _ => string.Empty
    };
}

public sealed record PluginGameVersionEnforcement(
    string PluginName,
    string DisabledPath,
    PluginGameVersionStatus Status);

public sealed class PluginGameVersionGuard
{
    public const string RuleKey = "GameVersion";

    public const string IgnoreSettingKey = "IgnorePluginGameVersionRestriction";

    private static readonly string[] SupportRuleKeys =
    {
        RuleKey, "SupportedGameVersions", "GameVersions", "游戏版本", "支持游戏版本"
    };

    private static readonly object StaticCacheLock = new();
    private static string? _staticGamePath;
    private static DateTime _staticConfigStamp;
    private static string _staticGameVersion = string.Empty;
    private static bool _staticResolved;

    private readonly ILocalSettingsService _localSettings;
    private readonly object _cacheLock = new();

    private string? _cachedGamePath;
    private DateTime _cachedConfigStamp;
    private string _cachedGameVersion = string.Empty;
    private bool _cached;

    public PluginGameVersionGuard(ILocalSettingsService localSettings)
    {
        _localSettings = localSettings;
    }

    public string GetGameVersion()
    {
        var gamePath = ResolveGamePath(_localSettings);
        if (string.IsNullOrEmpty(gamePath)) return string.Empty;

        var configPath = Path.Combine(gamePath, GameConstants.CONFIG_FILE_NAME);
        var stamp = GetTimestamp(configPath);

        lock (_cacheLock)
        {
            if (_cached && string.Equals(_cachedGamePath, gamePath, StringComparison.OrdinalIgnoreCase) &&
                _cachedConfigStamp == stamp)
            {
                return _cachedGameVersion;
            }
        }

        var version = ReadGameVersion(configPath);

        lock (_cacheLock)
        {
            _cached = true;
            _cachedGamePath = gamePath;
            _cachedConfigStamp = stamp;
            _cachedGameVersion = version;
        }

        return version;
    }

    public PluginGameVersionStatus Inspect(string pluginDirectory, string? configPath = null)
    {
        var resolvedConfig = configPath;
        if (string.IsNullOrEmpty(resolvedConfig) || !File.Exists(resolvedConfig))
        {
            resolvedConfig = ResolveConfigPath(pluginDirectory);
        }

        var status = Evaluate(ReadSupportRule(resolvedConfig), GetGameVersion());
        return IsIgnoreEnabled() ? status with { Ignored = true } : status;
    }

    public bool IsIgnoreEnabled()
    {
        try
        {
            var value = RunOnThreadPool(() => _localSettings.ReadSettingAsync(IgnoreSettingKey));
            return value != null && Convert.ToBoolean(value);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to read ignore setting: " + ex.Message);
            return false;
        }
    }

    public bool TryDisablePlugin(string pluginDirectory, string? configPath, out string disabledPath)
    {
        disabledPath = string.Empty;

        try
        {
            var resolvedConfig = configPath;
            if (string.IsNullOrEmpty(resolvedConfig) || !File.Exists(resolvedConfig))
            {
                resolvedConfig = ResolveConfigPath(pluginDirectory);
            }

            var dllPath = ResolvePluginDllPath(pluginDirectory, ReadGeneral(resolvedConfig));
            if (string.IsNullOrEmpty(dllPath)) return false;

            if (dllPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                disabledPath = dllPath;
                return true;
            }

            disabledPath = dllPath + ".disabled";
            File.Move(dllPath, disabledPath, true);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to disable plugin in '" + pluginDirectory + "': " + ex.Message);
            return false;
        }
    }

    public IReadOnlyList<PluginGameVersionEnforcement> EnforceAll()
    {
        var results = new List<PluginGameVersionEnforcement>();
        var root = LightweightPluginService.PluginsDir;
        if (!Directory.Exists(root)) return results;

        foreach (var directory in EnumerateDirectories(root))
        {
            var configPath = ResolveConfigPath(directory);
            if (string.IsNullOrEmpty(configPath)) continue;

            var general = ReadGeneral(configPath);
            if (general == null || !general.TryGetValue("File", out var declaredFile) ||
                string.IsNullOrWhiteSpace(declaredFile))
            {
                continue;
            }

            var status = Inspect(directory, configPath);
            if (!status.IsBlocked) continue;
            if (!TryDisablePlugin(directory, configPath, out var disabledPath)) continue;

            results.Add(new PluginGameVersionEnforcement(Path.GetFileName(directory), disabledPath, status));
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Disabled '" + directory + "' (game " + status.GameVersion +
                ", rule '" + status.DeclaredRule + "')");
        }

        return results;
    }

    public static PluginGameVersionStatus Evaluate(string? rawRule, string? gameVersion)
    {
        var version = gameVersion?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(rawRule))
        {
            return new PluginGameVersionStatus(PluginGameVersionSupport.Unspecified, version, string.Empty,
                string.Empty);
        }

        var ruleText = rawRule.Trim();

        if (!PluginGameVersionRequirement.TryParse(ruleText, out var requirement, out var invalidSegment))
        {
            return new PluginGameVersionStatus(PluginGameVersionSupport.Malformed, version, ruleText,
                invalidSegment);
        }

        if (version.Length == 0 || !PluginGameVersion.TryParseGameVersion(version, out var parsedVersion))
        {
            return new PluginGameVersionStatus(PluginGameVersionSupport.Unknown, version, ruleText, string.Empty);
        }

        return requirement!.IsSatisfiedBy(parsedVersion)
            ? new PluginGameVersionStatus(PluginGameVersionSupport.Supported, version, ruleText, string.Empty)
            : new PluginGameVersionStatus(PluginGameVersionSupport.Unsupported, version, ruleText, string.Empty);
    }

    public static string? ReadSupportRule(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return null;

        try
        {
            var sections = new IniFile(configPath).ReadAll();
            if (!sections.TryGetValue("General", out var general)) return null;

            foreach (var key in SupportRuleKeys)
            {
                if (!general.TryGetValue(key, out var value)) continue;
                if (string.IsNullOrWhiteSpace(value)) continue;
                return value.Trim();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to read '" + configPath + "': " + ex.Message);
        }

        return null;
    }

    public static string? ResolveConfigPath(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;

        var standard = Path.Combine(directory, GameConstants.CONFIG_FILE_NAME);
        if (File.Exists(standard)) return standard;

        try
        {
            return Directory.GetFiles(directory, "*.ini", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to list '" + directory + "': " + ex.Message);
            return null;
        }
    }

    public static bool IsPluginUsable(string? dllPath)
    {
        try
        {
            var directory = string.IsNullOrEmpty(dllPath) ? null : Path.GetDirectoryName(dllPath);
            if (string.IsNullOrEmpty(directory)) return true;

            var configPath = ResolveConfigPath(directory);
            if (string.IsNullOrEmpty(configPath)) return true;

            var hasGuard = TryGetRegisteredGuard(out var guard);
            if (hasGuard ? guard!.IsIgnoreEnabled() : ResolveIgnoredFromRepository()) return true;

            var gameVersion = hasGuard ? guard!.GetGameVersion() : ResolveInstalledGameVersion();

            return !Evaluate(ReadSupportRule(configPath), gameVersion).IsBlocked;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to inspect '" + dllPath + "': " + ex.Message);
            return true;
        }
    }

    private static bool TryGetRegisteredGuard(out PluginGameVersionGuard? guard)
    {
        guard = null;

        try
        {
            if (Microsoft.UI.Xaml.Application.Current is not App application) return false;
            guard = application.Host?.Services.GetService(typeof(PluginGameVersionGuard)) as PluginGameVersionGuard;
            return guard != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static string ResolveInstalledGameVersion()
    {
        lock (StaticCacheLock)
        {
            if (_staticResolved && !string.IsNullOrEmpty(_staticGamePath))
            {
                var cachedConfig = Path.Combine(_staticGamePath, GameConstants.CONFIG_FILE_NAME);
                if (GetTimestamp(cachedConfig) == _staticConfigStamp) return _staticGameVersion;
            }
        }

        var gamePath = string.Empty;

        try
        {
            var repository = new LocalSettingsRepository();
            var (success, found, value) =
                RunOnThreadPool(() => repository.TryGetSettingAsync(GameLauncherService.GamePathKey));
            if (success && found) gamePath = (value ?? string.Empty).Trim('"').Trim();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to resolve game path: " + ex.Message);
        }

        var configPath = string.IsNullOrEmpty(gamePath)
            ? null
            : Path.Combine(gamePath, GameConstants.CONFIG_FILE_NAME);
        var version = ReadGameVersion(configPath);

        lock (StaticCacheLock)
        {
            _staticResolved = true;
            _staticGamePath = string.IsNullOrEmpty(gamePath) ? null : gamePath;
            _staticConfigStamp = GetTimestamp(configPath);
            _staticGameVersion = version;
        }

        return version;
    }

    private static bool ResolveIgnoredFromRepository()
    {
        try
        {
            var repository = new LocalSettingsRepository();
            var (success, found, value) =
                RunOnThreadPool(() => repository.TryGetSettingAsync(IgnoreSettingKey));
            return success && found && Convert.ToBoolean(value);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to read ignore setting: " + ex.Message);
            return false;
        }
    }

    private static string ResolveGamePath(ILocalSettingsService settings)
    {
        try
        {
            var value = RunOnThreadPool(() => settings.ReadSettingAsync(GameLauncherService.GamePathKey));
            return value?.ToString()?.Trim('"').Trim() ?? string.Empty;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to read game path: " + ex.Message);
            return string.Empty;
        }
    }

    private static string ReadGameVersion(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return string.Empty;

        try
        {
            var sections = new IniFile(configPath).ReadAll();
            return sections.TryGetValue("General", out var general) &&
                   general.TryGetValue("game_version", out var version) &&
                   !string.IsNullOrWhiteSpace(version)
                ? version.Trim()
                : string.Empty;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to read game version: " + ex.Message);
            return string.Empty;
        }
    }

    private static Dictionary<string, string>? ReadGeneral(string? configPath)
    {
        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath)) return null;

        try
        {
            return new IniFile(configPath).ReadAll().GetValueOrDefault("General");
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to read '" + configPath + "': " + ex.Message);
            return null;
        }
    }

    private static string? ResolvePluginDllPath(string directory, IReadOnlyDictionary<string, string>? general)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;

        if (general != null && general.TryGetValue("File", out var fileName) &&
            !string.IsNullOrWhiteSpace(fileName))
        {
            var name = Path.GetFileName(fileName.Trim());
            if (name.Length > 0)
            {
                var enabled = Path.Combine(directory, name);
                if (File.Exists(enabled)) return enabled;

                var disabled = enabled + ".disabled";
                if (File.Exists(disabled)) return disabled;

                var shortDisabled = Path.Combine(directory, Path.GetFileNameWithoutExtension(name) + ".disabled");
                if (File.Exists(shortDisabled)) return shortDisabled;
            }
        }

        try
        {
            var dlls = Directory.GetFiles(directory, "*.dll", SearchOption.TopDirectoryOnly);
            if (dlls.Length == 1) return dlls[0];

            if (dlls.Length == 0)
            {
                var disabledDlls = Directory.GetFiles(directory, "*.dll.disabled", SearchOption.TopDirectoryOnly);
                if (disabledDlls.Length == 1) return disabledDlls[0];
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginGameVersionGuard",
                "[PluginGameVersion] Failed to resolve plugin dll in '" + directory + "': " + ex.Message);
        }

        return null;
    }

    private static IEnumerable<string> EnumerateDirectories(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] children;

            try
            {
                children = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                DebugLog.Info("PluginGameVersionGuard",
                    "[PluginGameVersion] Failed to enumerate '" + current + "': " + ex.Message);
                continue;
            }

            foreach (var child in children)
            {
                yield return child;
                pending.Push(child);
            }
        }
    }

    private static T RunOnThreadPool<T>(Func<Task<T>> operation)
    {
        var started = Task.Factory.StartNew(operation, CancellationToken.None, TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
        return started.Unwrap().GetAwaiter().GetResult();
    }

    private static DateTime GetTimestamp(string? path)
    {
        try
        {
            return !string.IsNullOrEmpty(path) && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : default;
        }
        catch (Exception)
        {
            return default;
        }
    }
}
