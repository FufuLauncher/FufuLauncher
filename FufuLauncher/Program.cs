/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Runtime.InteropServices;
using FufuLauncher.Services;
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
            SentrySdk.Init(options => 
            { 
                options.Dsn = "https://9c8e89f029c240e3dba227979a26759a@o4511497397272576.ingest.de.sentry.io/4511497409265745"; 
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
        if (args.Length < 2)
        {
            return;
        }

        var gameExePath = args[1];
        
        int presetIndex = Array.IndexOf(args, "--preset");
        if (presetIndex != -1 && args.Length > presetIndex + 1)
        {
            string presetId = args[presetIndex + 1];
            ApplyPreset(presetId);
        }

        var tempLauncher = new LauncherService(); 
        var dllPath = tempLauncher.GetDefaultDllPath();
        
        var commandLineArgs = args.Length > 4 ? args[4] : string.Empty; 

        var launcher = new LauncherService();
        var result = launcher.LaunchGameAndInject(gameExePath, dllPath, commandLineArgs, out var errorMessage, out var pid);

        if (result != 0)
        {
            MessageBox(IntPtr.Zero, string.Format("Program_InjectionFailed".GetLocalized(), errorMessage, result), "Program_ErrorTitle".GetLocalized(), 0x10);
        }

        exitCode = result == 0 ? 0 : 1;
    }
    catch (Exception ex)
    {
        MessageBox(IntPtr.Zero, string.Format("Program_InjectionException".GetLocalized(), ex.Message), "Program_ErrorTitle".GetLocalized(), 0x10);
    }
    finally
    {
        Environment.Exit(exitCode);
    }
}

private static void ApplyPreset(string presetId)
{
    try
    {
        var presetsDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "Presets");
        var presetFile = Path.Combine(presetsDir, $"{presetId}.json");
        
        if (File.Exists(presetFile))
        {
            var content = File.ReadAllText(presetFile);
            using var doc = JsonDocument.Parse(content);
            
            if (doc.RootElement.TryGetProperty("ConfigData", out var configData))
            {
                var pluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "FuFuPlugin");
                var iniPath = Path.Combine(pluginDir, "config.ini");
                
                var iniFile = new IniFile(iniPath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(configData.GetRawText());
                
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
