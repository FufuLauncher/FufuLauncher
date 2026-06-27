/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.RegularExpressions;
using FufuLauncher.Helpers;
using FufuLauncher.Constants;
using MihoyoBBS;

namespace FufuLauncher.ViewModels
{

    public enum WindowBackdropType
    {
        None = 0,
        Acrylic = 1,
        Mica = 2
    }
    public enum AppLanguage
    {
        Default = 0,
        zhCN = 1,
        zhTW = 2
    }

    public enum WindowModeType
    {
        Normal,
        Popup
    }
    
    public enum AppProcessPriority
    {
        Normal = 0,
        AboveNormal = 1,
        High = 2
    }

    public partial class SettingsViewModel : ObservableRecipient
    {
        private readonly IThemeSelectorService _themeSelectorService;
        private readonly IBackgroundRenderer _backgroundRenderer;
        private readonly ILocalSettingsService _localSettingsService;
        private readonly INavigationService _navigationService;
        private readonly IGameLauncherService _gameLauncherService;
        private readonly IFilePickerService _filePickerService;
        private readonly AccountManager _accountManager;
        public record MonitorItem(string DisplayName, int Index);

        [ObservableProperty] private ElementTheme _elementTheme;
        [ObservableProperty] private string _versionDescription;
        public string AppVersion => $"版本 {Assembly.GetEntryAssembly()?.GetName().Version}";
        [ObservableProperty] private ServerType _selectedServer;
        [ObservableProperty] private bool _isBackgroundEnabled = true;
        [ObservableProperty] private AppLanguage _selectedLanguage;
        [ObservableProperty] private bool _minimizeToTray;
        [ObservableProperty] private string _customLaunchParameters = "";
        [ObservableProperty] private WindowModeType _launchArgsWindowMode = WindowModeType.Normal;
        [ObservableProperty] private string _launchArgsWidth;
        [ObservableProperty] private string _launchArgsHeight;
        [ObservableProperty] private string _launchArgsPreview = "";
        [ObservableProperty] private string _customBackgroundPath;
        [ObservableProperty] private bool _hasCustomBackground;
        
        [ObservableProperty] private bool _isBackgroundSlideshowEnabled;
        [ObservableProperty] private string _backgroundSlideshowFolder;
        [ObservableProperty] private bool _hasBackgroundSlideshowFolder;
        [ObservableProperty] private int _backgroundSlideshowInterval = 60; // seconds

        [ObservableProperty] private string _appThemeColor = ""; // empty means default
        [ObservableProperty] private Windows.UI.Color _appThemeColorObj = Windows.UI.Color.FromArgb(255, 0, 120, 215);

        [ObservableProperty] private double _panelBackgroundOpacity = 0.5;
        [ObservableProperty] private bool _isShortTermSupportEnabled;
        [ObservableProperty] private bool _isBetterGIIntegrationEnabled;
        [ObservableProperty] private bool _isBetterGICloseOnExitEnabled;
        private double _betterGIStartupDelaySeconds = 0.0;
        public double BetterGIStartupDelaySeconds
        {
            get => _betterGIStartupDelaySeconds;
            set
            {
                var clampedValue = double.IsNaN(value) ? 0.0 : Math.Clamp(value, 0.0, 60.0);
                if (SetProperty(ref _betterGIStartupDelaySeconds, clampedValue))
                {
                    _ = _localSettingsService.SaveSettingAsync("BetterGIStartupDelaySeconds", clampedValue);
                }
            }
        }
        [ObservableProperty] private double _globalBackgroundOverlayOpacity = 0.0;
        [ObservableProperty] private double _contentFrameBackgroundOpacity = 0.5;
        [ObservableProperty] private bool _isSaveWindowSizeEnabled;
        [ObservableProperty] private bool _isMinWindowSizeLimitEnabled = true;
        [ObservableProperty] private double _globalBackgroundImageOpacity = 1.0;
        [ObservableProperty] private bool _isAcrylicOverlayEnabled;
        
        [ObservableProperty] private bool _isHideGameNewsCardEnabled;
        [ObservableProperty] private bool _isHideCheckinCardEnabled;
        [ObservableProperty] private bool _isHideDailyNoteCardEnabled = true;

        [ObservableProperty] private bool _showDailyNoteResin = true;
        [ObservableProperty] private bool _showDailyNoteDailyTasks = true;
        [ObservableProperty] private bool _showDailyNoteHomeCoin = true;
        [ObservableProperty] private bool _showDailyNoteExpeditions = false;
        [ObservableProperty] private bool _showDailyNoteTransformer = false;

        [ObservableProperty] private string _gameNewsCardTextColor = "#FFFFFF";
        [ObservableProperty] private double _gameNewsCardTextOpacity = 1.0;
        [ObservableProperty] private string _launchButtonTextColor = "#FFFFFF";
        [ObservableProperty] private double _launchButtonTextOpacity = 1.0;
        [ObservableProperty] private string _gameCheckinTextColor = "#FFFFFF";
        [ObservableProperty] private double _gameCheckinTextOpacity = 1.0;

        [ObservableProperty] private WindowBackdropType _currentWindowBackdrop;
        [ObservableProperty] private string _webView2CacheSize;
        [ObservableProperty] private bool _isAutoCheckinEnabled;
        [ObservableProperty] private string _customGameExeName;
        
        [ObservableProperty] private ObservableCollection<MonitorItem> _availableMonitors = new();
        [ObservableProperty] private MonitorItem _selectedMonitor;
        [ObservableProperty] private int _launchArgsMonitorIndex = 0;
        
        [ObservableProperty] private bool _isShowPresetCardEnabled;
        
        [ObservableProperty] private bool _isShowWidgetCardEnabled;
        [ObservableProperty] private bool _showWidgetGacha = true;
        [ObservableProperty] private bool _showWidgetAchievement = true;
        [ObservableProperty] private bool _showWidgetInventory = true;
        [ObservableProperty] private bool _showWidgetPlayerRole = true;
        [ObservableProperty] private bool _showWidgetDailyNoteWindow = true;
        [ObservableProperty] private bool _showWidgetVideo = true;
        [ObservableProperty] private bool _showWidgetBBS = true;
        
        [ObservableProperty] private AppProcessPriority _appProcessPriority;
        
        [ObservableProperty] private string _customBackgroundApiUrl = "";
        [ObservableProperty] private string _currentBackgroundApiUrl = "";
        
        [ObservableProperty] private string _launchButtonOverlayColor = "#0078D7";
        [ObservableProperty] private bool _isCpuUsageWarningEnabled = true;
        [ObservableProperty] private double _cpuUsageWarningThreshold = ProcessCpuUsageMonitor.DefaultCpuThreshold;

        [ObservableProperty] private bool _isRedeemCodeNotificationEnabled = true;
        
        [ObservableProperty] private bool _isScreenshotEnabled;
        [ObservableProperty] private string _screenshotHotkey = "F12";
        [ObservableProperty] private string _screenshotSavePath;
        [ObservableProperty] private bool _hasScreenshotSavePath;

        partial void OnIsScreenshotEnabledChanged(bool value)
        {
            if (_isInitializing) return;
            _ = _localSettingsService.SaveSettingAsync("IsScreenshotEnabled", value);
        }

        partial void OnScreenshotHotkeyChanged(string value)
        {
            if (_isInitializing) return;
            _ = _localSettingsService.SaveSettingAsync("ScreenshotHotkey", value);
        }

        partial void OnScreenshotSavePathChanged(string value)
        {
            if (_isInitializing) return;
            HasScreenshotSavePath = !string.IsNullOrEmpty(value);
            _ = _localSettingsService.SaveSettingAsync("ScreenshotSavePath", value);
        }

        public IAsyncRelayCommand SelectScreenshotFolderCommand { get; }
        public IAsyncRelayCommand ClearScreenshotFolderCommand { get; }
        public IAsyncRelayCommand OpenScreenshotFolderCommand { get; }

        [ObservableProperty] private PostLaunchBehavior _postLaunchBehavior;

        public record PostLaunchBehaviorItem(string DisplayName, PostLaunchBehavior Value);

        public List<PostLaunchBehaviorItem> PostLaunchBehaviorItems { get; } = new()
        {
            new("不变", Models.PostLaunchBehavior.None),
            new("最小化到托盘", Models.PostLaunchBehavior.MinimizeToTray),
            new("保存状态并退出", Models.PostLaunchBehavior.Exit)
        };

        [ObservableProperty] private PostLaunchBehaviorItem _selectedPostLaunchBehaviorItem = null!;

        partial void OnSelectedPostLaunchBehaviorItemChanged(PostLaunchBehaviorItem value)
        {
            if (value == null) return;
            _postLaunchBehavior = value.Value;
            _ = _localSettingsService.SaveSettingAsync("PostLaunchBehavior", value.Value.ToString());
        }



