/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using FufuLauncher.Contracts.Services;
using Windows.System;

namespace FufuLauncher.Services
{
    public enum AutoClickerMode
    {
        Keyboard,
        MouseLeft,
        MouseRight
    }

    public interface IAutoClickerService : IDisposable
    {
        bool IsEnabled { get; set; }
        VirtualKey TriggerKey { get; set; }
        VirtualKey ClickKey { get; set; }
        VirtualKey StopKey { get; set; }
        AutoClickerMode Mode { get; set; }
        bool IsAutoClicking { get; }
        event EventHandler<bool> IsAutoClickingChanged;
        event EventHandler<bool> IsEnabledChanged;
        void Initialize();
        void Start();
        void Stop();
    }

    public class AutoClickerService : IAutoClickerService
    {
        private readonly ILocalSettingsService _settingsService;
        
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private IntPtr _keyboardHookId = IntPtr.Zero;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private readonly LowLevelKeyboardProc _keyboardHookCallback;
        private readonly LowLevelMouseProc _mouseHookCallback;
        private CancellationTokenSource _clickCts;
        private bool _isTriggerKeyPressed;
        private bool _isMouseTriggerPressed;
        private bool _isEnabled;
        private VirtualKey _triggerKey = VirtualKey.F8;
        private VirtualKey _clickKey = VirtualKey.F;
        private VirtualKey _stopKey = VirtualKey.None;
        private AutoClickerMode _mode = AutoClickerMode.Keyboard;
        
        private readonly object _stateLock = new object();

        private Thread _hookThread;

