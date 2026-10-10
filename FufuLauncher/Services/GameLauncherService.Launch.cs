using System.Diagnostics;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.Services;

public partial class GameLauncherService
{
    public async Task<LaunchResult> LaunchGameAsync(CancellationToken cancellationToken = default)
    {
        var result = new LaunchResult
            { Success = false, ErrorMessage = "LaunchErr_UnknownError".GetLocalized(), DetailLog = "" };
        var logBuilder = new StringBuilder();
        string gamePath = null;
        List<string> processNames = null;

        try
        {
            logBuilder.AppendLine("[启动流程] 开始启动游戏");

            gamePath = GetGamePath();
            logBuilder.AppendLine($"[启动流程] 游戏路径: {gamePath}");

            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                result.ErrorMessage = "LaunchErr_InvalidGamePath".GetLocalized();
                logBuilder.AppendLine($"[启动流程] ? 错误: {result.ErrorMessage}");
                result.DetailLog = logBuilder.ToString();
                return result;
            }

            var exeNames = await GameExeManager.GetExeNamesAsync();
            processNames = exeNames.Select(Path.GetFileNameWithoutExtension).ToList();
            var foundExes = exeNames.Where(name => File.Exists(Path.Combine(gamePath, name))).ToList();

            if (foundExes.Count == 0)
            {
                result.ErrorMessage =
                    string.Format("LaunchErr_ExeNotFound".GetLocalized(), string.Join("\n", exeNames));
                logBuilder.AppendLine($"[启动流程] 错误: {result.ErrorMessage}");
                result.DetailLog = logBuilder.ToString();
                return result;
            }

            if (foundExes.Count > 1 && exeNames.Count > 1)
            {
                result.ErrorMessage = "LaunchErr_MultipleExeFound".GetLocalized();
                logBuilder.AppendLine($"[启动流程] 错误: {result.ErrorMessage}");
                result.DetailLog = logBuilder.ToString();
                return result;
            }

            var gameExePath = Path.Combine(gamePath, foundExes.First());
            logBuilder.AppendLine($"[启动流程] 找到游戏程序: {gameExePath}");

            var config = await _gameConfigService.LoadGameConfigAsync(gamePath);
            if (config == null)
            {
                result.ErrorMessage = "LaunchErr_CannotLoadConfig".GetLocalized();
                logBuilder.AppendLine($"[启动流程] ? 错误: {result.ErrorMessage}");
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
                logBuilder.AppendLine("[启动流程] 已启用米游社/HoyoLAB账户启动");
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
                            var configContent = await File.ReadAllTextAsync(configIniPath);
                            isBilibili = configContent.Contains("channel=14") ||
                                         configContent.Contains("cps=bilibili");
                        }
                    }
                    catch
                    {
                    }

                    bool isGameOversea =
                        _gameServerConfigurationService.TryDetectCurrentScheme(gamePath)?.IsOversea == true;

