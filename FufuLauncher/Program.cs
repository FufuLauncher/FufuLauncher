/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Runtime.InteropServices;
using FufuLauncher.Services;
using FufuLauncher.Services.CodeSigning;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Text.Json;
using FufuLauncher.Helpers;
using Sentry;

namespace FufuLauncher
{
    public static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length >= 2 &&
                string.Equals(args[0], "--backpack-elevated-inject", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(Services.Backpack.GameLaunchService.RunElevatedInjection(args[1]));
                return;
            }

            if (args.Length >= 2 && string.Equals(args[0], "--yae-inject", StringComparison.OrdinalIgnoreCase))
            {
                Environment.Exit(Services.Yae.YaeAchievementReader.RunElevatedInjection(args[1]));
                return;
            }

            if (TrustCertCli.IsTrustCertCommand(args))
            {
                Environment.Exit(TrustCertCli.Run(args));
                return;
            }

            if (TrustCertCli.IsTrustDiagCommand(args))
            {
                Environment.Exit(TrustCertCli.RunDiagnostics(args));
                return;
            }

            SentrySdk.Init(options =>
            {
                options.Dsn =
                    "https://9c8e89f029c240e3dba227979a26759a@o4511497397272576.ingest.de.sentry.io/4511497409265745";
                options.Debug = false;
                options.AutoSessionTracking = true;
                options.TracesSampleRate = 1.0;
                options.ProfilesSampleRate = 1.0;

                var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                options.Release = $"FufuLauncher@{version}";
                options.Environment = "Production";

                options.AddIntegration(new ProfilingIntegration(
                    TimeSpan.FromMilliseconds(500)
                ));
            });

            if (args.Length > 0 && string.Equals(args[0], "--elevated-inject", StringComparison.OrdinalIgnoreCase))
            {
                RunElevatedInjection(args);
                return;
            }

            var key = "FufuLauncher";

            if (args.Length > 0 && string.Equals(args[0], "restart", StringComparison.OrdinalIgnoreCase))
            {
                const int maxRetries = 50;
                const int retryDelayMs = 100;
                for (int i = 0; i < maxRetries; i++)
                {
                    var instance = AppInstance.FindOrRegisterForKey(key);
                    if (instance.IsCurrent)
                    {
                        goto startApp;
                    }

                    instance.UnregisterKey();
                    Thread.Sleep(retryDelayMs);
                }
            }

            var mainInstance = AppInstance.FindOrRegisterForKey(key);

            if (!mainInstance.IsCurrent)
            {
                var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                var task = mainInstance.RedirectActivationToAsync(activationArgs).AsTask();
                task.Wait();
                return;
            }

