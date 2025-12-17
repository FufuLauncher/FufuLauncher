using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using FufuLauncher.Core.Contracts.Services;
using FufuLauncher.Contracts.Services;

namespace FufuLauncher.Services
{

    public class LaunchResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string DetailLog { get; set; } = string.Empty;
    }

    public class GameLauncherService : IGameLauncherService
    {
        private readonly ILocalSettingsService _localSettingsService;
        private readonly IGameConfigService _gameConfigService;
        private readonly ILauncherService _launcherService;
        private const string GamePathKey = "GameInstallationPath";
        private const string UseInjectionKey = "UseInjection";
        private const string CustomLaunchParametersKey = "CustomLaunchParameters";
        private bool _lastUseInjection;

        public GameLauncherService(
            ILocalSettingsService localSettingsService, 
            IGameConfigService gameConfigService,
            ILauncherService launcherService)
        {
            _localSettingsService = localSettingsService;
            _gameConfigService = gameConfigService;
            _launcherService = launcherService;
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
            Debug.WriteLine($"[启动服务] 包含非ASCII字符: {path.Any(c => c > 127)}");
            Debug.WriteLine($"[启动服务] Unicode转义: {string.Join(" ", path.Select(c => ((int)c).ToString("X4")))}");
    
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

        public async Task<LaunchResult> LaunchGameAsync()
        {
            var result = new LaunchResult { Success = false, ErrorMessage = "未知错误", DetailLog = "" };
            var logBuilder = new System.Text.StringBuilder();
            
            try
            {
                logBuilder.AppendLine("[启动流程] 开始启动游戏");
                
                var gamePath = GetGamePath();
                logBuilder.AppendLine($"[启动流程] 游戏路径: {gamePath}");
                
                if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
                {
                    result.ErrorMessage = "游戏路径无效或不存在";
                    logBuilder.AppendLine($"[启动流程] ❌ 错误: {result.ErrorMessage}");
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
                    logBuilder.AppendLine($"[启动流程] ❌ 错误: {result.ErrorMessage}");
                    result.DetailLog = logBuilder.ToString();
                    return result;
                }

                logBuilder.AppendLine($"[启动流程] 找到游戏程序: {gameExePath}");

                var config = await _gameConfigService.LoadGameConfigAsync(gamePath);
                if (config == null)
                {
                    result.ErrorMessage = "无法加载游戏配置文件";
                    logBuilder.AppendLine($"[启动流程] ❌ 错误: {result.ErrorMessage}");
                    result.DetailLog = logBuilder.ToString();
                    return result;
                }

                var arguments = BuildLaunchArguments(config);
                logBuilder.AppendLine($"[启动流程] 启动参数: {arguments}");
                
                bool useInjection = await GetUseInjectionAsync();
                logBuilder.AppendLine($"[启动流程] 注入模式: {(useInjection ? "启用" : "禁用")}");
                
                bool gameStarted = false;
                string errorDetail = "";

                if (useInjection)
                {
                    var dllPath = _launcherService.GetDefaultDllPath();
                    logBuilder.AppendLine($"[启动流程] 注入DLL路径: {dllPath}");
                    
                    if (File.Exists(dllPath))
                    {
                        int injectResult = _launcherService.LaunchGameAndInject(gameExePath, dllPath, arguments, out string injectError, out int pid);
                        if (injectResult == 0)
                        {
                            gameStarted = true;
                            logBuilder.AppendLine($"[启动流程] 注入成功，PID: {pid}");
                        }
                        else
                        {
                            errorDetail = $"注入失败: {injectError} (错误码: {injectResult})";
                            logBuilder.AppendLine($"[启动流程] ❌ {errorDetail}");
                        }
                    }
                    else
                    {
                        logBuilder.AppendLine($"[启动流程] DLL不存在，改用普通启动");
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
                    
                    result.Success = true;
                    result.ErrorMessage = "";
                }
                else
                {
                    result.ErrorMessage = $"游戏启动失败\n\n{errorDetail}";
                }
                
                result.DetailLog = logBuilder.ToString();
                Debug.WriteLine(result.DetailLog);
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"启动过程中发生严重异常: {ex.Message}";
                result.DetailLog = $"[启动流程] 💥 未处理异常: {ex}\n{ex.StackTrace}";
                Debug.WriteLine(result.DetailLog);
                return result;
            }
        }
    
        private bool StartGameNormally(string exePath, string args, string workingDir, System.Text.StringBuilder log)
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
                log.AppendLine($"[普通启动] ❌ 异常: {ex.Message}");
                return false;
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

        private string BuildLaunchArguments(GameConfig config)
        {
            var args = new System.Text.StringBuilder();

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

            return args.ToString().Trim();
        }
    }
}