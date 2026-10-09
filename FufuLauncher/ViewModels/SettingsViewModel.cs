/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FufuLauncher.ViewModels;

public enum WindowBackdropType
{
    None = 0,
    Acrylic = 1,
    Mica = 2
}

public enum NotificationPosition
{
    BottomRight = 0,
    TopRight = 1,
    TopLeft = 2,
    BottomLeft = 3
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
    #region 服务、字段与命令

    private readonly IThemeSelectorService _themeSelectorService;
    private readonly IBackgroundRenderer _backgroundRenderer;
    private readonly IDevBuildDetectionService _devBuildDetectionService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly INavigationService _navigationService;
    private readonly IGameLauncherService _gameLauncherService;
    private readonly IFilePickerService _filePickerService;
    private readonly INotificationService _notificationService;
    private readonly AccountManager _accountManager;
    private readonly Services.AuthTicket.IAuthTicketService _authTicketService;
    private readonly DispatcherQueue _dispatcherQueue;

    public record MonitorItem(string DisplayName, int Index);

    [ObservableProperty] private ElementTheme _elementTheme;
    [ObservableProperty] private string _versionDescription;
    public string AppVersion => string.Format("AppVersionFormat".GetLocalized(), AppVersionHelper.FullVersion);
    public bool IsPreviewBuild => AppVersionHelper.IsPreviewBuild;
    [ObservableProperty] private ServerType _selectedServer;
    [ObservableProperty] private bool _isBackgroundEnabled = true;
    [ObservableProperty] private AppLanguage _selectedLanguage;
    [ObservableProperty] private bool _minimizeToTray;
    [ObservableProperty] private bool _isStartupEnabled;
    [ObservableProperty] private string _customLaunchParameters = "";
    [ObservableProperty] private WindowModeType _launchArgsWindowMode = WindowModeType.Normal;
    [ObservableProperty] private string _launchArgsWidth = "";
    [ObservableProperty] private string _launchArgsHeight = "";
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
            if (SetProperty(ref _betterGIStartupDelaySeconds, clampedValue) && !_isInitializing)
            {
                _ = _localSettingsService.SaveSettingAsync("BetterGIStartupDelaySeconds", clampedValue);
            }
        }
    }

    [ObservableProperty] private double _globalBackgroundOverlayOpacity = 0.0;
    [ObservableProperty] private double _contentFrameBackgroundOpacity = 0.5;
    [ObservableProperty] private bool _isSaveWindowSizeEnabled;
    [ObservableProperty] private bool _isMinWindowSizeLimitEnabled = true;
    [ObservableProperty] private NotificationPosition _notificationPosition;
    [ObservableProperty] private double _globalBackgroundImageOpacity = 1.0;
    [ObservableProperty] private bool _isAcrylicOverlayEnabled;
    [ObservableProperty] private bool _isPageOverlaySemiTransparentEnabled;
    [ObservableProperty] private double _pageOverlayTargetOpacity = 0.7;
    [ObservableProperty] private bool _isHamburgerButtonEnabled;

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

    [ObservableProperty] private bool _isUsingHoyolabAccount;

    [ObservableProperty] private bool _isScreenshotEnabled;
    [ObservableProperty] private string _screenshotHotkey = "F12";
    [ObservableProperty] private string _screenshotSavePath;
    [ObservableProperty] private bool _hasScreenshotSavePath;

    [ObservableProperty] private bool _isUseThirdPartyCDNEnabled = true;

    [ObservableProperty] private bool _isPreviewUpdateAnnouncementEnabled = true;

    [ObservableProperty] private bool _isPluginMirrorAccelerationEnabled = true;

    [ObservableProperty] private bool _isPluginConflictCheckEnabled = true;
    [ObservableProperty] private bool _isPluginConflictMainDllOnly = true;

    [ObservableProperty] private bool _ignoreConstraintRestrictions;

    [ObservableProperty] private bool _isCaptchaPopupDisabled;

    [ObservableProperty] private bool _isCaptchaNoticeEnabled = true;

    public IAsyncRelayCommand SelectScreenshotFolderCommand
    {
        get;
    }

    public IAsyncRelayCommand ClearScreenshotFolderCommand
    {
        get;
    }

    public IAsyncRelayCommand OpenScreenshotFolderCommand
    {
        get;
    }

    [ObservableProperty] private PostLaunchBehavior _postLaunchBehavior;

    public record PostLaunchBehaviorItem(string DisplayName, PostLaunchBehavior Value);

    public List<PostLaunchBehaviorItem> PostLaunchBehaviorItems
    {
        get;
    } = new()
    {
        new("不变", Models.PostLaunchBehavior.None),
        new("最小化到托盘", Models.PostLaunchBehavior.MinimizeToTray),
        new("保存状态并退出", Models.PostLaunchBehavior.Exit)
    };

    [ObservableProperty] private PostLaunchBehaviorItem _selectedPostLaunchBehaviorItem = null!;

    public ObservableCollection<NavItemConfig> NavItems
    {
        get;
    } = new();

    [ObservableProperty] private bool _isGameCheckinEnabled = true;
    [ObservableProperty] private ObservableCollection<CheckinGameItem> _checkinGames = new();
    [ObservableProperty] private bool _isBatchCheckinEnabled;
    [ObservableProperty] private bool _isCommunityCheckinEnabled = true;
    [ObservableProperty] private bool _isCommunityLikeEnabled;
    [ObservableProperty] private bool _isCommunityReadEnabled;
    [ObservableProperty] private bool _isCommunityShareEnabled;
    [ObservableProperty] private bool _isCloudGameCheckinEnabled;
    [ObservableProperty] private ObservableCollection<CheckinAccountItem> _checkinAccounts = new();
    [ObservableProperty] private bool _isLoadingCheckinAccounts;

    public IAsyncRelayCommand ResetGameExeNameCommand
    {
        get;
    }

    public IAsyncRelayCommand ClearWebView2CacheCommand
    {
        get;
    }

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

    public ICommand CheckPreviewUpdateCommand
    {
        get;
    }

    public ICommand CheckRollbackCommand
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

    public IAsyncRelayCommand ResetBackgroundApiCommand
    {
        get;
    }

    public IAsyncRelayCommand ResetLaunchButtonOverlayColorCommand
    {
        get;
    }

    public IAsyncRelayCommand ResetCpuUsageWarningSettingsCommand
    {
        get;
    }

    private static string? _cachedWebView2CacheSize;

    private bool _isUpdatingDailyNote;

    private bool _isLoadingLaunchParams = false;

    #endregion

    #region 构造函数

    public SettingsViewModel(
        IThemeSelectorService themeSelectorService,
        IBackgroundRenderer backgroundRenderer,
        IDevBuildDetectionService devBuildDetectionService,
        ILocalSettingsService localSettingsService,
        INavigationService navigationService,
        IGameLauncherService gameLauncherService,
        IFilePickerService filePickerService,
        INotificationService notificationService,
        AccountManager accountManager,
        Services.AuthTicket.IAuthTicketService authTicketService)
    {
        _themeSelectorService = themeSelectorService;
        _backgroundRenderer = backgroundRenderer;
        _devBuildDetectionService = devBuildDetectionService;
        _localSettingsService = localSettingsService;
        _navigationService = navigationService;
        _gameLauncherService = gameLauncherService;
        _filePickerService = filePickerService;
        _notificationService = notificationService;
        _accountManager = accountManager;
        _authTicketService = authTicketService;
        _dispatcherQueue = App.MainWindow.DispatcherQueue;

        RegisterSettingSections();

        InitializeDefaultResolution();

        SelectStartupSoundCommand = new AsyncRelayCommand(SelectStartupSoundAsync);
        ClearStartupSoundCommand = new AsyncRelayCommand(ClearStartupSound);
        CheckUpdateCommand = new RelayCommand(CheckUpdate);
        CheckPreviewUpdateCommand = new RelayCommand(CheckPreviewUpdate);
        CheckRollbackCommand = new RelayCommand(CheckRollback);
        ElementTheme = _themeSelectorService.Theme;
        _versionDescription = GetVersionDescription();
        ClearWebView2CacheCommand = new AsyncRelayCommand(ClearWebView2CacheAsync);
        UpdateWebView2CacheSizeAsync();
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

        SwitchThemeCommand = new RelayCommand<ElementTheme>(async (param) =>
        {
            if (ElementTheme != param)
            {
                ElementTheme = param;
                await _themeSelectorService.SetThemeAsync(param);
            }
        });

        SwitchLanguageCommand = new RelayCommand<object>(async (param) =>
        {
            try
            {
                int languageCode = Convert.ToInt32(param);
                var language = (AppLanguage)languageCode;

                Debug.WriteLine(
                    $"[SettingsVM] SwitchLanguageCommand: param={param}, language={language}, current SelectedLanguage={SelectedLanguage}");

                // Always apply - the TwoWay binding on IsChecked may have already
                // updated SelectedLanguage, so the old guard was incorrectly
                // preventing ApplyLanguageChangeAsync from being called.
                SelectedLanguage = language;
                await ApplyLanguageChangeAsync(language);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"语言切换失败: {ex.Message}");
            }
        });

        SetResolutionPresetCommand = new RelayCommand<string>((param) =>
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

        SelectScreenshotFolderCommand = new AsyncRelayCommand(SelectScreenshotFolderAsync);
        ClearScreenshotFolderCommand = new AsyncRelayCommand(ClearScreenshotFolderAsync);
        OpenScreenshotFolderCommand = new AsyncRelayCommand(OpenScreenshotFolderAsync);
    }

    #endregion
}
