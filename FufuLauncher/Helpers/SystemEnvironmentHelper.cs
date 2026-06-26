/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System;
using System.Linq;
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

        private static readonly string[] InvalidValues = {
            "To Be Filled By O.E.M.",
            "System Serial Number",
            "Default string",
            "None",
            "N/A"
        };

        public static string GetHwid()
        {
            try
            {
                string cpuId = WmiQuery("Win32_Processor", "ProcessorId");
                string boardSn = WmiQuery("Win32_BaseBoard", "SerialNumber");
                string biosSn = WmiQuery("Win32_BIOS", "SerialNumber");
                string diskSn = GetSystemDiskSerial();

                var parts = new[] { cpuId, boardSn, biosSn, diskSn }
                    .Where(s => !string.IsNullOrWhiteSpace(s) && !InvalidValues.Contains(s, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                if (parts.Count == 0) return "Unknown";

                string raw = string.Join("|", parts);
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                byte[] hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(raw));
                string hex = BitConverter.ToString(hash).Replace("-", "");
                return hex.Substring(0, 32);
            }
            catch
            {
                return "Unknown";
            }
        }

        private static string WmiQuery(string wmiClass, string property)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
                foreach (var obj in searcher.Get())
                {
                    var val = obj[property]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(val)) return val;
                }
            }
            catch { }
            return string.Empty;
        }

        private static string GetSystemDiskSerial()
        {
            try
            {
                string sysDrive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
                using var partSearcher = new System.Management.ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{sysDrive}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                foreach (var part in partSearcher.Get())
                {
                    string deviceId = part["DeviceID"]?.ToString() ?? "";
                    var match = System.Text.RegularExpressions.Regex.Match(deviceId, @"Disk #(\d+)");
                    if (match.Success)
                    {
                        string diskIndex = match.Groups[1].Value;
                        using var diskSearcher = new System.Management.ManagementObjectSearcher(
                            $"SELECT SerialNumber FROM Win32_DiskDrive WHERE Index={diskIndex}");
                        foreach (var disk in diskSearcher.Get())
                        {
                            var sn = disk["SerialNumber"]?.ToString()?.Trim();
                            if (!string.IsNullOrWhiteSpace(sn)) return sn;
                        }
                    }
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
