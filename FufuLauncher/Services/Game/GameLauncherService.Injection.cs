using System.ComponentModel;
using System.Diagnostics;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    private LaunchResult? BlockInjectionForPluginDllConflicts(LaunchFlowLog logBuilder)
    {
        foreach (var quarantined in PluginInjectionGuard.QuarantineRootStrayFiles())
        {
            logBuilder.AppendLine(
                $"[LaunchFlow] Quarantined stray file in plugin root: '{Path.GetFileName(quarantined)}'");
        }

        var conflicts = PluginInjectionGuard.FindDuplicateDllNames(PluginConflictSettings.Read());
        if (conflicts.Count == 0) return null;

        logBuilder.AppendLine($"[LaunchFlow] Found {conflicts.Count} duplicate plugin DLL group(s), injection aborted");
        foreach (var conflict in conflicts)
        {
            logBuilder.AppendLine($"[LaunchFlow] Duplicate plugin DLL name: {conflict.DllName}");
            foreach (var candidate in conflict.Candidates)
            {
                logBuilder.AppendLine(
                    $"[LaunchFlow] Candidate DLL: '{candidate.FilePath}' (modified: {candidate.LastWriteTime:yyyy-MM-dd HH:mm:ss})");
            }
        }

        if (_registrySnapshot.HasSnapshot)
        {
            try
            {
                _registrySnapshot.RestoreSnapshot();
                logBuilder.AppendLine("[LaunchFlow] Registry snapshot restored");
            }
            catch (Exception ex)
            {
                logBuilder.AppendLine($"[LaunchFlow] Registry snapshot restore failed: {ex.Message}");
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

    private async Task<bool> LaunchViaExeModuleAsync(string gameExePath, string arguments, LaunchFlowLog log)
    {
        try
        {
            var launcher2Path = Path.Combine(AppContext.BaseDirectory, "Launcher_2.exe");
            if (!File.Exists(launcher2Path))
            {
                log.AppendLine($"[LaunchFlow] EXE injection failed: Launcher_2.exe not found at '{launcher2Path}'");
                return false;
            }

            log.AppendLine("[LaunchFlow] Launching via EXE injection module");
            log.AppendLine($"[LaunchFlow] EXE injection launcher path: '{launcher2Path}'");
            log.AppendLine($"[LaunchFlow] EXE injection game path: '{gameExePath}'");
            log.AppendLine($"[LaunchFlow] EXE injection arguments: {arguments}");

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
            log.AppendLine("[LaunchFlow] Launcher_2.exe started");
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            log.AppendLine("[LaunchFlow] EXE injection failed: elevation cancelled by user");
            return false;
        }
        catch (Exception ex)
        {
            log.AppendLine($"[LaunchFlow] EXE injection failed: {ex.Message}");
            return false;
        }
    }

    private async Task<(bool Started, string? Failure)> LaunchViaElevatedProcessAsync(string gameExePath,
        string dllPath, int configMask, string arguments, LaunchFlowLog log, CancellationToken cancellationToken)
    {
        try
        {
            var currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                log.AppendLine("[LaunchFlow] Error: launcher executable path unavailable");
                return (false, null);
            }

            var psi = new ProcessStartInfo
            {
                FileName = currentExe,
                Arguments = BuildElevatedArgumentString(gameExePath, arguments),
                UseShellExecute = true,
                Verb = "runas",
                WorkingDirectory = Path.GetDirectoryName(currentExe)
            };

            log.AppendLine("[LaunchFlow] Starting elevated injection process");

            ElevatedInjectionResultFile.Clear();

            using var process = Process.Start(psi);
            if (process == null)
            {
                log.AppendLine("[LaunchFlow] Error: failed to start elevated injection process");
                return (false, null);
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                log.AppendLine("[LaunchFlow] Launch cancelled by user, terminating elevated injection process");
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch (Exception killEx)
                {
                    log.AppendLine($"[LaunchFlow] Failed to terminate elevated injection process: {killEx.Message}");
                }

                return (false, null);
            }

            log.AppendLine($"[LaunchFlow] Elevated injection process exited (code: {process.ExitCode})");

            if (process.ExitCode == 0)
            {
                return (true, null);
            }

            var failureMessage = ElevatedInjectionResultFile.Consume();
            if (!string.IsNullOrWhiteSpace(failureMessage))
            {
                log.AppendLine($"[LaunchFlow] Injection failure: {failureMessage}");
            }

            return (false, failureMessage);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            log.AppendLine("[LaunchFlow] Elevation cancelled by user");
            return (false, null);
        }
        catch (Exception ex)
        {
            log.AppendLine($"[LaunchFlow] Elevated injection process failed: {ex.Message}");
            return (false, null);
        }
    }
}