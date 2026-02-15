using System.Runtime.InteropServices;
using FufuLauncher.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

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
            int exitCode = 1;
            try
            {
                if (args.Length < 2)
                {
                    return;
                }

                var gameExePath = args[1];
                var tempLauncher = new LauncherService(); 
                var dllPath = tempLauncher.GetDefaultDllPath();
                var commandLineArgs = string.Empty; 
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
    }
}