        public ObservableCollection<NavItemConfig> NavItems { get; } = new();

        public async Task InitializeNavItemsAsync()
        {
            var allItems = new List<NavItemConfig>
            {
                new() { ViewModelKey = "FufuLauncher.ViewModels.MainViewModel",       DisplayName = "主页",           IconGlyph = "\uE80F", IsForceVisible = true },
                new() { ViewModelKey = "FufuLauncher.ViewModels.PluginSettingsViewModel", DisplayName = "注入设置",    IconGlyph = "\uEA86" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.ControlPanelModel",   DisplayName = "控制面板",       IconGlyph = "\uE80A" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.BlankViewModel",      DisplayName = "游戏设置",       IconGlyph = "\uE7FC" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.AccountViewModel",    DisplayName = "账户设置",       IconGlyph = "\uE77B" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.OtherViewModel",      DisplayName = "其他功能",       IconGlyph = "\uE71D" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.PluginViewModel",     DisplayName = "插件管理",       IconGlyph = "\uE7B5" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.DataViewModel",       DisplayName = "数据中心",       IconGlyph = "\uE9D9" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.HelpViewModel",       DisplayName = "帮助文档",       IconGlyph = "\uE82D" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.CommunityViewModel",  DisplayName = "VanillaBBS",     IconGlyph = "\uE716" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.CalculatorViewModel",  DisplayName = "养成计算",      IconGlyph = "\uE1D0" },
                new() { ViewModelKey = "FufuLauncher.ViewModels.SettingsViewModel",   DisplayName = "设置中心",       IconGlyph = "\uE713", IsForceVisible = true },
            };

            foreach (var item in allItems)
            {
                var val = await _localSettingsService.ReadSettingAsync($"NavVisible_{SanitizeKey(item.ViewModelKey)}");
                if (val is bool b)
                    item.IsUserVisible = b;
                else if (val is string str && bool.TryParse(str, out var parsed))
                    item.IsUserVisible = parsed;

        
                var captured = item;
                item.VisibilityChanged += async (_, _) =>
                {
                    var key = $"NavVisible_{SanitizeKey(captured.ViewModelKey)}";
                    await _localSettingsService.SaveSettingAsync(key, captured.IsUserVisible);
                    WeakReferenceMessenger.Default.Send(new NavigationVisibilityChangedMessage(captured));
                };

                NavItems.Add(item);
            }
        }

        private static string SanitizeKey(string viewModelKey)
        {

            var parts = viewModelKey.Split('.');
            return parts[^1];
        }

        partial void OnIsRedeemCodeNotificationEnabledChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("IsRedeemCodeNotificationEnabled", value);
        }

        partial void OnIsShowWidgetCardEnabledChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("IsShowWidgetCardEnabled", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }
        partial void OnShowWidgetGachaChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("ShowWidgetGacha", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }
        partial void OnShowWidgetAchievementChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("ShowWidgetAchievement", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }
        partial void OnShowWidgetInventoryChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("ShowWidgetInventory", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }
        partial void OnShowWidgetPlayerRoleChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("ShowWidgetPlayerRole", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }
        partial void OnShowWidgetDailyNoteWindowChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("ShowWidgetDailyNoteWindow", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }
        partial void OnShowWidgetVideoChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("ShowWidgetVideo", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }
        partial void OnShowWidgetBBSChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("ShowWidgetBBS", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }

        partial void OnIsShowPresetCardEnabledChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("IsShowPresetCardEnabled", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }

        partial void OnLaunchArgsMonitorIndexChanged(int value)
        {
            ApplyPresetsToText();
        }

        partial void OnSelectedMonitorChanged(MonitorItem value)
        {
            if (value != null && LaunchArgsMonitorIndex != value.Index)
            {
                LaunchArgsMonitorIndex = value.Index;
            }
        }
        
        private void LoadMonitors()
        {
            AvailableMonitors.Clear();
            AvailableMonitors.Add(new MonitorItem("默认 (不指定)", 0));
    
            var displayAreas = DisplayArea.FindAll();
            for (int i = 0; i < displayAreas.Count; i++)
            {
                int index = i + 1;
                AvailableMonitors.Add(new MonitorItem($"显示器 {index} ({displayAreas[i].OuterBounds.Width}x{displayAreas[i].OuterBounds.Height})", index));
            }

            SelectedMonitor = AvailableMonitors.FirstOrDefault(m => m.Index == LaunchArgsMonitorIndex) ?? AvailableMonitors.FirstOrDefault();
        }
        

        [ObservableProperty] private bool _isGameCheckinEnabled = true;
        [ObservableProperty] private bool _isBatchCheckinEnabled;
        [ObservableProperty] private bool _isCommunityCheckinEnabled = true;
        [ObservableProperty] private bool _isCommunityLikeEnabled;
        [ObservableProperty] private bool _isCommunityReadEnabled;
        [ObservableProperty] private bool _isCommunityShareEnabled;
        [ObservableProperty] private bool _isCloudGameCheckinEnabled;
        [ObservableProperty] private ObservableCollection<CheckinAccountItem> _checkinAccounts = new();
        [ObservableProperty] private bool _isLoadingCheckinAccounts;
        public IAsyncRelayCommand ResetGameExeNameCommand { get; }

        partial void OnIsAutoCheckinEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 自动签到设置变更为 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsAutoCheckinEnabled", value);
        }

        partial void OnIsGameCheckinEnabledChanged(bool value)
            => _ = _localSettingsService.SaveSettingAsync("IsGameCheckinEnabled", value);
        partial void OnIsCommunityCheckinEnabledChanged(bool value)
            => _ = _localSettingsService.SaveSettingAsync("IsCommunityCheckinEnabled", value);
        partial void OnIsCommunityLikeEnabledChanged(bool value)
            => _ = _localSettingsService.SaveSettingAsync("IsCommunityLikeEnabled", value);
        partial void OnIsCommunityReadEnabledChanged(bool value)
            => _ = _localSettingsService.SaveSettingAsync("IsCommunityReadEnabled", value);
        partial void OnIsCommunityShareEnabledChanged(bool value)
            => _ = _localSettingsService.SaveSettingAsync("IsCommunityShareEnabled", value);
        partial void OnIsCloudGameCheckinEnabledChanged(bool value)
            => _ = _localSettingsService.SaveSettingAsync("IsCloudGameCheckinEnabled", value);
        partial void OnIsBatchCheckinEnabledChanged(bool value)
            => _ = _localSettingsService.SaveSettingAsync("IsBatchCheckinEnabled", value);

        public IAsyncRelayCommand ClearWebView2CacheCommand { get; }
        public ICommand SwitchThemeCommand
        {
            get;
        }
        public ICommand SwitchLanguageCommand
        {
            get;
        }
        public ICommand SetResolutionPresetCommand
        {
            get;
        }
        public IAsyncRelayCommand SelectCustomBackgroundCommand
        {
            get;
        }

        public IAsyncRelayCommand SelectBackgroundSlideshowFolderCommand
        {
            get;
        }
        
        public IAsyncRelayCommand ClearBackgroundSlideshowFolderCommand
        {
            get;
        }

        public ICommand CheckUpdateCommand
        {
            get;
        }
        
        private bool _isInitializing;

        [ObservableProperty] private bool _isStartupSoundEnabled;
        [ObservableProperty] private string _startupSoundPath;
        [ObservableProperty] private bool _hasCustomStartupSound;

        public IAsyncRelayCommand SelectStartupSoundCommand
        {
            get;
        }
        public IAsyncRelayCommand ClearStartupSoundCommand
        {
            get;
        }
        
        public IAsyncRelayCommand ClearCustomBackgroundCommand
        {
            get;
        }
        
        public IAsyncRelayCommand DownloadLatestBackgroundImageCommand { get; }
        public IAsyncRelayCommand DownloadLatestBackgroundVideoCommand { get; }
        
        public IAsyncRelayCommand ResetBackgroundApiCommand { get; }
        public IAsyncRelayCommand ResetLaunchButtonOverlayColorCommand { get; }
        public IAsyncRelayCommand ResetCpuUsageWarningSettingsCommand { get; }
        
