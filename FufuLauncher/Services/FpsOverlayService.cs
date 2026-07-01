/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Diagnostics.Tracing.Session;

namespace FufuLauncher.Services
{
    public class FpsOverlayService
    {
        private static FpsOverlayService _instance;
        public static FpsOverlayService Instance => _instance ??= new FpsOverlayService();

        private bool _isRunning = false;
        private IntPtr _overlayHwnd = IntPtr.Zero;
        private Thread _overlayThread;
        private Thread _statsThread;
        private Thread _etwThread;

        private int _targetPid = 0;
        private TraceEventSession _etwSession;

        private int _currentFps = 0;
        private int _etwFrameCount = 0; 
        private float _currentCpu = 0;
        private float _currentGpu = 0;

        private readonly string _logFilePath = Path.Combine(AppContext.BaseDirectory, "fps_overlay_debug.log");

        // P/Invoke 定义
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateFont(int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet, uint iOutPrecision, uint iClipPrecision, uint iQuality, uint iPitchAndFamily, string pszFaceName);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr ho);

        [DllImport("gdi32.dll")]
        private static extern uint SetTextColor(IntPtr hdc, int crColor);

        [DllImport("gdi32.dll")]
        private static extern int SetBkMode(IntPtr hdc, int iBkMode);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateSolidBrush(uint crColor);

        [DllImport("user32.dll")]
        private static extern bool DrawText(IntPtr hDC, string lpString, int nCount, ref RECT lpRect, uint uFormat);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll")]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr SetTimer(IntPtr hWnd, IntPtr nIDEvent, uint uElapse, IntPtr lpTimerFunc);

        [DllImport("user32.dll")]
        private static extern bool KillTimer(IntPtr hWnd, IntPtr uIDEvent);

        [DllImport("user32.dll")]
        private static extern IntPtr BeginPaint(IntPtr hwnd, out PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        private static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);

