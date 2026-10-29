/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using Windows.Foundation;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.Tray;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.UI.ViewManagement;

namespace FufuLauncher;

public sealed partial class MainWindow : WindowEx
{
    #region Fields & Native APIs

    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;
    private UISettings settings;
    private readonly IBackgroundRenderer _backgroundRenderer;
    private readonly IDevBuildDetectionService _devBuildDetectionService;
    private readonly ILocalSettingsService _localSettingsService;
    private readonly BingWallpaperService _bingWallpaperService;
    private readonly IAutoThemeColorService _autoThemeColorService;
    private readonly AccountManager _accountManager;
    private MediaPlayer? _globalBackgroundPlayer;
    private IMediaPlaybackSource? _suspendedVideoSource;
    private TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>? _bgVideoFailedHandler;
    private MediaSource? _bgVideoFallbackSource;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _bgImageFadeStoryboard;
    private int _backgroundLoadVersion;
    private bool _isFallbackBackground;
    private int? _slideshowLoadingVersion;
    private double _frameBackgroundOpacity;
    private bool _minimizeToTray;
    private bool _isExit;
    private TrayIconController? _trayIconController;
    private bool _isOverlayShown;
    private Microsoft.UI.Xaml.Media.Animation.Storyboard? _pageOverlayStoryboard;
    private bool _isAcrylicOverlayEnabled;
    private bool _isPageOverlaySemiTransparent;
    private double _pageOverlayTargetOpacity = 1.0;
    private bool _isHamburgerButtonEnabled;
    private NotificationPosition _notificationPosition;

    private bool _isVideoBackground;

    private DispatcherTimer _messageDismissTimer;
    private readonly NetworkMonitorService _networkMonitorService;
    private bool _isSystemMessageVisible;

    private bool _isMainUiLoaded;

    private DispatcherTimer _announcementCheckTimer;
    private readonly IAnnouncementService _announcementService;

    private DispatcherTimer _constraintCheckTimer;
    private readonly ConstraintService _constraintService;

    private DispatcherTimer _memoryOptimizationTimer;
    private DispatcherTimer _periodicMemoryTimer;

    private DispatcherTimer _slideshowTimer;
    private List<string> _slideshowImages = new List<string>();
    private int _currentSlideshowIndex = 0;

    private bool _isSuspended;

    private readonly Dictionary<string, bool> _navItemVisibility = new();

    public bool IsAgreementShowing
    {
        get;
        private set;
    }

    public IRelayCommand ShowWindowCommand
    {
        get;
    }

    public IRelayCommand ExitApplicationCommand
    {
        get;
    }

    public IRelayCommand ShowTrayMenuCommand
    {
        get;
    }

    private void SyncPageTheme()
    {
        if (Content is FrameworkElement rootElement)
        {
            if (ContentFrame.Content is FrameworkElement page)
            {
                page.RequestedTheme = rootElement.RequestedTheme;
            }

            if (AgreementFrame.Content is FrameworkElement agreementPage)
            {
                agreementPage.RequestedTheme = rootElement.RequestedTheme;
            }
        }
    }

    #endregion

    #region Initialization

