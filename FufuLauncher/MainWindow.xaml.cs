using System.Diagnostics;
using System.Numerics;
using Windows.Foundation;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Playback;
using Windows.UI;
using Windows.UI.ViewManagement;
using FufuLauncher.Constants;

namespace FufuLauncher;

public sealed partial class MainWindow : WindowEx
{
    #region Fields & Native APIs
    
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;
    private UISettings settings;
    private readonly IBackgroundRenderer _backgroundRenderer;
    private readonly ILocalSettingsService _localSettingsService;
    private MediaPlayer? _globalBackgroundPlayer;
    private double _frameBackgroundOpacity;
    private bool _minimizeToTray;
    private bool _isExit;
    private bool _isOverlayShown;
    private bool _isAcrylicOverlayEnabled;

    private bool _isVideoBackground;
    
    private DispatcherTimer _messageDismissTimer;
    private readonly NetworkMonitorService _networkMonitorService;
    private bool _isSystemMessageVisible;

    private bool _isMainUiLoaded;
    
    private DispatcherTimer _announcementCheckTimer;
    private readonly IAnnouncementService _announcementService;
    
    private DispatcherTimer _memoryOptimizationTimer;
    private DispatcherTimer _periodicMemoryTimer; 
    private bool _isSuspended;

    public IRelayCommand ShowWindowCommand
    {
        get;
    }
    public IRelayCommand ExitApplicationCommand
    {
        get;
    }
    #endregion
    
    #region Initialization

