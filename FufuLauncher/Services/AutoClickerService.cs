using System.Diagnostics;
using System.Runtime.InteropServices;
using FufuLauncher.Contracts.Services;
using Windows.System;

namespace FufuLauncher.Services
{
    public interface IAutoClickerService : IDisposable
    {
        bool IsEnabled { get; set; }
        VirtualKey TriggerKey { get; set; }
        VirtualKey ClickKey { get; set; }
        bool IsAutoClicking { get; }
        event EventHandler<bool> IsAutoClickingChanged;
        void Initialize();
        void Start();
        void Stop();
    }

    public class AutoClickerService : IAutoClickerService
    {
        private readonly ILocalSettingsService _settingsService;
        
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _hookCallback;
        private CancellationTokenSource _clickCts;
        private bool _isTriggerKeyPressed;
        private bool _isEnabled;
        private VirtualKey _triggerKey = VirtualKey.F8;
        private VirtualKey _clickKey = VirtualKey.F;

        public event EventHandler<bool> IsAutoClickingChanged;

        public bool IsEnabled
        {
            get => _isEnabled; set
            {
                if (_isEnabled != value) { _isEnabled = value; if (value) Start(); else Stop(); _ = SaveSettingsAsync(); }
            }
        }
        public VirtualKey TriggerKey
        {
            get => _triggerKey; set
            {
                _triggerKey = value; _isTriggerKeyPressed = false; _ = SaveSettingsAsync();
            }
        }
        public VirtualKey ClickKey
        {
            get => _clickKey; set
            {
                _clickKey = value; _ = SaveSettingsAsync();
            }
        }
        public bool IsAutoClicking { get; private set; }

        public AutoClickerService(ILocalSettingsService settingsService)
        {
            _settingsService = settingsService;
            _hookCallback = HookCallback;
            try {
            } catch { }
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

                if (enabled != null) _isEnabled = Convert.ToBoolean(enabled);

                string triggerKeyStr = triggerKey?.ToString()?.Trim('"');
                string clickKeyStr = clickKey?.ToString()?.Trim('"');

                if (!string.IsNullOrEmpty(triggerKeyStr) && Enum.TryParse(triggerKeyStr, out VirtualKey tk)) _triggerKey = tk;
                if (!string.IsNullOrEmpty(clickKeyStr) && Enum.TryParse(clickKeyStr, out VirtualKey ck)) _clickKey = ck;

                _isTriggerKeyPressed = false; IsAutoClicking = false;
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
            }
            catch { }
        }

        public void Start()
        {
            if (_hookId != IntPtr.Zero) return;

            try
            {
                using var curProcess = Process.GetCurrentProcess();
                using var curModule = curProcess.MainModule;
                var moduleHandle = GetModuleHandle(curModule.ModuleName);
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, moduleHandle, 0);
                Debug.WriteLine(_hookId == IntPtr.Zero ? "[连点器] 钩子安装失败" : "[连点器] 钩子安装成功");
            }
            catch (Exception ex) 
            {
                Debug.WriteLine($"[连点器] Start 异常: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                if (_hookId != IntPtr.Zero) { UnhookWindowsHookEx(_hookId); _hookId = IntPtr.Zero; }
                StopClicking();
                _isTriggerKeyPressed = false;
            }
            catch { }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _isEnabled)
            {
                try
                {
                    KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    
                    bool isInjected = (hookStruct.flags & 0x10) != 0;
                    
                    if (!isInjected)
                    {
                        var vk = (VirtualKey)hookStruct.vkCode;
                        bool down = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
                        bool up = wParam == WM_KEYUP || wParam == WM_SYSKEYUP;

                        if (vk == _triggerKey)
                        {
                            if (down)
                            {
                                if (!_isTriggerKeyPressed)
                                {
                                    _isTriggerKeyPressed = true;
                                    StartClicking();
                                }
                            }
                            else if (up)
                            {
                                _isTriggerKeyPressed = false;
                                StopClicking();
                            }
                        }
                    }
                }
                catch { }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private void StartClicking()
        {
            if (IsAutoClicking) return;
            IsAutoClicking = true;
            IsAutoClickingChanged?.Invoke(this, true);
            _clickCts = new CancellationTokenSource();
            
            Task.Factory.StartNew(async () => { await ClickLoop(_clickCts.Token); }, _clickCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void StopClicking()
        {
            if (!IsAutoClicking) return;
            _clickCts?.Cancel();
            IsAutoClicking = false;
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
                    SendNativeInput(scanCode);
                    
                    await Task.Delay(50, token); 
                } 
            } 
            catch (TaskCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[连点器] 循环异常: {ex.Message}"); }
        }
        
        private void SendNativeInput(ushort scanCode)
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

        public void Dispose()
        {
            Stop();
            Debug.WriteLine("[连点器服务] 已释放");
        }

        #region P/Invoke
        
        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
            [FieldOffset(0)] public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT {int dx; int dy; uint mouseData; uint dwFlags; uint time; IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        struct HARDWAREINPUT { uint uMsg; ushort wParamL; ushort wParamH; }

        private const int INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_SCANCODE = 0x0008;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint MAPVK_VK_TO_VSC = 0;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

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