    private Task RunOnUIThreadAsync(Action action)
    {
        if (dispatcherQueue.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!dispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    action();
                    tcs.SetResult();
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
            tcs.SetException(new InvalidOperationException("主窗口的 UI 调度队列已关闭"));
        return tcs.Task;
    }

    public MainWindow()
    {
        try
        {
            InitializeComponent();
        }
        catch (Exception ex) when (ex is Microsoft.UI.Xaml.Markup.XamlParseException ||
                                   ex is System.IO.FileNotFoundException)
        {
            DebugLog.Info("MainWindow", $"XAML parse failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                DebugLog.Info("MainWindow", $"Inner exception: {ex.InnerException.Message}");
            }


            try
            {
                InitializeComponent();
            }
            catch (Exception retryEx)
            {
                DebugLog.Info("MainWindow", $"XAML parse retry failed: {retryEx.Message}");
                throw;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow", $"XAML parse failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                DebugLog.Info("MainWindow", $"Inner exception: {ex.InnerException.Message}");
            }

            throw;
        }

        PluginFolderHelper.CheckAndCreatePluginsFolder();

        ShowWindowCommand = new RelayCommand(ShowWindow);
        ExitApplicationCommand = new RelayCommand(ExitApplication);

        _trayIconController = new TrayIconController(TrayIcon, this, ShowWindowCommand, ExitApplicationCommand);
        ShowTrayMenuCommand = new RelayCommand(() => _trayIconController?.ShowTrayMenu());
        TrayIcon.RightClickCommand = ShowTrayMenuCommand;
        TrayIcon.LeftClickCommand = ShowWindowCommand;
        TrayIcon.DoubleClickCommand = ShowWindowCommand;

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Title = "AppDisplayName".GetLocalized();
        ExtendsContentIntoTitleBar = true;
        AppWindow.Closing += AppWindow_Closing;

        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Services.LuaPluginInstaller.UIDispatcher = dispatcherQueue;

        RootGrid.Loaded += (_, _) =>
        {
            Services.LuaPluginInstaller.MainXamlRoot = RootGrid.XamlRoot;
        };

        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged;
        _backgroundRenderer = App.GetService<IBackgroundRenderer>();
        _devBuildDetectionService = App.GetService<IDevBuildDetectionService>();
        _localSettingsService = App.GetService<ILocalSettingsService>();
        _bingWallpaperService = App.GetService<BingWallpaperService>();
        _autoThemeColorService = App.GetService<IAutoThemeColorService>();
        _accountManager = App.GetService<AccountManager>();
        AccountFlyout.Closed += (_, _) =>
        {
            var action = _accountFlyoutClosedAction;
            _accountFlyoutClosedAction = null;
            if (action != null)
                dispatcherQueue.TryEnqueue(() => action());
        };

        WeakReferenceMessenger.Default.Register<AgreementAcceptedMessage>(this, (_, _) =>
        {
            dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    IsAgreementShowing = false;
                    AgreementFrame.Visibility = Visibility.Collapsed;
                    AgreementFrame.Content = null;
                    await ApplyMainWindowSizeAsync();
                    await Task.Delay(50);
                    await PerformMainInitAsync();
                    _announcementCheckTimer.Start();
                    await CheckAndWarnVCRedistAsync();
                }
                catch (Exception ex)
                {
                    DebugLog.Info("MainWindow", $"Message handling exception: {ex.Message}");
                }
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(800);
                    await ((App)App.Current).PlayStartupSoundAsync();
                }
                catch (Exception ex)
                {
                    DebugLog.Info("MainWindow", $"Failed to play startup sound: {ex.Message}");
                }
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500);
                    var announcementService = App.GetService<IAnnouncementService>();
                    var announcementUrl = await announcementService.CheckForNewAnnouncementAsync();
                    if (!string.IsNullOrEmpty(announcementUrl))
                    {
                        dispatcherQueue.TryEnqueue(() =>
                        {
                            var announcementWindow = new Views.WebAnnouncementWindow(announcementUrl);
                            announcementWindow.Activate();
                        });
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Info("MainWindow", $"[Announcement] Announcement check failed: {ex.Message}");
                }
            });
        });

        WeakReferenceMessenger.Default.Register<GameRunningStateChangedMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyGameMemoryStrategy(m.IsRunning));

            if (m.IsRunning) return;

            dispatcherQueue.TryEnqueue(async () => await CheckPeriodicAnnouncementAsync());
            dispatcherQueue.TryEnqueue(async () => await CheckPeriodicConstraintAsync());
        });

        WeakReferenceMessenger.Default.Register<MemoryReleaseEnabledChangedMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyMemoryReleaseEnabled(m.IsEnabled));
        });

        WeakReferenceMessenger.Default.Register<OverlayStyleChangedMessage>(this, (_, m) =>
        {
            _isAcrylicOverlayEnabled = m.Value;
            dispatcherQueue.TryEnqueue(() => UpdateBackgroundOverlayTheme());
        });

        WeakReferenceMessenger.Default.Register<PageOverlayOpacityModeChangedMessage>(this, (_, m) =>
        {
            _isPageOverlaySemiTransparent = m.Value;
            dispatcherQueue.TryEnqueue(() =>
            {
                if (_isOverlayShown)
                {
                    PageBackgroundOverlay.Opacity = _isPageOverlaySemiTransparent ? _pageOverlayTargetOpacity : 1.0;
                }
            });
        });

        WeakReferenceMessenger.Default.Register<PageOverlayTargetOpacityChangedMessage>(this, (_, m) =>
        {
            _pageOverlayTargetOpacity = Math.Clamp(m.Value, 0.1, 1.0);
            dispatcherQueue.TryEnqueue(() =>
            {
                if (_isOverlayShown && _isPageOverlaySemiTransparent)
                {
                    PageBackgroundOverlay.Opacity = _pageOverlayTargetOpacity;
                }
            });
        });

        WeakReferenceMessenger.Default.Register<HamburgerButtonVisibilityChangedMessage>(this, (_, m) =>
        {
            _isHamburgerButtonEnabled = m.Value;
            dispatcherQueue.TryEnqueue(() => ApplyHamburgerButtonVisibility(_isHamburgerButtonEnabled));
        });

        if (Content is FrameworkElement rootElement)
        {
            rootElement.ActualThemeChanged += (_, _) =>
            {
                UpdateBackgroundOverlayTheme();
                SyncPageTheme();
            };
        }

        WeakReferenceMessenger.Default.Register<ValueChangedMessage<WindowBackdropType>>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyBackdrop(m.Value));
        });

        WeakReferenceMessenger.Default.Register<NotificationMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ShowNotification(m));
        });

        WeakReferenceMessenger.Default.Register<ValueChangedMessage<NotificationPosition>>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyNotificationPosition(m.Value));
        });

        WeakReferenceMessenger.Default.Register<BackgroundRefreshMessage>(this, (_, _) =>
        {
            dispatcherQueue.TryEnqueue(async void () =>
            {
                await LoadGlobalBackgroundAsync();
            });
        });

        WeakReferenceMessenger.Default.Register<BackgroundOverlayOpacityChangedMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyOverlayOpacity(m.Value));
        });

        WeakReferenceMessenger.Default.Register<AccountChangedMessage>(this, (_, _) =>
        {
            dispatcherQueue.TryEnqueue(RefreshAccountMenu);
        });

        WeakReferenceMessenger.Default.Register<GameRoleChangedMessage>(this, (_, _) =>
        {
            dispatcherQueue.TryEnqueue(RefreshAccountMenu);
        });

        _memoryOptimizationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _memoryOptimizationTimer.Tick += OnMemoryOptimizationTick!;

        _periodicMemoryTimer = new DispatcherTimer { Interval = NormalMemoryReleaseInterval };
        _periodicMemoryTimer.Tick += (_, _) => FlushMemory(force: true);
        _periodicMemoryTimer.Start();
        _ = LoadMemoryReleaseSettingAsync();

        AppWindow.Changed += AppWindow_Changed;

        WeakReferenceMessenger.Default.Register<FrameBackgroundOpacityChangedMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyFrameBackgroundOpacity(m.Value));
        });

        WeakReferenceMessenger.Default.Register<MinimizeToTrayChangedMessage>(this, (_, m) =>
        {
            _minimizeToTray = m.Value;
        });

        WeakReferenceMessenger.Default.Register<NavigationVisibilityChangedMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyNavItemVisibility(m.Value));
        });

        WeakReferenceMessenger.Default.Register<MinWindowSizeLimitChangedMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyMinWindowSizeLimit(m.Value));
        });

        WeakReferenceMessenger.Default.Register<BackgroundImageOpacityChangedMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyBackgroundImageOpacity(m.Value));
        });

        dispatcherQueue.TryEnqueue(async void () => await LoadBackgroundImageOpacityAsync());
        Activated += OnWindowActivated;

        if (!Helpers.AppPaths.IsFirstRun)
        {
            dispatcherQueue.TryEnqueue(async void () =>
            {
                try
                {
                    await CheckAndWarnVCRedistAsync();
                }
                catch (Exception ex)
                {
                    DebugLog.Info("MainWindow", $"Unhandled exception in startup popup check: {ex.Message}");
                }
            });
        }

        SizeChanged += MainWindow_SizeChanged;

        UpdateBackgroundOverlayTheme();

        _messageDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _messageDismissTimer.Tick += (_, _) => HideSystemMessage();
        _networkMonitorService = new NetworkMonitorService();
        _networkMonitorService.NetworkStatusChanged += OnNetworkStatusChanged;

        _announcementService = App.GetService<IAnnouncementService>();
        _announcementCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _announcementCheckTimer.Tick += async (_, _) => await CheckPeriodicAnnouncementAsync();
        if (!Helpers.AppPaths.IsFirstRun)
        {
            _announcementCheckTimer.Start();
        }

        _constraintService = App.GetService<ConstraintService>();
        _constraintCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _constraintCheckTimer.Tick += async (_, _) => await CheckPeriodicConstraintAsync();
        if (!Helpers.AppPaths.IsFirstRun)
        {
            _constraintCheckTimer.Start();
        }
    }

    #endregion
}