using System.Diagnostics;
using System.Text;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    public const string GamePathKey = "GameInstallationPath";
    private const string UseInjectionKey = "UseInjection";
    public const string CustomLaunchParametersKey = "CustomLaunchParameters";
    public const string IsCustomLaunchParametersEnabledKey = "IsCustomLaunchParametersEnabled";
    private const string UsingHoyolabAccountKey = "UsingHoyolabAccount";
    public const string GenshinHDRConfigKey = "IsGenshinHDRForcedEnabled";

    public bool IsGamePathSelected()
    {
        try
        {
            var savedPath = GetGamePath();
            bool exists = !string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath);
            DebugLog.Info("GameLauncherService.Configuration",
                $"[LaunchService] Checking path: '{savedPath}', exists: {exists}, length: {savedPath?.Length}");
            return exists;
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameLauncherService.Configuration", $"[LaunchService] Path check failed: {ex.Message}");
            return false;
        }
    }

    public string GetGamePath()
    {
        var pathObj = _localSettingsService.ReadSettingAsync(GamePathKey).Result;
        string path = pathObj?.ToString() ?? string.Empty;

        if (!string.IsNullOrEmpty(path))
        {
            path = path.Trim('"').Trim();
        }

        DebugLog.Info("GameLauncherService.Configuration", $"[LaunchService] Read path: '{path}'");
        return path;
    }

    public async Task SaveGamePathAsync(string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            path = path.Trim('"').Trim();
        }

        await _localSettingsService.SaveSettingAsync(GamePathKey, path);
        DebugLog.Info("GameLauncherService.Configuration", $"[LaunchService] Saved path: '{path}'");
    }

    public async Task<bool> GetUseInjectionAsync()
    {
        var obj = await _localSettingsService.ReadSettingAsync(UseInjectionKey);
        bool useInjection = obj != null && Convert.ToBoolean(obj);
        DebugLog.Info("GameLauncherService.Configuration", $"[LaunchService] Read injection option: {useInjection}");
        _lastUseInjection = useInjection;
        return useInjection;
    }

    public async Task SetUseInjectionAsync(bool useInjection)
    {
        if (useInjection == _lastUseInjection) return;
        _lastUseInjection = useInjection;
        await _localSettingsService.SaveSettingAsync(UseInjectionKey, useInjection);
        DebugLog.Info("GameLauncherService.Configuration", $"[LaunchService] Saved injection option: {useInjection}");
    }

    public async Task<string> GetCustomLaunchParametersAsync()
    {
        var obj = await _localSettingsService.ReadSettingAsync(CustomLaunchParametersKey);
        return obj?.ToString() ?? string.Empty;
    }

    public async Task SetCustomLaunchParametersAsync(string parameters)
    {
        await _localSettingsService.SaveSettingAsync(CustomLaunchParametersKey, parameters);
        DebugLog.Info("GameLauncherService.Configuration", $"[LaunchService] Saved custom parameters: '{parameters}'");
    }

    public async Task<bool> GetIsCustomLaunchParametersEnabledAsync()
    {
        var obj = await _localSettingsService.ReadSettingAsync(IsCustomLaunchParametersEnabledKey);
        return obj != null && Convert.ToBoolean(obj);
    }

    public async Task SetIsCustomLaunchParametersEnabledAsync(bool enabled)
    {
        await _localSettingsService.SaveSettingAsync(IsCustomLaunchParametersEnabledKey, enabled);
        DebugLog.Info("GameLauncherService.Configuration",
            $"[LaunchService] Saved custom parameter enabled state: {enabled}");
    }

    public async Task<bool> GetUsingHoyolabAccountAsync()
    {
        var obj = await _localSettingsService.ReadSettingAsync(UsingHoyolabAccountKey);
        return obj != null && Convert.ToBoolean(obj);
    }

    public async Task SetUsingHoyolabAccountAsync(bool value)
    {
        await _localSettingsService.SaveSettingAsync(UsingHoyolabAccountKey, value);
        DebugLog.Info("GameLauncherService.Configuration",
            $"[LaunchService] Saved Miyoushe account launch option: {value}");
    }

    private async Task ApplyGenshinHDRConfigAsync(LaunchFlowLog logBuilder)
    {
        try
        {
            var obj = await _localSettingsService.ReadSettingAsync(GenshinHDRConfigKey);
            bool isEnabled = obj != null && Convert.ToBoolean(obj);

            logBuilder.AppendLine($"[LaunchFlow] Forcing HDR state: {(isEnabled ? "enabled (1)" : "disabled (0)")}");
            GameSettingService.SetGenshinHDRState(isEnabled);
        }
        catch (Exception ex)
        {
            logBuilder.AppendLine($"[LaunchFlow] Failed to apply HDR state: {ex.Message}");
        }
    }

    private StringBuilder BuildLaunchArguments(GameConfig config, string? authTicket = null)
    {
        var args = new StringBuilder();

        var isCustomParamsEnabledObj =
            _localSettingsService.ReadSettingAsync(IsCustomLaunchParametersEnabledKey).Result;
        bool isCustomParamsEnabled = isCustomParamsEnabledObj != null && Convert.ToBoolean(isCustomParamsEnabledObj);

        if (isCustomParamsEnabled)
        {
            var customParamsObj = _localSettingsService.ReadSettingAsync(CustomLaunchParametersKey).Result;
            if (customParamsObj != null)
            {
                string customParams = customParamsObj.ToString();

                if (!string.IsNullOrWhiteSpace(customParams))
                {
                    customParams = customParams.Trim('"').Trim();

                    if (!string.IsNullOrEmpty(customParams))
                    {
                        if (args.Length > 0) args.Append(' ');
                        args.Append(customParams);
                        DebugLog.Info("GameLauncherService.Configuration",
                            $"[LaunchService] Using custom arguments: '{customParams}'");
                    }
                }
            }
        }
        else
        {
            DebugLog.Info("GameLauncherService.Configuration",
                "[LaunchService] Game launch arguments disabled, skipping custom arguments");
        }

        if (!string.IsNullOrEmpty(authTicket))
        {
            if (args.Length > 0) args.Append(' ');
            args.Append($"login_auth_ticket={authTicket}");
            DebugLog.Info("GameLauncherService.Configuration", "[LaunchService] Appended login_auth_ticket argument");
        }

        return args;
    }
}