            startApp:
            Application.Start((p) =>
            {
                var context = new DispatcherQueueSynchronizationContext(
                    DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
        }

        private static void RunElevatedInjection(string[] args)
        {
            var exitCode = 1;
            try
            {
                // --elevated-inject 快捷方式直启不会经过 App() 构造，需要先加载自定义数据目录
                // （paths.json），否则预设读取会落到默认 %LOCALAPPDATA% 目录，
                // --preset 指定的配置不会生效。
                AppPaths.EnsureDirectories();

                if (args.Length < 2)
                {
                    return;
                }

                var gameExePath = args[1];
                var launcher = new LauncherService();

                string dllPath;
                string commandLineArgs;
                var separatorIndex = Array.IndexOf(args, "--", 2);
                if (separatorIndex == -1 &&
                    TryParseLegacyElevatedInjection(args, out var legacyDllPath, out var legacyCommandLineArgs))
                {
                    dllPath = string.IsNullOrEmpty(legacyDllPath) ? ResolveInjectDllPath(launcher) : legacyDllPath;
                    commandLineArgs = legacyCommandLineArgs;
                }
                else
                {
                    if (separatorIndex == -1)
                    {
                        return;
                    }

                    foreach (var quarantined in PluginInjectionGuard.QuarantineRootStrayFiles())
                    {
                        Debug.WriteLine($"[Program] 已重命名插件根目录残留文件: {quarantined}");
                    }

                    var conflicts = PluginInjectionGuard.FindDuplicateDllNames(PluginConflictSettings.Read());
                    if (conflicts.Count > 0)
                    {
                        MessageBox(IntPtr.Zero,
                            PluginInjectionGuard.BuildConflictReport(conflicts),
                            "PluginDllConflict_Title".GetLocalized(), 0x30);
                        exitCode = 4;
                        return;
                    }

                    dllPath = ResolveInjectDllPath(launcher);

                    // Without an explicit preset, keep the config.ini prepared by the current in-app preset.
                    for (var i = 2; i < separatorIndex; i++)
                    {
                        if (string.Equals(args[i], "--preset", StringComparison.OrdinalIgnoreCase))
                        {
                            if (i + 1 < separatorIndex)
                            {
                                ApplyPreset(args[++i], Path.GetDirectoryName(dllPath) ?? string.Empty);
                            }
                        }
                    }

                    commandLineArgs = string.Join(" ", args
                        .Skip(separatorIndex + 1)
                        .Select(argument => GameLauncherService.QuoteArgument(argument)));
                }

                try
                {
                    var trustGate = new ModTrustGate(new CodeSigningTrustService());
                    var decision = trustGate.EvaluateForLoading(dllPath);
                    if (!decision.Allowed)
                    {
                        MessageBox(IntPtr.Zero,
                            string.Format("ModTrust_BlockedMsg".GetLocalized(), decision.Reason),
                            "ModTrust_BlockedTitle".GetLocalized(), 0x30);
                        exitCode = 3;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Program] 注入前信任校验异常（继续注入）：{ex.Message}");
                }

                var result = launcher.LaunchGameAndInject(gameExePath, dllPath, commandLineArgs, out var errorMessage,
                    out var pid);
                if (result != 0)
                {
                    MessageBox(IntPtr.Zero,
                        string.Format("Program_InjectionFailed".GetLocalized(), errorMessage, result),
                        "Program_ErrorTitle".GetLocalized(), 0x10);
                }

                exitCode = result == 0 ? 0 : 1;
            }
            catch (Exception ex)
            {
                MessageBox(IntPtr.Zero, string.Format("Program_InjectionException".GetLocalized(), ex.Message),
                    "Program_ErrorTitle".GetLocalized(), 0x10);
            }
            finally
            {
                Environment.Exit(exitCode);
            }
        }

        private static bool TryParseLegacyElevatedInjection(string[] args, out string dllPath,
            out string commandLineArgs)
        {
            dllPath = string.Empty;
            commandLineArgs = string.Empty;

            if (args.Length != 5 ||
                !int.TryParse(args[3], out _) ||
                (!string.IsNullOrEmpty(args[2]) &&
                 !string.Equals(Path.GetExtension(args[2]), ".dll", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            dllPath = args[2];
            commandLineArgs = args[4];
            return true;
        }

        private static string ResolveInjectDllPath(LauncherService launcher)
        {
            var defaultDllPath = launcher.GetDefaultDllPath();
            if (!string.IsNullOrEmpty(defaultDllPath) && File.Exists(defaultDllPath))
            {
                return defaultDllPath;
            }

            var lightweightDllPath = LightweightPluginService.LitePluginDllPath;
            if (File.Exists(lightweightDllPath))
            {
                return lightweightDllPath;
            }

            try
            {
                var pluginsDir = LightweightPluginService.PluginsDir;
                if (Directory.Exists(pluginsDir))
                {
                    var pluginDll = Directory.GetFiles(pluginsDir, "*.dll", SearchOption.AllDirectories)
                        .FirstOrDefault(file => !file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));

                    if (!string.IsNullOrEmpty(pluginDll))
                    {
                        return pluginDll;
                    }
                }
            }
            catch
            {
                // ignored
            }

            return defaultDllPath;
        }

        private static string ResolvePluginConfigPath(string pluginDirectory)
        {
            if (string.IsNullOrEmpty(pluginDirectory) || !Directory.Exists(pluginDirectory))
            {
                return Path.Combine(AppContext.BaseDirectory, "Plugins", LightweightPluginService.MainPluginFolderName,
                    "config.ini");
            }

            var lowerCaseConfig = Path.Combine(pluginDirectory, "config.ini");
            if (File.Exists(lowerCaseConfig)) return lowerCaseConfig;

            var upperCaseConfig = Path.Combine(pluginDirectory, LightweightPluginService.LitePluginConfigName);
            if (File.Exists(upperCaseConfig)) return upperCaseConfig;

            return lowerCaseConfig;
        }

        private static void ApplyPreset(string presetId, string pluginDirectory)
        {
            try
            {
                var folder = string.IsNullOrWhiteSpace(pluginDirectory)
                    ? LightweightPluginService.MainPluginFolderName : Path.GetFileName(Path.TrimEndingDirectorySeparator(pluginDirectory));
                var presetsDir = PluginPresetStorage.GetDirectory(folder);
                var includeLegacyMain = folder.Equals(LightweightPluginService.MainPluginFolderName,
                    StringComparison.OrdinalIgnoreCase);
                var presetFile = PluginPresetStorage.ReadPresets(presetsDir, includeLegacyMain)
                    .FirstOrDefault(preset => preset.Id == presetId)?.FilePath;

                if (!string.IsNullOrEmpty(presetFile) && File.Exists(presetFile))
                {
                    var content = File.ReadAllText(presetFile);
                    using var doc = JsonDocument.Parse(content);

                    if (doc.RootElement.TryGetProperty("ConfigData", out var configData))
                    {
                        var iniPath = ResolvePluginConfigPath(pluginDirectory);

                        var iniFile = new IniFile(iniPath);
                        var dict =
                            JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(
                                configData.GetRawText());

                        if (dict != null)
                        {
                            dict.Remove("General");
                            iniFile.UpdateMultiple(dict);

                            var stateFile = Path.Combine(presetsDir, "active_state.json");
                            var stateDict = new Dictionary<string, string> { { "ActiveId", presetId } };
                            File.WriteAllText(stateFile, JsonSerializer.Serialize(stateDict));
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }
        }
    }
}