        private async Task ResetBackgroundApiAsync()
        {
            CustomBackgroundApiUrl = string.Empty;
            CurrentBackgroundApiUrl = GetDefaultBackgroundApiUrl(SelectedServer);
            await _localSettingsService.SaveSettingAsync("CustomBackgroundApiUrl", string.Empty);
            await _localSettingsService.SaveSettingAsync("BackgroundJsonHash", string.Empty);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", string.Empty);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }
        
        private static string GetDefaultBackgroundApiUrl(ServerType server)
        {
            return server switch
            {
                ServerType.CN => ApiEndpoints.BackgroundCnApi,
                ServerType.OS => ApiEndpoints.BackgroundOsApi,
                _ => ApiEndpoints.BackgroundCnApi
            };
        }
        
        private async Task ResetLaunchButtonOverlayColorAsync()
        {
            LaunchButtonOverlayColor = "#0078D7";
            await _localSettingsService.SaveSettingAsync("LaunchButtonOverlayColor", "#0078D7");
            WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
        }

        partial void OnLaunchButtonOverlayColorChanged(string value)
        {
            _localSettingsService.SaveSettingAsync("LaunchButtonOverlayColor", value);
            WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
        }

        partial void OnIsCpuUsageWarningEnabledChanged(bool value)
        {
            if (_isInitializing) return;
            _ = _localSettingsService.SaveSettingAsync(ProcessCpuUsageMonitor.IsEnabledSettingKey, value);
        }

        partial void OnCpuUsageWarningThresholdChanged(double value)
        {
            if (_isInitializing) return;
            _ = _localSettingsService.SaveSettingAsync(ProcessCpuUsageMonitor.ThresholdSettingKey, Math.Clamp(value, 5.0, 100.0));
        }

        private async Task ResetCpuUsageWarningSettingsAsync()
        {
            IsCpuUsageWarningEnabled = true;
            CpuUsageWarningThreshold = ProcessCpuUsageMonitor.DefaultCpuThreshold;
            await _localSettingsService.SaveSettingAsync(ProcessCpuUsageMonitor.IsEnabledSettingKey, true);
            await _localSettingsService.SaveSettingAsync(ProcessCpuUsageMonitor.ThresholdSettingKey, ProcessCpuUsageMonitor.DefaultCpuThreshold);
        }
        
        partial void OnAppProcessPriorityChanged(AppProcessPriority value)
        {
            _localSettingsService.SaveSettingAsync("AppProcessPriority", (int)value);
            ApplyProcessPriority(value);
        }

        private void ApplyProcessPriority(AppProcessPriority priority)
        {
            try
            {
                var process = Process.GetCurrentProcess();
                switch (priority)
                {
                    case AppProcessPriority.Normal:
                        process.PriorityClass = ProcessPriorityClass.Normal;
                        break;
                    case AppProcessPriority.AboveNormal:
                        process.PriorityClass = ProcessPriorityClass.AboveNormal;
                        break;
                    case AppProcessPriority.High:
                        process.PriorityClass = ProcessPriorityClass.High;
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置进程优先级失败: {ex.Message}");
            }
        }
        
        public SettingsViewModel(
            IThemeSelectorService themeSelectorService,
            IBackgroundRenderer backgroundRenderer,
            ILocalSettingsService localSettingsService,
            INavigationService navigationService,
            IGameLauncherService gameLauncherService,
            IFilePickerService filePickerService,
            AccountManager accountManager)
        {
            _themeSelectorService = themeSelectorService;
            _backgroundRenderer = backgroundRenderer;
            _localSettingsService = localSettingsService;
            _navigationService = navigationService;
            _gameLauncherService = gameLauncherService;
            _filePickerService = filePickerService;
            _accountManager = accountManager;

            InitializeDefaultResolution();

            SelectStartupSoundCommand = new AsyncRelayCommand(SelectStartupSoundAsync);
            ClearStartupSoundCommand = new AsyncRelayCommand(ClearStartupSound);
            CheckUpdateCommand = new RelayCommand(CheckUpdate);
            ElementTheme = _themeSelectorService.Theme;
            _versionDescription = GetVersionDescription();
            ClearWebView2CacheCommand = new AsyncRelayCommand(ClearWebView2CacheAsync);
            UpdateWebView2CacheSize();
            ClearCustomBackgroundCommand = new AsyncRelayCommand(ClearCustomBackgroundAsync);
            ResetGameExeNameCommand = new AsyncRelayCommand(ResetGameExeNameAsync);
            ResetBackgroundApiCommand = new AsyncRelayCommand(ResetBackgroundApiAsync);
            ResetCpuUsageWarningSettingsCommand = new AsyncRelayCommand(ResetCpuUsageWarningSettingsAsync);
            
            ResetLaunchButtonOverlayColorCommand = new AsyncRelayCommand(ResetLaunchButtonOverlayColorAsync);

            WeakReferenceMessenger.Default.Register<CloudCredentialUpdatedMessage>(this, (r, m) =>
            {
                if (CheckinAccounts != null)
                {
                    var account = CheckinAccounts.FirstOrDefault(a => a.Uid == m.Value);
                    if (account != null)
                    {
                        account.HasCloudCredential = true;
                    }
                }
            });

            SwitchThemeCommand = new RelayCommand<ElementTheme>(
                async (param) =>
                {
                    if (ElementTheme != param)
                    {
                        ElementTheme = param;
                        await _themeSelectorService.SetThemeAsync(param);
                    }
                });

            SwitchLanguageCommand = new RelayCommand<object>(
                async (param) =>
                {
                    try
                    {
                        int languageCode = Convert.ToInt32(param);
                        var language = (AppLanguage)languageCode;

                        if (SelectedLanguage != language)
                        {
                            SelectedLanguage = language;
                            await ApplyLanguageChangeAsync(language);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"语言切换失败: {ex.Message}");
                    }
                });

            SetResolutionPresetCommand = new RelayCommand<string>(
                (param) =>
                {
                    var parts = param.Split(' ');
                    if (parts.Length == 2)
                    {
                        LaunchArgsWidth = parts[0];
                        LaunchArgsHeight = parts[1];
                    }
                });

            SelectCustomBackgroundCommand = new AsyncRelayCommand(SelectCustomBackgroundAsync);
            SelectBackgroundSlideshowFolderCommand = new AsyncRelayCommand(SelectBackgroundSlideshowFolderAsync);
            ClearBackgroundSlideshowFolderCommand = new AsyncRelayCommand(ClearBackgroundSlideshowFolderAsync);

            DownloadLatestBackgroundImageCommand = new AsyncRelayCommand(DownloadLatestBackgroundImageAsync);
            DownloadLatestBackgroundVideoCommand = new AsyncRelayCommand(DownloadLatestBackgroundVideoAsync);

            SelectScreenshotFolderCommand = new AsyncRelayCommand(SelectScreenshotFolderAsync);
            ClearScreenshotFolderCommand = new AsyncRelayCommand(ClearScreenshotFolderAsync);
            OpenScreenshotFolderCommand = new AsyncRelayCommand(OpenScreenshotFolderAsync);
        }
        
        private async Task ClearCustomBackgroundAsync()
        {
            try
            {
                await _localSettingsService.SaveSettingAsync<string>("CustomBackgroundPath", null);
                CustomBackgroundPath = null;
                HasCustomBackground = false;
        
                WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除自定义背景失败: {ex.Message}");
            }
        }

        private async Task SelectScreenshotFolderAsync()
        {
            try
            {
                var folder = await _filePickerService.PickFolderAsync();
                if (!string.IsNullOrEmpty(folder))
                {
                    ScreenshotSavePath = folder;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择截图文件夹失败: {ex.Message}");
            }
        }

        private async Task ClearScreenshotFolderAsync()
        {
            ScreenshotSavePath = null;
            HasScreenshotSavePath = false;
            await _localSettingsService.SaveSettingAsync<string>("ScreenshotSavePath", null);
        }

        private async Task OpenScreenshotFolderAsync()
        {
            var path = ScreenshotSavePath;
            if (string.IsNullOrEmpty(path))
            {
                path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "FufuScreenshots");
            }

            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            else
            {
                Directory.CreateDirectory(path);
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            await Task.CompletedTask;
        }

        private async Task DownloadLatestBackgroundImageAsync()
        {
            try
            {
                var service = App.GetService<IHoyoverseBackgroundService>();
                var (imgUrl, _) = await service.GetLatestBackgroundUrlsAsync(SelectedServer);
                if (!string.IsNullOrEmpty(imgUrl))
                {
                    await DownloadAndSaveFileAsync(imgUrl, "背景图片", ".png");
                }
                else
                {
                    ShowDialogMessage("提示", "当前服务器没有可用的背景图片。");
                }
            }
            catch (Exception ex)
            {
                ShowDialogMessage("错误", $"下载图片失败: {ex.Message}");
            }
        }

        private async Task DownloadLatestBackgroundVideoAsync()
        {
            try
            {
                var service = App.GetService<IHoyoverseBackgroundService>();
                var (_, videoUrl) = await service.GetLatestBackgroundUrlsAsync(SelectedServer);
                if (!string.IsNullOrEmpty(videoUrl))
                {
                    await DownloadAndSaveFileAsync(videoUrl, "背景视频", ".mp4");
                }
                else
                {
                    ShowDialogMessage("提示", "当前服务器没有可用的背景视频。");
                }
            }
            catch (Exception ex)
            {
                ShowDialogMessage("错误", $"下载视频失败: {ex.Message}");
            }
        }

        private async Task DownloadAndSaveFileAsync(string url, string typeName, string extension)
        {
            var savePicker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

            savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary;
            if (extension == ".mp4")
            {
                savePicker.FileTypeChoices.Add("视频文件", new List<string> { ".mp4" });
                savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.VideosLibrary;
            }
            else
            {
                savePicker.FileTypeChoices.Add("图片文件", new List<string> { ".png", ".jpg" });
            }
            savePicker.SuggestedFileName = $"FufuLauncher_{typeName}_{DateTime.Now:yyyyMMddHHmmss}";

            var file = await savePicker.PickSaveFileAsync();
            if (file == null) return;

            using var httpClient = new HttpClient();
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = await file.OpenStreamForWriteAsync();
            await stream.CopyToAsync(fileStream);

            ShowDialogMessage("下载成功", $"{typeName} 已保存至：\n{file.Path}");
        }

        private async void ShowDialogMessage(string title, string content)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = "确定",
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch { }
        }

        
        private string FormatSize(long bytes)
        {
            if (bytes == 0) return "0 B";
    
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
    
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return string.Format("{0:n2} {1}", number, suffixes[counter]);
        }
        
        private void UpdateWebView2CacheSize()
        {
            try
            {
                string cacheFolder = Path.Combine(AppContext.BaseDirectory, "FufuLauncher.exe.WebView2");
                if (Directory.Exists(cacheFolder))
                {
                    long size = GetDirectorySize(new DirectoryInfo(cacheFolder));
                    WebView2CacheSize = FormatSize(size);
                }
                else
                {
                    WebView2CacheSize = "0 MB";
                }
            }
            catch
            {
                WebView2CacheSize = "未知大小";
            }
        }
        
        partial void OnIsAcrylicOverlayEnabledChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("IsAcrylicOverlayEnabled", value);
            WeakReferenceMessenger.Default.Send(new OverlayStyleChangedMessage(value));
        }

        private long GetDirectorySize(DirectoryInfo d)
        {
            long size = 0;
            try
            {
                FileInfo[] fis = d.GetFiles();
                foreach (FileInfo fi in fis)
                {
                    size += fi.Length;
                }
                
                DirectoryInfo[] dis = d.GetDirectories();
                foreach (DirectoryInfo di in dis)
                {
                    size += GetDirectorySize(di);
                }
            }
            catch
            {
                // ignored
            }

            return size;
        }

        private async Task ClearWebView2CacheAsync()
        {
            try
            {
                var cacheFolder = Path.Combine(AppContext.BaseDirectory, "FufuLauncher.exe.WebView2");
        
                if (Directory.Exists(cacheFolder))
                {
                    await Task.Run(() => SafeDeleteDirectory(cacheFolder));
                }
                
                UpdateWebView2CacheSize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除 WebView2 缓存失败: {ex.Message}");
            }
        }
        
        private void SafeDeleteDirectory(string targetDir)
        {
            try
            {
                var files = Directory.GetFiles(targetDir);
                var dirs = Directory.GetDirectories(targetDir);
                
                foreach (var file in files)
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch
                    {
                        // ignored
                    }
                }
                
                foreach (var dir in dirs)
                {
                    SafeDeleteDirectory(dir);
                    try
                    {
                        Directory.Delete(dir, false);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            catch
            {
                // ignored
            }
        }
        
        private void CheckUpdate()
        {
            try
            {
                string updaterPath = Path.Combine(AppContext.BaseDirectory, "UpdateFufuLauncher.exe");
                
                if (File.Exists(updaterPath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = updaterPath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    Process.Start(startInfo);
                }
                else
                {
                    Debug.WriteLine("未找到 UpdateFufuLauncher.exe");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动更新程序失败: {ex.Message}");
            }
        }
        
        partial void OnLaunchArgsWidthChanged(string value) => ApplyPresetsToText();
        partial void OnLaunchArgsHeightChanged(string value) => ApplyPresetsToText();
        partial void OnLaunchArgsWindowModeChanged(WindowModeType value) => ApplyPresetsToText();

        private void InitializeDefaultResolution()
        {
            try
            {
                var displayArea = DisplayArea.Primary;

                if (displayArea != null)
                {
                    _launchArgsWidth = displayArea.OuterBounds.Width.ToString();
                    _launchArgsHeight = displayArea.OuterBounds.Height.ToString();
                }
                else
                {
                    _launchArgsWidth = "1920";
                    _launchArgsHeight = "1080";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取屏幕分辨率失败: {ex.Message}，使用默认值。");
                _launchArgsWidth = "1920";
                _launchArgsHeight = "1080";
            }
        }

        public async Task ReloadSettingsAsync()
        {
            _isLoadingLaunchParams = true;
            _isInitializing = true;

            try
            {
                await LoadUserPreferencesAsync();
                await LoadCustomBackgroundSettingsAsync();
                await InitializeNavItemsAsync();
                
                OnPropertyChanged(nameof(IsStartupSoundEnabled));
                OnPropertyChanged(nameof(StartupSoundPath));
                OnPropertyChanged(nameof(HasCustomStartupSound));
                OnPropertyChanged(nameof(ElementTheme));
                OnPropertyChanged(nameof(SelectedServer));
                OnPropertyChanged(nameof(IsBackgroundEnabled));
                OnPropertyChanged(nameof(SelectedLanguage));
                OnPropertyChanged(nameof(MinimizeToTray));
                OnPropertyChanged(nameof(CustomLaunchParameters));
                OnPropertyChanged(nameof(LaunchArgsWindowMode));
                OnPropertyChanged(nameof(LaunchArgsWidth));
                OnPropertyChanged(nameof(LaunchArgsHeight));
                OnPropertyChanged(nameof(CustomBackgroundPath));
                OnPropertyChanged(nameof(HasCustomBackground));
                OnPropertyChanged(nameof(IsBackgroundSlideshowEnabled));
                OnPropertyChanged(nameof(BackgroundSlideshowFolder));
                OnPropertyChanged(nameof(HasBackgroundSlideshowFolder));
                OnPropertyChanged(nameof(BackgroundSlideshowInterval));
                OnPropertyChanged(nameof(CustomBackgroundApiUrl));
                OnPropertyChanged(nameof(CurrentBackgroundApiUrl));
                OnPropertyChanged(nameof(AppThemeColor));
                OnPropertyChanged(nameof(CurrentWindowBackdrop));
                OnPropertyChanged(nameof(IsShortTermSupportEnabled));
                OnPropertyChanged(nameof(IsBetterGIIntegrationEnabled));
                OnPropertyChanged(nameof(IsBetterGICloseOnExitEnabled));
                OnPropertyChanged(nameof(BetterGIStartupDelaySeconds));
                OnPropertyChanged(nameof(GlobalBackgroundOverlayOpacity));
                OnPropertyChanged(nameof(ContentFrameBackgroundOpacity));
                OnPropertyChanged(nameof(IsSaveWindowSizeEnabled));
                OnPropertyChanged(nameof(IsMinWindowSizeLimitEnabled));
                OnPropertyChanged(nameof(IsHideGameNewsCardEnabled));
                OnPropertyChanged(nameof(IsHideCheckinCardEnabled));
                OnPropertyChanged(nameof(IsAcrylicOverlayEnabled));
                OnPropertyChanged(nameof(IsAutoCheckinEnabled));
                OnPropertyChanged(nameof(AppProcessPriority));
                OnPropertyChanged(nameof(IsCpuUsageWarningEnabled));
                OnPropertyChanged(nameof(CpuUsageWarningThreshold));
                OnPropertyChanged(nameof(IsRedeemCodeNotificationEnabled));
                LoadMonitors();
            }
            finally
            {
                _isLoadingLaunchParams = false;
                _isInitializing = false;
            }

            await LoadCheckinAccountsAsync();
        }

        private bool _isUpdatingDailyNote;

        private async Task LoadUserPreferencesAsync()
        {
            var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            int serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
            SelectedServer = (ServerType)serverValue;
            var customBackgroundApiJson = await _localSettingsService.ReadSettingAsync("CustomBackgroundApiUrl");
            CustomBackgroundApiUrl = customBackgroundApiJson?.ToString() ?? string.Empty;
            CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(CustomBackgroundApiUrl)
                ? GetDefaultBackgroundApiUrl(SelectedServer)
                : CustomBackgroundApiUrl;

            var enabledJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.IsBackgroundEnabledKey);
            IsBackgroundEnabled = enabledJson == null ? true : Convert.ToBoolean(enabledJson);

            var languageJson = await _localSettingsService.ReadSettingAsync("AppLanguage");
            int languageValue = languageJson != null ? Convert.ToInt32(languageJson) : 0;
            SelectedLanguage = (AppLanguage)languageValue;

            var trayJson = await _localSettingsService.ReadSettingAsync("MinimizeToTray");
            MinimizeToTray = trayJson != null && Convert.ToBoolean(trayJson);
            
            var acrylicOverlayJson = await _localSettingsService.ReadSettingAsync("IsAcrylicOverlayEnabled");
            IsAcrylicOverlayEnabled = acrylicOverlayJson != null && Convert.ToBoolean(acrylicOverlayJson);
            
            var launchOverlayColorJson = await _localSettingsService.ReadSettingAsync("LaunchButtonOverlayColor");
            LaunchButtonOverlayColor = launchOverlayColorJson?.ToString() ?? "#0078D7";

            var paramsJson = await _localSettingsService.ReadSettingAsync("CustomLaunchParameters");
            if (paramsJson != null)
            {
                CustomLaunchParameters = paramsJson.ToString();
                ParseLaunchParameters(CustomLaunchParameters);
            }

            var backdropJson = await _localSettingsService.ReadSettingAsync("WindowBackdrop");
            if (backdropJson != null)
            {
                CurrentWindowBackdrop = (WindowBackdropType)Convert.ToInt32(backdropJson);
            }
            else
            {
                CurrentWindowBackdrop = WindowBackdropType.None;
            }

            var appThemeColorJson = await _localSettingsService.ReadSettingAsync("AppThemeColor");
            if (appThemeColorJson != null)
            {
                AppThemeColor = appThemeColorJson.ToString();
            }
            else
            {
                AppThemeColor = "";
            }

            var shortTermJson = await _localSettingsService.ReadSettingAsync("IsShortTermSupportEnabled");
            IsShortTermSupportEnabled = shortTermJson != null && Convert.ToBoolean(shortTermJson);

            var betterGIJson = await _localSettingsService.ReadSettingAsync("IsBetterGIIntegrationEnabled");
            IsBetterGIIntegrationEnabled = betterGIJson != null && Convert.ToBoolean(betterGIJson);

            var betterGICloseJson = await _localSettingsService.ReadSettingAsync("IsBetterGICloseOnExitEnabled");
            IsBetterGICloseOnExitEnabled = betterGICloseJson != null && Convert.ToBoolean(betterGICloseJson);

            var betterGIDelayJson = await _localSettingsService.ReadSettingAsync("BetterGIStartupDelaySeconds");
            BetterGIStartupDelaySeconds = betterGIDelayJson != null ? Math.Clamp(Convert.ToDouble(betterGIDelayJson), 0.0, 60.0) : 0.0;

            var soundJson = await _localSettingsService.ReadSettingAsync("IsStartupSoundEnabled");
            IsStartupSoundEnabled = soundJson != null && Convert.ToBoolean(soundJson);
            
            var autoCheckinJson = await _localSettingsService.ReadSettingAsync("IsAutoCheckinEnabled");
            IsAutoCheckinEnabled = autoCheckinJson != null && Convert.ToBoolean(autoCheckinJson);

            var cpuWarningEnabledJson = await _localSettingsService.ReadSettingAsync(ProcessCpuUsageMonitor.IsEnabledSettingKey);
            IsCpuUsageWarningEnabled = cpuWarningEnabledJson == null || Convert.ToBoolean(cpuWarningEnabledJson);

var cpuWarningThresholdJson = await _localSettingsService.ReadSettingAsync(ProcessCpuUsageMonitor.ThresholdSettingKey);
            CpuUsageWarningThreshold = cpuWarningThresholdJson != null
                ? Math.Clamp(Convert.ToDouble(cpuWarningThresholdJson), 5.0, 100.0)
                : ProcessCpuUsageMonitor.DefaultCpuThreshold;

            var redeemNotifyJson = await _localSettingsService.ReadSettingAsync("IsRedeemCodeNotificationEnabled");
            IsRedeemCodeNotificationEnabled = redeemNotifyJson == null || Convert.ToBoolean(redeemNotifyJson);

            var behaviorJson = await _localSettingsService.ReadSettingAsync("PostLaunchBehavior");
            PostLaunchBehavior postLaunchBehavior = PostLaunchBehavior.None;
            if (behaviorJson is string behaviorStr && Enum.TryParse<PostLaunchBehavior>(behaviorStr, out var parsed))
                postLaunchBehavior = parsed;
            _postLaunchBehavior = postLaunchBehavior;
            SelectedPostLaunchBehaviorItem = PostLaunchBehaviorItems.First(i => i.Value == postLaunchBehavior);

            // 截图设置
            var screenshotEnabledJson = await _localSettingsService.ReadSettingAsync("IsScreenshotEnabled");
            IsScreenshotEnabled = screenshotEnabledJson != null && Convert.ToBoolean(screenshotEnabledJson);

            var screenshotHotkeyJson = await _localSettingsService.ReadSettingAsync("ScreenshotHotkey");
            ScreenshotHotkey = screenshotHotkeyJson?.ToString() ?? "F12";

            var screenshotPathJson = await _localSettingsService.ReadSettingAsync("ScreenshotSavePath");
            ScreenshotSavePath = screenshotPathJson?.ToString();
            HasScreenshotSavePath = !string.IsNullOrEmpty(ScreenshotSavePath);

            var customExeJson = await _localSettingsService.ReadSettingAsync(GameExeManager.CustomExeNameKey);
            CustomGameExeName = customExeJson?.ToString() ?? string.Empty;

            var soundPathJson = await _localSettingsService.ReadSettingAsync("StartupSoundPath");
            if (soundPathJson != null)
            {
                StartupSoundPath = soundPathJson.ToString();
                HasCustomStartupSound = File.Exists(StartupSoundPath);
            }
            else
            {
                StartupSoundPath = null;
                HasCustomStartupSound = false;
            }

            var overlayOpacityJson = await _localSettingsService.ReadSettingAsync("GlobalBackgroundOverlayOpacity");
            try
            {
                GlobalBackgroundOverlayOpacity = overlayOpacityJson != null ? Convert.ToDouble(overlayOpacityJson) : 0;
            }
            catch
            {
                GlobalBackgroundOverlayOpacity = 0;
            }

            var frameOpacityJson = await _localSettingsService.ReadSettingAsync("ContentFrameBackgroundOpacity");
            try
            {
                ContentFrameBackgroundOpacity = frameOpacityJson != null ? Convert.ToDouble(frameOpacityJson) : 0.5;
            }
            catch
            {
                ContentFrameBackgroundOpacity = 0.5;
            }
            
            var gameNewsCardColorJson = await _localSettingsService.ReadSettingAsync("GameNewsCardTextColor");
            GameNewsCardTextColor = gameNewsCardColorJson?.ToString() ?? "#FFFFFF";
            
            var gameNewsCardOpacityJson = await _localSettingsService.ReadSettingAsync("GameNewsCardTextOpacity");
            GameNewsCardTextOpacity = gameNewsCardOpacityJson != null ? Convert.ToDouble(gameNewsCardOpacityJson) : 1.0;

            var launchBtnColorJson = await _localSettingsService.ReadSettingAsync("LaunchButtonTextColor");
            LaunchButtonTextColor = launchBtnColorJson?.ToString() ?? "#FFFFFF";
            
            var launchBtnOpacityJson = await _localSettingsService.ReadSettingAsync("LaunchButtonTextOpacity");
            LaunchButtonTextOpacity = launchBtnOpacityJson != null ? Convert.ToDouble(launchBtnOpacityJson) : 1.0;

            var checkinColorJson = await _localSettingsService.ReadSettingAsync("GameCheckinTextColor");
            GameCheckinTextColor = checkinColorJson?.ToString() ?? "#FFFFFF";
            
            var checkinOpacityJson = await _localSettingsService.ReadSettingAsync("GameCheckinTextOpacity");
            GameCheckinTextOpacity = checkinOpacityJson != null ? Convert.ToDouble(checkinOpacityJson) : 1.0;

            var saveWindowSizeJson = await _localSettingsService.ReadSettingAsync("IsSaveWindowSizeEnabled");
            IsSaveWindowSizeEnabled = saveWindowSizeJson != null && Convert.ToBoolean(saveWindowSizeJson);

            var minSizeLimitJson = await _localSettingsService.ReadSettingAsync("IsMinWindowSizeLimitEnabled");
            IsMinWindowSizeLimitEnabled = minSizeLimitJson == null || Convert.ToBoolean(minSizeLimitJson);

            var hideNewsCardJson = await _localSettingsService.ReadSettingAsync("IsHideGameNewsCardEnabled");
            IsHideGameNewsCardEnabled = hideNewsCardJson != null && Convert.ToBoolean(hideNewsCardJson);

            var hideCheckinCardJson = await _localSettingsService.ReadSettingAsync("IsHideCheckinCardEnabled");
            IsHideCheckinCardEnabled = hideCheckinCardJson != null && Convert.ToBoolean(hideCheckinCardJson);

            var hideDailyNoteCardJson = await _localSettingsService.ReadSettingAsync("IsHideDailyNoteCardEnabled");
            IsHideDailyNoteCardEnabled = hideDailyNoteCardJson == null || Convert.ToBoolean(hideDailyNoteCardJson);

            _isUpdatingDailyNote = true;
            int activeCount = 0;

            var showResinJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteResin");
            ShowDailyNoteResin = showResinJson == null || Convert.ToBoolean(showResinJson);
            if (ShowDailyNoteResin) activeCount++;

            var showDailyTasksJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteDailyTasks");
            ShowDailyNoteDailyTasks = (showDailyTasksJson == null || Convert.ToBoolean(showDailyTasksJson)) && activeCount < 3;
            if (ShowDailyNoteDailyTasks) activeCount++;

            var showHomeCoinJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteHomeCoin");
            ShowDailyNoteHomeCoin = (showHomeCoinJson == null || Convert.ToBoolean(showHomeCoinJson)) && activeCount < 3;
            if (ShowDailyNoteHomeCoin) activeCount++;

            var showExpeditionsJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteExpeditions");
            ShowDailyNoteExpeditions = (showExpeditionsJson == null || Convert.ToBoolean(showExpeditionsJson)) && activeCount < 3;
            if (ShowDailyNoteExpeditions) activeCount++;

            var showTransformerJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteTransformer");
            ShowDailyNoteTransformer = (showTransformerJson == null || Convert.ToBoolean(showTransformerJson)) && activeCount < 3;
            
            var showPresetCardJson = await _localSettingsService.ReadSettingAsync("IsShowPresetCardEnabled");
            IsShowPresetCardEnabled = showPresetCardJson != null && Convert.ToBoolean(showPresetCardJson);
    
            _isUpdatingDailyNote = false;
            
            var showWidgetCardJson = await _localSettingsService.ReadSettingAsync("IsShowWidgetCardEnabled");
            IsShowWidgetCardEnabled = showWidgetCardJson != null && Convert.ToBoolean(showWidgetCardJson);

            var showWidgetGachaJson = await _localSettingsService.ReadSettingAsync("ShowWidgetGacha");
            ShowWidgetGacha = showWidgetGachaJson == null || Convert.ToBoolean(showWidgetGachaJson);
            
            var showWidgetAchievementJson = await _localSettingsService.ReadSettingAsync("ShowWidgetAchievement");
            ShowWidgetAchievement = showWidgetAchievementJson == null || Convert.ToBoolean(showWidgetAchievementJson);
            
            var showWidgetInventoryJson = await _localSettingsService.ReadSettingAsync("ShowWidgetInventory");
            ShowWidgetInventory = showWidgetInventoryJson == null || Convert.ToBoolean(showWidgetInventoryJson);
            
            var showWidgetPlayerRoleJson = await _localSettingsService.ReadSettingAsync("ShowWidgetPlayerRole");
            ShowWidgetPlayerRole = showWidgetPlayerRoleJson == null || Convert.ToBoolean(showWidgetPlayerRoleJson);
            
            var showWidgetDailyNoteWindowJson = await _localSettingsService.ReadSettingAsync("ShowWidgetDailyNoteWindow");
            ShowWidgetDailyNoteWindow = showWidgetDailyNoteWindowJson == null || Convert.ToBoolean(showWidgetDailyNoteWindowJson);
            
            var showWidgetVideoJson = await _localSettingsService.ReadSettingAsync("ShowWidgetVideo");
            ShowWidgetVideo = showWidgetVideoJson == null || Convert.ToBoolean(showWidgetVideoJson);
            
            var showWidgetBBSJson = await _localSettingsService.ReadSettingAsync("ShowWidgetBBS");
            ShowWidgetBBS = showWidgetBBSJson == null || Convert.ToBoolean(showWidgetBBSJson);

            var panelOpacityJson = await _localSettingsService.ReadSettingAsync("PanelBackgroundOpacity");
            try
            {
                PanelBackgroundOpacity = panelOpacityJson != null ? Convert.ToDouble(panelOpacityJson) : 0.5;
            }
            catch
            {
                PanelBackgroundOpacity = 0.5;
            }
            var bgImageOpacityJson = await _localSettingsService.ReadSettingAsync("GlobalBackgroundImageOpacity");
            try
            {
                GlobalBackgroundImageOpacity = bgImageOpacityJson != null ? Convert.ToDouble(bgImageOpacityJson) : 1.0;
            }
            catch
            {
                GlobalBackgroundImageOpacity = 1.0;
            }

            var gameCheckinJson = await _localSettingsService.ReadSettingAsync("IsGameCheckinEnabled");
            IsGameCheckinEnabled = gameCheckinJson == null || Convert.ToBoolean(gameCheckinJson);

            var communityCheckinJson = await _localSettingsService.ReadSettingAsync("IsCommunityCheckinEnabled");
            IsCommunityCheckinEnabled = communityCheckinJson == null || Convert.ToBoolean(communityCheckinJson);

            var communityLikeJson = await _localSettingsService.ReadSettingAsync("IsCommunityLikeEnabled");
            IsCommunityLikeEnabled = communityLikeJson != null && Convert.ToBoolean(communityLikeJson);

            var communityReadJson = await _localSettingsService.ReadSettingAsync("IsCommunityReadEnabled");
            IsCommunityReadEnabled = communityReadJson != null && Convert.ToBoolean(communityReadJson);

            var communityShareJson = await _localSettingsService.ReadSettingAsync("IsCommunityShareEnabled");
            IsCommunityShareEnabled = communityShareJson != null && Convert.ToBoolean(communityShareJson);

            var cloudGameCheckinJson = await _localSettingsService.ReadSettingAsync("IsCloudGameCheckinEnabled");
            IsCloudGameCheckinEnabled = cloudGameCheckinJson != null && Convert.ToBoolean(cloudGameCheckinJson);

            var batchCheckinJson = await _localSettingsService.ReadSettingAsync("IsBatchCheckinEnabled");
            IsBatchCheckinEnabled = batchCheckinJson != null && Convert.ToBoolean(batchCheckinJson);
            
            var priorityJson = await _localSettingsService.ReadSettingAsync("AppProcessPriority");
            if (priorityJson != null)
            {
                AppProcessPriority = (AppProcessPriority)Convert.ToInt32(priorityJson);
            }
            else
            {
                AppProcessPriority = AppProcessPriority.Normal;
            }
            ApplyProcessPriority(AppProcessPriority);
        }
        
        private void CheckAndLimitDailyNoteItems(string settingName, Action revertAction)
        {
            if (_isUpdatingDailyNote) return;

            int activeCount = 0;
            if (ShowDailyNoteResin) activeCount++;
            if (ShowDailyNoteDailyTasks) activeCount++;
            if (ShowDailyNoteHomeCoin) activeCount++;
            if (ShowDailyNoteExpeditions) activeCount++;
            if (ShowDailyNoteTransformer) activeCount++;

            if (activeCount > 3)
            {
                _isUpdatingDailyNote = true;
                revertAction();
                _isUpdatingDailyNote = false;
                return;
            }

            var propertyValue = settingName switch
            {
                "ShowDailyNoteResin" => ShowDailyNoteResin,
                "ShowDailyNoteDailyTasks" => ShowDailyNoteDailyTasks,
                "ShowDailyNoteHomeCoin" => ShowDailyNoteHomeCoin,
                "ShowDailyNoteExpeditions" => ShowDailyNoteExpeditions,
                "ShowDailyNoteTransformer" => ShowDailyNoteTransformer,
                _ => false
            };

            _ = _localSettingsService.SaveSettingAsync(settingName, propertyValue);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }

        partial void OnShowDailyNoteResinChanged(bool value) => CheckAndLimitDailyNoteItems("ShowDailyNoteResin", () => ShowDailyNoteResin = false);
        partial void OnShowDailyNoteDailyTasksChanged(bool value) => CheckAndLimitDailyNoteItems("ShowDailyNoteDailyTasks", () => ShowDailyNoteDailyTasks = false);
        partial void OnShowDailyNoteHomeCoinChanged(bool value) => CheckAndLimitDailyNoteItems("ShowDailyNoteHomeCoin", () => ShowDailyNoteHomeCoin = false);
        partial void OnShowDailyNoteExpeditionsChanged(bool value) => CheckAndLimitDailyNoteItems("ShowDailyNoteExpeditions", () => ShowDailyNoteExpeditions = false);
        partial void OnShowDailyNoteTransformerChanged(bool value) => CheckAndLimitDailyNoteItems("ShowDailyNoteTransformer", () => ShowDailyNoteTransformer = false);
        
        partial void OnCustomGameExeNameChanged(string value)
        {
            _localSettingsService.SaveSettingAsync(GameExeManager.CustomExeNameKey, value);
        }

        partial void OnAppThemeColorChanged(string value)
        {
            _localSettingsService.SaveSettingAsync("AppThemeColor", value);
            
            try
            {
                if (!string.IsNullOrEmpty(value) && value.StartsWith("#") && (value.Length == 7 || value.Length == 9))
                {
                    string hex = value.Replace("#", "");
                    byte a = 255;
                    byte r = 0;
                    byte g = 0;
                    byte b = 0;
                    if (hex.Length == 8)
                    {
                        a = Convert.ToByte(hex.Substring(0, 2), 16);
                        r = Convert.ToByte(hex.Substring(2, 2), 16);
                        g = Convert.ToByte(hex.Substring(4, 2), 16);
                        b = Convert.ToByte(hex.Substring(6, 2), 16);
                    }
                    else if (hex.Length == 6)
                    {
                        r = Convert.ToByte(hex.Substring(0, 2), 16);
                        g = Convert.ToByte(hex.Substring(2, 2), 16);
                        b = Convert.ToByte(hex.Substring(4, 2), 16);
                    }
                    var color = Windows.UI.Color.FromArgb(a, r, g, b);
                    if (_appThemeColorObj != color)
                    {
                        _appThemeColorObj = color;
                        OnPropertyChanged(nameof(AppThemeColorObj));
                    }
                }
            }
            catch { }

            WeakReferenceMessenger.Default.Send(new AcrylicSettingChangedMessage(true)); // reuse or create new msg
            ThemeHelper.ApplyThemeColor(value);
        }

        partial void OnAppThemeColorObjChanged(Windows.UI.Color value)
        {
            string hex = $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}";
            if (AppThemeColor != hex)
            {
                AppThemeColor = hex;
            }
        }

        private async Task LoadCheckinAccountsAsync()
        {
            try
            {
                var disabledUidsJson = await _localSettingsService.ReadSettingAsync("CheckinDisabledUids");
                var disabledUids = new HashSet<string>();
                if (disabledUidsJson != null)
                {
                    try
                    {
                        var list = JsonSerializer.Deserialize<List<string>>(disabledUidsJson.ToString() ?? "[]");
                        if (list != null) disabledUids = new HashSet<string>(list);
                    }
                    catch { }
                }

                var accounts = new ObservableCollection<CheckinAccountItem>();
                var entries = _accountManager.GetAllAccounts();
                foreach (var entry in entries)
                {
                    var cookies = await _accountManager.LoadCookiesAsync(entry.Id);
                    if (cookies == null || cookies.Count == 0) continue;

                    string uid = entry.Stuid;
                    string nickname = entry.Nickname ?? $"用户 {uid}";

                    string cloudTokenKey = $"CloudComboToken_{uid}";
                    var cloudTokenObj = await _localSettingsService.ReadSettingAsync(cloudTokenKey);
                    bool hasCloudCredential = !string.IsNullOrEmpty(cloudTokenObj?.ToString());

                    accounts.Add(new CheckinAccountItem
                    {
                        Uid = uid,
                        Nickname = nickname,
                        IsSelected = !disabledUids.Contains(uid),
                        HasCloudCredential = hasCloudCredential
                    });
                }

                CheckinAccounts = accounts;

                foreach (var account in CheckinAccounts)
                {
                    account.PropertyChanged += async (s, e) =>
                    {
                        if (e.PropertyName == nameof(CheckinAccountItem.IsSelected))
                        {
                            var disabled = CheckinAccounts.Where(a => !a.IsSelected).Select(a => a.Uid).ToList();
                            await _localSettingsService.SaveSettingAsync("CheckinDisabledUids",
                                JsonSerializer.Serialize(disabled));
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LoadCheckinAccountsAsync 异常: {ex.Message}");
            }
        }

        public static async Task SaveCloudCredentialAsync(string uid, string credential)
        {
            try
            {
                var localSettings = App.GetService<ILocalSettingsService>();
                string key = $"CloudComboToken_{uid}";
                await localSettings.SaveSettingAsync(key, credential);

                WeakReferenceMessenger.Default.Send(new CloudCredentialUpdatedMessage(uid));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"保存云游戏凭证失败: {ex.Message}");
            }
        }

        private async Task ResetGameExeNameAsync()
        {
            CustomGameExeName = string.Empty;
            await _localSettingsService.SaveSettingAsync<string>(GameExeManager.CustomExeNameKey, null);
        }
        
        partial void OnGlobalBackgroundImageOpacityChanged(double value)
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(clamped - value) > 0.0001)
            {
                GlobalBackgroundImageOpacity = clamped;
                return;
            }

            _ = _localSettingsService.SaveSettingAsync("GlobalBackgroundImageOpacity", clamped);
            WeakReferenceMessenger.Default.Send(new BackgroundImageOpacityChangedMessage(clamped));
        }

        partial void OnPanelBackgroundOpacityChanged(double value)
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);

            if (Math.Abs(clamped - value) > 0.001)
            {
                PanelBackgroundOpacity = clamped;
                return;
            }

            _localSettingsService.SaveSettingAsync("PanelBackgroundOpacity", clamped);

            WeakReferenceMessenger.Default.Send(new PanelOpacityChangedMessage(clamped));
        }


        partial void OnCurrentWindowBackdropChanged(WindowBackdropType value)
        {
            Debug.WriteLine($"[ViewModel] 属性已更新为: {value}");

            if (!_isInitializing)
            {
                _localSettingsService.SaveSettingAsync("WindowBackdrop", (int)value);

                WeakReferenceMessenger.Default.Send(new ValueChangedMessage<WindowBackdropType>(value));
            }
        }
        private async Task SelectStartupSoundAsync()
        {
            try
            {
                var path = await _filePickerService.PickAudioFileAsync();
                if (!string.IsNullOrEmpty(path))
                {
                    StartupSoundPath = path;
                    HasCustomStartupSound = true;

                    await _localSettingsService.SaveSettingAsync<string>("StartupSoundPath", path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择启动语音失败: {ex.Message}");
            }
        }

        private async Task ClearStartupSound()
        {
            try
            {

                await _localSettingsService.SaveSettingAsync<string>("StartupSoundPath", null);
                StartupSoundPath = null;
                HasCustomStartupSound = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除启动语音失败: {ex.Message}");
            }
        }

        partial void OnIsStartupSoundEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存启动语音开关 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsStartupSoundEnabled", value);
        }
        private async Task LoadCustomBackgroundSettingsAsync()
        {
            var path = await _localSettingsService.ReadSettingAsync("CustomBackgroundPath");
            if (path != null)
            {
                CustomBackgroundPath = path.ToString();
                HasCustomBackground = File.Exists(CustomBackgroundPath);
            }
            else
            {
                CustomBackgroundPath = null;
                HasCustomBackground = false;
            }

            var isSlideshowEnabledJson = await _localSettingsService.ReadSettingAsync("IsBackgroundSlideshowEnabled");
            IsBackgroundSlideshowEnabled = isSlideshowEnabledJson != null && Convert.ToBoolean(isSlideshowEnabledJson);

            var slideshowFolderJson = await _localSettingsService.ReadSettingAsync("BackgroundSlideshowFolder");
            if (slideshowFolderJson != null)
            {
                BackgroundSlideshowFolder = slideshowFolderJson.ToString();
                HasBackgroundSlideshowFolder = Directory.Exists(BackgroundSlideshowFolder);
            }
            else
            {
                BackgroundSlideshowFolder = null;
                HasBackgroundSlideshowFolder = false;
            }

            var slideshowIntervalJson = await _localSettingsService.ReadSettingAsync("BackgroundSlideshowInterval");
            if (slideshowIntervalJson != null)
            {
                BackgroundSlideshowInterval = Convert.ToInt32(slideshowIntervalJson);
            }
            else
            {
                BackgroundSlideshowInterval = 60;
            }
        }

        private void ParseLaunchParameters(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return;
    
            try
            {
                if (args.Contains("-popupwindow"))
                {
                    LaunchArgsWindowMode = WindowModeType.Popup;
                }
                else
                {
                    LaunchArgsWindowMode = WindowModeType.Normal;
                }
                
                var monitorMatch = Regex.Match(args, @"-monitor\s+(\d+)");
                if (monitorMatch.Success && int.TryParse(monitorMatch.Groups[1].Value, out int mIndex))
                {
                    LaunchArgsMonitorIndex = mIndex;
                }
                else
                {
                    LaunchArgsMonitorIndex = 0;
                }

                var parts = args.Split(' ');
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i] == "-screen-width")
                        LaunchArgsWidth = parts[i + 1];
                    if (parts[i] == "-screen-height")
                        LaunchArgsHeight = parts[i + 1];
                }
            }
            catch
            {
                // ignored
            }
        }
        
