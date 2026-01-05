using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using FufuLauncher.Contracts.Services;
using FufuLauncher.ViewModels;

namespace FufuLauncher.Services
{
    public class LaunchResult
    {
        public bool Success
        {
            get; set;
        }
        public string ErrorMessage { get; set; } = string.Empty;
        public string DetailLog { get; set; } = string.Empty;
    }

    public class GameLauncherService : IGameLauncherService
    {
        private readonly ILocalSettingsService _localSettingsService;
        private readonly IGameConfigService _gameConfigService;
        private readonly ILauncherService _launcherService;
        private readonly ControlPanelModel _controlPanelModel;
        private const string GamePathKey = "GameInstallationPath";
        private const string UseInjectionKey = "UseInjection";
        private const string CustomLaunchParametersKey = "CustomLaunchParameters";
        public const string GenshinHDRConfigKey = "IsGenshinHDRForcedEnabled";
        
        private bool _lastUseInjection;

        public GameLauncherService(
            ILocalSettingsService localSettingsService,
            IGameConfigService gameConfigService,
            ILauncherService launcherService,
            ControlPanelModel controlPanelModel)
        {
            _localSettingsService = localSettingsService;
            _gameConfigService = gameConfigService;
            _launcherService = launcherService;
            _controlPanelModel = controlPanelModel;
        }

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

        public async Task<LaunchResult> LaunchGameAsync()
        {
            var result = new LaunchResult { Success = false, ErrorMessage = "未知错误", DetailLog = "" };
            var logBuilder = new StringBuilder();

            try
            {
                logBuilder.AppendLine("[启动流程] 开始启动游戏");

                var gamePath = GetGamePath();
                logBuilder.AppendLine($"[启动流程] 游戏路径: {gamePath}");

                if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
                {
                    result.ErrorMessage = "游戏路径无效或不存在";
                    logBuilder.AppendLine($"[启动流程] ? 错误: {result.ErrorMessage}");
                    result.DetailLog = logBuilder.ToString();
                    return result;
                }

                var gameExePath = Path.Combine(gamePath, "GenshinImpact.exe");
                if (!File.Exists(gameExePath))
                {
                    gameExePath = Path.Combine(gamePath, "YuanShen.exe");
                    logBuilder.AppendLine($"[启动流程] 尝试备用路径: {gameExePath}");
                }

                if (!File.Exists(gameExePath))
                {
                    result.ErrorMessage = $"游戏主程序不存在\n查找路径:\n- {Path.Combine(gamePath, "GenshinImpact.exe")}\n- {Path.Combine(gamePath, "YuanShen.exe")}";
                    logBuilder.AppendLine($"[启动流程] ? 错误: {result.ErrorMessage}");
                    result.DetailLog = logBuilder.ToString();
                    return result;
                }

                logBuilder.AppendLine($"[启动流程] 找到游戏程序: {gameExePath}");

                var config = await _gameConfigService.LoadGameConfigAsync(gamePath);
                if (config == null)
                {
                    result.ErrorMessage = "无法加载游戏配置文件";
                    logBuilder.AppendLine($"[启动流程] ? 错误: {result.ErrorMessage}");
                    result.DetailLog = logBuilder.ToString();
                    return result;
                }
                
                await ApplyGenshinHDRConfigAsync(logBuilder);

                string arguments = BuildLaunchArguments(config).ToString();
                logBuilder.AppendLine($"[启动流程] 启动参数: {arguments}");

                bool useInjection = await GetUseInjectionAsync();
                logBuilder.AppendLine($"[启动流程] 注入模式: {(useInjection ? "启用" : "禁用")}");

                bool gameStarted = false;

                if (useInjection)
                {
                    int configMask = 0;
                    
                    logBuilder.AppendLine($"[启动流程] 配置掩码: {configMask}");
                    
                    string targetDllPath = null;
                    string defaultDllPath = _launcherService.GetDefaultDllPath();
                    
                    if (!string.IsNullOrEmpty(defaultDllPath) && File.Exists(defaultDllPath))
                    {
                        targetDllPath = defaultDllPath;
                        logBuilder.AppendLine($"[启动流程] 发现默认DLL: {targetDllPath}");
                    }
                    else
                    {
                        try
                        {
                            string pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");
                            if (Directory.Exists(pluginsDir))
                            {
                                logBuilder.AppendLine($"[启动流程] 在扫描插件目录: {pluginsDir}");
                                
                                var pluginDll = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories)
                                    .FirstOrDefault(f => !f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));

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
                        logBuilder.AppendLine($"[启动流程] 准备注入 DLL: {targetDllPath}");
                        gameStarted = await LaunchViaElevatedProcessAsync(gameExePath, targetDllPath, configMask, arguments, logBuilder);
                    }
                    else
                    {
                        logBuilder.AppendLine($"[启动流程] 未找到任何可用的注入DLL (默认路径无效且无插件)，降级为普通启动");
                        gameStarted = StartGameNormally(gameExePath, arguments, gamePath, logBuilder);
                    }
                }
                else
                {
                    gameStarted = StartGameNormally(gameExePath, arguments, gamePath, logBuilder);
                }

                if (gameStarted)
                {
                    logBuilder.AppendLine("[启动流程] 游戏进程已启动");
                    await LaunchAdditionalProgramAsync();
                    await LaunchBetterGIAsync();

                    result.Success = true;
                    result.ErrorMessage = "";
                }
                //else
                //{
                //    result.ErrorMessage = $"游戏启动失败\n\n{errorDetail}";
                //}

                result.DetailLog = logBuilder.ToString();
                Debug.WriteLine(result.DetailLog);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"启动过程中发生严重异常: {ex.Message}";
                result.DetailLog = $"[启动流程] ?? 未处理异常: {ex}\n{ex.StackTrace}";
                Debug.WriteLine(result.DetailLog);
                return result;
            }
        }
        
