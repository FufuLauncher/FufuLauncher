using System.Diagnostics;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    private async Task CheckAndLaunchFpsOverlayAsync(LaunchFlowLog logBuilder, int gamePid)
    {
        try
        {
            var isFpsEnabled = await _localSettingsService.ReadSettingAsync("IsFpsOverlayEnabled");
            if (isFpsEnabled != null && Convert.ToBoolean(isFpsEnabled))
            {
                if (!IsAdministrator())
                {
                    logBuilder.AppendLine(
                        "[LaunchFlow] FPS overlay: administrator privileges required, setting reset to disabled");
                    await _localSettingsService.SaveSettingAsync("IsFpsOverlayEnabled", false);
                    return;
                }

                if (gamePid > 0)
                {
                    logBuilder.AppendLine($"[LaunchFlow] FPS overlay: starting for PID {gamePid}");
                    FpsOverlayService.Instance.StartOverlay(gamePid);
                }
                else
                {
                    logBuilder.AppendLine("[LaunchFlow] FPS overlay: game PID unavailable, aborting");
                }
            }
        }
        catch (Exception ex)
        {
            logBuilder.AppendLine($"[LaunchFlow] FPS overlay failed to start: {ex.Message}");
        }
    }

    private async Task CheckAndLaunchScreenshotServiceAsync(LaunchFlowLog logBuilder, int gamePid)
    {
        try
        {
            var isEnabled = await _localSettingsService.ReadSettingAsync("IsScreenshotEnabled");
            if (isEnabled != null && Convert.ToBoolean(isEnabled))
            {
                logBuilder.AppendLine($"[LaunchFlow] Screenshot service: starting for PID {gamePid}");
                await _screenshotService.StartAsync(gamePid);
                logBuilder.AppendLine("[LaunchFlow] Screenshot service started");
            }
        }
        catch (Exception ex)
        {
            logBuilder.AppendLine($"[LaunchFlow] Screenshot service failed to start: {ex.Message}");
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
                DebugLog.Info("GameLauncherService.Integrations", $"[AdditionalProgram] Raw path: '{path}'");
                DebugLog.Info("GameLauncherService.Integrations", $"[AdditionalProgram] Cleaned path: '{programPath}'");

                if (!string.IsNullOrEmpty(programPath) && File.Exists(programPath))
                {
                    DebugLog.Info("GameLauncherService.Integrations",
                        $"[AdditionalProgram] File exists, starting: {programPath}");

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = programPath,
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WorkingDirectory = Path.GetDirectoryName(programPath)
                    };

                    Process.Start(startInfo);
                    DebugLog.Info("GameLauncherService.Integrations", "[AdditionalProgram] Started successfully");
                }
                else
                {
                    DebugLog.Info("GameLauncherService.Integrations",
                        $"[AdditionalProgram] File not found or invalid path: '{programPath}'");
                }
            }
            else
            {
                DebugLog.Info("GameLauncherService.Integrations",
                    $"[AdditionalProgram] Disabled or empty path: enabled={enabled}, path={path}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameLauncherService.Integrations",
                $"[AdditionalProgram] Start failed: {ex.Message}\n{ex.StackTrace}");
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

                DebugLog.Info("GameLauncherService.Integrations",
                    $"[BetterGI] Config enabled, launching bettergi://start via URL scheme in {delaySeconds:0.#} seconds");

                if (delaySeconds > 0)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        DebugLog.Info("GameLauncherService.Integrations",
                            "[BetterGI] Launch canceled, skipping BetterGI start");
                        return;
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    DebugLog.Info("GameLauncherService.Integrations",
                        "[BetterGI] Launch canceled, skipping BetterGI start");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = "bettergi://start",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };

                Process.Start(startInfo);
                DebugLog.Info("GameLauncherService.Integrations", "[BetterGI] URL scheme launch command sent");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameLauncherService.Integrations", $"[BetterGI] URL scheme launch failed: {ex.Message}");
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
                        DebugLog.Info("GameLauncherService.Integrations", "[BetterGI] Process terminated");
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("GameLauncherService.Integrations",
                            $"[BetterGI] Failed to terminate process: {ex.Message}");
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
                DebugLog.Info("GameLauncherService.Integrations", "[BetterGI] Sending taskkill command");
            }
            catch (Exception ex)
            {
                DebugLog.Info("GameLauncherService.Integrations",
                    $"[BetterGI] taskkill termination failed: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameLauncherService.Integrations", $"[BetterGI] Stop failed: {ex.Message}");
        }
    }
}