        private void ApplyPresetsToText()
        {
            if (_isLoadingLaunchParams) return;

            var currentArgs = CustomLaunchParameters ?? "";
            
            currentArgs = Regex.Replace(currentArgs, @"-screen-width\s+\S+", "");
            currentArgs = Regex.Replace(currentArgs, @"-screen-height\s+\S+", "");
            currentArgs = Regex.Replace(currentArgs, @"-popupwindow", "");
            currentArgs = Regex.Replace(currentArgs, @"-monitor\s+\d+", "");
    
            var sb = new System.Text.StringBuilder(currentArgs);
            if (!string.IsNullOrWhiteSpace(LaunchArgsWidth) && !string.IsNullOrWhiteSpace(LaunchArgsHeight))
            {
                sb.Append($" -screen-width {LaunchArgsWidth} -screen-height {LaunchArgsHeight}");
            }
            if (LaunchArgsWindowMode == WindowModeType.Popup)
            {
                sb.Append(" -popupwindow");
            }
            if (LaunchArgsMonitorIndex > 0)
            {
                sb.Append($" -monitor {LaunchArgsMonitorIndex}");
            }

            var finalArgs = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            if (CustomLaunchParameters != finalArgs)
            {
                CustomLaunchParameters = finalArgs;
            }
        }

