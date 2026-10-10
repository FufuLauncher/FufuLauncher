using System.Diagnostics;
using System.Text;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    private async Task CheckAndLaunchFpsOverlayAsync(StringBuilder logBuilder, int gamePid)
    {
        try
        {
            var isFpsEnabled = await _localSettingsService.ReadSettingAsync("IsFpsOverlayEnabled");
            if (isFpsEnabled != null && Convert.ToBoolean(isFpsEnabled))
            {
                if (!IsAdministrator())
                {
                    logBuilder.AppendLine("[启动流程] 检查到系统未以管理员权限运行，不允许启用帧数监控，已自动重置该设置");
                    await _localSettingsService.SaveSettingAsync("IsFpsOverlayEnabled", false);
                    return;
                }

                if (gamePid > 0)
                {
                    logBuilder.AppendLine($"[启动流程] 权限校验通过，正在为进程(PID:{gamePid})启动系统性能监控遮罩");
                    FpsOverlayService.Instance.StartOverlay(gamePid);
                }
                else
                {
                    logBuilder.AppendLine("[启动流程] 无法获取游戏进程PID，帧数监控启动中止");
                }
            }
        }
        catch (Exception ex)
        {
            logBuilder.AppendLine($"[启动流程] 帧数监控遮罩启动异常: {ex.Message}");
        }
    }

    private async Task CheckAndLaunchScreenshotServiceAsync(StringBuilder logBuilder, int gamePid)
    {
        try
        {
            var isEnabled = await _localSettingsService.ReadSettingAsync("IsScreenshotEnabled");
            if (isEnabled != null && Convert.ToBoolean(isEnabled))
            {
                logBuilder.AppendLine($"[启动流程] 正在为进程(PID:{gamePid})启动截图服务");
                await _screenshotService.StartAsync(gamePid);
                logBuilder.AppendLine("[启动流程] 截图服务已启动");
            }
        }
        catch (Exception ex)
        {
            logBuilder.AppendLine($"[启动流程] 截图服务启动异常: {ex.Message}");
        }
    }

    private async Task LaunchAdditionalProgramAsync()
    {
        try
        {
            var enabled = await _localSettingsService.ReadSettingAsync("AdditionalProgramEnabled");
            var path = await _localSettingsService.ReadSettingAsync("AdditionalProgramPath");

            if (enabled != null && Convert.ToBoolean(enabled) && path != null)
            {
                string programPath = path.ToString().Trim('"').Trim();
                Debug.WriteLine($"[附加程序] 原始路径: '{path}'");
                Debug.WriteLine($"[附加程序] 清理后路径: '{programPath}'");

                if (!string.IsNullOrEmpty(programPath) && File.Exists(programPath))
                {
                    Debug.WriteLine($"[附加程序] 文件存在，准备启动: {programPath}");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = programPath,
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WorkingDirectory = Path.GetDirectoryName(programPath)
                    };

                    Process.Start(startInfo);
                    Debug.WriteLine("[附加程序] 启动成功");
                }
                else
                {
                    Debug.WriteLine($"[附加程序] 文件不存在或路径无效: '{programPath}'");
                }
            }
            else
            {
                Debug.WriteLine($"[附加程序] 未启用或路径为空: enabled={enabled}, path={path}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[附加程序] 启动失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async Task LaunchBetterGIAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var enabled = await _localSettingsService.ReadSettingAsync("IsBetterGIIntegrationEnabled");
            if (enabled != null && Convert.ToBoolean(enabled))
            {
                var delaySetting = await _localSettingsService.ReadSettingAsync("BetterGIStartupDelaySeconds");
                var delaySeconds = delaySetting != null
                    ? Math.Clamp(Convert.ToDouble(delaySetting), 0.0, 60.0)
                    : 0.0;

                Debug.WriteLine($"[BetterGI] 配置已启用，将在 {delaySeconds:0.#} 秒后通过URL Scheme启动 bettergi://start");

                if (delaySeconds > 0)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine("[BetterGI] 启动流程已取消，跳过 BetterGI 启动");
                        return;
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine("[BetterGI] 启动流程已取消，跳过 BetterGI 启动");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "bettergi://start",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);
                Debug.WriteLine("[BetterGI] 通过URL Scheme启动指令已发送成功");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BetterGI] 通过URL Scheme启动失败: {ex.Message}");
        }
    }

    public async Task StopBetterGIAsync()
    {
        try
        {
            var enabled = await _localSettingsService.ReadSettingAsync("IsBetterGIIntegrationEnabled");
            var closeOnExit = await _localSettingsService.ReadSettingAsync("IsBetterGICloseOnExitEnabled");
            if (enabled == null || !Convert.ToBoolean(enabled) || closeOnExit == null ||
                !Convert.ToBoolean(closeOnExit)) return;

            var processes = Process.GetProcessesByName("BetterGI");
            if (processes.Length > 0)
            {
                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill();
                        await p.WaitForExitAsync();
                        Debug.WriteLine("[BetterGI] 进程已终止");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[BetterGI] 终止进程失败: {ex.Message}");
                    }
                }

                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/IM BetterGI.exe /F",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                Process.Start(startInfo);
                Debug.WriteLine("[BetterGI] 发送 taskkill 指令");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BetterGI] 使用 taskkill 终止失败: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BetterGI] Stop 异常: {ex.Message}");
        }
    }
}
