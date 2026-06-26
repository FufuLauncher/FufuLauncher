/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
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
            info.NetworkStatus = isNetworkAvailable ? "已连接" : "未连接";

            if (isNetworkAvailable)
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    regionCode = await client.GetStringAsync("http://ip-api.com/line/?fields=countryCode");
                    regionCode = regionCode.Trim();
                    info.NetworkRegion = regionCode == "CN" ? "国内" : "海外";
                }
                catch
                {
                    regionCode = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
                    info.NetworkRegion = regionCode == "CN" ? "国内 (按系统设置)" : "海外 (按系统设置)";
                }
            }
            else
            {
                info.NetworkRegion = "无网络";
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
                        info.CpuName = item["Name"]?.ToString() ?? "未知处理器";
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
                        info.GpuName = item["Name"]?.ToString() ?? "未知显卡";
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
                    info.DiskSpace = "读取失败";
                }

                try
                {
                    using (var searcher = new ManagementObjectSearcher("select State from Win32_Service where Name='WinDefend'"))
                    {
                        bool found = false;
                        foreach (var item in searcher.Get())
                        {
                            info.SecurityCenterStatus = item["State"]?.ToString() == "Running" ? "已开启" : "未开启";
                            found = true;
                            break;
                        }
                        if (!found) info.SecurityCenterStatus = "未安装或已卸载";
                    }
                }
                catch
                {
                    info.SecurityCenterStatus = "读取失败";
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
                info.MaxRefreshRate = maxHz > 0 ? $"{maxHz} Hz" : "无法检测";

                info.Suggestion = GenerateSuggestion(info, totalMemoryGB, freeDiskGB, isNetworkAvailable, regionCode);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Diagnostics] Error: {ex.Message}");
                info.Suggestion = "诊断过程中发生错误，部分信息可能不准确";
            }

            return info;
        });
    }

    private string GenerateSuggestion(SystemDiagnosticsInfo info, long totalMemoryGB, long freeDiskGB, bool isNetworkAvailable, string regionCode)
    {
        var suggestions = new List<string>();

        if (!isNetworkAvailable)
        {
            suggestions.Add("网络未连接，请检查系统网络设置");
        }
        else if (regionCode == "CN")
        {
            suggestions.Add("当前处于国内网络环境，访问服务器可能存在缓慢情况");
        }
        
        if (info.SecurityCenterStatus == "已开启")
        {
            suggestions.Add("Windows 安全中心已开启，这可能会导致插件注入失败，建议关闭 Windows 安全中心");
        }

        if (totalMemoryGB >= 0 && totalMemoryGB < 12)
        {
            suggestions.Add($"当前物理内存为 {totalMemoryGB}GB，不符合最低 12GB 的要求");
        }

        if (freeDiskGB >= 0 && freeDiskGB < 1)
        {
            suggestions.Add($"系统盘剩余空间为 {freeDiskGB}GB，不符合最低 1GB 的要求");
        }

        if (int.TryParse(info.CurrentRefreshRate.Replace(" Hz", ""), out int currentHz) &&
            int.TryParse(info.MaxRefreshRate.Replace(" Hz", ""), out int maxHz))
        {
            if (currentHz < maxHz)
            {
                suggestions.Add($"您的显示器支持 {maxHz}Hz，但当前仅运行在 {currentHz}Hz");
            }
        }

        if (suggestions.Count == 0) return "正常，系统与网络环境符合运行要求";

        return string.Join("\n", suggestions);
    }
}
