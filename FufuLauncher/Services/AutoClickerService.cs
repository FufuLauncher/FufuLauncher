using System.Diagnostics;
using System.Runtime.InteropServices;
using FufuLauncher.Contracts.Services;
using Windows.System;
using WindowsInput;
using WindowsInput.Native;

namespace FufuLauncher.Services
{
    public interface IAutoClickerService : IDisposable
    {
        bool IsEnabled
        {
            get; set;
        }
        VirtualKey TriggerKey
        {
            get; set;
        }
        VirtualKey ClickKey
        {
            get; set;
        }
        bool IsAutoClicking
        {
            get;
        }
        event EventHandler<bool> IsAutoClickingChanged;
        void Initialize();
        void Start();
        void Stop();
    }

    public class AutoClickerService : IAutoClickerService
    {
        private readonly ILocalSettingsService _settingsService;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private readonly InputSimulator _simulator = new InputSimulator();

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc _hookCallback;
        private CancellationTokenSource _clickCts;
        private bool _isTriggerKeyPressed = false;
        private bool _isEnabled = false;
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
        public bool IsAutoClicking
        {
            get; private set;
        }

        public AutoClickerService(ILocalSettingsService settingsService)
        {
            _settingsService = settingsService;
            _dispatcherQueue = App.MainWindow.DispatcherQueue;
            _hookCallback = HookCallback;
            Debug.WriteLine("[连点器服务] InputSimulatorPlus版本初始化");
        }

        public void Initialize()
        {
            LoadSettings(); Debug.WriteLine("[连点器服务] 配置加载完成");
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
                Debug.WriteLine(_hookId == IntPtr.Zero ? "[连点器] 钩子失败" : "[连点器] 钩子成功");
            }
            catch { }
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
            try
            {
                if (nCode >= 0 && _isEnabled)
                {
                    var vk = (VirtualKey)Marshal.ReadInt32(lParam);
                    bool down = wParam == (IntPtr)WM_KEYDOWN;
                    bool up = wParam == (IntPtr)WM_KEYUP;

                    if (vk == _triggerKey)
                    {
                        if (down && !_isTriggerKeyPressed) { _isTriggerKeyPressed = true; if (!IsAutoClicking) StartClicking(); }
                        else if (up) { _isTriggerKeyPressed = false; StopClicking(); }
                    }
                }
            }
            catch { }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        private void StartClicking()
        {
            if (IsAutoClicking) return;
            IsAutoClicking = true;
            IsAutoClickingChanged?.Invoke(this, true);
            _clickCts = new CancellationTokenSource();
            Task.Run(async () => { await ClickLoop(_clickCts.Token); });
        }

        private void StopClicking()
        {
            if (!IsAutoClicking) return;
            _clickCts?.Cancel();
            IsAutoClicking = false;
            IsAutoClickingChanged?.Invoke(this, false);
        }

        private async Task ClickLoop(CancellationToken token)
        {
            int count = 0;
            try { while (!token.IsCancellationRequested) { SendKey(_clickKey); count++; if (count % 10 == 0) Debug.WriteLine($"[连点器] 已发送 {count} 次"); await Task.Delay(50, token); } } catch { }
            Debug.WriteLine($"[连点器] 结束，共 {count} 次");
        }

        private void SendKey(VirtualKey key)
        {
            try
            {
                var code = (VirtualKeyCode)key;
                _simulator.Keyboard.KeyDown(code);
                _simulator.Keyboard.KeyUp(code);
            }
            catch (Exception ex) { Debug.WriteLine($"[连点器] 发送失败: {ex.Message}"); }
        }

        #region P/Invoke 仅钩子
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);
        #endregion

        public void Dispose()
        {
            Stop();
            Debug.WriteLine("[连点器服务] 已释放");
        }
    }
}