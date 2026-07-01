/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace FufuLauncher.Services;

public class ScreenshotService : IScreenshotService, IDisposable
{
    private readonly ILocalSettingsService _settingsService;
    private readonly INotificationService _notificationService;

    private const string ScreenshotEnabledKey = "IsScreenshotEnabled";
    private const string ScreenshotHotkeyKey = "ScreenshotHotkey";
    private const string ScreenshotSavePathKey = "ScreenshotSavePath";

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private IntPtr _keyboardHookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _keyboardHookCallback;
    private Thread _hookThread;

    private int _gamePid;
    private IntPtr _gameWindowHandle = IntPtr.Zero;
    private bool _isRunning;
    private bool _isCapturing;
    
    private VirtualKey _hotkey = VirtualKey.F12;
    private bool _hotkeyCtrl;
    private bool _hotkeyAlt;
    private bool _hotkeyShift;

    public bool IsRunning => _isRunning;

    public ScreenshotService(ILocalSettingsService settingsService, INotificationService notificationService)
    {
        _settingsService = settingsService;
        _notificationService = notificationService;
        _keyboardHookCallback = KeyboardHookCallback;
    }

    public async Task StartAsync(int gamePid)
    {
        if (_isRunning) return;

        var enabledObj = await _settingsService.ReadSettingAsync(ScreenshotEnabledKey);
        if (enabledObj == null || !Convert.ToBoolean(enabledObj))
            return;

        _gamePid = gamePid;
        await LoadHotkeySettingsAsync();
        
        _gameWindowHandle = FindMainWindowByPid(gamePid);
        if (_gameWindowHandle == IntPtr.Zero)
        {
            Debug.WriteLine("[截图服务] 未找到游戏窗口句柄，等待后重试...");
            await Task.Delay(3000);
            _gameWindowHandle = FindMainWindowByPid(gamePid);
        }

        if (_gameWindowHandle == IntPtr.Zero)
        {
            Debug.WriteLine("[截图服务] 无法找到游戏窗口，截图服务启动中止");
            return;
        }
        
        _hookThread = new Thread(HookThreadProc)
        {
            IsBackground = true,
            Name = "ScreenshotHookThread"
        };
        _hookThread.Start();

        _isRunning = true;
        Debug.WriteLine($"[截图服务] 已启动，监听快捷键，游戏PID: {gamePid}，窗口句柄: {_gameWindowHandle}");
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        try
        {
            if (_hookThread != null && _hookThread.IsAlive)
            {
                PostThreadMessage((uint)_hookThread.ManagedThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
                _hookThread = null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[截图服务] Stop 异常: {ex.Message}");
        }

        _isRunning = false;
        _gameWindowHandle = IntPtr.Zero;
        _gamePid = 0;
        Debug.WriteLine("[截图服务] 已停止");
        return Task.CompletedTask;
    }

    private async Task LoadHotkeySettingsAsync()
    {
        try
        {
            var hotkeyObj = await _settingsService.ReadSettingAsync(ScreenshotHotkeyKey);
            var hotkeyStr = hotkeyObj?.ToString() ?? "F12";
            ParseHotkey(hotkeyStr);
        }
        catch
        {
            _hotkey = VirtualKey.F12;
            _hotkeyCtrl = false;
            _hotkeyAlt = false;
            _hotkeyShift = false;
        }
    }

    private void ParseHotkey(string hotkeyStr)
    {
        _hotkeyCtrl = false;
        _hotkeyAlt = false;
        _hotkeyShift = false;
        _hotkey = VirtualKey.F12;

        if (string.IsNullOrWhiteSpace(hotkeyStr)) return;

        var parts = hotkeyStr.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "CTRL":
                case "CONTROL":
                    _hotkeyCtrl = true;
                    break;
                case "ALT":
                    _hotkeyAlt = true;
                    break;
                case "SHIFT":
                    _hotkeyShift = true;
                    break;
                default:
                    if (Enum.TryParse<VirtualKey>(part, true, out var vk))
                        _hotkey = vk;
                    else if (int.TryParse(part, out var code))
                        _hotkey = (VirtualKey)code;
                    break;
            }
        }
    }

