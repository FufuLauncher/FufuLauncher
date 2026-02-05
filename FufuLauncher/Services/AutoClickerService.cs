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
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        
        // 移除 InputSimulator，使用原生 P/Invoke 以获得更底层的控制权

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104; // 增加对系统按键(如Alt组合)的监听
        private const int WM_SYSKEYUP = 0x0105;

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _hookCallback;
        private CancellationTokenSource _clickCts;
        private bool _isTriggerKeyPressed = false;
        private bool _isEnabled = false;
        
        // 默认键位
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
            // 确保钩子委托不被GC回收
            _hookCallback = HookCallback;
            // 如果是 WinUI 3 且不在主线程，需要注意 DispatcherQueue 的获取方式，这里假设 App.MainWindow 已就绪
            try { _dispatcherQueue = App.MainWindow.DispatcherQueue; } catch { }
            Debug.WriteLine("[连点器服务] 初始化 (原生API版)");
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
                    // 使用结构体读取详细信息
                    KBDLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

                    // 关键优化：检查 LLKHF_INJECTED (0x00000010)
                    // 如果这个按键是由连点器(SendInput)生成的，直接忽略，避免自身逻辑干扰
                    bool isInjected = (hookStruct.flags & 0x10) != 0;
                    
                    if (!isInjected)
                    {
                        var vk = (VirtualKey)hookStruct.vkCode;
                        bool down = (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN);
                        bool up = (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP);

                        if (vk == _triggerKey)
                        {
                            if (down)
                            {
                                // 只有当标记为未按下时才启动，防止按住不放时的重复触发干扰 Loop 启动逻辑
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
            
            // 使用 TaskCreationOptions.LongRunning 暗示调度器这是一个长时间循环，避免线程池饥饿
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
            // 预先计算扫描码，性能更好且兼容游戏
            ushort scanCode = (ushort)MapVirtualKey((uint)_clickKey, MAPVK_VK_TO_VSC);
            
            try 
            { 
                while (!token.IsCancellationRequested) 
                { 
                    // 使用 SendNativeInput 发送纯净的硬件扫描码
                    SendNativeInput(scanCode);
                    
                    // 这里的 Delay 决定了连点速度，50ms = 20次/秒
                    await Task.Delay(50, token); 
                } 
            } 
            catch (TaskCanceledException) { }
            catch (Exception ex) { Debug.WriteLine($"[连点器] 循环异常: {ex.Message}"); }
        }

        /// <summary>
        /// 使用 SendInput API 直接发送扫描码。
        /// 这种方式不会受 Ctrl/Shift/Alt 等按键状态影响，也不会因为其他按键被按下而中断。
        /// </summary>
        private void SendNativeInput(ushort scanCode)
        {
            var inputs = new INPUT[2];

            // 按下
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = 0; // 使用扫描码时，VirtualKey 设为 0
            inputs[0].u.ki.wScan = scanCode;
            inputs[0].u.ki.dwFlags = KEYEVENTF_SCANCODE; // 指定使用扫描码
            inputs[0].u.ki.time = 0;
            inputs[0].u.ki.dwExtraInfo = IntPtr.Zero;

            // 抬起
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

        #region P/Invoke 定义
        
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
        struct MOUSEINPUT { /* 占位，不需要具体实现 */ int dx; int dy; uint mouseData; uint dwFlags; uint time; IntPtr dwExtraInfo; }

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