        public event EventHandler<bool> IsAutoClickingChanged;
        public event EventHandler<bool> IsEnabledChanged;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    if (value) Start(); else Stop();
                    IsEnabledChanged?.Invoke(this, value);
                    _ = SaveSettingsAsync();
                }
            }
        }

        public VirtualKey TriggerKey
        {
            get => _triggerKey;
            set
            {
                _triggerKey = value;
                _isTriggerKeyPressed = false;
                _ = SaveSettingsAsync();
            }
        }

        public VirtualKey ClickKey
        {
            get => _clickKey;
            set
            {
                _clickKey = value;
                _ = SaveSettingsAsync();
            }
        }

        public VirtualKey StopKey
        {
            get => _stopKey;
            set
            {
                _stopKey = value;
                _ = SaveSettingsAsync();
            }
        }

        public AutoClickerMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;

                StopClicking();
                _isTriggerKeyPressed = false;
                _isMouseTriggerPressed = false;
                _mode = value;
                _ = SaveSettingsAsync();
            }
        }

        public bool IsAutoClicking { get; private set; }

        public AutoClickerService(ILocalSettingsService settingsService)
        {
            _settingsService = settingsService;
            _keyboardHookCallback = KeyboardHookCallback;
            _mouseHookCallback = MouseHookCallback;
            Debug.WriteLine("[连点器服务] 初始化");
        }

        public void Initialize()
        {
            LoadSettings(); 
            Debug.WriteLine("[连点器服务] 配置加载完成");
        }

        private void LoadSettings()
        {
            try
            {
                var enabled = _settingsService.ReadSettingAsync("AutoClickerEnabled").Result;
                var triggerKey = _settingsService.ReadSettingAsync("AutoClickerTriggerKey").Result;
                var clickKey = _settingsService.ReadSettingAsync("AutoClickerClickKey").Result;
                var stopKey = _settingsService.ReadSettingAsync("AutoClickerStopKey").Result;
                var mode = _settingsService.ReadSettingAsync("AutoClickerMode").Result;

                if (enabled != null) _isEnabled = Convert.ToBoolean(enabled);

                string triggerKeyStr = triggerKey?.ToString()?.Trim('"');
                string clickKeyStr = clickKey?.ToString()?.Trim('"');
                string stopKeyStr = stopKey?.ToString()?.Trim('"');
                string modeStr = mode?.ToString()?.Trim('"');

                if (!string.IsNullOrEmpty(triggerKeyStr) && Enum.TryParse(triggerKeyStr, out VirtualKey tk)) _triggerKey = tk;
                if (!string.IsNullOrEmpty(clickKeyStr) && Enum.TryParse(clickKeyStr, out VirtualKey ck)) _clickKey = ck;
                if (!string.IsNullOrEmpty(stopKeyStr) && Enum.TryParse(stopKeyStr, out VirtualKey sk)) _stopKey = sk;
                if (!string.IsNullOrEmpty(modeStr) && Enum.TryParse(modeStr, out AutoClickerMode savedMode)) _mode = savedMode;

                _isTriggerKeyPressed = false;
                _isMouseTriggerPressed = false;
                IsAutoClicking = false;
                if (_isEnabled) Start();
            }
            catch { }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                await _settingsService.SaveSettingAsync("AutoClickerEnabled", _isEnabled);
                await _settingsService.SaveSettingAsync("AutoClickerTriggerKey", _triggerKey.ToString());
                await _settingsService.SaveSettingAsync("AutoClickerClickKey", _clickKey.ToString());
                await _settingsService.SaveSettingAsync("AutoClickerStopKey", _stopKey.ToString());
                await _settingsService.SaveSettingAsync("AutoClickerMode", _mode.ToString());
            }
            catch { }
        }

        public void Start()
        {
            if (_hookThread != null && _hookThread.IsAlive) return;

            try
            {
                _hookThread = new Thread(HookThreadProc)
                {
                    IsBackground = true,
                    Name = "AutoClickerHookThread"
                };
                _hookThread.Start();
                Debug.WriteLine("[连点器] 钩子线程启动");
            }
            catch (Exception ex) 
            {
                Debug.WriteLine($"[连点器] Start 异常: {ex.Message}");
            }
        }

        private void HookThreadProc()
        {
            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                var moduleHandle = GetModuleHandle(curModule.ModuleName);
                _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookCallback, moduleHandle, 0);
                _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseHookCallback, moduleHandle, 0);
                Debug.WriteLine(_keyboardHookId == IntPtr.Zero ? "[连点器] 键盘钩子安装失败" : "[连点器] 键盘钩子安装成功");
                Debug.WriteLine(_mouseHookId == IntPtr.Zero ? "[连点器] 鼠标钩子安装失败" : "[连点器] 鼠标钩子安装成功");

                if (_keyboardHookId != IntPtr.Zero || _mouseHookId != IntPtr.Zero)
                {
                    MSG msg;
                    while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                    {
                        TranslateMessage(ref msg);
                        DispatchMessage(ref msg);
                    }

                    if (_keyboardHookId != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(_keyboardHookId);
                        _keyboardHookId = IntPtr.Zero;
                    }

                    if (_mouseHookId != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(_mouseHookId);
                        _mouseHookId = IntPtr.Zero;
                    }

                    Debug.WriteLine("[连点器] 钩子已卸载");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[连点器] HookThreadProc 异常: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                if (_hookThread != null && _hookThread.IsAlive)
                {
                    PostThreadMessage((uint)_hookThread.ManagedThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
                    _hookThread = null;
                }
                StopClicking();
                _isTriggerKeyPressed = false;
                _isMouseTriggerPressed = false;
            }
            catch { }
        }

        private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isEnabled)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                int flags = Marshal.ReadInt32(lParam, 8);
                bool isInjected = (flags & 0x10) != 0;

                if (!isInjected)
                {
                    var vk = (VirtualKey)vkCode;
                    int wp = wParam.ToInt32();
                    bool down = wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN;
                    bool up = wp == WM_KEYUP || wp == WM_SYSKEYUP;

                    if (down && _stopKey != VirtualKey.None && vk == _stopKey)
                    {
                        IsEnabled = false;
                        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
                    }

                    if (_mode == AutoClickerMode.Keyboard && vk == _triggerKey)
                    {
                        if (down && !_isTriggerKeyPressed)
                        {
                            _isTriggerKeyPressed = true;
                            Task.Run(() => StartClicking());
                        }
                        else if (up)
                        {
                            _isTriggerKeyPressed = false;
                            Task.Run(() => StopClicking());
                        }
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isEnabled && _mode != AutoClickerMode.Keyboard)
            {
                var mouseData = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                bool isInjected = (mouseData.flags & 0x1) != 0;

                if (!isInjected)
                {
                    int message = wParam.ToInt32();
                    bool targetDown = (_mode == AutoClickerMode.MouseLeft && message == WM_LBUTTONDOWN) ||
                                      (_mode == AutoClickerMode.MouseRight && message == WM_RBUTTONDOWN);
                    bool targetUp = (_mode == AutoClickerMode.MouseLeft && message == WM_LBUTTONUP) ||
                                    (_mode == AutoClickerMode.MouseRight && message == WM_RBUTTONUP);

                    if (targetDown && !_isMouseTriggerPressed)
                    {
                        _isMouseTriggerPressed = true;
                        Task.Run(() => StartClicking());
                    }
                    else if (targetUp)
                    {
                        _isMouseTriggerPressed = false;
                        Task.Run(() => StopClicking());
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private void StartClicking()
        {
            lock (_stateLock)
            {
                if (IsAutoClicking) return;
                IsAutoClicking = true;
                _clickCts = new CancellationTokenSource();
                
                _ = Task.Run(() => ClickLoop(_clickCts.Token), _clickCts.Token);
            }
            IsAutoClickingChanged?.Invoke(this, true);
        }

        private void StopClicking()
        {
            lock (_stateLock)
            {
                if (!IsAutoClicking) return;
                _clickCts?.Cancel();
                _clickCts?.Dispose();
                _clickCts = null;
                IsAutoClicking = false;
            }
            IsAutoClickingChanged?.Invoke(this, false);
            Debug.WriteLine("[连点器] 停止");
        }

        private async Task ClickLoop(CancellationToken token)
        {
            Debug.WriteLine("[连点器] 循环开始");
            ushort scanCode = (ushort)MapVirtualKey((uint)_clickKey, MAPVK_VK_TO_VSC);
            
            try 
            { 
                while (!token.IsCancellationRequested) 
                {
                    if (_mode == AutoClickerMode.Keyboard)
                    {
                        SendKeyboardInput(scanCode);
                    }
                    else
                    {
                        SendMouseInput(_mode == AutoClickerMode.MouseLeft);
                    }
                    await Task.Delay(50, token); 
                } 
            } 
            catch (TaskCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[连点器] 循环异常: {ex.Message}"); }
        }
        
        private void SendKeyboardInput(ushort scanCode)
        {
            var inputs = new INPUT[2];
            
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = 0;
            inputs[0].u.ki.wScan = scanCode;
            inputs[0].u.ki.dwFlags = KEYEVENTF_SCANCODE;
            inputs[0].u.ki.time = 0;
            inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;
            
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = 0;
            inputs[1].u.ki.wScan = scanCode;
            inputs[1].u.ki.dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP;
            inputs[1].u.ki.time = 0;
            inputs[1].u.ki.dwExtraInfo = IntPtr.Zero;

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        private void SendMouseInput(bool leftButton)
        {
            var inputs = new INPUT[2];
            uint downFlag = leftButton ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_RIGHTDOWN;
            uint upFlag = leftButton ? MOUSEEVENTF_LEFTUP : MOUSEEVENTF_RIGHTUP;

            inputs[0].type = INPUT_MOUSE;
            inputs[0].u.mi.dwFlags = downFlag;
            inputs[0].u.mi.dwExtraInfo = IntPtr.Zero;

            inputs[1].type = INPUT_MOUSE;
            inputs[1].u.mi.dwFlags = upFlag;
            inputs[1].u.mi.dwExtraInfo = IntPtr.Zero;

            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }

        public void Dispose()
        {
            Stop();
            Debug.WriteLine("[连点器服务] 已释放");
        }

        #region P/Invoke
        
        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MAPVK_VK_TO_VSC = 0;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

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
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        #endregion
    }
}

