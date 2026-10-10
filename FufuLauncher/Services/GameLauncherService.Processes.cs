using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private async Task<int> WaitGenshinStartAsync(CancellationToken cancellationToken)
    {
        int timeoutMs = 60000;
        int elapsedMs = 0;
        int delayMs = 1000;

        var exeNames = await GameExeManager.GetExeNamesAsync();
        var processNames = exeNames.Select(Path.GetFileNameWithoutExtension).ToList();

        while (elapsedMs < timeoutMs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Trace.WriteLine("[启动流程] 等待游戏进程期间用户取消启动");
                return 0;
            }

            try
            {
                await Task.Delay(delayMs, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Trace.WriteLine("[启动流程] 等待游戏进程期间用户取消启动");
                return 0;
            }

            elapsedMs += delayMs;

            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                if (processNames.Any(name => process.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        await Task.Delay(2000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        Trace.WriteLine("[启动流程] 等待游戏进程期间用户取消启动");
                        return 0;
                    }

                    return process.Id;
                }
            }
        }

        Trace.WriteLine("[启动流程] 警告：等待游戏主程序超时 (1分钟)");
        return 0;
    }

    private void KillGameProcesses(List<string> processNames, string gamePath)
    {
        try
        {
            var processes = new List<Process>();
            foreach (var name in processNames)
            {
                processes.AddRange(Process.GetProcessesByName(name));
            }

            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;

                    if (!string.IsNullOrEmpty(gamePath))
                    {
                        try
                        {
                            var processPath = process.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(processPath) &&
                                !processPath.StartsWith(gamePath, StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }
                        catch (Win32Exception)
                        {
                            // ignored
                        }
                        catch (InvalidOperationException)
                        {
                            continue;
                        }
                    }

                    process.Kill();
                    Debug.WriteLine($"[启动流程] 已终止游戏进程: {process.ProcessName} (PID:{process.Id})");
                }
                catch
                {
                    // ignored
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[启动流程] 清理游戏进程异常: {ex.Message}");
        }
    }

    private bool StartGameNormally(string exePath, string args, string workingDir, StringBuilder log)
    {
        try
        {
            log.AppendLine($"[普通启动] 程序: {exePath}");
            log.AppendLine($"[普通启动] 参数: {args}");
            log.AppendLine($"[普通启动] 工作目录: {workingDir}");

            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                WorkingDirectory = workingDir,
                UseShellExecute = true
            });

            log.AppendLine("[普通启动] 进程已创建");
            return true;
        }
        catch (Exception ex)
        {
            log.AppendLine($"[普通启动] ? 异常: {ex.Message}");
            return false;
        }
    }
}
