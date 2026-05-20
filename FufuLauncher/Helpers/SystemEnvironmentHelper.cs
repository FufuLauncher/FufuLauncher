using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace FufuLauncher.Helpers
{
    public static class SystemEnvironmentHelper
    {
        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool GetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private enum TOKEN_INFORMATION_CLASS
        {
            TokenElevationType = 18
        }

        private enum TOKEN_ELEVATION_TYPE
        {
            TokenElevationTypeFull = 2
        }

        public static bool IsRunningAsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch { return false; }
        }

        public static bool IsUacElevatedWithConsent()
        {
            try
            {
                if (!IsRunningAsAdministrator()) return false;
                if (OpenProcessToken(GetCurrentProcess(), 0x0008, out var tokenHandle))
                {
                    var size = Marshal.SizeOf(typeof(int));
                    var ptr = Marshal.AllocHGlobal(size);
                    try
                    {
                        if (GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenElevationType, ptr, (uint)size, out _))
                        {
                            var type = (TOKEN_ELEVATION_TYPE)Marshal.ReadInt32(ptr);
                            return type == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;
                        }
                    }
                    finally 
                    { 
                        Marshal.FreeHGlobal(ptr); 
                        if (tokenHandle != IntPtr.Zero) CloseHandle(tokenHandle); 
                    }
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        public static bool IsVCRedistInstalled()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"))
                {
                    if (key != null && key.GetValue("Installed") is int installed && installed == 1) return true;
                }
                
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86"))
                {
                    if (key != null && key.GetValue("Installed") is int installed && installed == 1) return true;
                }
            }
            catch
            {
                // ignored
            }
            
            return false;
        }
    }
}