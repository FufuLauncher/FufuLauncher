using System.Diagnostics;
using System.Security.Principal;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Services;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using Windows.System;
using FufuLauncher.Views;
using WinRT.Interop;

namespace FufuLauncher.ViewModels
{
    public partial class OtherViewModel : ObservableObject
    {
        private readonly ILocalSettingsService _localSettingsService;
        private readonly IAutoClickerService _autoClickerService;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private bool _isInitializing;
        private bool _isReverting;
        public IRelayCommand OpenBrowserCommand { get; }

        [ObservableProperty] private bool _isAdditionalProgramEnabled;
        [ObservableProperty] private string _additionalProgramPath = string.Empty;
        [ObservableProperty] private string _statusMessage = string.Empty;

        [ObservableProperty] private bool _isAutoClickerEnabled;
        [ObservableProperty] private string _triggerKey = "F";
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
            OpenBrowserCommand = new RelayCommand(OpenBrowserWindow);
            
            LoadSettings();
        }
        
        
        
        private void OpenBrowserWindow()
        {
            try
            {
                if (_dispatcherQueue.HasThreadAccess)
                {
                    var newWindow = new BrowserWindow();
                    newWindow.Activate();
                }
                else
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        var newWindow = new BrowserWindow();
                        newWindow.Activate();
                    });
                }
                Debug.WriteLine("[OtherViewModel] 浏览器窗口已创建");
            }
            catch (Exception ex)
            {
                StatusMessage = $"打开浏览器失败: {ex.Message}";
                Debug.WriteLine($"[OtherViewModel] 打开浏览器失败: {ex.Message}");
            }
        }

        partial void OnAdditionalProgramPathChanged(string value)
        {
            IsApplyButtonEnabled = !string.IsNullOrWhiteSpace(value);

            if (!string.IsNullOrWhiteSpace(value))
            {
                var trimmedPath = value.Trim('"');
                if (File.Exists(trimmedPath) && Path.GetExtension(trimmedPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
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
        
        private bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        
        private async Task ShowAdminRequiredDialogAsync()
        {
            try
            {
                await _dispatcherQueue.EnqueueAsync(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "需要管理员权限",
                        Content = "使用全局连点器功能需要管理员权限才能正常拦截和发送按键\n\n请关闭本程序，右键选择“以管理员身份运行”后再次尝试",
                        CloseButtonText = "我知道了",
                        XamlRoot = App.MainWindow.Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"显示管理员提示对话框失败: {ex.Message}");
                StatusMessage = "错误: 缺少管理员权限，请重启程序";
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
                
                _isInitializing = true;
                IsAutoClickerEnabled = autoClickerEnabled != null && Convert.ToBoolean(autoClickerEnabled);
                _autoClickerService.IsEnabled = IsAutoClickerEnabled;
                _isInitializing = false;

                TriggerKey = triggerKey?.ToString()?.Trim('"') ?? "F";
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
        
        private async Task<bool> ShowLatencyWarningDialogAsync()
        {
            bool result = false;
            try
            {
                await _dispatcherQueue.EnqueueAsync(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "风险提示",
                        Content = "开启连点器功能将会安装全局键盘拦截钩子，这可能会导致您的所有按键操作出现轻微延迟\n\n您确定要开启此功能吗？",
                        PrimaryButtonText = "确认开启",
                        CloseButtonText = "取消",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = App.MainWindow.Content.XamlRoot
                    };
                    var dialogResult = await dialog.ShowAsync();
                    result = dialogResult == ContentDialogResult.Primary;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"显示延迟警告对话框失败: {ex.Message}");
            }
            return result;
        }
        
        partial void OnIsAutoClickerEnabledChanged(bool value)
        {
            if (_isInitializing || _isReverting) return;

            if (value)
            {
                Debug.WriteLine("[OtherViewModel] 拦截开启请求，弹出风险提示");
                _ = HandleAutoClickerEnableRequestAsync();
            }
            else
            {
                _autoClickerService.IsEnabled = false;
                _ = SaveSettingsAsync();
                Debug.WriteLine($"[OtherViewModel] 连点器启用状态切换: {value}");
            }
        }

        private async Task HandleAutoClickerEnableRequestAsync()
        {
            bool confirmed = await ShowLatencyWarningDialogAsync();

            if (!confirmed)
            {
                Debug.WriteLine("[OtherViewModel] 用户取消开启连点器");
                _isReverting = true;
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsAutoClickerEnabled = false;
                    _isReverting = false;
                });
                return;
            }
            
            if (!IsAdministrator())
            {
                Debug.WriteLine("[OtherViewModel] 尝试启用连点器，但没有管理员权限被拦截");
                _isReverting = true;
                _dispatcherQueue.TryEnqueue(() =>
                {
                    IsAutoClickerEnabled = false;
                    _isReverting = false;
                });
                _ = ShowAdminRequiredDialogAsync();
                return;
            }
            
            _autoClickerService.IsEnabled = true;
            _ = SaveSettingsAsync();
            Debug.WriteLine($"[OtherViewModel] 连点器启用状态切换: True");
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