    private Task RunOnUIThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource();
        dispatcherQueue.TryEnqueue(() =>
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
        });
        return tcs.Task;
    }

    public MainWindow()
    {
        InitializeComponent();
        PluginFolderHelper.CheckAndCreatePluginsFolder();

        ShowWindowCommand = new RelayCommand(ShowWindow);
        ExitApplicationCommand = new RelayCommand(ExitApplication);

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        Title = "芙芙启动器";
        ExtendsContentIntoTitleBar = true;
        AppWindow.Closing += AppWindow_Closing;

        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged;
        _backgroundRenderer = App.GetService<IBackgroundRenderer>();
        _localSettingsService = App.GetService<ILocalSettingsService>();

        WeakReferenceMessenger.Default.Register<AgreementAcceptedMessage>(this, (_, _) =>
        {
            dispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    AgreementFrame.Visibility = Visibility.Collapsed;
                    AgreementFrame.Content = null;
                    await ApplyMainWindowSizeAsync();
                    await Task.Delay(50);
                    await PerformMainInitAsync();
                }
                catch (Exception ex) { Debug.WriteLine($"消息处理异常: {ex.Message}"); }
            });
        });
        
        WeakReferenceMessenger.Default.Register<OverlayStyleChangedMessage>(this, (_, m) =>
        {
            _isAcrylicOverlayEnabled = m.Value;
            dispatcherQueue.TryEnqueue(() => UpdateBackgroundOverlayTheme());
        });

        if (Content is FrameworkElement rootElement)
        {
            rootElement.ActualThemeChanged += (_, _) => UpdateBackgroundOverlayTheme();
        }

        WeakReferenceMessenger.Default.Register<ValueChangedMessage<WindowBackdropType>>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyBackdrop(m.Value));
        });

        WeakReferenceMessenger.Default.Register<NotificationMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ShowNotification(m));
        });

        WeakReferenceMessenger.Default.Register<BackgroundRefreshMessage>(this, (_, _) =>
        {
            dispatcherQueue.TryEnqueue(async void () => { await LoadGlobalBackgroundAsync(); });
        });

        WeakReferenceMessenger.Default.Register<BackgroundOverlayOpacityChangedMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyOverlayOpacity(m.Value));
        });
        
        _memoryOptimizationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _memoryOptimizationTimer.Tick += OnMemoryOptimizationTick!;
        
        _periodicMemoryTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _periodicMemoryTimer.Tick += (_, _) => FlushMemory();
        _periodicMemoryTimer.Start();
        
        AppWindow.Changed += AppWindow_Changed;

        WeakReferenceMessenger.Default.Register<FrameBackgroundOpacityChangedMessage>(this, (_, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyFrameBackgroundOpacity(m.Value));
        });

        WeakReferenceMessenger.Default.Register<MinimizeToTrayChangedMessage>(this, (_, m) =>
        {
            _minimizeToTray = m.Value;
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

        dispatcherQueue.TryEnqueue(async void () =>
        {
            try
            {
                await CheckAndWarnVCRedistAsync();
                await CheckAndWarnUacElevationAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动弹窗检查发生未捕获异常: {ex.Message}");
            }
        });

        SizeChanged += MainWindow_SizeChanged;

        UpdateBackgroundOverlayTheme();

        _messageDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _messageDismissTimer.Tick += (_, _) => HideSystemMessage();
        _networkMonitorService = new NetworkMonitorService();
        _networkMonitorService.NetworkStatusChanged += OnNetworkStatusChanged;
        
        _announcementService = App.GetService<IAnnouncementService>();
        _announcementCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _announcementCheckTimer.Tick += async (_, _) => await CheckPeriodicAnnouncementAsync();
        _announcementCheckTimer.Start();

    }
    
    private async Task LoadAcrylicOverlaySettingAsync()
    {
        try
        {
            var valueObj = await _localSettingsService.ReadSettingAsync("IsAcrylicOverlayEnabled");
            _isAcrylicOverlayEnabled = valueObj != null && Convert.ToBoolean(valueObj);
            UpdateBackgroundOverlayTheme();
        }
        catch { _isAcrylicOverlayEnabled = false; }
    }
    
    #endregion
    
    #region Memory Management
    
    private void FlushMemory()
    {
        try
        {
            if (ContentFrame.BackStackDepth > 0)
            {
                ContentFrame.BackStack.Clear();
            }
            
            var isMinimized = AppWindow.Presenter.Kind == AppWindowPresenterKind.Overlapped && 
                              ((OverlappedPresenter)AppWindow.Presenter).State == OverlappedPresenterState.Minimized;
            
            var isHidden = !Visible;

            var memoryService = new MemoryOptimizationService();
            memoryService.FlushMemory(isMinimized || isHidden);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"内存清理异常: {ex.Message}");
        }
    }
    
    private void OnMemoryOptimizationTick(object sender, object e)
    {
        _memoryOptimizationTimer.Stop();
        PerformMemoryOptimization();
    }

    private void PerformMemoryOptimization()
    {
        if (_isSuspended) return;
        _isSuspended = true;
    
        if (_globalBackgroundPlayer != null && _globalBackgroundPlayer.PlaybackSession.CanPause)
        {
            _globalBackgroundPlayer.Pause();
        }
    
        _networkMonitorService.Stop();
        _messageDismissTimer.Stop();
        
        _announcementCheckTimer.Stop();
        FlushMemory();

        Debug.WriteLine("应用挂起");
    }

    private void RestoreFromSuspension()
    {
        _memoryOptimizationTimer.Stop();

        if (!_isSuspended) return;
        _isSuspended = false;
        
        if (_isVideoBackground && _globalBackgroundPlayer != null)
        {
            _globalBackgroundPlayer.Play();
        }
        
        if (!_networkMonitorService.IsEnabled)
        {
            _networkMonitorService.Start();
        }
        
        if (!_announcementCheckTimer.IsEnabled)
        {
            _announcementCheckTimer.Start();
        }
        
        Debug.WriteLine("应用已唤醒");
    }
    
    #endregion
    
    #region Network & System Messages

    private void OnNetworkStatusChanged(object? sender, NetworkStatusChangedEventArgs e)
    {
        if (!_isMainUiLoaded) return;

        var msg = "";
        var icon = "";
        var color = Colors.White;

        if (e.IsNetworkLost)
        {
            msg = "网络连接已断开，请检测你的网络设置";
            icon = "\uEB55";
            color = Colors.OrangeRed;
        }
        else if (e.IsProxyNewlyEnabled)
        {
            msg = "正在使用代理网络连接，请注意你的流量消耗";
            icon = "\uE12B";
            color = Colors.DodgerBlue;
        }

        ShowAutoDismissMessage(msg, icon, color);
    }

    private void ShowAutoDismissMessage(string message, string iconGlyph, Color iconColor)
    {
        if (!_isMainUiLoaded) return;

        if (SystemMessageBar.Visibility == Visibility.Collapsed)
            SystemMessageBar.Visibility = Visibility.Visible;

        SystemMessageText.Text = message;
        SystemMessageIcon.Glyph = iconGlyph;
        SystemMessageIcon.Foreground = new SolidColorBrush(iconColor);

        _messageDismissTimer.Stop();
        _messageDismissTimer.Start();

        if (_isSystemMessageVisible) return;

        _isSystemMessageVisible = true;

        var anim = new DoubleAnimation
        {
            From = 100,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, SystemMessageTranslate);
        Storyboard.SetTargetProperty(anim, "Y");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void HideSystemMessage()
    {
        _messageDismissTimer.Stop();

        if (!_isSystemMessageVisible) return;
        _isSystemMessageVisible = false;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 100,
            Duration = TimeSpan.FromMilliseconds(400),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(anim, SystemMessageTranslate);
        Storyboard.SetTargetProperty(anim, "Y");
        var sb = new Storyboard();
        sb.Children.Add(anim);
        sb.Begin();
    }

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        if (!_isOverlayShown)
        {
            OverlayTranslate.Y = Bounds.Height + 100;
        }
    }
    
    private async Task CheckPeriodicAnnouncementAsync()
    {
        try
        {
            var announcementUrl = await _announcementService.CheckForNewAnnouncementAsync();
            if (!string.IsNullOrEmpty(announcementUrl))
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    var announcementWindow = new Views.AnnouncementWindowL(announcementUrl);
                    announcementWindow.Activate();
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Announcement] 定时检查公告失败: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Environment Checks

    private async Task CheckAndWarnUacElevationAsync()
    {
        var ignoreFilePath = Path.Combine(AppContext.BaseDirectory, ".no_uac_warning");
        if (File.Exists(ignoreFilePath)) return;

        if (SystemEnvironmentHelper.IsUacElevatedWithConsent())
    {
        try
        {
            if (Content is FrameworkElement rootElement)
            {
                if (rootElement.XamlRoot == null)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    RoutedEventHandler onLoaded = null!;
                    onLoaded = (_, _) =>
                    {
                        rootElement.Loaded -= onLoaded;
                        tcs.TrySetResult(true);
                    };
                    rootElement.Loaded += onLoaded;
                    await tcs.Task;
                }
                
                ContentDialog dialog = new()
                {
                    XamlRoot = rootElement.XamlRoot,
                    Title = "警告",
                    Content = "程序正以管理员身份运行，可能会影响部分功能，例如你的文件选择有问题",
                    PrimaryButtonText = "不再显示",
                    CloseButtonText = "我知道了",
                    DefaultButton = ContentDialogButton.Close
                };
                
                dialog.PrimaryButtonClick += (_, _) =>
                {
                    try { File.Create(ignoreFilePath).Dispose(); }
                    catch
                    {
                        // ignored
                    }
                };

                await dialog.ShowAsync(); 
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示 UAC 警告弹窗失败: {ex.Message}");
        }
    }
}
    
    private async Task CheckAndWarnVCRedistAsync()
    {
        var ignoreFilePath = Path.Combine(AppContext.BaseDirectory, ".no_vc_warning");
        if (File.Exists(ignoreFilePath)) return;

        if (!SystemEnvironmentHelper.IsVCRedistInstalled())
        {
        try
        {
            if (Content is FrameworkElement rootElement)
            {
                if (rootElement.XamlRoot == null)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    RoutedEventHandler onLoaded = null!;
                    onLoaded = (_, _) =>
                    {
                        rootElement.Loaded -= onLoaded;
                        tcs.TrySetResult(true);
                    };
                    rootElement.Loaded += onLoaded;
                    await tcs.Task;
                }

                ContentDialog dialog = new()
                {
                    XamlRoot = rootElement.XamlRoot,
                    Title = "缺少 C++ 运行库",
                    Content = "系统未安装 C++ VC14 运行库，这会导致本程序的注入功能无法正常使用\n\n是否前往微软官网下载并安装？",
                    PrimaryButtonText = "前往下载(X64)",
                    SecondaryButtonText = "不再提示",
                    CloseButtonText = "忽略警告",
                    DefaultButton = ContentDialogButton.Primary
                };

                var result = await dialog.ShowAsync();

                if (result == ContentDialogResult.Primary)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = ApiEndpoints.VcRedistDownloadUrl,
                        UseShellExecute = true
                    });
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    File.Create(ignoreFilePath).Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示 VC 运行库警告弹窗失败: {ex.Message}");
        }
    }
}

    #endregion
    
    #region Window & Background Management

    private async Task LoadBackgroundImageOpacityAsync()
    {
        try
        {
            var valueObj = await _localSettingsService.ReadSettingAsync("GlobalBackgroundImageOpacity");
            var opacity = 1.0;
            if (valueObj != null && double.TryParse(valueObj.ToString(), out var parsed)) opacity = parsed;
            ApplyBackgroundImageOpacity(opacity);
        }
        catch { ApplyBackgroundImageOpacity(1.0); }
    }

    private void ApplyBackgroundImageOpacity(double value)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        if (GlobalBackgroundImage != null) GlobalBackgroundImage.Opacity = clamped;
        if (GlobalBackgroundVideo != null) GlobalBackgroundVideo.Opacity = clamped;
    }

    private void ShowWindow()
    {
        RestoreFromSuspension();

        this.Show();
        BringToFront();
    }
    
    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange)
        {
            var presenter = sender.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                if (presenter.State == OverlappedPresenterState.Minimized)
                {
                    if (!_memoryOptimizationTimer.IsEnabled)
                    {
                        _memoryOptimizationTimer.Start();
                    }
                }
                else if (presenter.State != OverlappedPresenterState.Minimized)
                {
                    RestoreFromSuspension();
                }
            }
        }
    }

    private async void ExitApplication()
    {
        await SaveWindowSizeAsync();
        _isExit = true;
        TrayIcon.Dispose();
        Close();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExit) return;
        args.Cancel = true;

        if (!_isMainUiLoaded)
        {
            _isExit = true;
            TrayIcon.Dispose();
            Close();
            return;
        }

        if (_minimizeToTray)
        {
            this.Hide();
            _memoryOptimizationTimer.Start(); 
        }
        else
        {
            await SaveWindowSizeAsync();
            _isExit = true;
            TrayIcon.Dispose();
            Close();
        }
    }

    private async Task SaveWindowSizeAsync()
    {
        try
        {
            var localSettings = App.GetService<ILocalSettingsService>();
            var saveEnabledObj = await localSettings.ReadSettingAsync("IsSaveWindowSizeEnabled");
            if (saveEnabledObj != null && Convert.ToBoolean(saveEnabledObj))
            {
                await localSettings.SaveSettingAsync("SavedWindowWidth", Width);
                await localSettings.SaveSettingAsync("SavedWindowHeight", Height);
            }
        }
        catch
        {
            // ignored
        }
    }

    private void UpdateBackgroundOverlayTheme()
    {
        if (Content is FrameworkElement rootElement)
        {
            var currentTheme = rootElement.ActualTheme;
            if (currentTheme == ElementTheme.Default)
                currentTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        
            var themeBgColor = currentTheme == ElementTheme.Dark 
                ? Color.FromArgb(255, 32, 32, 32) 
                : Color.FromArgb(255, 243, 243, 243);

            GlobalBackgroundOverlay.Fill = new SolidColorBrush(themeBgColor);
            
            if (_isAcrylicOverlayEnabled && !_isVideoBackground)
            {
                PageBackgroundOverlay.Background = new AcrylicBrush
                {
                    TintColor = themeBgColor,
                    TintOpacity = 0.6,
                    FallbackColor = themeBgColor
                };
            }
            else
            {
                PageBackgroundOverlay.Background = new SolidColorBrush(themeBgColor);
            }

            ApplyFrameBackgroundOpacity(_frameBackgroundOpacity);
        }
    }
    private async Task LoadAndApplyAcrylicSettingAsync()
    {
        try
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var backdropJson = await localSettingsService.ReadSettingAsync("WindowBackdrop");
            WindowBackdropType backdropType;

            if (backdropJson != null)
                backdropType = (WindowBackdropType)Convert.ToInt32(backdropJson);
            else
            {
                var acrylicEnabled = await localSettingsService.ReadSettingAsync("IsAcrylicEnabled");
                var isEnabled = acrylicEnabled == null ? true : Convert.ToBoolean(acrylicEnabled);
                backdropType = isEnabled ? WindowBackdropType.Acrylic : WindowBackdropType.None;
            }
            ApplyBackdrop(backdropType);
        }
        catch { ApplyBackdrop(WindowBackdropType.Acrylic); }
    }

    private async Task LoadGlobalBackgroundAsync()
    {
        try
        {
            var enabledJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.IsBackgroundEnabledKey);
            var isCustomEnabled = enabledJson == null ? true : Convert.ToBoolean(enabledJson);

            if (isCustomEnabled)
            {
                var customPathObj = await _localSettingsService.ReadSettingAsync("CustomBackgroundPath");
                var customPath = customPathObj?.ToString();

                if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                {
                    var customResult = await _backgroundRenderer.GetCustomBackgroundAsync(customPath);
                    await ApplyGlobalBackgroundAsync(customResult);
                    return;
                }
            }

            var preferVideoSetting = await _localSettingsService.ReadSettingAsync("UserPreferVideoBackground");
            var preferVideo = preferVideoSetting != null && Convert.ToBoolean(preferVideoSetting);

            var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            var serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
            var server = (ServerType)serverValue;
            
            var result = await _backgroundRenderer.GetBackgroundAsync(server, preferVideo);
            await ApplyGlobalBackgroundAsync(result);
        }
        catch
        {
            await ClearGlobalBackgroundAsync();
        }
    }