        private StringBuilder BuildLaunchArguments(GameConfig config)
        {
            var args = new StringBuilder();

            if (config.ServerType.Contains("官服"))
            {
            }
            else if (config.ServerType.Contains("B服"))
            {
            }

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

            return args;
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

        private async Task<bool> LaunchViaElevatedProcessAsync(string gameExePath, string dllPath, int configMask, string arguments, StringBuilder log)
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
                    Arguments = BuildElevatedArgumentString(gameExePath, dllPath, configMask, arguments),
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

                await process.WaitForExitAsync();
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

        private static string BuildElevatedArgumentString(string gameExePath, string dllPath, int configMask, string commandLineArgs)
        {
            return $"--elevated-inject {QuoteArgument(gameExePath)} {QuoteArgument(dllPath)} {configMask} {QuoteArgument(commandLineArgs ?? string.Empty)}";
        }

        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument)) return "\"\"";
            if (!argument.Contains(' ') && !argument.Contains('\t') && !argument.Contains('\n') && !argument.Contains('\v') && !argument.Contains('\"'))
            {
                return argument;
            }

            var sb = new StringBuilder();
            sb.Append('"');

            for (int i = 0; i < argument.Length; i++)
            {
                int backslashes = 0;
                while (i < argument.Length && argument[i] == '\\')
                {
                    backslashes++;
                    i++;
                }

                if (i == argument.Length)
                {
                    sb.Append('\\', backslashes * 2);
                    break;
                }
                else if (argument[i] == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(argument[i]);
                }
            }

            sb.Append('"');
            return sb.ToString();
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

        private async Task LaunchBetterGIAsync()
        {
            try
            {
                await Task.Delay(5000);

                var enabled = await _localSettingsService.ReadSettingAsync("IsBetterGIIntegrationEnabled");
                if (enabled != null && Convert.ToBoolean(enabled))
                {
                    Debug.WriteLine("[BetterGI] 配置已启用，准备通过URL Scheme启动 bettergi://start");

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
                if (enabled == null || !Convert.ToBoolean(enabled) || closeOnExit == null || !Convert.ToBoolean(closeOnExit)) return;

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
}