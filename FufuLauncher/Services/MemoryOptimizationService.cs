using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FufuLauncher.Services
{
    public class MemoryOptimizationService
    {
        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetCurrentProcess();

        public void FlushMemory(bool isMinimizedOrHidden)
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                
                if (isMinimizedOrHidden && Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"内存清理异常: {ex.Message}");
            }
        }
    }
}