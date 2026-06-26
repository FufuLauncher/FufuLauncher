/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Updater
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (Environment.OSVersion.Version.Build < 19045)
            {
                MessageBox.Show("此更新程序仅允许在Windows 10 22H2或更高版本上运行", "版本不支持", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }
            
            WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            {
                ProcessStartInfo psi = new()
                {
                    FileName = Process.GetCurrentProcess().MainModule.FileName,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                try
                {
                    Process.Start(psi);
                }
                catch
                {
                    // ignored
                }

                Environment.Exit(0);
            }
            base.OnStartup(e);
        }
    }
}
