using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Contracts.Services;

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

                var arguments = BuildLaunchArguments(config);
                logBuilder.AppendLine($"[启动流程] 启动参数: {arguments}");

                bool useInjection = await GetUseInjectionAsync();
                logBuilder.AppendLine($"[启动流程] 注入模式: {(useInjection ? "启用" : "禁用")}");

                bool gameStarted = false;
                string errorDetail = "";

                if (useInjection)
                {
                    bool hideQuestBanner = await GetHideQuestBannerAsync();
                    bool disableDamageText = await GetDisableDamageTextAsync();
                    bool useTouchScreen = await GetUseTouchScreenAsync();
                    bool disableEventCameraMove = await GetDisableEventCameraMoveAsync();
                    bool removeTeamProgress = await GetRemoveTeamProgressAsync();
                    bool redirectCombineEntry = await GetRedirectCombineEntryAsync();
                    bool resin106 = await GetResin106Async();
                    bool resin201 = await GetResin201Async();
                    bool resin107009 = await GetResin107009Async();
                    bool resin107012 = await GetResin107012Async();
                    bool resin220007 = await GetResin220007Async();

                    logBuilder.AppendLine($"[启动流程] 隐藏任务横幅: {hideQuestBanner}");
                    logBuilder.AppendLine($"[启动流程] 禁用伤害文本: {disableDamageText}");
                    logBuilder.AppendLine($"[启动流程] 触屏模式: {useTouchScreen}");
                    logBuilder.AppendLine($"[启动流程] 禁用事件镜头: {disableEventCameraMove}");
                    logBuilder.AppendLine($"[启动流程] 移除组队进度: {removeTeamProgress}");
                    logBuilder.AppendLine($"[启动流程] 重定向合成: {redirectCombineEntry}");
                    logBuilder.AppendLine($"[启动流程] 树脂106: {resin106}");
                    logBuilder.AppendLine($"[启动流程] 树脂201: {resin201}");
                    logBuilder.AppendLine($"[启动流程] 树脂107009: {resin107009}");
                    logBuilder.AppendLine($"[启动流程] 树脂107012: {resin107012}");
                    logBuilder.AppendLine($"[启动流程] 树脂220007: {resin220007}");

                    _launcherService.UpdateConfig(gameExePath, hideQuestBanner, disableDamageText, useTouchScreen,
                        disableEventCameraMove, removeTeamProgress, redirectCombineEntry,
                        resin106, resin201, resin107009, resin107012, resin220007);
                    logBuilder.AppendLine("[启动流程] 配置已同步到共享内存");

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
                            logBuilder.AppendLine($"[启动流程] ? {errorDetail}");
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
                    await LaunchBetterGIAsync();

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
                result.DetailLog = $"[启动流程] ?? 未处理异常: {ex}\n{ex.StackTrace}";
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
                log.AppendLine($"[普通启动] ? 异常: {ex.Message}");
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

        private async Task LaunchBetterGIAsync()
        {
            try
            {
                var enabled = await _localSettingsService.ReadSettingAsync("IsBetterGIIntegrationEnabled");
                if (enabled != null && Convert.ToBoolean(enabled))
                {
                    string processDir = null;
                    try
                    {
                        processDir = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
                    }
                    catch { /* ignore */ }

                    var candidates = new[]
                    {
                        Path.Combine(AppContext.BaseDirectory, "BetterGI.exe"),
                        Path.Combine(AppContext.BaseDirectory, "Assets", "BetterGI.exe"),
                        Path.Combine(AppContext.BaseDirectory, "BetterGI.lnk"),
                        Path.Combine(AppContext.BaseDirectory, "Assets", "BetterGI.lnk"),
                        processDir == null ? null : Path.Combine(processDir, "BetterGI.exe"),
                        processDir == null ? null : Path.Combine(processDir, "Assets", "BetterGI.exe"),
                        processDir == null ? null : Path.Combine(processDir, "BetterGI.lnk"),
                        processDir == null ? null : Path.Combine(processDir, "Assets", "BetterGI.lnk")
                    };

                    string found = null;
                    foreach (var c in candidates)
                    {
                        if (string.IsNullOrEmpty(c)) continue;
                        if (File.Exists(c))
                        {
                            found = c;
                            break;
                        }
                    }

                    Debug.WriteLine($"[BetterGI] 尝试启动，候选路径: {string.Join(", ", candidates.Where(p => !string.IsNullOrEmpty(p)))}");

                    if (!string.IsNullOrEmpty(found))
                    {
                        Debug.WriteLine($"[BetterGI] 找到: {found}");

                        var startInfo = new ProcessStartInfo
                        {
                            FileName = found,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(found)
                        };

                        Process.Start(startInfo);
                        Debug.WriteLine("[BetterGI] 启动成功");
                    }
                    else
                    {
                        Debug.WriteLine("[BetterGI] 未找到可用的 BetterGI 可执行或快捷方式");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BetterGI] 启动失败: {ex.Message}");
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

        private async Task<bool> GetHideQuestBannerAsync()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("EnableQuestBannerControl", out var prop))
                    {
                        return !prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取HideQuestBanner失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetDisableDamageTextAsync()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("EnableDamageTextControl", out var prop))
                    {
                        return !prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取DisableDamageText失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetUseTouchScreenAsync()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("EnableTouchScreenMode", out var prop))
                    {
                        return prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取UseTouchScreen失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetDisableEventCameraMoveAsync()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("EnableEventCameraMove", out var prop))
                    {
                        return !prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取DisableEventCameraMove失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetRemoveTeamProgressAsync()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("EnableTeamProgress", out var prop))
                    {
                        return !prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取RemoveTeamProgress失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetRedirectCombineEntryAsync()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("EnableRedirectCombineEntry", out var prop))
                    {
                        return prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取RedirectCombineEntry失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetResin106Async()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ResinListItemId000106Allowed", out var prop))
                    {
                        return prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取Resin106失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetResin201Async()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ResinListItemId000201Allowed", out var prop))
                    {
                        return prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取Resin201失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetResin107009Async()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ResinListItemId107009Allowed", out var prop))
                    {
                        return prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取Resin107009失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetResin107012Async()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ResinListItemId107012Allowed", out var prop))
                    {
                        return prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取Resin107012失败: {ex.Message}");
            }
            return false;
        }

        private async Task<bool> GetResin220007Async()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu", "FufuConfig.cfg");
                if (File.Exists(configPath))
                {
                    var json = await File.ReadAllTextAsync(configPath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ResinListItemId220007Allowed", out var prop))
                    {
                        return prop.GetBoolean();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[GameLauncherService] 读取Resin220007失败: {ex.Message}");
            }
            return false;
        }
    }
}