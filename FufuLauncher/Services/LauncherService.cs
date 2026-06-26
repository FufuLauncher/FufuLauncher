/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace FufuLauncher.Services
{
    public interface ILauncherService
    {
        bool ValidateGamePath(string gamePath);
        bool ValidateDllPath(string dllPath);
        int LaunchGameAndInject(string gamePath, string dllPath, string commandLineArgs, out string errorMessage, out int processId);
        string GetDefaultDllPath();
        void UpdateConfig(string gamePath, bool hideQuestBanner, bool disableDamageText, bool useTouchScreen,
                         bool disableEventCameraMove, bool removeTeamProgress, bool redirectCombineEntry,
                         bool resin106, bool resin201, bool resin107009, bool resin107012, bool resin220007);
    }

    public class LauncherService : ILauncherService
    {
        private const string DllName = "Launcher.dll";
        
        public static bool IsLauncherDllLoaded { get; private set; } = false;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        static LauncherService()
        {
            try
            {
                string extractDirectory = AppContext.BaseDirectory;
                Environment.CurrentDirectory = extractDirectory;
                SetDllDirectory(extractDirectory);
        
                string absoluteDllPath = Path.Combine(extractDirectory, DllName);
                
                if (!File.Exists(absoluteDllPath))
                {
                    Debug.WriteLine($"找不到核心文件: {absoluteDllPath}");
                    IsLauncherDllLoaded = false;
                    return;
                }

                IntPtr handle = LoadLibrary(absoluteDllPath);
                if (handle == IntPtr.Zero)
                {
                    int errorCode = Marshal.GetLastWin32Error();
                    Debug.WriteLine($"加载 {DllName} 失败。文件存在，但缺少依赖项或架构不匹配。Win32错误码: {errorCode}");
                    IsLauncherDllLoaded = false;
                    return;
                }
                
                IsLauncherDllLoaded = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化 LauncherService 时发生异常: {ex.Message}");
                IsLauncherDllLoaded = false;
            }
        }

        [DllImport(DllName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool ValidateGamePathInternal([MarshalAs(UnmanagedType.LPWStr)] string gamePath);

        [DllImport(DllName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool ValidateDllPathInternal([MarshalAs(UnmanagedType.LPWStr)] string dllPath);

        [DllImport(DllName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int LaunchGameAndInject(
            [MarshalAs(UnmanagedType.LPWStr)] string gamePath,
            [MarshalAs(UnmanagedType.LPWStr)] string dllPath,
            [MarshalAs(UnmanagedType.LPWStr)] string commandLineArgs,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder errorMessage,
            int errorMessageSize);

        [DllImport(DllName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern int GetDefaultDllPath(
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder dllPath,
            int dllPathSize);

        [DllImport(DllName, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern void UpdateConfig(
            [MarshalAs(UnmanagedType.LPWStr)] string gamePath,
            int hideQuestBanner,
            int disableDamageText,
            int useTouchScreen,
            int disableEventCameraMove,
            int removeTeamProgress,
            int redirectCombineEntry,
            int resin106,
            int resin201,
            int resin107009,
            int resin107012,
            int resin220007);
        

        public bool ValidateGamePath(string gamePath)
        {
            if (!IsLauncherDllLoaded) return false;
            return ValidateGamePathInternal(gamePath);
        }

        public bool ValidateDllPath(string dllPath)
        {
            if (!IsLauncherDllLoaded) return false;
            return ValidateDllPathInternal(dllPath);
        }

        public int LaunchGameAndInject(string gamePath, string dllPath, string commandLineArgs, out string errorMessage, out int processId)
        {
            if (!IsLauncherDllLoaded)
            {
                errorMessage = "由于系统缺少 C++ 运行库或核心文件被杀毒软件拦截，Launcher.dll 未加载成功，无法启动。";
                processId = 0;
                return -1;
            }

            var errorBuffer = new StringBuilder(1024);

            int result = LaunchGameAndInject(gamePath, dllPath ?? "", commandLineArgs ?? "", errorBuffer, errorBuffer.Capacity);

            errorMessage = errorBuffer.ToString();

            if (result == 0 && int.TryParse(errorMessage, out int pid))
            {
                processId = pid;
                errorMessage = "";
            }
            else
            {
                processId = 0;
            }

            return result;
        }

        public string GetDefaultDllPath()
        {
            if (!IsLauncherDllLoaded) return string.Empty;

            var pathBuffer = new StringBuilder(1024);
            return GetDefaultDllPath(pathBuffer, pathBuffer.Capacity) == 0
                ? pathBuffer.ToString()
                : string.Empty;
        }

        public void UpdateConfig(string gamePath, bool hideQuestBanner, bool disableDamageText, bool useTouchScreen,
                                bool disableEventCameraMove, bool removeTeamProgress, bool redirectCombineEntry,
                                bool resin106, bool resin201, bool resin107009, bool resin107012, bool resin220007)
        {
            if (!IsLauncherDllLoaded) return;

            UpdateConfig(gamePath ?? "",
                hideQuestBanner ? 1 : 0,
                disableDamageText ? 1 : 0,
                useTouchScreen ? 1 : 0,
                disableEventCameraMove ? 1 : 0,
                removeTeamProgress ? 1 : 0,
                redirectCombineEntry ? 1 : 0,
                resin106 ? 1 : 0,
                resin201 ? 1 : 0,
                resin107009 ? 1 : 0,
                resin107012 ? 1 : 0,
                resin220007 ? 1 : 0);
        }
    }
}
