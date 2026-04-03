using System.Runtime.InteropServices;
using FufuLauncher.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher
{
    public static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "--elevated-inject", StringComparison.OrdinalIgnoreCase))
            {
                RunElevatedInjection(args);
                return;
            }

            var key = "FufuLauncher_Main_Instance_Key";
            var mainInstance = AppInstance.FindOrRegisterForKey(key);

            if (!mainInstance.IsCurrent)
            {
                var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
                var task = mainInstance.RedirectActivationToAsync(activationArgs).AsTask();
                task.Wait();
                return;
            }

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
            MessageBox(IntPtr.Zero, $"注入启动失败: {errorMessage} (代码: {result})", "FufuLauncher 错误", 0x10);
        }

        exitCode = result == 0 ? 0 : 1;
    }
    catch (Exception ex)
    {
        MessageBox(IntPtr.Zero, $"注入进程发生异常: {ex.Message}", "FufuLauncher 错误", 0x10);
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