    #region Keyboard Hook

    private void HookThreadProc()
    {
        try
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var moduleHandle = GetModuleHandle(curModule.ModuleName);
            _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookCallback, moduleHandle, 0);

            Debug.WriteLine(_keyboardHookId == IntPtr.Zero
                ? "[截图服务] 键盘钩子安装失败"
                : "[截图服务] 键盘钩子安装成功");

            if (_keyboardHookId != IntPtr.Zero)
            {
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
                Debug.WriteLine("[截图服务] 键盘钩子已卸载");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[截图服务] HookThreadProc 异常: {ex.Message}");
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRunning)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int flags = Marshal.ReadInt32(lParam, 8);
            bool isInjected = (flags & 0x10) != 0;

            if (!isInjected)
            {
                int wp = wParam.ToInt32();
                bool down = wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN;

                if (down && (VirtualKey)vkCode == _hotkey && CheckModifiers())
                {
                    Task.Run(CaptureScreenshotAsync);
                }
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private bool CheckModifiers()
    {
        bool ctrlPressed = (GetAsyncKeyState((int)VirtualKey.Control) & 0x8000) != 0;
        bool altPressed = (GetAsyncKeyState((int)VirtualKey.Menu) & 0x8000) != 0;
        bool shiftPressed = (GetAsyncKeyState((int)VirtualKey.Shift) & 0x8000) != 0;

        return ctrlPressed == _hotkeyCtrl && altPressed == _hotkeyAlt && shiftPressed == _hotkeyShift;
    }

    #endregion

    #region Screenshot Capture

    private async Task CaptureScreenshotAsync()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            try
            {
                var proc = Process.GetProcessById(_gamePid);
                if (proc.HasExited)
                {
                    await StopAsync();
                    return;
                }
            }
            catch
            {
                await StopAsync();
                return;
            }
            
            if (!IsWindow(_gameWindowHandle))
            {
                _gameWindowHandle = FindMainWindowByPid(_gamePid);
                if (_gameWindowHandle == IntPtr.Zero)
                {
                    Debug.WriteLine("[截图服务] 游戏窗口已不可用");
                    return;
                }
            }
            
            var savePath = await GetSavePathAsync();
            Directory.CreateDirectory(savePath);

            var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            var filePath = Path.Combine(savePath, fileName);
            
            var success = await CaptureWindowAsync(_gameWindowHandle, filePath);

            if (success)
            {
                Debug.WriteLine($"[截图服务] 截图已保存: {filePath}");
                WeakReferenceMessenger.Default.Send(new ScreenshotTakenMessage(filePath, true));
                _notificationService.Show("截图已保存", fileName, NotificationType.Success, 0);
            }
            else
            {
                Debug.WriteLine("[截图服务] 截图失败");
                WeakReferenceMessenger.Default.Send(new ScreenshotTakenMessage("", false, "截图捕获失败"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[截图服务] 截图异常: {ex.Message}");
            WeakReferenceMessenger.Default.Send(new ScreenshotTakenMessage("", false, ex.Message));
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task<bool> CaptureWindowAsync(IntPtr hwnd, string filePath)
    {
        IDirect3DDevice device = null;
        Direct3D11CaptureFramePool framePool = null;
        GraphicsCaptureSession session = null;
        Direct3D11CaptureFrame frame = null;

        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var winUIWindowId = new Windows.UI.WindowId(windowId.Value);
            var captureItem = GraphicsCaptureItem.TryCreateFromWindowId(winUIWindowId);

            if (captureItem == null)
            {
                Debug.WriteLine("[截图服务] 无法创建 GraphicsCaptureItem");
                return false;
            }

            device = CreateDirect3DDevice();
            if (device == null)
            {
                Debug.WriteLine("[截图服务] 无法创建 Direct3D 设备");
                return false;
            }

            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                captureItem.Size);

            session = framePool.CreateCaptureSession(captureItem);

            try
            {
                session.IsBorderRequired = false;
            }
            catch
            {
                // ignored
            }

            var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>();
            framePool.FrameArrived += (pool, _) =>
            {
                var f = pool.TryGetNextFrame();
                tcs.TrySetResult(f);
            };

            session.StartCapture();

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(2000));

            session.Dispose();
            session = null;

            if (completedTask != tcs.Task)
            {
                Debug.WriteLine("[截图服务] 捕获超时");
                return false;
            }

            frame = tcs.Task.Result;
            var frameSize = frame.ContentSize;

            var saved = await SaveFrameToFileAsync(frame, filePath, frameSize);
            return saved;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[截图服务] CaptureWindowAsync 异常: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
        finally
        {
            try { frame?.Dispose(); } catch { }
            try { framePool?.Dispose(); } catch { }
            try { session?.Dispose(); } catch { }
            if (device != null)
            {
                try { device.Dispose(); }
                catch { Marshal.FinalReleaseComObject(device); }
            }
        }
    }

    private async Task<bool> SaveFrameToFileAsync(Direct3D11CaptureFrame frame, string filePath, SizeInt32 size)
    {
        try
        {
            var softwareBitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);

            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(softwareBitmap);
            
            if (size.Width > 0 && size.Height > 0)
            {
                encoder.BitmapTransform.Bounds = new BitmapBounds
                {
                    X = 0,
                    Y = 0,
                    Width = (uint)size.Width,
                    Height = (uint)size.Height
                };
            }

            await encoder.FlushAsync();

            stream.Seek(0);
            var buffer = new byte[stream.Size];
            var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(buffer);

            await File.WriteAllBytesAsync(filePath, buffer);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[截图服务] SaveFrameToFileAsync 异常: {ex.Message}");
            return false;
        }
    }

    private async Task<string> GetSavePathAsync()
    {
        try
        {
            var pathObj = await _settingsService.ReadSettingAsync(ScreenshotSavePathKey);
            var path = pathObj?.ToString()?.Trim('"')?.Trim();
            if (!string.IsNullOrEmpty(path))
                return path;
        }
        catch { }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FufuScreenshots");
    }

    #endregion

    #region Window Finding

    private static IntPtr FindMainWindowByPid(int pid)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint windowPid);
            if (windowPid == pid && IsWindowVisible(hwnd))
            {
                var style = GetWindowLong(hwnd, -16);
                if ((style & 0x40000000) == 0)
                {
                    var length = GetWindowTextLength(hwnd);
                    if (length > 0)
                    {
                        foundHwnd = hwnd;
                        return false;
                    }
                }
            }
            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    #endregion