        private async Task ApplyLanguageChangeAsync(AppLanguage language)
        {
            try
            {
                await _localSettingsService.SaveSettingAsync("AppLanguage", (int)language);
                var culture = language == AppLanguage.zhCN ? "zh-CN" : "zh-TW";
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = culture;

                var dialog = new ContentDialog
                {
                    Title = "语言已更改",
                    Content = "语言设置已更改。由于技术限制，部分UI需要重启才能完全生效。",
                    PrimaryButtonText = "立即重启",
                    CloseButtonText = "稍后手动重启",
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    RestartApp();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"应用语言失败: {ex.Message}");
            }
        }

        private void RestartApp()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath,
                        Arguments = "restart",
                        UseShellExecute = true
                    }
                };
                process.Start();
                App.MainWindow.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"重启应用失败: {ex.Message}");
            }
        }

        partial void OnCustomBackgroundApiUrlChanged(string value)
        {
            if (_isInitializing) return;
            var normalized = value?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(normalized) &&
                (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                return;
            }

            _ = _localSettingsService.SaveSettingAsync("CustomBackgroundApiUrl", normalized);
            CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(normalized) ? GetDefaultBackgroundApiUrl(SelectedServer) : normalized;
            _ = _localSettingsService.SaveSettingAsync("BackgroundJsonHash", string.Empty);
            _ = _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", string.Empty);
            _ = _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }

        partial void OnSelectedServerChanged(ServerType value)
        {
            if (_isInitializing) return;
            Debug.WriteLine($"SettingsViewModel: 保存服务器设置 {value}");
            CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(CustomBackgroundApiUrl) ? GetDefaultBackgroundApiUrl(value) : CustomBackgroundApiUrl;
            _ = _localSettingsService.SaveSettingAsync(LocalSettingsService.BackgroundServerKey, (int)value);
            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }

        partial void OnIsBackgroundSlideshowEnabledChanged(bool value)
        {
            if (_isInitializing) return;
            _ = _localSettingsService.SaveSettingAsync("IsBackgroundSlideshowEnabled", value);
            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }

        partial void OnBackgroundSlideshowIntervalChanged(int value)
        {
            if (_isInitializing) return;
            if (value < 1) value = 1; // min 1 second
            _ = _localSettingsService.SaveSettingAsync("BackgroundSlideshowInterval", value);
            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }

        partial void OnIsBackgroundEnabledChanged(bool value)
        {
            if (_isInitializing) return;
            // Now means: whether custom background is allowed. If disabled, we fall back to official background.
            Debug.WriteLine($"SettingsViewModel: 保存自定义背景开关 {value}");
            _ = _localSettingsService.SaveSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, value);

            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());

            if (!value)
            {
                _backgroundRenderer.ClearCustomBackground();
            }
        }

        partial void OnIsBetterGIIntegrationEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: BetterGI联动设置变更为 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsBetterGIIntegrationEnabled", value);
        }
        partial void OnIsBetterGICloseOnExitEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: BetterGI 关闭随游戏退出设置变更为 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsBetterGICloseOnExitEnabled", value);
        }

        partial void OnGameNewsCardTextColorChanged(string value)
        {
            _ = _localSettingsService.SaveSettingAsync("GameNewsCardTextColor", value);
            WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
        }
        partial void OnGameNewsCardTextOpacityChanged(double value)
        {
            _ = _localSettingsService.SaveSettingAsync("GameNewsCardTextOpacity", value);
            WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
        }
        partial void OnLaunchButtonTextColorChanged(string value)
        {
            _ = _localSettingsService.SaveSettingAsync("LaunchButtonTextColor", value);
            WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
        }
        partial void OnLaunchButtonTextOpacityChanged(double value)
        {
            _ = _localSettingsService.SaveSettingAsync("LaunchButtonTextOpacity", value);
            WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
        }
        partial void OnGameCheckinTextColorChanged(string value)
        {
            _ = _localSettingsService.SaveSettingAsync("GameCheckinTextColor", value);
            WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
        }
        partial void OnGameCheckinTextOpacityChanged(double value)
        {
            _ = _localSettingsService.SaveSettingAsync("GameCheckinTextOpacity", value);
            WeakReferenceMessenger.Default.Send(new FufuLauncher.Messages.TextStyleChangedMessage());
        }

        partial void OnGlobalBackgroundOverlayOpacityChanged(double value)
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);

            if (Math.Abs(clamped - value) > 0.0001)
            {
                GlobalBackgroundOverlayOpacity = clamped;
                return;
            }

            _ = _localSettingsService.SaveSettingAsync("GlobalBackgroundOverlayOpacity", clamped);
            WeakReferenceMessenger.Default.Send(new BackgroundOverlayOpacityChangedMessage(clamped));
        }

        partial void OnContentFrameBackgroundOpacityChanged(double value)
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(clamped - value) > 0.0001)
            {
                ContentFrameBackgroundOpacity = clamped;
                return;
            }

            _ = _localSettingsService.SaveSettingAsync("ContentFrameBackgroundOpacity", clamped);
            WeakReferenceMessenger.Default.Send(new FrameBackgroundOpacityChangedMessage(clamped));
        }

        partial void OnIsSaveWindowSizeEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存窗口大小记忆设置 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsSaveWindowSizeEnabled", value);
        }

        partial void OnIsMinWindowSizeLimitEnabledChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("IsMinWindowSizeLimitEnabled", value);
            WeakReferenceMessenger.Default.Send(new MinWindowSizeLimitChangedMessage(value));
        }

        partial void OnIsHideGameNewsCardEnabledChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("IsHideGameNewsCardEnabled", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }

        partial void OnIsHideCheckinCardEnabledChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("IsHideCheckinCardEnabled", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }

        partial void OnIsHideDailyNoteCardEnabledChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("IsHideDailyNoteCardEnabled", value);
            WeakReferenceMessenger.Default.Send(new CardVisibilityChangedMessage());
        }

        private bool _isLoadingLaunchParams = false;

        partial void OnMinimizeToTrayChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存托盘设置 {value}");
            _ = _localSettingsService.SaveSettingAsync("MinimizeToTray", value);
            WeakReferenceMessenger.Default.Send(new MinimizeToTrayChangedMessage(value));
        }
        

        partial void OnCustomLaunchParametersChanged(string value)
        {
            _localSettingsService.SaveSettingAsync("CustomLaunchParameters", value);
        }

        private async Task SelectCustomBackgroundAsync()
        {
            try
            {
                var path = await _filePickerService.PickImageOrVideoAsync();
                if (!string.IsNullOrEmpty(path))
                {
                    CustomBackgroundPath = path;
                    HasCustomBackground = true;
                    await _localSettingsService.SaveSettingAsync("CustomBackgroundPath", path);

                    WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
                    await RefreshMainPageBackground();

                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择自定义背景失败: {ex.Message}");
            }
        }

        private async Task SelectBackgroundSlideshowFolderAsync()
        {
            try
            {
                var folder = await _filePickerService.PickFolderAsync();
                if (!string.IsNullOrEmpty(folder))
                {
                    BackgroundSlideshowFolder = folder;
                    HasBackgroundSlideshowFolder = true;
                    await _localSettingsService.SaveSettingAsync("BackgroundSlideshowFolder", folder);

                    WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择轮播图文件夹失败: {ex.Message}");
            }
        }

        private async Task ClearBackgroundSlideshowFolderAsync()
        {
            try
            {
                await _localSettingsService.SaveSettingAsync<string>("BackgroundSlideshowFolder", null);
                BackgroundSlideshowFolder = null;
                HasBackgroundSlideshowFolder = false;

                WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除轮播图文件夹失败: {ex.Message}");
            }
        }

        private async Task RefreshMainPageBackground()
        {
            await Task.CompletedTask;
        }

        private static string GetVersionDescription()
        {
            var version = Assembly.GetEntryAssembly().GetName().Version;
            if (version == null) version = new Version(1, 0, 0, 0);

            return $"FufuLauncher - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }
}