        [DllImport("user32.dll")]
        private static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        // 常量与结构体
        private const uint WS_EX_LAYERED = 0x00080000;
        private const uint WS_EX_TRANSPARENT = 0x00000020;
        private const uint WS_EX_TOPMOST = 0x00000008;
        private const uint WS_POPUP = 0x80000000;
        private const uint WS_VISIBLE = 0x10000000;
        private const uint LWA_COLORKEY = 0x00000001;
        private const int TRANSPARENT = 1;
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        
        private const uint WM_DESTROY = 0x0002;
        private const uint WM_PAINT = 0x000F;
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_TIMER = 0x0113;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int pt_x; public int pt_y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PAINTSTRUCT { public IntPtr hdc; public bool fErase; public RECT rcPaint; public bool fRestore; public bool fIncUpdate; [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WNDCLASSEX { public uint cbSize; public uint style; public IntPtr lpfnWndProc; public int cbClsExtra; public int cbWndExtra; public IntPtr hInstance; public IntPtr hIcon; public IntPtr hCursor; public IntPtr hbrBackground; public string lpszMenuName; public string lpszClassName; public IntPtr hIconSm; }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
            public ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private WndProcDelegate _wndProcDelegate;

        private void Log(string message)
        {
            string formattedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Debug.WriteLine(formattedMessage);
            try { File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine); } catch { }
        }

        public bool IsAdministrator()
        {
            using WindowsIdentity identity = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        public void StartOverlay(int targetProcessId)
        {
            if (_isRunning) return;
            if (!IsAdministrator()) throw new UnauthorizedAccessException("StartOverlay requires Administrator privileges.");

            try { File.WriteAllText(_logFilePath, $"--- Log Initialization (Target PID: {targetProcessId}) ---{Environment.NewLine}"); } catch { }

            _targetPid = targetProcessId;
            _isRunning = true;
            Log($"StartOverlay triggered, Target PID: {_targetPid}");

            _overlayThread = new Thread(InitializeAndRunOverlay);
            _overlayThread.SetApartmentState(ApartmentState.STA);
            _overlayThread.IsBackground = true;
            _overlayThread.Start();

            _etwThread = new Thread(MonitorEtwEvents);
            _etwThread.IsBackground = true;
            _etwThread.Start();

            _statsThread = new Thread(MonitorSystemStats);
            _statsThread.IsBackground = true;
            _statsThread.Start();
        }

        public void StopOverlay()
        {
            if (!_isRunning) return;
            Log("StopOverlay triggered, cleaning up resources.");
            _isRunning = false;
            
            if (_overlayHwnd != IntPtr.Zero)
            {
                PostMessage(_overlayHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }

            try
            {
                _etwSession?.Stop();
                _etwSession?.Dispose();
            }
            catch (Exception ex)
            {
                Log($"ETW Session cleanup exception: {ex.Message}");
            }
        }

        private void MonitorEtwEvents()
        {
            Log("ETW DXGI Monitor Thread Started.");
            try
            {
                string sessionName = "FufuFpsEtwSession_" + Guid.NewGuid().ToString("N");

                if (TraceEventSession.GetActiveSessionNames().Contains(sessionName))
                {
                    var existingSession = new TraceEventSession(sessionName);
                    existingSession.Stop();
                }

                _etwSession = new TraceEventSession(sessionName);
                
                Guid dxgiProviderId = new Guid("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
                _etwSession.EnableProvider(dxgiProviderId);

                _etwSession.Source.Dynamic.All += (data) =>
                {
                    try
                    {
                        if (data.ProcessID == _targetPid)
                        {
                            if ((int)data.ID == 42 || string.Equals(data.EventName, "DXGIPresent/Start", StringComparison.OrdinalIgnoreCase))
                            {
                                Interlocked.Increment(ref _etwFrameCount);
                            }
                        }
                    }
                    catch 
                    {
                    }
                };

                _etwSession.Source.Process();
            }
            catch (Exception ex)
            {
                Log($"ETW Monitor initialization failed: {ex.Message}");
            }
            Log("ETW DXGI Monitor Thread Exited.");
        }

        private void MonitorSystemStats()
        {
            Log("Hardware Stats Monitor Thread Started.");
            Dictionary<string, PerformanceCounter> gpuCounters = new();
            PerformanceCounterCategory gpuCategory = null;

            ulong lastIdleTime = 0;
            ulong lastSystemTime = 0;

            try
            {
                gpuCategory = new PerformanceCounterCategory("GPU Engine");
                Log("GPU Engine Category initialized.");
            }
            catch (Exception ex)
            {
                Log($"GPU Engine initialization exception (Platform unsupported, GPU will show N/A): {ex.Message}");
            }

            while (_isRunning)
            {
                try
                {
                    if (_targetPid > 0)
                    {
                        var process = Process.GetProcessById(_targetPid);
                        if (process == null || process.HasExited)
                        {
                            Log("Target process exited, terminating monitor.");
                            StopOverlay();
                            break;
                        }
                    }

                    if (GetSystemTimes(out var idle, out var kernel, out var user))
                    {
                        ulong currentIdleTime = idle.ToUInt64();
                        ulong currentSystemTime = kernel.ToUInt64() + user.ToUInt64();

                        if (lastSystemTime != 0)
                        {
                            ulong systemTimeDiff = currentSystemTime - lastSystemTime;
                            ulong idleTimeDiff = currentIdleTime - lastIdleTime;

                            if (systemTimeDiff > 0)
                            {
                                _currentCpu = (float)((systemTimeDiff - idleTimeDiff) * 100.0 / systemTimeDiff);
                            }
                        }
                        lastIdleTime = currentIdleTime;
                        lastSystemTime = currentSystemTime;
                    }

                    if (gpuCategory != null)
                    {
                        try
                        {
                            var instances = gpuCategory.GetInstanceNames()
                                .Where(x => x.Contains($"pid_{_targetPid}_") && x.EndsWith("engtype_3D"))
                                .ToList();

                            float totalGpu = 0;
                            var toRemove = gpuCounters.Keys.Except(instances).ToList();
                            
                            foreach (var key in toRemove)
                            {
                                gpuCounters[key].Dispose();
                                gpuCounters.Remove(key);
                            }

                            foreach (var instance in instances)
                            {
                                if (!gpuCounters.ContainsKey(instance))
                                {
                                    var pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true);
                                    pc.NextValue(); 
                                    gpuCounters[instance] = pc;
                                }
                                else
                                {
                                    totalGpu += gpuCounters[instance].NextValue();
                                }
                            }
                            _currentGpu = totalGpu;
                        }
                        catch 
                        {
                            _currentGpu = -1;
                        }
                    }

                    Thread.Sleep(1000);
                }
                catch
                {
                    StopOverlay();
                    break;
                }
            }

            foreach (var pc in gpuCounters.Values) pc.Dispose();
        }

        private void InitializeAndRunOverlay()
        {
            Log("Window Render Thread Started.");
            string className = "FufuFpsOverlayClass_" + Guid.NewGuid().ToString("N");
            _wndProcDelegate = WndProc;

            WNDCLASSEX wndClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX)),
                style = 0x0003, 
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate),
                hInstance = Process.GetCurrentProcess().Handle,
                lpszClassName = className,
                hbrBackground = CreateSolidBrush(0x000000) 
            };

            RegisterClassEx(ref wndClass);

            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);

            _overlayHwnd = CreateWindowEx(
                WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST,
                className, "FufuFpsOverlay", WS_POPUP | WS_VISIBLE,
                0, 0, screenWidth, screenHeight,
                IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

            if (_overlayHwnd == IntPtr.Zero) return;

            SetLayeredWindowAttributes(_overlayHwnd, 0x000000, 0, LWA_COLORKEY);
            SetTimer(_overlayHwnd, (IntPtr)1, 1000, IntPtr.Zero); 

            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_TIMER:
                    _currentFps = Interlocked.Exchange(ref _etwFrameCount, 0);
                    InvalidateRect(hWnd, IntPtr.Zero, true);
                    return IntPtr.Zero;

                case WM_PAINT:
                    PAINTSTRUCT ps;
                    IntPtr hdc = BeginPaint(hWnd, out ps);

                    IntPtr hFont = CreateFont(24, 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Consolas");
                    IntPtr oldFont = SelectObject(hdc, hFont);

                    SetBkMode(hdc, TRANSPARENT);
                    SetTextColor(hdc, 0x00FF00); 

                    RECT rect = new RECT { Left = 20, Top = 20, Right = 400, Bottom = 200 };
                    
                    string gpuText = _currentGpu < 0 ? "N/A" : $"{_currentGpu:F1}%";
                    string text = $"FPS: {_currentFps}\nCPU: {_currentCpu:F1}%\nGPU: {gpuText}";
                    
                    DrawText(hdc, text, text.Length, ref rect, 0x0100); 

                    SelectObject(hdc, oldFont);
                    DeleteObject(hFont);

                    EndPaint(hWnd, ref ps);
                    return IntPtr.Zero;

                case WM_DESTROY:
                    KillTimer(hWnd, (IntPtr)1);
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }
    }
}
