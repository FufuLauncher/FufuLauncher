/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed class QuickFpsHotkeyService : IDisposable
{
    public const string SettingsKey = "QuickFpsSettings";

    private const string TargetFpsSection = "TargetFps";
    private const string TargetFpsValueKey = "Value";
    private const string MainPluginConfigName = "config.ini";
    private static readonly TimeSpan GameWatchInterval = TimeSpan.FromSeconds(3);

    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmSysKeyDown = 0x0104;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;
    private const int WmQuit = 0x0012;
    private const int LlkhfInjected = 0x10;

    private readonly ILocalSettingsService _settingsService;
    private readonly LightweightPluginService _lightweightPlugin;
    private readonly object _stateLock = new();
    private readonly HashSet<int> _downKeys = new();
    private readonly LowLevelKeyboardProc _hookProc;
    private readonly ManualResetEventSlim _hookReady = new(false);
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly object _watcherLock = new();

    private QuickFpsSettings _settings = new();
    private string[] _gameProcessNames = ["YuanShen", "GenshinImpact"];
    private Thread? _hookThread;
    private IntPtr _hookHandle = IntPtr.Zero;
    private uint _hookThreadId;
    private int _stopRequested;
    private CancellationTokenSource? _gameWatchCts;
    private volatile bool _gameRunning;
    private bool _disposed;

    public QuickFpsHotkeyService(ILocalSettingsService settingsService, LightweightPluginService lightweightPlugin)
    {
        _settingsService = settingsService;
        _lightweightPlugin = lightweightPlugin;
        _hookProc = KeyboardHookCallback;
    }

    public QuickFpsSettings Settings
    {
        get
        {
            lock (_stateLock)
            {
                return _settings.Clone();
            }
        }
    }

    public bool IsMainPluginInstalled =>
        File.Exists(LightweightPluginService.MainPluginDllPath) ||
        LightweightPluginService.FindMainPluginDisabledPath() != null;

    public bool IsMainPluginConfigAvailable => File.Exists(GetMainPluginConfigPath());

    public async Task InitializeAsync()
    {
        await _updateGate.WaitAsync();
        try
        {
            DebugLog.Info("QuickFpsHotkeyService", "Initialize start");
            await RefreshGameProcessNamesAsync();
            await LoadSettingsAsync();
            ApplyListeningState();

            QuickFpsSettings settings;
            string[] processNames;
            lock (_stateLock)
            {
                settings = _settings.Clone();
                processNames = _gameProcessNames;
            }

            DebugLog.Info("QuickFpsHotkeyService", $"Initialize done: {Describe(settings)}");
            DebugLog.Info("QuickFpsHotkeyService", $"Game process names: {string.Join(", ", processNames)}");
            DebugLog.Info("QuickFpsHotkeyService",
                $"Main plugin config: {GetMainPluginConfigPath()} (exists={File.Exists(GetMainPluginConfigPath())})");
            DebugLog.Info("QuickFpsHotkeyService", $"Lightweight mode: {_lightweightPlugin.IsLightweightMode}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsHotkeyService", $"Initialize failed: {ex}");
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public async Task UpdateSettingsAsync(QuickFpsSettings settings)
    {
        var applied = false;
        await _updateGate.WaitAsync();
        try
        {
            settings.Normalize();
            lock (_stateLock)
            {
                _settings = settings.Clone();
            }

            DebugLog.Info("QuickFpsHotkeyService", $"Settings update: {Describe(settings)}");
            await _settingsService.SaveSettingAsync(SettingsKey, settings);
            DebugLog.Info("QuickFpsHotkeyService", "Settings saved");
            ApplyListeningState();
            applied = true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsHotkeyService", $"Settings update failed: {ex}");
        }
        finally
        {
            _updateGate.Release();
        }

        if (!applied)
        {
            return;
        }

        try
        {
            WeakReferenceMessenger.Default.Send(new QuickFpsSettingsChangedMessage(Settings));
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsHotkeyService", $"Settings broadcast failed: {ex.Message}");
        }
    }

    private void ApplyListeningState()
    {
        var settings = Settings;
        DebugLog.Info("QuickFpsHotkeyService", $"ApplyListeningState: {Describe(settings)}");

        if (!settings.Enabled)
        {
            StopGameWatcher();
            StopHook();
            DebugLog.Info("QuickFpsHotkeyService", "Listening disabled");
            return;
        }

        StartHook();

        if (settings.ListenOnlyWhileGameRunning)
        {
            StartGameWatcher();
        }
        else
        {
            StopGameWatcher();
            _gameRunning = true;
        }
    }

    private void StartGameWatcher()
    {
        lock (_watcherLock)
        {
            if (_gameWatchCts != null)
            {
                return;
            }

            var cancellation = new CancellationTokenSource();
            _gameWatchCts = cancellation;
            DebugLog.Info("QuickFpsHotkeyService", "Game watcher started");
            _ = WatchGameAsync(cancellation);
        }
    }

    private void StopGameWatcher()
    {
        CancellationTokenSource? cancellation;
        lock (_watcherLock)
        {
            cancellation = _gameWatchCts;
            _gameWatchCts = null;
        }

        _gameRunning = false;

        if (cancellation == null)
        {
            return;
        }

        DebugLog.Info("QuickFpsHotkeyService", "Game watcher stopped");
        try
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
        catch
        {
        }
    }

    private async Task WatchGameAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var wasRunning = false;

        while (!token.IsCancellationRequested)
        {
            var isRunning = false;
            try
            {
                isRunning = await GameProcessHelper.IsGameRunningAsync();
            }
            catch (Exception ex)
            {
                DebugLog.Info("QuickFpsHotkeyService", $"Game process check failed: {ex.Message}");
            }

            if (token.IsCancellationRequested)
            {
                break;
            }

            if (isRunning != wasRunning)
            {
                _gameRunning = isRunning;
                DebugLog.Info("QuickFpsHotkeyService",
                    isRunning ? "Game running detected" : "Game not running detected");
            }

            wasRunning = isRunning;

            try
            {
                await Task.Delay(GameWatchInterval, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task LoadSettingsAsync()
    {
        QuickFpsSettings? loaded = null;
        try
        {
            var stored = await _settingsService.ReadSettingAsync(SettingsKey);
            var json = stored switch
            {
                JsonElement element when element.ValueKind == JsonValueKind.Object => element.GetRawText(),
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
                string text => text,
                _ => null
            };

            DebugLog.Info("QuickFpsHotkeyService", $"Stored settings raw: {json ?? "<null>"}");

            if (!string.IsNullOrWhiteSpace(json))
            {
                loaded = JsonSerializer.Deserialize<QuickFpsSettings>(json);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsHotkeyService", $"Failed to read settings: {ex.Message}");
        }

        loaded ??= new QuickFpsSettings();
        loaded.Normalize();
        lock (_stateLock)
        {
            _settings = loaded;
        }
    }

    private async Task RefreshGameProcessNamesAsync()
    {
        try
        {
            var exeNames = await GameExeManager.GetExeNamesAsync();
            var processNames = exeNames
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (processNames.Length > 0)
            {
                lock (_stateLock)
                {
                    _gameProcessNames = processNames;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsHotkeyService", $"Failed to resolve game process names: {ex.Message}");
        }
    }

    private void StartHook()
    {
        lock (_stateLock)
        {
            if (_hookThread is { IsAlive: true })
            {
                return;
            }

            Interlocked.Exchange(ref _stopRequested, 0);
            _hookReady.Reset();
            _hookThread = new Thread(HookThreadProc)
            {
                IsBackground = true,
                Name = "QuickFpsHotkeyThread"
            };
            _hookThread.Start();
            DebugLog.Info("QuickFpsHotkeyService", "Hook thread starting");
        }
    }

    private void StopHook()
    {
        Thread? thread;
        lock (_stateLock)
        {
            thread = _hookThread;
            if (thread == null)
            {
                return;
            }

            _hookThread = null;
        }

        DebugLog.Info("QuickFpsHotkeyService", "Stopping hook");
        Interlocked.Exchange(ref _stopRequested, 1);
        if (!thread.IsAlive)
        {
            return;
        }

        _hookReady.Wait(500);

        uint threadId;
        IntPtr handle;
        lock (_stateLock)
        {
            threadId = _hookThreadId;
            handle = _hookHandle;
        }

        if (threadId != 0)
        {
            PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        if (handle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(handle);
        }
    }

    private void HookThreadProc()
    {
        try
        {
            _hookThreadId = GetCurrentThreadId();
            using var process = Process.GetCurrentProcess();
            using var module = process.MainModule;
            var moduleHandle = module != null ? GetModuleHandle(module.ModuleName) : IntPtr.Zero;
            _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, moduleHandle, 0);
            _hookReady.Set();

            if (_hookHandle == IntPtr.Zero)
            {
                var error = Marshal.GetLastWin32Error();
                DebugLog.Info("QuickFpsHotkeyService", $"SetWindowsHookEx failed, win32={error}");
                _hookThreadId = 0;
                return;
            }

            if (Volatile.Read(ref _stopRequested) != 0)
            {
                DebugLog.Info("QuickFpsHotkeyService", "Hook thread stop requested before loop");
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
                _hookThreadId = 0;
                return;
            }

            DebugLog.Info("QuickFpsHotkeyService", $"Keyboard hook installed, thread={_hookThreadId}");
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            DebugLog.Info("QuickFpsHotkeyService", "Keyboard hook message loop exited");
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
            _hookThreadId = 0;
        }
        catch (Exception ex)
        {
            _hookReady.Set();
            DebugLog.Info("QuickFpsHotkeyService", $"Hook thread failed: {ex}");
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && lParam != IntPtr.Zero)
            {
                var message = wParam.ToInt32();
                if (message is WmKeyDown or WmSysKeyDown or WmKeyUp or WmSysKeyUp)
                {
                    var vkCode = Marshal.ReadInt32(lParam);
                    var flags = Marshal.ReadInt32(lParam, 8);
                    if ((flags & LlkhfInjected) == 0)
                    {
                        if (message is WmKeyDown or WmSysKeyDown)
                        {
                            bool isFirstPress;
                            lock (_downKeys)
                            {
                                isFirstPress = _downKeys.Add(vkCode);
                            }

                            if (isFirstPress)
                            {
                                HandleKeyPress(vkCode);
                            }
                        }
                        else
                        {
                            lock (_downKeys)
                            {
                                _downKeys.Remove(vkCode);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsHotkeyService", $"Hook callback failed: {ex.Message}");
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void HandleKeyPress(int vkCode)
    {
        QuickFpsSettings settings;
        lock (_stateLock)
        {
            settings = _settings.Clone();
        }

        if (!IsConfiguredKey(settings, vkCode))
        {
            return;
        }

        DebugLog.Info("QuickFpsHotkeyService", $"Hotkey matched: enabled={settings.Enabled}, " +
                                               $"listenOnlyWhileGame={settings.ListenOnlyWhileGameRunning}, gameRunning={_gameRunning}");

        if (!settings.Enabled)
        {
            DebugLog.Info("QuickFpsHotkeyService", "Key ignored: feature disabled");
            return;
        }

        if (settings.ListenOnlyWhileGameRunning && !_gameRunning)
        {
            DebugLog.Info("QuickFpsHotkeyService", "Key ignored: game not running");
            return;
        }

        if (settings.SwitchPresetsEnabled)
        {
            for (var i = 0; i < QuickFpsSettings.SlotCount; i++)
            {
                if (vkCode != settings.SlotKeys[i])
                {
                    continue;
                }

                DebugLog.Info("QuickFpsHotkeyService", $"Slot {i + 1} matched, fps={settings.SlotFps[i]}");
                if (settings.SlotFps[i] >= 1)
                {
                    ApplyTargetFps(settings.SlotFps[i]);
                }

                return;
            }
        }

        if (!settings.AdjustEnabled)
        {
            DebugLog.Info("QuickFpsHotkeyService", "Key ignored: adjust disabled");
            return;
        }

        var increase = settings.IncreaseKey != 0 && vkCode == settings.IncreaseKey;
        var decrease = settings.DecreaseKey != 0 && vkCode == settings.DecreaseKey;
        if (!increase && !decrease)
        {
            DebugLog.Info("QuickFpsHotkeyService", "Key ignored: no action configured for this key");
            return;
        }

        var current = ReadTargetFps(GetMainPluginConfigPath());
        DebugLog.Info("QuickFpsHotkeyService",
            $"Adjust matched: {(increase ? "increase" : "decrease")}, current={current}, step={settings.AdjustStep}");

        if (current < 1)
        {
            DebugLog.Info("QuickFpsHotkeyService", "Adjust ignored: could not read current TargetFps");
            return;
        }

        if (increase)
        {
            ApplyTargetFps(Math.Clamp(current + settings.AdjustStep,
                QuickFpsSettings.MinHotkeyFps, QuickFpsSettings.MaxFps));
        }
        else if (current > QuickFpsSettings.MinHotkeyFps)
        {
            ApplyTargetFps(Math.Max(QuickFpsSettings.MinHotkeyFps, current - settings.AdjustStep));
        }
        else
        {
            DebugLog.Info("QuickFpsHotkeyService", "Adjust ignored: current fps is already at the 60 floor");
        }
    }

    private static bool IsConfiguredKey(QuickFpsSettings settings, int vkCode)
    {
        if (settings.IncreaseKey != 0 && vkCode == settings.IncreaseKey) return true;
        if (settings.DecreaseKey != 0 && vkCode == settings.DecreaseKey) return true;
        if (!settings.SwitchPresetsEnabled) return false;

        for (var i = 0; i < QuickFpsSettings.SlotCount; i++)
        {
            if (settings.SlotKeys[i] != 0 && vkCode == settings.SlotKeys[i]) return true;
        }

        return false;
    }

    private void ApplyTargetFps(int fps)
    {
        try
        {
            if (_lightweightPlugin.IsLightweightMode)
            {
                DebugLog.Info("QuickFpsHotkeyService", "Write skipped: lightweight mode");
                return;
            }

            var configPath = GetMainPluginConfigPath();
            if (!File.Exists(configPath))
            {
                DebugLog.Info("QuickFpsHotkeyService", $"Write skipped: config not found ({configPath})");
                return;
            }

            fps = Math.Clamp(fps, 1, QuickFpsSettings.MaxFps);
            var current = ReadTargetFps(configPath);
            if (current == fps)
            {
                DebugLog.Info("QuickFpsHotkeyService", $"Write skipped: TargetFps already {fps}");
                return;
            }

            var iniFile = new IniFile(configPath);
            iniFile.WriteValue(TargetFpsSection, TargetFpsValueKey,
                fps.ToString(CultureInfo.InvariantCulture));
            DebugLog.Info("QuickFpsHotkeyService", $"TargetFps written: {current} -> {fps} ({configPath})");

            UpdateActivePresetTargetFps(fps);
            WeakReferenceMessenger.Default.Send(new QuickFpsTargetFpsChangedMessage(fps));
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsHotkeyService", $"TargetFps write failed: {ex}");
        }
    }

    private static void UpdateActivePresetTargetFps(int fps)
    {
        try
        {
            var presetsDirectory = PluginPresetStorage.GetDirectory(LightweightPluginService.MainPluginFolderName);
            var activeId = PluginPresetStorage.ReadActivePresetId(presetsDirectory, includeLegacyMain: true);
            if (string.IsNullOrWhiteSpace(activeId))
            {
                DebugLog.Info("QuickFpsHotkeyService", "[QuickFps] Active preset update skipped: no active preset");
                return;
            }

            var preset = PluginPresetStorage.ReadPresets(presetsDirectory, includeLegacyMain: true)
                .FirstOrDefault(item => string.Equals(item.Id, activeId, StringComparison.OrdinalIgnoreCase));
            if (preset == null || string.IsNullOrEmpty(preset.FilePath))
            {
                DebugLog.Info("QuickFpsHotkeyService",
                    $"[QuickFps] Active preset update skipped: preset not found ({activeId})");
                return;
            }

            var section = preset.ConfigData
                .FirstOrDefault(item => item.Key.Equals(TargetFpsSection, StringComparison.OrdinalIgnoreCase))
                .Value;
            if (section == null)
            {
                section = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                preset.ConfigData[TargetFpsSection] = section;
            }

            var valueKey = section.Keys
                               .FirstOrDefault(key => key.Equals(TargetFpsValueKey, StringComparison.OrdinalIgnoreCase))
                           ?? TargetFpsValueKey;
            section[valueKey] = fps.ToString(CultureInfo.InvariantCulture);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(preset.FilePath, JsonSerializer.Serialize(preset, options));
            DebugLog.Info("QuickFpsHotkeyService",
                $"[QuickFps] Active preset '{preset.Name}' TargetFps -> {fps}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsHotkeyService", $"[QuickFps] Active preset update failed: {ex.Message}");
        }
    }

    private static int ReadTargetFps(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                return 0;
            }

            var sections = new IniFile(configPath).ReadAll();
            if (sections.TryGetValue(TargetFpsSection, out var section) &&
                section.TryGetValue(TargetFpsValueKey, out var raw) &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                value >= 1)
            {
                return Math.Min(value, QuickFpsSettings.MaxFps);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("QuickFpsHotkeyService", $"TargetFps read failed: {ex.Message}");
        }

        return 0;
    }

    private static string GetMainPluginConfigPath() =>
        Path.Combine(LightweightPluginService.MainPluginDir, MainPluginConfigName);

    private static string Describe(QuickFpsSettings settings)
    {
        var slots = string.Join(", ", Enumerable.Range(0, QuickFpsSettings.SlotCount)
            .Select(i => $"{i + 1}:{settings.SlotFps[i]}/key={settings.SlotKeys[i]}"));

        return $"enabled={settings.Enabled}, gameOnly={settings.ListenOnlyWhileGameRunning}, " +
               $"switch={settings.SwitchPresetsEnabled}, adjust={settings.AdjustEnabled}, " +
               $"step={settings.AdjustStep}, inc={settings.IncreaseKey}, dec={settings.DecreaseKey}, " +
               $"slots=[{slots}]";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DebugLog.Info("QuickFpsHotkeyService", "Dispose");
        StopGameWatcher();
        StopHook();
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point pt;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}