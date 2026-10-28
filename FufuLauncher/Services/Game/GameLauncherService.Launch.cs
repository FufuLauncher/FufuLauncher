using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    private const string LaunchLogModule = "GameLauncherService.Launch";

    public async Task<LaunchResult> LaunchGameAsync(CancellationToken cancellationToken = default)
    {
        var result = new LaunchResult
            { Success = false, ErrorMessage = "LaunchErr_UnknownError".GetLocalized(), DetailLog = "" };
        var logBuilder = new LaunchFlowLog();
        string gamePath = null;
        List<string> processNames = null;

        try
        {
            logBuilder.AppendLine("[LaunchFlow] Starting game");

            gamePath = GetGamePath();
            logBuilder.AppendLine($"[LaunchFlow] Game path: '{gamePath}'");

            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                result.ErrorMessage = "LaunchErr_InvalidGamePath".GetLocalized();
                logBuilder.AppendLine($"[LaunchFlow] Error: invalid game path '{gamePath}'");
                result.DetailLog = logBuilder.ToString();
                return result;
            }

            DisableIncompatiblePlugins(logBuilder);

            var exeNames = await GameExeManager.GetExeNamesAsync();
            processNames = exeNames.Select(Path.GetFileNameWithoutExtension).ToList();
            var foundExes = exeNames.Where(name => File.Exists(Path.Combine(gamePath, name))).ToList();

            if (foundExes.Count == 0)
            {
                result.ErrorMessage =
                    string.Format("LaunchErr_ExeNotFound".GetLocalized(), string.Join("\n", exeNames));
                logBuilder.AppendLine($"[LaunchFlow] Error: no game executable found in '{gamePath}' (expected: {string.Join(", ", exeNames)})");
                result.DetailLog = logBuilder.ToString();
                return result;
            }

            if (foundExes.Count > 1 && exeNames.Count > 1)
            {
                result.ErrorMessage = "LaunchErr_MultipleExeFound".GetLocalized();
                logBuilder.AppendLine($"[LaunchFlow] Error: multiple game executables found: {string.Join(", ", foundExes)}");
                result.DetailLog = logBuilder.ToString();
                return result;
            }

            var gameExePath = Path.Combine(gamePath, foundExes.First());
            logBuilder.AppendLine($"[LaunchFlow] Game executable: '{gameExePath}'");

            var config = await _gameConfigService.LoadGameConfigAsync(gamePath);
            if (config == null)
            {
                result.ErrorMessage = "LaunchErr_CannotLoadConfig".GetLocalized();
                logBuilder.AppendLine("[LaunchFlow] Error: failed to load game config");
                result.DetailLog = logBuilder.ToString();
                return result;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return BuildCancelledResult(logBuilder);
            }

            await ApplyGenshinHDRConfigAsync(logBuilder);

            string? authTicket = null;
            bool usingHoyolabAccount = await GetUsingHoyolabAccountAsync();
            if (usingHoyolabAccount)
            {
                logBuilder.AppendLine("[LaunchFlow] Miyoushe/HoyoLAB account launch enabled");
                var activeAccountId = _accountManager.ActiveAccountId;
                if (!string.IsNullOrEmpty(activeAccountId))
                {
                    bool isOversea = activeAccountId.StartsWith("os", StringComparison.OrdinalIgnoreCase);

                    bool isBilibili = false;
                    try
                    {
                        var configIniPath = Path.Combine(gamePath, "config.ini");
                        if (File.Exists(configIniPath))
                        {
                            isBilibili = _gameServerConfigurationService.TryDetectCurrentScheme(gamePath)?.Channel
                                         == Models.GameServer.ChannelType.Bili;
                        }
                    }
                    catch
                    {
                    }

                    bool isGameOversea =
                        _gameServerConfigurationService.TryDetectCurrentScheme(gamePath)?.IsOversea == true;

                    if (isBilibili)
                    {
                        logBuilder.AppendLine("[LaunchFlow] Bilibili channel, skipping AuthTicket");
                    }
                    else if (isOversea && !isGameOversea)
                    {
                        logBuilder.AppendLine("[LaunchFlow] Overseas account with CN game, skipping AuthTicket");
                        WeakReferenceMessenger.Default.Send(new NotificationMessage(
                            "HoyolabAccount_ServerMismatch_Title".GetLocalized(),
                            "HoyolabAccount_ServerMismatch_OverseaMsg".GetLocalized(),
                            NotificationType.Warning,
                            5000));
                    }
                    else if (!isOversea && isGameOversea)
                    {
                        logBuilder.AppendLine("[LaunchFlow] CN account with overseas game, skipping AuthTicket");
                        WeakReferenceMessenger.Default.Send(new NotificationMessage(
                            "HoyolabAccount_ServerMismatch_Title".GetLocalized(),
                            "HoyolabAccount_ServerMismatch_CnMsg".GetLocalized(),
                            NotificationType.Warning,
                            5000));
                    }
                    else
                    {
                        _registrySnapshot.TakeSnapshot(isOversea);
                        logBuilder.AppendLine("[LaunchFlow] Registry snapshot saved");

                        var ticketResult = await _authTicketService.CreateAuthTicketAsync(activeAccountId);
                        if (ticketResult.Success)
                        {
                            authTicket = ticketResult.Ticket;
                            logBuilder.AppendLine($"[LaunchFlow] AuthTicket acquired (length: {authTicket.Length})");
                        }
                        else
                        {
                            logBuilder.AppendLine($"[LaunchFlow] AuthTicket creation failed: {ticketResult.ErrorMessage}");
                            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                                "HoyolabAccount_EnableFailed_Title".GetLocalized(),
                                "HoyolabAccount_EnableFailed_Message".GetLocalized(),
                                NotificationType.Warning,
                                5000));
                        }
                    }
                }
                else
                {
                    logBuilder.AppendLine("[LaunchFlow] No Miyoushe/HoyoLAB account selected, skipping AuthTicket");
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return BuildCancelledResult(logBuilder);
            }

            var arguments = BuildLaunchArguments(config, authTicket).ToString();
            logBuilder.AppendLine($"[LaunchFlow] Launch arguments: {arguments}");

            _constraintService.TriggerBackgroundRefresh();

            if (_constraintService.IsRestricted)
            {
                LightweightPluginService.DisableMainPluginIfPresent();
                logBuilder.AppendLine("[LaunchFlow] Policy restriction active, main plugin disabled");
            }

            if (_lightweightPluginService.EnforceModeAtLaunch(_lightweightPluginService.IsLightweightMode))
            {
                logBuilder.AppendLine("[LaunchFlow] Main/lightweight plugin state synchronized for current mode");
            }

            var useInjection = await GetUseInjectionAsync();
            logBuilder.AppendLine($"[LaunchFlow] Injection mode: {(useInjection ? "enabled" : "disabled")}");

            if (useInjection)
            {
                var conflictResult = BlockInjectionForPluginDllConflicts(logBuilder);
                if (conflictResult != null)
                {
                    return conflictResult;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return BuildCancelledResult(logBuilder);
            }

            logBuilder.AppendLine("[LaunchFlow] Starting additional program");
            await LaunchAdditionalProgramAsync();

            var gameStarted = false;

            if (useInjection)
            {
                var injectionModuleObj = await _localSettingsService.ReadSettingAsync("InjectionModule");
                var injectionModule = injectionModuleObj?.ToString() ?? "DLL";
                logBuilder.AppendLine($"[LaunchFlow] Injection module: {injectionModule}");

                if (injectionModule == "EXE")
                {
                    gameStarted = await LaunchViaExeModuleAsync(gameExePath, arguments, logBuilder);
                }
                else
                {
                    int configMask = 0;

                    logBuilder.AppendLine($"[LaunchFlow] Config mask: {configMask}");

                    string targetDllPath = null;
                    var defaultDllPath = _launcherService.GetDefaultDllPath();

                    if (_lightweightPluginService.IsLightweightMode &&
                        File.Exists(LightweightPluginService.LitePluginDllPath))
                    {
                        targetDllPath = LightweightPluginService.LitePluginDllPath;
                        logBuilder.AppendLine($"[LaunchFlow] Lightweight mode enabled, using lightweight plugin DLL: '{targetDllPath}'");
                    }
                    else if (!string.IsNullOrEmpty(defaultDllPath) && File.Exists(defaultDllPath))
                    {
                        targetDllPath = defaultDllPath;
                        logBuilder.AppendLine($"[LaunchFlow] Using default plugin DLL: '{targetDllPath}'");
                    }
                    else
                    {
                        try
                        {
                            var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
                            if (Directory.Exists(pluginsDir))
                            {
                                logBuilder.AppendLine($"[LaunchFlow] Scanning plugin directory: '{pluginsDir}'");

                                var pluginDll = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories)
                                    .FirstOrDefault(f =>
                                        !f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));

                                if (!string.IsNullOrEmpty(pluginDll))
                                {
                                    targetDllPath = pluginDll;
                                    logBuilder.AppendLine($"[LaunchFlow] Plugin DLL found: '{targetDllPath}'");
                                }
                                else
                                {
                                    logBuilder.AppendLine("[LaunchFlow] No valid plugin DLL found in plugin directory");
                                }
                            }
                            else
                            {
                                logBuilder.AppendLine($"[LaunchFlow] Plugin directory does not exist: '{pluginsDir}'");
                            }
                        }
                        catch (Exception ex)
                        {
                            logBuilder.AppendLine($"[LaunchFlow] Plugin directory scan failed: {ex.Message}");
                        }
                    }

                    if (!string.IsNullOrEmpty(targetDllPath) && File.Exists(targetDllPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(targetDllPath);
                            if (fileInfo.Length < 10 * 1024)
                            {
                                logBuilder.AppendLine($"[LaunchFlow] Warning: plugin DLL may be corrupted (size: {fileInfo.Length} bytes)");
                                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                                    "LaunchErr_PluginDamagedTitle".GetLocalized(),
                                    "LaunchErr_PluginDamagedMsg".GetLocalized(),
                                    NotificationType.Warning,
                                    6000));
                            }
                        }
                        catch (Exception ex)
                        {
                            logBuilder.AppendLine($"[LaunchFlow] Plugin DLL size check failed: {ex.Message}");
                        }

                        try
                        {
                            var trustDecision = _modTrustGate.EvaluateForLoading(targetDllPath);
                            logBuilder.AppendLine(trustDecision.Mode == CodeSigning.ModTrustEnforcement.Off
                                ? "[LaunchFlow] Plugin signature verification disabled, skipping trust check"
                                : $"[LaunchFlow] Signature trust decision: {trustDecision.Result.Status} - {trustDecision.Reason}");

                            if (!trustDecision.Allowed)
                            {
                                logBuilder.AppendLine("[LaunchFlow] Strict trust mode blocked the DLL, injection cancelled");
                                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                                    "ModTrust_BlockedTitle".GetLocalized(),
                                    string.Format("ModTrust_BlockedMsg".GetLocalized(), trustDecision.Reason),
                                    NotificationType.Warning,
                                    8000));

                                if (_registrySnapshot.HasSnapshot)
                                {
                                    try
                                    {
                                        _registrySnapshot.RestoreSnapshot();
                                    }
                                    catch (Exception ex)
                                    {
                                        logBuilder.AppendLine($"[LaunchFlow] Registry snapshot restore failed: {ex.Message}");
                                    }
                                }

                                var blockedMessage = string.Format("ModTrust_BlockedMsg".GetLocalized(),
                                    trustDecision.Reason);
                                var blockedResult = new LaunchResult
                                {
                                    Success = false,
                                    Cancelled = false,
                                    ErrorMessage = blockedMessage,
                                    DetailLog = logBuilder.ToString()
                                };
                                return blockedResult;
                            }

                            if (trustDecision.ShouldNotify)
                            {
                                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                                    "ModTrust_WarnTitle".GetLocalized(),
                                    trustDecision.Reason,
                                    NotificationType.Warning,
                                    6000));
                            }
                        }
                        catch (Exception ex)
                        {
                            logBuilder.AppendLine($"[LaunchFlow] Signature trust check failed (launch continues): {ex.Message}");
                        }

                        logBuilder.AppendLine($"[LaunchFlow] Preparing DLL injection: '{targetDllPath}'");
                        var elevatedResult = await LaunchViaElevatedProcessAsync(gameExePath, targetDllPath,
                            configMask, arguments, logBuilder, cancellationToken);
                        gameStarted = elevatedResult.Started;
                        if (!gameStarted && !string.IsNullOrWhiteSpace(elevatedResult.Failure))
                        {
                            result.ErrorMessage = elevatedResult.Failure!;
                        }
                    }
                    else
                    {
                        logBuilder.AppendLine("[LaunchFlow] No usable injection DLL (default path invalid, no plugin), falling back to normal launch");
                        gameStarted = StartGameNormally(gameExePath, arguments, gamePath, logBuilder);
                    }
                }
            }
            else
            {
                gameStarted = StartGameNormally(gameExePath, arguments, gamePath, logBuilder);
            }

            if (gameStarted)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    logBuilder.AppendLine("[LaunchFlow] Launch cancelled by user, cleaning up game processes");
                    KillGameProcesses(processNames, gamePath);
                    return BuildCancelledResult(logBuilder);
                }

                logBuilder.AppendLine("[LaunchFlow] Game process started, waiting for target PID");
                int gamePid = await WaitGenshinStartAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    logBuilder.AppendLine("[LaunchFlow] Launch cancelled by user, cleaning up game processes");
                    KillGameProcesses(processNames, gamePath);
                    return BuildCancelledResult(logBuilder);
                }

                if (gamePid > 0)
                {
                    _ = LaunchBetterGIAsync(cancellationToken);
                    await CheckAndLaunchFpsOverlayAsync(logBuilder, gamePid);
                    await CheckAndLaunchScreenshotServiceAsync(logBuilder, gamePid);

                    if (_registrySnapshot.HasSnapshot)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var proc = Process.GetProcessById(gamePid);
                                await proc.WaitForExitAsync();
                            }
                            catch
                            {
                            }
                            finally
                            {
                                _registrySnapshot.RestoreSnapshot();
                                DebugLog.Info("GameLauncherService.Launch",
                                    "[LaunchFlow] Game exited, registry snapshot restored");
                            }
                        });
                    }

                    result.Success = true;
                    result.ErrorMessage = "";
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "LaunchErr_LaunchTimeout".GetLocalized();
                }
            }

            result.DetailLog = logBuilder.ToString();
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (processNames != null && !string.IsNullOrEmpty(gamePath))
            {
                logBuilder.AppendLine("[LaunchFlow] Launch cancelled by user, cleaning up game processes");
                KillGameProcesses(processNames, gamePath);
            }

            return BuildCancelledResult(logBuilder);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = string.Format("LaunchErr_FatalError".GetLocalized(), ex.Message);
            result.DetailLog = $"[LaunchFlow] Unhandled exception: {ex}";
            DebugLog.Error(LaunchLogModule, "[LaunchFlow] Unhandled exception", ex);
            return result;
        }
    }

    private LaunchResult BuildCancelledResult(LaunchFlowLog logBuilder)
    {
        logBuilder.AppendLine("[LaunchFlow] Launch cancelled by user, launch flow terminated");

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

        var result = new LaunchResult
        {
            Success = false,
            Cancelled = true,
            ErrorMessage = string.Empty,
            DetailLog = logBuilder.ToString()
        };
        return result;
    }

    private sealed class LaunchFlowLog
    {
        private readonly StringBuilder _detail = new();

        public void AppendLine(string line)
        {
            _detail.AppendLine(line);
            DebugLog.Info(LaunchLogModule, line);
        }

        public override string ToString() => _detail.ToString();
    }
}