    #region Direct3D Device Creation

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int DriverType,
        IntPtr Software,
        uint Flags,
        int[] pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    /// <summary>
    /// 创建一个正确的 WinRT IDirect3DDevice，使用 CsWinRT interop 兼容的方式
    /// </summary>
    private static IDirect3DDevice CreateDirect3DDevice()
    {
        try
        {
            // 创建 D3D11 设备
            var hr = D3D11CreateDevice(
                IntPtr.Zero,
                1, // D3D_DRIVER_TYPE_HARDWARE
                IntPtr.Zero,
                0x20, // D3D11_CREATE_DEVICE_BGRA_SUPPORT
                null,
                0,
                7, // D3D11_SDK_VERSION
                out var d3dDevice,
                out _,
                out var immediateContext);

            if (hr != 0 || d3dDevice == IntPtr.Zero)
            {
                Debug.WriteLine($"[截图服务] D3D11CreateDevice HARDWARE 失败, HRESULT: 0x{hr:X8}");
                hr = D3D11CreateDevice(
                    IntPtr.Zero,
                    5, // D3D_DRIVER_TYPE_WARP
                    IntPtr.Zero,
                    0x20,
                    null,
                    0,
                    7,
                    out d3dDevice,
                    out _,
                    out immediateContext);

                if (hr != 0 || d3dDevice == IntPtr.Zero)
                {
                    Debug.WriteLine($"[截图服务] D3D11CreateDevice WARP 也失败, HRESULT: 0x{hr:X8}");
                    return null;
                }
            }

            if (immediateContext != IntPtr.Zero)
                Marshal.Release(immediateContext);

            // 获取 IDXGIDevice
            var iidDxgi = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            hr = Marshal.QueryInterface(d3dDevice, ref iidDxgi, out var dxgiDevice);
            Marshal.Release(d3dDevice);

            if (hr != 0 || dxgiDevice == IntPtr.Zero)
            {
                Debug.WriteLine($"[截图服务] QueryInterface IDXGIDevice 失败, HRESULT: 0x{hr:X8}");
                return null;
            }

            // 使用 CreateDirect3D11DeviceFromDXGIDevice 创建 WinRT inspectable
            var inspectable = IntPtr.Zero;
            var funcPtr = GetCreateDirect3DDeviceFuncPtr();

            if (funcPtr != IntPtr.Zero)
            {
                var del = Marshal.GetDelegateForFunctionPointer<CreateDirect3D11DeviceFromDXGIDeviceDelegate>(funcPtr);
                hr = del(dxgiDevice, out inspectable);
            }

            Marshal.Release(dxgiDevice);

            if (hr != 0 || inspectable == IntPtr.Zero)
            {
                Debug.WriteLine($"[截图服务] CreateDirect3D11DeviceFromDXGIDevice 失败, HRESULT: 0x{hr:X8}");
                return null;
            }

            // 使用 CsWinRT 的 MarshalInterface 正确包装为 IDirect3DDevice
            var winrtDevice = MarshalIDirect3DDevice(inspectable);
            Marshal.Release(inspectable);

            if (winrtDevice == null)
            {
                Debug.WriteLine("[截图服务] MarshalIDirect3DDevice 返回 null");
            }

            return winrtDevice;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[截图服务] CreateDirect3DDevice 异常: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    private static IDirect3DDevice MarshalIDirect3DDevice(IntPtr inspectable)
    {
        try
        {
            // 尝试使用 WinRT.MarshalInterface 来正确创建 CsWinRT 包装
            // IDirect3DDevice 的 IID: A37624AB-8D5F-4650-9D3E-9EAE3D9BC670
            var iidDirect3DDevice = new Guid("A37624AB-8D5F-4650-9D3E-9EAE3D9BC670");
            Marshal.QueryInterface(inspectable, ref iidDirect3DDevice, out var devicePtr);

            if (devicePtr != IntPtr.Zero)
            {
                // 使用 WinRT.MarshalInterface<IDirect3DDevice>.FromAbi 来获取正确的 CsWinRT projection
                var device = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(devicePtr);
                Marshal.Release(devicePtr);
                return device;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[截图服务] MarshalInterface 方式失败: {ex.Message}");
        }

        return null;
    }

    private static IntPtr GetCreateDirect3DDeviceFuncPtr()
    {
        // 尝试从多个可能的 DLL 中加载 CreateDirect3D11DeviceFromDXGIDevice
        string[] dlls = { "d3d11.dll", "api-ms-win-gaming-deviceinformation-l1-1-0.dll" };
        foreach (var dll in dlls)
        {
            var hModule = LoadLibraryEx(dll, IntPtr.Zero, 0);
            if (hModule == IntPtr.Zero) continue;
            var proc = GetProcAddress(hModule, "CreateDirect3D11DeviceFromDXGIDevice");
            if (proc != IntPtr.Zero) return proc;
        }
        return IntPtr.Zero;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDirect3D11DeviceFromDXGIDeviceDelegate(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    #endregion

    #region Dispose

    public void Dispose()
    {
        _ = StopAsync();
    }

    #endregion

    #region P/Invoke

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    #endregion
}
