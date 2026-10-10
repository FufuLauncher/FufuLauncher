using System.Diagnostics;
using System.Text;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    private const string GamePathKey = "GameInstallationPath";
    private const string UseInjectionKey = "UseInjection";
    private const string CustomLaunchParametersKey = "CustomLaunchParameters";
    private const string UsingHoyolabAccountKey = "UsingHoyolabAccount";
    public const string GenshinHDRConfigKey = "IsGenshinHDRForcedEnabled";

    public bool IsGamePathSelected()
    {
        try
        {
            var savedPath = GetGamePath();
            bool exists = !string.IsNullOrEmpty(savedPath) && Directory.Exists(savedPath);
            Trace.WriteLine($"[启动服务] 检查路径: '{savedPath}', 存在: {exists}, 长度: {savedPath?.Length}");
            return exists;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[启动服务] 检查路径异常: {ex.Message}");
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

        Debug.WriteLine($"[启动服务] 读取路径: '{path}'");
        return path;
    }

    public async Task SaveGamePathAsync(string path)
    {
        if (!string.IsNullOrEmpty(path))
        {
            path = path.Trim('"').Trim();
        }

        await _localSettingsService.SaveSettingAsync(GamePathKey, path);
        Trace.WriteLine($"[启动服务] 保存路径: '{path}'");
    }

    public async Task<bool> GetUseInjectionAsync()
    {
        var obj = await _localSettingsService.ReadSettingAsync(UseInjectionKey);
        bool useInjection = obj != null && Convert.ToBoolean(obj);
        Trace.WriteLine($"[启动服务] 读取注入选项: {useInjection}");
        _lastUseInjection = useInjection;
        return useInjection;
    }

    public async Task SetUseInjectionAsync(bool useInjection)
    {
        if (useInjection == _lastUseInjection) return;
        _lastUseInjection = useInjection;
        await _localSettingsService.SaveSettingAsync(UseInjectionKey, useInjection);
        Trace.WriteLine($"[启动服务] 保存注入选项: {useInjection}");
    }

    public async Task<string> GetCustomLaunchParametersAsync()
    {
        var obj = await _localSettingsService.ReadSettingAsync(CustomLaunchParametersKey);
        return obj?.ToString() ?? string.Empty;
    }

    public async Task SetCustomLaunchParametersAsync(string parameters)
    {
        await _localSettingsService.SaveSettingAsync(CustomLaunchParametersKey, parameters);
        Trace.WriteLine($"[启动服务] 保存自定义参数: '{parameters}'");
    }

    public async Task<bool> GetUsingHoyolabAccountAsync()
    {
        var obj = await _localSettingsService.ReadSettingAsync(UsingHoyolabAccountKey);
        return obj != null && Convert.ToBoolean(obj);
    }

    public async Task SetUsingHoyolabAccountAsync(bool value)
    {
        await _localSettingsService.SaveSettingAsync(UsingHoyolabAccountKey, value);
        Trace.WriteLine($"[启动服务] 保存米游社账户启动选项: {value}");
    }

    private async Task ApplyGenshinHDRConfigAsync(StringBuilder logBuilder)
    {
        try
        {
            var obj = await _localSettingsService.ReadSettingAsync(GenshinHDRConfigKey);
            bool isEnabled = obj != null && Convert.ToBoolean(obj);

            logBuilder.AppendLine($"[启动流程] 强制设置HDR状态: {(isEnabled ? "开启 (1)" : "关闭 (0)")}");
            GameSettingService.SetGenshinHDRState(isEnabled);
        }
        catch (Exception ex)
        {
            logBuilder.AppendLine($"[启动流程] ? 设置HDR异常: {ex.Message}");
        }
    }

    private StringBuilder BuildLaunchArguments(GameConfig config, string? authTicket = null)
    {
        var args = new StringBuilder();

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
                    Debug.WriteLine($"[启动服务] 使用自定义参数: '{customParams}'");
                }
            }
        }

        if (!string.IsNullOrEmpty(authTicket))
        {
            if (args.Length > 0) args.Append(' ');
            args.Append($"login_auth_ticket={authTicket}");
            Debug.WriteLine("[启动服务] 已追加login_auth_ticket参数");
        }

        return args;
    }
}
