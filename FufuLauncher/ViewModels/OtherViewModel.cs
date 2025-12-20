using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace FufuLauncher.ViewModels
{
    public partial class OtherViewModel : ObservableObject
    {
        private readonly ILocalSettingsService _localSettingsService;
        private readonly IAutoClickerService _autoClickerService;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;

        [ObservableProperty] private bool _isAdditionalProgramEnabled;
        [ObservableProperty] private string _additionalProgramPath = string.Empty;
        [ObservableProperty] private string _statusMessage = string.Empty;

        [ObservableProperty] private bool _isAutoClickerEnabled;
        [ObservableProperty] private string _triggerKey = "F8";
        [ObservableProperty] private string _clickKey = "F";
        [ObservableProperty] private bool _isRecordingTriggerKey;
        [ObservableProperty] private bool _isRecordingClickKey;
        [ObservableProperty]
        private bool _isApplyButtonEnabled;


        public IAsyncRelayCommand BrowseProgramCommand
        {
            get;
        }
        public IAsyncRelayCommand SaveSettingsCommand
        {
            get;
        }
        public IRelayCommand RecordTriggerKeyCommand
        {
            get;
        }
        public IRelayCommand RecordClickKeyCommand
        {
            get;
        }
        public IAsyncRelayCommand ApplyProgramPathCommand
        {
            get;
        }

        public OtherViewModel(ILocalSettingsService localSettingsService, IAutoClickerService autoClickerService)
        {
            _localSettingsService = localSettingsService;
            _autoClickerService = autoClickerService;
            _dispatcherQueue = App.MainWindow.DispatcherQueue;

            BrowseProgramCommand = new AsyncRelayCommand(BrowseProgramAsync);
            SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
            RecordTriggerKeyCommand = new RelayCommand(StartRecordingTriggerKey);
            RecordClickKeyCommand = new RelayCommand(StartRecordingClickKey);
            ApplyProgramPathCommand = new AsyncRelayCommand(ApplyProgramPathAsync);

            LoadSettings();
        }

        partial void OnAdditionalProgramPathChanged(string value)
        {
            IsApplyButtonEnabled = !string.IsNullOrWhiteSpace(value);

            if (!string.IsNullOrWhiteSpace(value))
            {
                var trimmedPath = value.Trim('"');
                if (File.Exists(trimmedPath) && System.IO.Path.GetExtension(trimmedPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "路径有效";
                }
                else
                {
                    StatusMessage = "文件不存在或不是有效的 .exe 文件";
                }
            }
            else
            {
                StatusMessage = string.Empty;
            }
        }
        private async Task ApplyProgramPathAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(AdditionalProgramPath))
                {
                    StatusMessage = "路径不能为空";
                    return;
                }

                var trimmedPath = AdditionalProgramPath.Trim('"');

                if (File.Exists(trimmedPath) && System.IO.Path.GetExtension(trimmedPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "路径已应用";

                    await SaveSettingsAsync();

                    _ = Task.Delay(2000).ContinueWith(_ =>
                        _dispatcherQueue?.TryEnqueue(() => StatusMessage = string.Empty));
                }
                else
                {
                    StatusMessage = "无效的路径，请检查文件是否存在且为 .exe 格式";

                    var savedPath = await _localSettingsService.ReadSettingAsync("AdditionalProgramPath");
                    if (savedPath != null)
                    {
                        AdditionalProgramPath = savedPath.ToString().Trim('"');
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"应用失败: {ex.Message}";
                Debug.WriteLine($"[ApplyProgramPathAsync] 失败: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                Debug.WriteLine("[OtherViewModel] 开始加载配置...");

                var enabled = _localSettingsService.ReadSettingAsync("AdditionalProgramEnabled").Result;
                var path = _localSettingsService.ReadSettingAsync("AdditionalProgramPath").Result;
                IsAdditionalProgramEnabled = enabled != null && Convert.ToBoolean(enabled);
                AdditionalProgramPath = path?.ToString()?.Trim('"') ?? string.Empty;

                var autoClickerEnabled = _localSettingsService.ReadSettingAsync("AutoClickerEnabled").Result;
                var triggerKey = _localSettingsService.ReadSettingAsync("AutoClickerTriggerKey").Result;
                var clickKey = _localSettingsService.ReadSettingAsync("AutoClickerClickKey").Result;

                Debug.WriteLine($"[OtherViewModel] 原始配置 - Enabled: {autoClickerEnabled}, TriggerKey: {triggerKey}, ClickKey: {clickKey}");

                IsAutoClickerEnabled = autoClickerEnabled != null && Convert.ToBoolean(autoClickerEnabled);
                _autoClickerService.IsEnabled = IsAutoClickerEnabled;

                TriggerKey = triggerKey?.ToString()?.Trim('"') ?? "F8";
                ClickKey = clickKey?.ToString()?.Trim('"') ?? "F";

                if (Enum.TryParse<VirtualKey>(TriggerKey, out var tk))
                {
                    _autoClickerService.TriggerKey = tk;
                    Debug.WriteLine($"[OtherViewModel] 触发键解析成功: {tk}");
                }

                if (Enum.TryParse<VirtualKey>(ClickKey, out var ck))
                {
                    _autoClickerService.ClickKey = ck;
                    Debug.WriteLine($"[OtherViewModel] 连点键解析成功: {ck}");
                }

                Debug.WriteLine($"[OtherViewModel] 最终配置 - 启用: {IsAutoClickerEnabled}, 触发键: {TriggerKey}, 连点键: {ClickKey}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OtherViewModel] 加载配置失败: {ex.Message}");
            }
        }

        private void StartRecordingTriggerKey()
        {
            IsRecordingTriggerKey = true;
            IsRecordingClickKey = false;
            Debug.WriteLine("[OtherViewModel] 开始录制触发键");
        }

        private void StartRecordingClickKey()
        {
            IsRecordingClickKey = true;
            IsRecordingTriggerKey = false;
            Debug.WriteLine("[OtherViewModel] 开始录制连点键");
        }

        private async Task BrowseProgramAsync()
        {
            try
            {
                if (!_dispatcherQueue.HasThreadAccess)
                {
                    Debug.WriteLine("[错误] BrowseProgramAsync 不在UI线程上执行");
                    return;
                }

                var mainWindow = App.MainWindow;
                if (mainWindow == null)
                {
                    await ShowErrorAsync("无法获取主窗口句柄");
                    return;
                }

                var hwnd = WindowNative.GetWindowHandle(mainWindow);
                if (hwnd == IntPtr.Zero)
                {
                    StatusMessage = "错误：窗口句柄无效";
                    await ShowErrorAsync("窗口句柄无效，请以普通用户模式运行");
                    return;
                }

                var picker = new FileOpenPicker
                {
                    ViewMode = PickerViewMode.List,
                    SuggestedStartLocation = PickerLocationId.Desktop,
                    FileTypeFilter = { ".exe" }
                };

                try
                {
                    InitializeWithWindow.Initialize(picker, hwnd);
                }
                catch (Exception initEx)
                {
                    Debug.WriteLine($"[警告] InitializeWithWindow失败: {initEx.Message}");
                }

                var file = await picker.PickSingleFileAsync();

                if (file != null)
                {
                    var path = file.Path.Trim('"');
                    Debug.WriteLine($"[OtherViewModel] 用户选择程序: '{path}'");

                    if (File.Exists(path))
                    {
                        AdditionalProgramPath = path;
                    }
                    else
                    {
                        await ShowErrorAsync("文件不存在或无法访问");
                    }
                }
                else
                {
                    Debug.WriteLine("[OtherViewModel] 用户取消了文件选择");
                }
            }
            catch (UnauthorizedAccessException)
            {
                await ShowErrorAsync("权限错误：请以普通用户身份运行程序选择文件");
                Debug.WriteLine("[严重错误] 管理员模式权限问题");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择程序失败: {ex.Message}\n堆栈: {ex.StackTrace}");
                await ShowErrorAsync($"选择程序失败: {ex.Message}");
            }
        }
        private async Task ShowErrorAsync(string message)
        {
            try
            {
                await _dispatcherQueue.EnqueueAsync(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "操作失败",
                        Content = message,
                        CloseButtonText = "确定",
                        XamlRoot = App.MainWindow.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"显示错误对话框失败: {ex.Message}");
                StatusMessage = $"错误: {message}";
            }
        }
        partial void OnIsAutoClickerEnabledChanged(bool value)
        {
            _autoClickerService.IsEnabled = value;
            _ = SaveSettingsAsync();
            Debug.WriteLine($"[OtherViewModel] 连点器启用状态切换: {value}");
        }

        public void UpdateKey(string keyType, VirtualKey key)
        {
            var keyStr = key.ToString();
            Debug.WriteLine($"[OtherViewModel] 更新按键 - 类型: {keyType}, 按键: {keyStr}");

            if (keyType == "Trigger")
            {
                TriggerKey = keyStr;
                _autoClickerService.TriggerKey = key;
            }
            else if (keyType == "Click")
            {
                ClickKey = keyStr;
                _autoClickerService.ClickKey = key;
            }

            IsRecordingTriggerKey = false;
            IsRecordingClickKey = false;

            _ = SaveSettingsAsync();
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                string cleanPath = AdditionalProgramPath.Trim('"');
                await _localSettingsService.SaveSettingAsync("AdditionalProgramEnabled", IsAdditionalProgramEnabled);
                await _localSettingsService.SaveSettingAsync("AdditionalProgramPath", cleanPath);
                await _localSettingsService.SaveSettingAsync("AutoClickerEnabled", IsAutoClickerEnabled);

                await _localSettingsService.SaveSettingAsync("AutoClickerTriggerKey", TriggerKey);
                await _localSettingsService.SaveSettingAsync("AutoClickerClickKey", ClickKey);

                Debug.WriteLine($"[连点器] 配置保存成功 - 启用: {IsAutoClickerEnabled}, 触发键: {TriggerKey}, 连点键: {ClickKey}");

                _ = Task.Delay(2000).ContinueWith(_ =>
                    _dispatcherQueue?.TryEnqueue(() => StatusMessage = string.Empty));
                AdditionalProgramPath = cleanPath;
            }
            catch (Exception ex)
            {
                StatusMessage = $"保存失败: {ex.Message}";
                Debug.WriteLine($"[连点器] 配置保存失败: {ex.Message}");
            }
        }
    }
}