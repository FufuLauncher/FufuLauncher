/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class SystemDiagnosticsService
{
    [DllImport("user32.dll")]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVMODE
    {
        private const int CCHDEVICENAME = 32;
        private const int CCHFORMNAME = 32;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHDEVICENAME)]
        public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CCHFORMNAME)]
        public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    public async Task<SystemDiagnosticsInfo> GetSystemInfoAsync()
    {
        var info = new SystemDiagnosticsInfo();
        long totalMemoryGB = -1;
        long freeDiskGB = -1;
        bool isNetworkAvailable = false;
        string regionCode = "未知";

        try
        {
            isNetworkAvailable = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            info.NetworkStatus = isNetworkAvailable ? "Diagnostics_Connected".GetLocalized() : "Diagnostics_Disconnected".GetLocalized();

            if (isNetworkAvailable)
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    regionCode = await client.GetStringAsync("http://ip-api.com/line/?fields=countryCode");
                    regionCode = regionCode.Trim();
                    info.NetworkRegion = regionCode == "CN" ? "Diagnostics_Domestic".GetLocalized() : "Diagnostics_Overseas".GetLocalized();
                }
                catch
                {
                    regionCode = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
                    info.NetworkRegion = regionCode == "CN" ? "Diagnostics_DomesticSystem".GetLocalized() : "Diagnostics_OverseasSystem".GetLocalized();
                }
            }
            else
            {
                info.NetworkRegion = "Diagnostics_NoNetwork".GetLocalized();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Diagnostics] Network Error: {ex.Message}");
        }

        return await Task.Run(() =>
        {
            try
            {
                info.OsVersion = RuntimeInformation.OSDescription;

                using (var searcher = new ManagementObjectSearcher("select Name from Win32_Processor"))
                {
                    foreach (var item in searcher.Get())
                    {
                        info.CpuName = item["Name"]?.ToString() ?? "Diagnostics_UnknownCpu".GetLocalized();
                        break;
                    }
                }

                using (var searcher = new ManagementObjectSearcher("select Capacity from Win32_PhysicalMemory"))
                {
                    long totalCapacity = 0;
                    foreach (var item in searcher.Get())
                    {
                        if (long.TryParse(item["Capacity"]?.ToString(), out long capacity))
                        {
                            totalCapacity += capacity;
                        }
                    }
                    totalMemoryGB = totalCapacity / (1024 * 1024 * 1024);
                    info.TotalMemory = $"{totalMemoryGB} GB";
                }

                using (var searcher = new ManagementObjectSearcher("select Name from Win32_VideoController"))
                {
                    foreach (var item in searcher.Get())
                    {
                        info.GpuName = item["Name"]?.ToString() ?? "Diagnostics_UnknownGpu".GetLocalized();
                        if (info.GpuName.Contains("NVIDIA") || info.GpuName.Contains("AMD")) break;
                    }
                }

                try
                {
                    string systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
                    if (!string.IsNullOrEmpty(systemDrive))
                    {
                        DriveInfo drive = new(systemDrive);
                        freeDiskGB = drive.AvailableFreeSpace / (1024 * 1024 * 1024);
                        info.DiskSpace = $"{freeDiskGB} GB";
                    }
                }
                catch
                {
                    info.DiskSpace = "Diagnostics_ReadFailed".GetLocalized();
                }

                try
                {
                    using (var searcher = new ManagementObjectSearcher("select State from Win32_Service where Name='WinDefend'"))
                    {
                        bool found = false;
                        foreach (var item in searcher.Get())
                        {
                            info.SecurityCenterStatus = item["State"]?.ToString() == "Running" ? "Diagnostics_Enabled".GetLocalized() : "Diagnostics_Disabled".GetLocalized();
                            found = true;
                            break;
                        }
                        if (!found) info.SecurityCenterStatus = "Diagnostics_NotInstalled".GetLocalized();
                    }
                }
                catch
                {
                    info.SecurityCenterStatus = "Diagnostics_ReadFailed".GetLocalized();
                }

                DEVMODE dm = new();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

                if (EnumDisplaySettings(null, -1, ref dm))
                {
                    info.ScreenResolution = $"{dm.dmPelsWidth} x {dm.dmPelsHeight}";
                    info.CurrentRefreshRate = $"{dm.dmDisplayFrequency} Hz";
                }

                int maxHz = 0;
                int i = 0;
                while (EnumDisplaySettings(null, i, ref dm))
                {
                    if (dm.dmDisplayFrequency > maxHz)
                    {
                        maxHz = dm.dmDisplayFrequency;
                    }
                    i++;
                }
                info.MaxRefreshRate = maxHz > 0 ? $"{maxHz} Hz" : "Diagnostics_CannotDetect".GetLocalized();

                info.Suggestion = GenerateSuggestion(info, totalMemoryGB, freeDiskGB, isNetworkAvailable, regionCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diagnostics] Error: {ex.Message}");
                info.Suggestion = "Diagnostics_PartialError".GetLocalized();
            }

            return info;
        });
    }

    private string GenerateSuggestion(SystemDiagnosticsInfo info, long totalMemoryGB, long freeDiskGB, bool isNetworkAvailable, string regionCode)
    {
        var suggestions = new List<string>();

        if (!isNetworkAvailable)
        {
            suggestions.Add("Diagnostics_SuggestNoNetwork".GetLocalized());
        }
        else if (regionCode == "CN")
        {
            suggestions.Add("Diagnostics_SuggestDomesticSlow".GetLocalized());
        }
        
        if (info.SecurityCenterStatus == "Diagnostics_Enabled".GetLocalized())
        {
            suggestions.Add("Diagnostics_SuggestSecurityCenter".GetLocalized());
        }

        if (totalMemoryGB >= 0 && totalMemoryGB < 12)
        {
            suggestions.Add(string.Format("Diagnostics_SuggestLowMemory".GetLocalized(), totalMemoryGB));
        }

        if (freeDiskGB >= 0 && freeDiskGB < 1)
        {
            suggestions.Add(string.Format("Diagnostics_SuggestLowDisk".GetLocalized(), freeDiskGB));
        }

        if (int.TryParse(info.CurrentRefreshRate.Replace(" Hz", ""), out int currentHz) &&
            int.TryParse(info.MaxRefreshRate.Replace(" Hz", ""), out int maxHz))
        {
            if (currentHz < maxHz)
            {
                suggestions.Add(string.Format("Diagnostics_SuggestRefreshRate".GetLocalized(), maxHz, currentHz));
            }
        }

        if (suggestions.Count == 0) return "Diagnostics_AllNormal".GetLocalized();

        return string.Join("\n", suggestions);
    }
}