private Task ApplyGlobalBackgroundAsync(BackgroundRenderResult? result)
{
    return RunOnUIThreadAsync(async void () => 
    {
        if (result == null) { await ClearGlobalBackgroundAsync(); return; }

        if (result.IsVideo)
        {
            _isVideoBackground = true;
            GlobalBackgroundImage.Visibility = Visibility.Collapsed;
            _globalBackgroundPlayer?.Pause();
            _globalBackgroundPlayer = new MediaPlayer
            {
                Source = result.VideoSource,
                IsMuted = true,
                IsLoopingEnabled = true,
                AutoPlay = true
            };
            GlobalBackgroundVideo.SetMediaPlayer(_globalBackgroundPlayer);
            GlobalBackgroundVideo.Visibility = Visibility.Visible;
        }
        else
        {
            _isVideoBackground = false;
            _globalBackgroundPlayer?.Pause();
            GlobalBackgroundVideo.Visibility = Visibility.Collapsed;
            
            var targetOpacity = GlobalBackgroundImage.Opacity; 
            var finalOpacity = targetOpacity > 0 ? targetOpacity : 1.0;
            
            GlobalBackgroundImage.Opacity = 0.0;
            GlobalBackgroundImage.Source = result.ImageSource;
            GlobalBackgroundImage.Visibility = Visibility.Visible;
            
            await Task.Delay(900); 
            
            var fadeInAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = finalOpacity,
                Duration = TimeSpan.FromMilliseconds(1000),
                EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(fadeInAnimation, GlobalBackgroundImage);
            Storyboard.SetTargetProperty(fadeInAnimation, "Opacity");
            
            var storyboard = new Storyboard();
            storyboard.Children.Add(fadeInAnimation);
            storyboard.Begin();
        }
        
        UpdateBackgroundOverlayTheme();
    });
}

    private Task ClearGlobalBackgroundAsync()
    {
        return RunOnUIThreadAsync(() =>
        {
            GlobalBackgroundImage.Source = null;
            GlobalBackgroundImage.Visibility = Visibility.Collapsed;
            GlobalBackgroundVideo.Source = null;
            GlobalBackgroundVideo.Visibility = Visibility.Collapsed;
            _globalBackgroundPlayer?.Pause();
            _globalBackgroundPlayer = null;
        });
    }

    private void ApplyBackdrop(WindowBackdropType type)
    {
        try
        {
            SystemBackdrop = null;
            switch (type)
            {
                case WindowBackdropType.Mica:
                    SystemBackdrop = new MicaBackdrop();
                    break;
                case WindowBackdropType.Acrylic:
                    SystemBackdrop = new DesktopAcrylicBackdrop();
                    break;
            }
        }
        catch
        {
            // ignored
        }
    }

    public async Task InitializeWindowSizeAsync()
    {
        try
        {
            var localSettings = App.GetService<ILocalSettingsService>();

            var accepted = await localSettings.ReadSettingAsync("UserAgreementAccepted");
            if (accepted == null || !Convert.ToBoolean(accepted))
            {
                Width = 850;
                Height = 560;
                WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
                AgreementFrame.Visibility = Visibility.Visible;
                AgreementFrame.Navigate(typeof(Views.AgreementPage));
                return;
            }

            await ApplyMainWindowSizeAsync(localSettings);
        }
        catch
        {
            Width = 850;
            Height = 560;
            WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
        }
    }

    private async Task ApplyMainWindowSizeAsync(ILocalSettingsService? localSettings = null)
    {
        try
        {
            localSettings ??= App.GetService<ILocalSettingsService>();
            var saveEnabledObj = await localSettings.ReadSettingAsync("IsSaveWindowSizeEnabled");

            if (saveEnabledObj != null && Convert.ToBoolean(saveEnabledObj))
            {
                var widthObj = await localSettings.ReadSettingAsync("SavedWindowWidth");
                var heightObj = await localSettings.ReadSettingAsync("SavedWindowHeight");

                if (widthObj != null && heightObj != null &&
                    double.TryParse(widthObj.ToString(), out var w) &&
                    double.TryParse(heightObj.ToString(), out var h))
                {
                    Width = w;
                    Height = h;
                    if (!_isOverlayShown) OverlayTranslate.Y = h + 100;
                    WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
                    return;
                }
            }
            Width = 1360;
            Height = 768;
            if (!_isOverlayShown) OverlayTranslate.Y = Height + 100;
            WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
        }
        catch
        {
            Width = 1360;
            Height = 768;
            WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        try
        {
            SetTitleBar(AppTitleBar);
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath)) TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
            UpdateTitleBarWithAdminStatus();
        }
        catch
        {
            // ignored
        }

        Activated -= OnWindowActivated;
    }

    private void UpdateTitleBarWithAdminStatus()
    {
        try
        {
            var isAdmin = SystemEnvironmentHelper.IsRunningAsAdministrator();
            TitleBarText.Text = isAdmin ? "芙芙启动器 [管理员]" : "芙芙启动器";
        }
        catch
        {
            // ignored
        }
    }

    #endregion
    
    #region Navigation & Layout

    private async void NavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        bool isAccepted = false;
        try
        {
            var accepted = await _localSettingsService.ReadSettingAsync("UserAgreementAccepted");
            isAccepted = accepted != null && Convert.ToBoolean(accepted);
        }
        catch { }

        if (!isAccepted)
        {
            return;
        }

        await PerformMainInitAsync();
    }

    private async Task PerformMainInitAsync()
    {
        try
        {
            foreach (var item in NavigationView.MenuItems)
            {
                if (item is FrameworkElement uiItem) SetupSpringAnimation(uiItem);
            }
            foreach (var item in NavigationView.FooterMenuItems)
            {
                if (item is FrameworkElement uiItem) SetupSpringAnimation(uiItem);
            }

            await LoadFrameBackgroundOpacityAsync();
            await LoadOverlayOpacityAsync();
            await LoadAcrylicOverlaySettingAsync();
            await LoadAndApplyAcrylicSettingAsync();
            await LoadGlobalBackgroundAsync();
            await LoadMinimizeToTraySettingAsync();
            await LoadMinWindowSizeLimitSettingAsync();
            ShowMainContent();
            _ = Task.Run(async () =>
            {
                try
                {
                    var refreshService = new TokenRefreshService();
                    await refreshService.RefreshCookieAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"启动时刷新 Token 失败: {ex.Message}");
                }
            });
            _ = Task.Run(async () => await HashValidationService.ValidateFilesAsync());
        }
        catch { ShowMainContent(); }
    }

    private void SetupSpringAnimation(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        element.SizeChanged += (_, e) =>
        {
            visual.CenterPoint = new Vector3((float)e.NewSize.Width / 2f, (float)e.NewSize.Height / 2f, 0f);
        };

        element.PointerPressed += (_, _) =>
        {
            var anim = compositor.CreateSpringVector3Animation();
            anim.Target = "Scale";
            anim.FinalValue = new Vector3(0.92f, 0.92f, 1f);

            anim.Period = TimeSpan.FromMilliseconds(20);
            anim.DampingRatio = 0.6f;

            visual.StartAnimation("Scale", anim);
        };

        void ResetScale()
        {
            var anim = compositor.CreateSpringVector3Animation();
            anim.Target = "Scale";
            anim.FinalValue = new Vector3(1f, 1f, 1f);

            anim.Period = TimeSpan.FromMilliseconds(60);
            anim.DampingRatio = 0.5f;

            visual.StartAnimation("Scale", anim);
        }

        element.PointerReleased += (_, _) => ResetScale();
        element.PointerExited += (_, _) => ResetScale();
    }

    private async Task LoadMinimizeToTraySettingAsync()
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync("MinimizeToTray");
            _minimizeToTray = value != null && Convert.ToBoolean(value);
        }
        catch { _minimizeToTray = false; }
    }

    private async Task LoadMinWindowSizeLimitSettingAsync()
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync("IsMinWindowSizeLimitEnabled");
            var enabled = value == null || Convert.ToBoolean(value);
            ApplyMinWindowSizeLimit(enabled);
        }
        catch { ApplyMinWindowSizeLimit(true); }
    }

    private void ApplyMinWindowSizeLimit(bool enabled)
    {
        if (enabled)
        {
            MinWidth = 1360;
            MinHeight = 768;
        }
        else
        {
            MinWidth = 0;
            MinHeight = 0;
        }
    }

    private void ShowAgreementPage()
    {
        _isMainUiLoaded = false;
        SystemMessageBar.Visibility = Visibility.Collapsed;
        _networkMonitorService.Stop();

        Width = 850;
        Height = 560;
        WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);

        AgreementFrame.Visibility = Visibility.Visible;
        NavigationView.Visibility = Visibility.Collapsed;
        AgreementFrame.Navigate(typeof(Views.AgreementPage));
    }

    private void ShowMainContent()
    {
        AgreementFrame.Visibility = Visibility.Collapsed;
        NavigationView.Visibility = Visibility.Visible;
        NavigationView.SelectedItem = NavigationView.MenuItems[0];

        if (ContentFrame.CurrentSourcePageType != typeof(Views.MainPage))
            ContentFrame.Navigate(typeof(Views.MainPage));

        UpdatePageOverlayState(true);

        _isMainUiLoaded = true;
        SystemMessageBar.Visibility = Visibility.Visible;
        _networkMonitorService.Start();
        _ = _networkMonitorService.CheckNetworkAndProxyStatusAsync();
        
        var redeemService = new RedeemCodeReminderService(_localSettingsService);
        _ = redeemService.CheckRedeemCodesForTodayAsync(msg => 
        {
            dispatcherQueue.TryEnqueue(() => ShowNotification(msg));
        });
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem selectedItem)
        {
            var viewModelTag = selectedItem.Tag?.ToString();

            if (viewModelTag == "FufuLauncher.ViewModels.SettingsViewModel")
            {
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = new Duration(TimeSpan.FromSeconds(0.7)),
                    EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                };

                Storyboard.SetTarget(anim, SettingsIconRotation);
                Storyboard.SetTargetProperty(anim, "Angle");

                var sb = new Storyboard();
                sb.Children.Add(anim);
                sb.Begin();
            }

            if (!string.IsNullOrEmpty(viewModelTag)) NavigateToPage(viewModelTag);
        }
    }

    private async void NavigateToPage(string viewModelTag)
    {
        var pageType = viewModelTag switch
        {
            "FufuLauncher.ViewModels.MainViewModel" => typeof(Views.MainPage),
            "FufuLauncher.ViewModels.BlankViewModel" => typeof(Views.BlankPage),
            "FufuLauncher.ViewModels.SettingsViewModel" => typeof(Views.SettingsPage),
            "FufuLauncher.ViewModels.AccountViewModel" => typeof(Views.AccountPage),
            "FufuLauncher.ViewModels.OtherViewModel" => typeof(Views.OtherPage),
            "FufuLauncher.ViewModels.CalculatorViewModel" => typeof(Views.CalculatorPage),
            "FufuLauncher.ViewModels.ControlPanelModel" => typeof(Views.PanelPage),
            "FufuLauncher.ViewModels.PluginViewModel" => typeof(Views.PluginPage),
            "FufuLauncher.ViewModels.DataViewModel" => typeof(Views.DataPage),
            "FufuLauncher.ViewModels.PluginSettingsViewModel" => typeof(Views.PluginSettingsPage),
            "FufuLauncher.ViewModels.HelpViewModel" => typeof(Views.HelpPage),
            "FufuLauncher.ViewModels.CommunityViewModel" => typeof(Views.CommunityPage),
            _ => null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
        {
            if (ContentFrame.Content is Page currentPage)
            {
                var exitStoryboard = currentPage.FindName("ExitStoryboard") as Storyboard;
                if (exitStoryboard != null)
                {
                    exitStoryboard.Begin();
                    await Task.Delay(300);
                }
            }

            ContentFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo());
            var isMainPage = pageType == typeof(Views.MainPage);
            UpdatePageOverlayState(isMainPage);
        }
    }

    public async Task NavigateToSettingsUpdateSectionAsync()
    {
        Activate();

        for (var i = 0; i < 40 && !_isMainUiLoaded; i++)
        {
            await Task.Delay(100);
        }

        var settingsItem = NavigationView.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "FufuLauncher.ViewModels.SettingsViewModel");

        if (settingsItem != null)
        {
            NavigationView.SelectedItem = settingsItem;
        }
        else
        {
            NavigateToPage("FufuLauncher.ViewModels.SettingsViewModel");
        }

        for (var i = 0; i < 40; i++)
        {
            if (ContentFrame.Content is Views.SettingsPage settingsPage)
            {
                await settingsPage.NavigateToUpdateSectionAsync();
                return;
            }

            await Task.Delay(100);
        }
    }

    public async Task NavigateToAccountPageAsync()
    {
        Activate();

        for (var i = 0; i < 40 && !_isMainUiLoaded; i++)
        {
            await Task.Delay(100);
        }

        var accountItem = NavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "FufuLauncher.ViewModels.AccountViewModel");

        if (accountItem != null)
            NavigationView.SelectedItem = accountItem;
        else
            NavigateToPage("FufuLauncher.ViewModels.AccountViewModel");
    }

    private void UpdatePageOverlayState(bool isMainPage)
{
    try
    {
        var screenHeight = Bounds.Height > 0 ? Bounds.Height : 1000;

        if (isMainPage && _isOverlayShown)
        {
            var translateAnim = new DoubleAnimation
            {
                From = 0,
                To = screenHeight + 50,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var opacityAnim = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(400),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var sb = new Storyboard();
            Storyboard.SetTarget(translateAnim, OverlayTranslate);
            Storyboard.SetTargetProperty(translateAnim, "Y");
            
            Storyboard.SetTarget(opacityAnim, PageBackgroundOverlay);
            Storyboard.SetTargetProperty(opacityAnim, "Opacity");

            sb.Children.Add(translateAnim);
            sb.Children.Add(opacityAnim);
            sb.Begin();

            _isOverlayShown = false;
        }
        else if (!isMainPage && !_isOverlayShown)
        {
            OverlayTranslate.Y = screenHeight;
            PageBackgroundOverlay.Opacity = 0.0;

            var translateAnim = new DoubleAnimation
            {
                From = screenHeight,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var opacityAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(500),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var sb = new Storyboard();
            Storyboard.SetTarget(translateAnim, OverlayTranslate);
            Storyboard.SetTargetProperty(translateAnim, "Y");
            
            Storyboard.SetTarget(opacityAnim, PageBackgroundOverlay);
            Storyboard.SetTargetProperty(opacityAnim, "Opacity");

            sb.Children.Add(translateAnim);
            sb.Children.Add(opacityAnim);
            sb.Begin();

            _isOverlayShown = true;
        }
        else if (!isMainPage && _isOverlayShown)
        {
            OverlayTranslate.Y = 0;
            PageBackgroundOverlay.Opacity = 1.0;
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[MainWindow] 遮罩动画异常: {ex.Message}");
    }
}

    private void Settings_ColorValuesChanged(UISettings sender, object args)
    {
        dispatcherQueue.TryEnqueue(() => { TitleBarHelper.ApplySystemThemeToCaptionButtons(); });
    }
    
    #endregion
    
    #region Notifications

    private void ShowNotification(NotificationMessage message)
    {
        try
        {
            if (NotificationContainer.Visibility == Visibility.Collapsed || 
                (NotificationContainer.Tag is string state && state == "Closing"))
            {
                NotificationContainer.Tag = null;
                NotificationContainer.Visibility = Visibility.Visible;
                PlayContainerEntranceAnimation();
            }

            var infoBar = CreateInfoBar(message);
            NotificationPanel.Children.Insert(0, infoBar);
            PlayEntranceAnimation(infoBar);
            
            if (message.Duration > 0)
            {
                SetupAutoDismiss(infoBar, message.Duration);
            }
        }
        catch
        {
            // ignored
        }
    }

    private InfoBar CreateInfoBar(NotificationMessage message)
    {
        var infoBar = new InfoBar
        {
            Title = message.Title,
            Message = message.Message,
            Severity = GetInfoBarSeverity(message.Type),
            IsOpen = true,
            IsClosable = true,
            Margin = new Thickness(0, 0, 0, 8),
            Width = 360,
            RenderTransform = new TranslateTransform { X = 380 },
            Opacity = 0
        };

        infoBar.Closing += (sender, args) =>
        {
            args.Cancel = true;
            
            if (infoBar.Tag is string state && state == "Closing")
            {
                return;
            }
            
            infoBar.Tag = "Closing";
            
            infoBar.IsHitTestVisible = false;

            DismissInfoBar(infoBar);
        };

        return infoBar;
    }

    private InfoBarSeverity GetInfoBarSeverity(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => InfoBarSeverity.Success,
            NotificationType.Warning => InfoBarSeverity.Warning,
            NotificationType.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
    }

    private void PlayEntranceAnimation(FrameworkElement element)
    {
        var transformAnim = new DoubleAnimation
        {
            From = 380,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(transformAnim, element.RenderTransform);
        Storyboard.SetTargetProperty(transformAnim, "X");

        var opacityAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnim, element);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(transformAnim);
        storyboard.Children.Add(opacityAnim);
        storyboard.Begin();
    }

    private void DismissInfoBar(FrameworkElement element)
    {
        if (element is InfoBar infoBar && (infoBar.Title == "兑换码失效提醒" || infoBar.Title == "今日兑换码提醒"))
        {
            _ = _localSettingsService.SaveSettingAsync("LastRedeemCodeReminderDate", DateTime.Now.ToString("yyyy-MM-dd"));
            Debug.WriteLine("[RedeemCodes] 已将关闭状态写入数据库");
        }
        
        var transformAnim = new DoubleAnimation
        {
            From = 0,
            To = 380,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(transformAnim, element.RenderTransform);
        Storyboard.SetTargetProperty(transformAnim, "X");

        var opacityAnim = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(opacityAnim, element);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(transformAnim);
        storyboard.Children.Add(opacityAnim);
        
        var isLastNotification = NotificationPanel.Children
            .OfType<FrameworkElement>()
            .All(c => c.Tag is string state && state == "Closing");

        if (isLastNotification)
        {
            PlayContainerExitAnimation();
        }
        
        storyboard.Completed += (_, _) =>
        {
            try
            {
                NotificationPanel.Children.Remove(element);
            }
            catch
            {
                // ignored
            }
        };
        
        storyboard.Begin();
    }
    
    private void ClearAllNotifications_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var currentNotifications = NotificationPanel.Children.ToList();
            
            foreach (var child in currentNotifications)
            {
                if (child is InfoBar infoBar)
                {
                    if (infoBar.Tag is string state && state == "Closing") continue;
                    
                    infoBar.Tag = "Closing";
                    infoBar.IsHitTestVisible = false;
                    DismissInfoBar(infoBar);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"一键清除通知异常: {ex.Message}");
        }
    }
    
    private void PlayContainerEntranceAnimation()
    {
        NotificationContainer.RenderTransformOrigin = new Point(1, 1);
        var scaleTransform = new ScaleTransform { ScaleX = 0.8, ScaleY = 0.8 };
        NotificationContainer.RenderTransform = scaleTransform;
        NotificationContainer.Opacity = 0;
        
        var scaleXAnim = new DoubleAnimation
        {
            From = 0.8,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(350)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleXAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");
        
        var scaleYAnim = new DoubleAnimation
        {
            From = 0.8,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(350)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleYAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");
        
        var opacityAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnim, NotificationContainer);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleXAnim);
        storyboard.Children.Add(scaleYAnim);
        storyboard.Children.Add(opacityAnim);
        storyboard.Begin();
    }
    
    private void PlayContainerExitAnimation()
    {
        if (NotificationContainer.Tag is string state && state == "Closing") return;
        
        NotificationContainer.Tag = "Closing";

        if (!(NotificationContainer.RenderTransform is ScaleTransform scaleTransform))
        {
            scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            NotificationContainer.RenderTransform = scaleTransform;
        }

        var scaleXAnim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.8,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleXAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");

        var scaleYAnim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.8,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleYAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");

        var opacityAnim = new DoubleAnimation
        {
            From = NotificationContainer.Opacity,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(opacityAnim, NotificationContainer);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleXAnim);
        storyboard.Children.Add(scaleYAnim);
        storyboard.Children.Add(opacityAnim);
        
        storyboard.Completed += (_, _) =>
        {
            if (NotificationContainer.Tag is string finalState && finalState == "Closing")
            {
                NotificationContainer.Visibility = Visibility.Collapsed;
            }
        };
        
        storyboard.Begin();
    }

    private void SetupAutoDismiss(FrameworkElement element, int duration)
    {
        var timer = dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(duration);
        timer.Tick += (_, _) => 
        {
            timer.Stop();
            
            if (element.Tag is string state && state == "Closing")
            {
                return;
            }

            element.Tag = "Closing";
            element.IsHitTestVisible = false;

            DismissInfoBar(element);
        };
        timer.Start();
    }

    #endregion
    
    #region Opacity & Visual Settings

    private async Task LoadOverlayOpacityAsync()
    {
        try
        {
            var valueObj = await _localSettingsService.ReadSettingAsync("GlobalBackgroundOverlayOpacity");
            var opacity = 0.3;
            if (valueObj != null && double.TryParse(valueObj.ToString(), out var parsed)) opacity = parsed;
            ApplyOverlayOpacity(opacity);
        }
        catch { ApplyOverlayOpacity(0.3); }
    }

    private async Task LoadFrameBackgroundOpacityAsync()
    {
        try
        {
            var valueObj = await _localSettingsService.ReadSettingAsync("ContentFrameBackgroundOpacity");
            var opacity = 0.0;
            if (valueObj != null && double.TryParse(valueObj.ToString(), out var parsed)) opacity = parsed;
            ApplyFrameBackgroundOpacity(opacity);
        }
        catch { ApplyFrameBackgroundOpacity(0.0); }
    }

    private void ApplyOverlayOpacity(double value)
    {
        GlobalBackgroundOverlay.Opacity = Math.Clamp(value, 0.0, 1.0);
    }

    private void ApplyFrameBackgroundOpacity(double value)
    {
        _frameBackgroundOpacity = Math.Clamp(value, 0.0, 1.0);
        if (ContentFrame == null) return;

        if (_frameBackgroundOpacity < 0.05)
        {
            ContentFrame.Background = new SolidColorBrush(Colors.Transparent);
            return;
        }

        SolidColorBrush brush;
        if (ContentFrame.Background is SolidColorBrush existingBrush) brush = existingBrush;
        else { brush = new SolidColorBrush(); ContentFrame.Background = brush; }

        var theme = ElementTheme.Default;
        if (Content is FrameworkElement root)
        {
            theme = root.ActualTheme;
            if (theme == ElementTheme.Default) theme = Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
        }
        var baseColor = theme == ElementTheme.Dark ? Colors.Black : Colors.White;
        baseColor.A = (byte)(_frameBackgroundOpacity * 255);
        brush.Color = baseColor;
    }
    #endregion
}