                    if (isBilibili)
                    {
                        logBuilder.AppendLine("[启动流程] 跳过");
                    }
                    else if (isOversea && !isGameOversea)
                    {
                        logBuilder.AppendLine("[启动流程] 国际服跳过AuthTicket");
                        WeakReferenceMessenger.Default.Send(new NotificationMessage(
                            "HoyolabAccount_ServerMismatch_Title".GetLocalized(),
                            "HoyolabAccount_ServerMismatch_OverseaMsg".GetLocalized(),
                            NotificationType.Warning,
                            5000));
                    }
                    else if (!isOversea && isGameOversea)
                    {
                        logBuilder.AppendLine("[启动流程] 国服账户不能用于国际服游戏，跳过AuthTicket");
                        WeakReferenceMessenger.Default.Send(new NotificationMessage(
                            "HoyolabAccount_ServerMismatch_Title".GetLocalized(),
                            "HoyolabAccount_ServerMismatch_CnMsg".GetLocalized(),
                            NotificationType.Warning,
                            5000));
                    }
                    else
                    {
                        _registrySnapshot.TakeSnapshot(isOversea);
                        logBuilder.AppendLine("[启动流程] 已保存注册表快照");

                        var ticketResult = await _authTicketService.CreateAuthTicketAsync(activeAccountId);
                        if (ticketResult.Success)
                        {
                            authTicket = ticketResult.Ticket;
                            logBuilder.AppendLine($"[启动流程] 成功获取AuthTicket (长度: {authTicket.Length})");
                        }
                        else
                        {
                            logBuilder.AppendLine($"[启动流程] 获取AuthTicket失败: {ticketResult.ErrorMessage}");
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
                    logBuilder.AppendLine("[启动流程] 未选择米游社/HoyoLAB 账户，跳过 AuthTicket");
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return BuildCancelledResult(logBuilder);
            }

            var arguments = BuildLaunchArguments(config, authTicket).ToString();
            logBuilder.AppendLine($"[启动流程] 启动参数: {arguments}");

            _constraintService.TriggerBackgroundRefresh();

            if (_constraintService.IsRestricted)
            {
                LightweightPluginService.DisableMainPluginIfPresent();
                logBuilder.AppendLine("[启动流程] 策略限制生效，已静默禁用主插件");
            }

            if (_lightweightPluginService.EnforceModeAtLaunch(_lightweightPluginService.IsLightweightMode))
            {
                logBuilder.AppendLine("[启动流程] 已按当前模式同步主插件/轻量插件启用状态");
            }

            var useInjection = await GetUseInjectionAsync();
            logBuilder.AppendLine($"[启动流程] 注入模式: {(useInjection ? "启用" : "禁用")}");

            if (useInjection)
            {
                var conflictResult = BlockInjectionForPluginDllConflicts(logBuilder);
                if (conflictResult != null)
                {
                    Debug.WriteLine(conflictResult.DetailLog);
                    return conflictResult;
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return BuildCancelledResult(logBuilder);
            }

            logBuilder.AppendLine("[启动流程] 正在启动附加程序...");
            await LaunchAdditionalProgramAsync();

            var gameStarted = false;

            if (useInjection)
            {
                var injectionModuleObj = await _localSettingsService.ReadSettingAsync("InjectionModule");
                var injectionModule = injectionModuleObj?.ToString() ?? "DLL";
                logBuilder.AppendLine($"[启动流程] 注入模块: {injectionModule}");

                if (injectionModule == "EXE")
                {
                    gameStarted = await LaunchViaExeModuleAsync(gameExePath, arguments, logBuilder);
                }
                else
                {
                    int configMask = 0;

                    logBuilder.AppendLine($"[启动流程] 配置掩码: {configMask}");

                    string targetDllPath = null;
                    var defaultDllPath = _launcherService.GetDefaultDllPath();

                    if (_lightweightPluginService.IsLightweightMode &&
                        File.Exists(LightweightPluginService.LitePluginDllPath))
                    {
                        targetDllPath = LightweightPluginService.LitePluginDllPath;
                        logBuilder.AppendLine($"[启动流程] 轻量模式已启用，使用轻量插件DLL: {targetDllPath}");
                    }
                    else if (!string.IsNullOrEmpty(defaultDllPath) && File.Exists(defaultDllPath))
                    {
                        targetDllPath = defaultDllPath;
                        logBuilder.AppendLine($"[启动流程] 发现默认DLL: {targetDllPath}");
                    }
                    else
                    {
                        try
                        {
                            var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
                            if (Directory.Exists(pluginsDir))
                            {
                                logBuilder.AppendLine($"[启动流程] 在扫描插件目录: {pluginsDir}");

                                var pluginDll = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories)
                                    .FirstOrDefault(f =>
                                        !f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));

                                if (!string.IsNullOrEmpty(pluginDll))
                                {
                                    targetDllPath = pluginDll;
                                    logBuilder.AppendLine($"[启动流程] 扫描到可用插件DLL，将使用: {targetDllPath}");
                                }
                                else
                                {
                                    logBuilder.AppendLine($"[启动流程] 插件目录中未发现有效DLL");
                                }
                            }
                            else
                            {
                                logBuilder.AppendLine($"[启动流程] 插件目录不存在");
                            }
                        }
                        catch (Exception ex)
                        {
                            logBuilder.AppendLine($"[启动流程] 扫描插件目录时发生异常: {ex.Message}");
                        }
                    }

                    if (!string.IsNullOrEmpty(targetDllPath) && File.Exists(targetDllPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(targetDllPath);
                            if (fileInfo.Length < 10 * 1024)
                            {
                                logBuilder.AppendLine($"[启动流程] ! 警告: 插件文件({fileInfo.Length} bytes)大小异常，可能已经损坏");
                                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                                    "LaunchErr_PluginDamagedTitle".GetLocalized(),
                                    "LaunchErr_PluginDamagedMsg".GetLocalized(),
                                    NotificationType.Warning,
                                    6000));
                            }
                        }
                        catch (Exception ex)
                        {
                            logBuilder.AppendLine($"[启动流程] 检查插件大小失败: {ex.Message}");
                        }

                        try
                        {
                            var trustDecision = _modTrustGate.EvaluateForLoading(targetDllPath);
                            logBuilder.AppendLine(trustDecision.Mode == CodeSigning.ModTrustEnforcement.Off
                                ? "[启动流程] 插件签名校验已关闭，跳过校验"
                                : $"[启动流程] 签名信任判定：{trustDecision.Result.Status} - {trustDecision.Reason}");

                            if (!trustDecision.Allowed)
                            {
                                logBuilder.AppendLine("[启动流程] 严格信任模式已拦截该 DLL，取消注入");
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
                                        logBuilder.AppendLine($"[启动流程] 恢复注册表快照失败: {ex.Message}");
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
                                Debug.WriteLine(blockedResult.DetailLog);
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
                            logBuilder.AppendLine($"[启动流程] 签名信任校验异常（不影响启动）: {ex.Message}");
                        }

                        logBuilder.AppendLine($"[启动流程] 准备注入 DLL: {targetDllPath}");
                        gameStarted = await LaunchViaElevatedProcessAsync(gameExePath, targetDllPath, configMask,
                            arguments, logBuilder, cancellationToken);
                    }
                    else
                    {
                        logBuilder.AppendLine($"[启动流程] 未找到任何可用的注入DLL (默认路径无效且无插件)，降级为普通启动");
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
                    logBuilder.AppendLine("[启动流程] 用户取消启动，清理已启动的游戏进程...");
                    KillGameProcesses(processNames, gamePath);
                    return BuildCancelledResult(logBuilder);
                }

                logBuilder.AppendLine("[启动流程] 游戏进程已启动，正在捕获目标PID...");
                int gamePid = await WaitGenshinStartAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    logBuilder.AppendLine("[启动流程] 用户取消启动，清理已启动的游戏进程...");
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
                                Debug.WriteLine("[启动流程] 游戏退出，已恢复注册表快照");
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
            Debug.WriteLine(result.DetailLog);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (processNames != null && !string.IsNullOrEmpty(gamePath))
            {
                logBuilder.AppendLine("[启动流程] 用户取消启动，清理已启动的游戏进程...");
                KillGameProcesses(processNames, gamePath);
            }

            return BuildCancelledResult(logBuilder);
        }
        catch (Exception ex)
        {
            result.ErrorMessage = string.Format("LaunchErr_FatalError".GetLocalized(), ex.Message);
            result.DetailLog = $"[启动流程] ?? 未处理异常: {ex}\n{ex.StackTrace}";
            Debug.WriteLine(result.DetailLog);
            return result;
        }
    }

    private LaunchResult BuildCancelledResult(StringBuilder logBuilder)
    {
        logBuilder.AppendLine("[启动流程] 用户取消启动，启动流程已终止");

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

        var result = new LaunchResult
        {
            Success = false,
            Cancelled = true,
            ErrorMessage = string.Empty,
            DetailLog = logBuilder.ToString()
        };
        Debug.WriteLine(result.DetailLog);
        return result;
    }
}
