using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    private LaunchResult? BlockInjectionForPluginDllConflicts(StringBuilder logBuilder)
    {
        foreach (var quarantined in PluginInjectionGuard.QuarantineRootStrayFiles())
        {
            logBuilder.AppendLine($"[启动流程] 已重命名插件根目录残留文件: {Path.GetFileName(quarantined)}");
        }

        var conflicts = PluginInjectionGuard.FindDuplicateDllNames(PluginConflictSettings.Read());
        if (conflicts.Count == 0) return null;

        logBuilder.AppendLine($"[启动流程] 发现 {conflicts.Count} 组同名插件 DLL，注入已终止");
        foreach (var conflict in conflicts)
        {
            logBuilder.AppendLine($"[启动流程]   {conflict.DllName}");
            foreach (var candidate in conflict.Candidates)
            {
                logBuilder.AppendLine(
                    $"[启动流程]     {candidate.FilePath} ({candidate.LastWriteTime:yyyy-MM-dd HH:mm:ss})");
            }
        }

        if (_registrySnapshot.HasSnapshot)
        {
            try
            {
                _registrySnapshot.RestoreSnapshot();
                logBuilder.AppendLine("[启动流程] 已恢复注册表快照");
            }
            catch (Exception ex)
            {
                logBuilder.AppendLine($"[启动流程] 恢复注册表快照失败: {ex.Message}");
            }
        }

        return new LaunchResult
        {
            Success = false,
            ErrorMessage = "PluginDllConflict_Message".GetLocalized(),
            DetailLog = logBuilder.ToString(),
            PluginDllConflicts = conflicts
        };
    }

    private async Task<bool> LaunchViaExeModuleAsync(string gameExePath, string arguments, StringBuilder log)
    {
        try
        {
            var launcher2Path = Path.Combine(AppContext.BaseDirectory, "Launcher_2.exe");
            if (!File.Exists(launcher2Path))
            {
                log.AppendLine($"[EXE注入] 错误: Launcher_2.exe 不存在于: {launcher2Path}");
                return false;
            }

            log.AppendLine($"[EXE注入] 使用 Launcher_2.exe 注入模式");
            log.AppendLine($"[EXE注入] 路径: {launcher2Path}");
            log.AppendLine($"[EXE注入] 游戏: {gameExePath}");
            log.AppendLine($"[EXE注入] 启动参数: {arguments}");

            var launchArgs = QuoteArgument(gameExePath);
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                launchArgs += " " + arguments;
            }

            var psi = new ProcessStartInfo
            {
                FileName = launcher2Path,
                Arguments = launchArgs,
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(launcher2Path)
            };

            Process.Start(psi);
            log.AppendLine("[EXE注入] Launcher_2.exe 已启动");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            log.AppendLine("[EXE注入] 管理员授权被用户取消");
            return false;
        }
        catch (Exception ex)
        {
            log.AppendLine($"[EXE注入] 异常: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> LaunchViaElevatedProcessAsync(string gameExePath, string dllPath, int configMask,
        string arguments, StringBuilder log, CancellationToken cancellationToken)
    {
        try
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                log.AppendLine("[启动流程] ? 无法定位启动器可执行文件");
                return false;
            }

            var psi = new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = BuildElevatedArgumentString(gameExePath, arguments),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(currentExe)
            };

            log.AppendLine("[启动流程] 以管理员权限启动注入进程...");

            using var process = Process.Start(psi);
            if (process == null)
            {
                log.AppendLine("[启动流程] ? 管理员注入进程启动失败");
                return false;
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                log.AppendLine("[启动流程] 用户取消启动，终止管理员注入进程...");
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (Exception killEx)
                {
                    log.AppendLine($"[启动流程] 终止管理员注入进程失败: {killEx.Message}");
                }

                return false;
            }

            log.AppendLine($"[启动流程] 管理员注入进程退出，代码: {process.ExitCode}");

            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            log.AppendLine("[启动流程] 管理员授权被用户取消");
            return false;
        }
        catch (Exception ex)
        {
            log.AppendLine($"[启动流程] ? 管理员注入进程异常: {ex.Message}");
            return false;
        }
    }
}
