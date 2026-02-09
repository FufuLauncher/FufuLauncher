using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Graphics;
using Windows.Media.Playback;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace FufuLauncher;

public sealed partial class MainWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;
    private UISettings settings;
    private readonly IBackgroundRenderer _backgroundRenderer;
    private readonly ILocalSettingsService _localSettingsService;
    private MediaPlayer? _globalBackgroundPlayer;
    private double _frameBackgroundOpacity;
    private bool _minimizeToTray;
    private bool _isExit;
    private bool _isOverlayShown;

    private bool _isVideoBackground;

    private DispatcherTimer _networkCheckTimer;
    private DispatcherTimer _messageDismissTimer;
    private bool? _lastNetworkAvailable;
    private bool? _lastProxyEnabled;
    private bool _isSystemMessageVisible;

    private bool _isMainUiLoaded;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, TOKEN_INFORMATION_CLASS TokenInformationClass, IntPtr TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
    
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, int minimumWorkingSetSize, int maximumWorkingSetSize);
    
    private DispatcherTimer _memoryOptimizationTimer;
    private bool _isSuspended;

    private enum TOKEN_INFORMATION_CLASS
    {
        TokenElevationType = 18
    }

    private enum TOKEN_ELEVATION_TYPE
    {
        TokenElevationTypeFull = 2
    }

    public IRelayCommand ShowWindowCommand
    {
        get;
    }
    public IRelayCommand ExitApplicationCommand
    {
        get;
    }

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

        WeakReferenceMessenger.Default.Register<AgreementAcceptedMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(() =>
            {
                try { ShowMainContent(); }
                catch (Exception ex) { Debug.WriteLine($"消息处理异常: {ex.Message}"); }
            });
        });

        if (Content is FrameworkElement rootElement)
        {
            rootElement.ActualThemeChanged += (s, e) => UpdateBackgroundOverlayTheme();
        }

        WeakReferenceMessenger.Default.Register<ValueChangedMessage<WindowBackdropType>>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyBackdrop(m.Value));
        });

        WeakReferenceMessenger.Default.Register<NotificationMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ShowNotification(m));
        });

        WeakReferenceMessenger.Default.Register<BackgroundRefreshMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(async () => { await LoadGlobalBackgroundAsync(); });
        });

        WeakReferenceMessenger.Default.Register<BackgroundOverlayOpacityChangedMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyOverlayOpacity(m.Value));
        });
        
        _memoryOptimizationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _memoryOptimizationTimer.Tick += OnMemoryOptimizationTick;
        
        AppWindow.Changed += AppWindow_Changed;

        WeakReferenceMessenger.Default.Register<FrameBackgroundOpacityChangedMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyFrameBackgroundOpacity(m.Value));
        });

        WeakReferenceMessenger.Default.Register<MinimizeToTrayChangedMessage>(this, (r, m) =>
        {
            _minimizeToTray = m.Value;
        });
        WeakReferenceMessenger.Default.Register<BackgroundImageOpacityChangedMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(() => ApplyBackgroundImageOpacity(m.Value));
        });

        dispatcherQueue.TryEnqueue(async () => await LoadBackgroundImageOpacityAsync());
        Activated += OnWindowActivated;

        dispatcherQueue.TryEnqueue(() => CheckAndWarnUacElevation());

        SizeChanged += MainWindow_SizeChanged;

        UpdateBackgroundOverlayTheme();

        _messageDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _messageDismissTimer.Tick += (s, e) => HideSystemMessage();
        _networkCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _networkCheckTimer.Tick += (s, e) => CheckNetworkAndProxyStatus();

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
        
        _networkCheckTimer.Stop();
        _messageDismissTimer.Stop();
        
        GC.Collect();
        GC.WaitForPendingFinalizers();

        try
        {
            SetProcessWorkingSetSize(GetCurrentProcess(), -1, -1);
        }
        catch
        {
            // ignored
        }

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
        
        if (!_networkCheckTimer.IsEnabled)
        {
            _networkCheckTimer.Start();
        }
        
        Debug.WriteLine("应用已唤醒");
    }

    private async void CheckNetworkAndProxyStatus()
    {
        if (!_isMainUiLoaded) return;

        var (currentNetwork, currentProxy) = await Task.Run(() =>
        {
            bool isNet = NetworkInterface.GetIsNetworkAvailable();
            bool isProxy = false;
            if (isNet)
            {
                try
                {
                    var proxy = WebRequest.GetSystemWebProxy();
                    Uri resource = new("https://www.microsoft.com");
                    isProxy = !proxy.IsBypassed(resource);
                }
                catch { isProxy = false; }
            }
            return (isNet, isProxy);
        });

        bool shouldNotify = false;
        string msg = "";
        string icon = "";
        Color color = Colors.White;

        if (!currentNetwork && (_lastNetworkAvailable == null || _lastNetworkAvailable == true))
        {
            shouldNotify = true;
            msg = "网络连接已断开，请检测你的网络设置";
            icon = "\uEB55";
            color = Colors.OrangeRed;
        }
        else if (currentNetwork && currentProxy && (_lastProxyEnabled == null || _lastProxyEnabled == false))
        {
            shouldNotify = true;
            msg = "正在使用代理网络连接，请注意你的流量消耗";
            icon = "\uE12B";
            color = Colors.DodgerBlue;
        }

        _lastNetworkAvailable = currentNetwork;
        _lastProxyEnabled = currentProxy;

        if (shouldNotify)
        {
            ShowAutoDismissMessage(msg, icon, color);
        }
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

    private async void CheckAndWarnUacElevation()
    {
        var ignoreFilePath = Path.Combine(AppContext.BaseDirectory, ".no_uac_warning");
        
        if (File.Exists(ignoreFilePath)) return;

        if (IsUacElevatedWithConsent())
        {
            if (Content is FrameworkElement rootElement)
            {
                if (rootElement.XamlRoot == null)
                {
                    var tcs = new TaskCompletionSource<bool>();
                    void OnLoaded(object sender, RoutedEventArgs e)
                    {
                        rootElement.Loaded -= OnLoaded;
                        tcs.TrySetResult(true);
                    }
                    rootElement.Loaded += OnLoaded;
                    if (rootElement.XamlRoot == null) await tcs.Task;
                }
                
                ContentDialog dialog = new()
                {
                    XamlRoot = rootElement.XamlRoot,
                    Title = "权限警告",
                    Content = "程序正以管理员身份运行，可能会影响部分功能。",
                    PrimaryButtonText = "不再显示",
                    CloseButtonText = "我知道了",
                    DefaultButton = ContentDialogButton.Close
                };
                
                dialog.PrimaryButtonClick += (_, _) =>
                {
                    try
                    {
                        File.Create(ignoreFilePath).Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"创建忽略文件失败: {ex.Message}");
                    }
                };

                try { await dialog.ShowAsync(); } catch { }
            }
        }
    }

    private bool IsUacElevatedWithConsent()
    {
        try
        {
            if (!IsRunningAsAdministrator()) return false;
            IntPtr tokenHandle = IntPtr.Zero;
            if (OpenProcessToken(GetCurrentProcess(), 0x0008, out tokenHandle))
            {
                int size = Marshal.SizeOf(typeof(int));
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    if (GetTokenInformation(tokenHandle, TOKEN_INFORMATION_CLASS.TokenElevationType, ptr, (uint)size, out _))
                    {
                        var type = (TOKEN_ELEVATION_TYPE)Marshal.ReadInt32(ptr);
                        return type == TOKEN_ELEVATION_TYPE.TokenElevationTypeFull;
                    }
                }
                finally { Marshal.FreeHGlobal(ptr); if (tokenHandle != IntPtr.Zero) CloseHandle(tokenHandle); }
            }
        }
        catch { }
        return false;
    }

    private async Task LoadBackgroundImageOpacityAsync()
    {
        try
        {
            var valueObj = await _localSettingsService.ReadSettingAsync("GlobalBackgroundImageOpacity");
            double opacity = 1.0;
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
        this.Close();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isExit) return;
        args.Cancel = true;
        if (_minimizeToTray)
        {
            this.Hide();
            _memoryOptimizationTimer.Start(); 
        }
        else
        {
            await SaveWindowSizeAsync();
            _isExit = true;
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
        catch { }
    }

    private void UpdateBackgroundOverlayTheme()
    {
        if (Content is FrameworkElement rootElement)
        {
            var currentTheme = rootElement.ActualTheme;
            if (currentTheme == ElementTheme.Default)
                currentTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;

            if (currentTheme == ElementTheme.Dark)
                GlobalBackgroundOverlay.Fill = new SolidColorBrush(Colors.Black);
            else
                GlobalBackgroundOverlay.Fill = new SolidColorBrush(Colors.White);

            if (_isVideoBackground)
            {
                var solidColor = currentTheme == ElementTheme.Dark ? Colors.Black : Colors.White;
                solidColor.A = (byte)(currentTheme == ElementTheme.Dark ? 120 : 150);
                PageBackgroundOverlay.Background = new SolidColorBrush(solidColor);
            }
            else
            {
                var acrylic = new AcrylicBrush();

                if (currentTheme == ElementTheme.Dark)
                {
                    acrylic.TintColor = Colors.Black;
                    acrylic.TintOpacity = 0.3;
                    acrylic.FallbackColor = Color.FromArgb(20, 0, 0, 0);
                }
                else
                {
                    acrylic.TintColor = Colors.White;
                    acrylic.TintOpacity = 0.2;
                    acrylic.FallbackColor = Color.FromArgb(20, 255, 255, 255);
                }

                PageBackgroundOverlay.Background = acrylic;
            }

            if (SystemMessageBar.Children.Count > 0 && SystemMessageBar.Children[0] is Border msgBorder && msgBorder.Background is AcrylicBrush msgAcrylic)
            {
                msgAcrylic.TintColor = currentTheme == ElementTheme.Dark ? Colors.Black : Colors.White;
                msgAcrylic.Opacity = currentTheme == ElementTheme.Dark ? 0.7 : 0.6;
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
                bool isEnabled = acrylicEnabled == null ? true : Convert.ToBoolean(acrylicEnabled);
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
            bool preferVideo = preferVideoSetting != null && Convert.ToBoolean(preferVideoSetting);

            var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            int serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
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
        return RunOnUIThreadAsync(() =>
        {
            if (result == null) { ClearGlobalBackgroundAsync(); return; }

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
                GlobalBackgroundImage.Source = result.ImageSource;
                GlobalBackgroundImage.Visibility = Visibility.Visible;
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
            this.SystemBackdrop = null;
            switch (type)
            {
                case WindowBackdropType.Mica:
                    this.SystemBackdrop = new MicaBackdrop();
                    break;
                case WindowBackdropType.Acrylic:
                    this.SystemBackdrop = new DesktopAcrylicBackdrop();
                    break;
            }
        }
        catch { }
    }

    public async Task InitializeWindowSizeAsync()
    {
        try
        {
            var localSettings = App.GetService<ILocalSettingsService>();
            var saveEnabledObj = await localSettings.ReadSettingAsync("IsSaveWindowSizeEnabled");

            if (saveEnabledObj != null && Convert.ToBoolean(saveEnabledObj))
            {
                var widthObj = await localSettings.ReadSettingAsync("SavedWindowWidth");
                var heightObj = await localSettings.ReadSettingAsync("SavedWindowHeight");

                if (widthObj != null && heightObj != null &&
                    double.TryParse(widthObj.ToString(), out double w) &&
                    double.TryParse(heightObj.ToString(), out double h))
                {
                    Width = w;
                    Height = h;
                    if (!_isOverlayShown) OverlayTranslate.Y = h + 100;
                    CenterWindowOnScreen();
                    return;
                }
            }
            Width = 1360;
            Height = 768;
            if (!_isOverlayShown) OverlayTranslate.Y = Height + 100;
            CenterWindowOnScreen();
        }
        catch
        {
            Width = 1360;
            Height = 768;
            CenterWindowOnScreen();
        }
    }

    private void CenterWindowOnScreen()
    {
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            if (displayArea == null) return;

            var workArea = displayArea.WorkArea;
            var currentSize = AppWindow.Size;
            if (currentSize.Width <= 0 || currentSize.Height <= 0)
            {
                currentSize = new SizeInt32((int)Math.Round(Width), (int)Math.Round(Height));
            }

            int targetX = workArea.X + Math.Max(0, (workArea.Width - currentSize.Width) / 2);
            int targetY = workArea.Y + Math.Max(0, (workArea.Height - currentSize.Height) / 2);

            AppWindow.Move(new PointInt32(targetX, targetY));
        }
        catch { }
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
        catch { }
        Activated -= OnWindowActivated;
    }

    private void UpdateTitleBarWithAdminStatus()
    {
        try
        {
            bool isAdmin = IsRunningAsAdministrator();
            TitleBarText.Text = isAdmin ? "芙芙启动器 [管理员]" : "芙芙启动器";
        }
        catch { }
    }

    private bool IsRunningAsAdministrator()
    {
        try
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch { return false; }
    }

    private async void NavigationView_Loaded(object sender, RoutedEventArgs e)
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
            await LoadAndApplyAcrylicSettingAsync();
            await LoadGlobalBackgroundAsync();
            await LoadMinimizeToTraySettingAsync();
            await CheckUserAgreementAsync();
        }
        catch { ShowMainContent(); }
    }

    private void SetupSpringAnimation(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        element.SizeChanged += (s, e) =>
        {
            visual.CenterPoint = new Vector3((float)e.NewSize.Width / 2f, (float)e.NewSize.Height / 2f, 0f);
        };

        element.PointerPressed += (s, e) =>
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

        element.PointerReleased += (s, e) => ResetScale();
        element.PointerExited += (s, e) => ResetScale();
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

    private async Task CheckUserAgreementAsync()
    {
        try
        {
            var accepted = await _localSettingsService.ReadSettingAsync("UserAgreementAccepted");
            bool isAccepted = accepted != null && Convert.ToBoolean(accepted);
            if (!isAccepted) ShowAgreementPage();
            else ShowMainContent();
        }
        catch { ShowMainContent(); }
    }

    private void ShowAgreementPage()
    {
        _isMainUiLoaded = false;
        SystemMessageBar.Visibility = Visibility.Collapsed;
        _networkCheckTimer.Stop();

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
        _networkCheckTimer.Start();
        CheckNetworkAndProxyStatus();
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

    private void NavigateToPage(string viewModelTag)
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
            _ => null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
            var isMainPage = pageType == typeof(Views.MainPage);
            UpdatePageOverlayState(isMainPage);
        }
    }

    private void UpdatePageOverlayState(bool isMainPage)
    {
        try
        {
            double screenHeight = Bounds.Height > 0 ? Bounds.Height : 1000;

            if (isMainPage && _isOverlayShown)
            {
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = screenHeight + 50,
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };

                var sb = new Storyboard();
                Storyboard.SetTarget(anim, OverlayTranslate);
                Storyboard.SetTargetProperty(anim, "Y");
                sb.Children.Add(anim);
                sb.Begin();

                _isOverlayShown = false;
            }
            else if (!isMainPage && !_isOverlayShown)
            {
                OverlayTranslate.Y = screenHeight;

                var anim = new DoubleAnimation
                {
                    From = screenHeight,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(500),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var sb = new Storyboard();
                Storyboard.SetTarget(anim, OverlayTranslate);
                Storyboard.SetTargetProperty(anim, "Y");
                sb.Children.Add(anim);
                sb.Begin();

                _isOverlayShown = true;
            }
            else if (!isMainPage && _isOverlayShown)
            {
                OverlayTranslate.Y = 0;
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

    private void ShowNotification(NotificationMessage message)
    {
        try
        {
            var notificationCard = CreateNotificationCard(message);
            NotificationPanel.Children.Add(notificationCard);
            AnimateNotification(notificationCard, 380, 0, 300);
            if (message.Duration > 0) SetupAutoDismiss(notificationCard, message.Duration);
        }
        catch { }
    }

    private Grid CreateNotificationCard(NotificationMessage message)
    {
        var card = new Grid
        {
            Background = GetNotificationBrush(message.Type),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Height = 80,
            Width = 360,
            Margin = new Thickness(0, 0, 0, 8),
            RenderTransform = new TranslateTransform { X = 380 }
        };
        var icon = new FontIcon { FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16, Glyph = GetNotificationIcon(message.Type), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0), Foreground = new SolidColorBrush(Colors.White) };
        var contentPanel = new StackPanel { Spacing = 4, Margin = new Thickness(12, 0, 0, 0) };
        contentPanel.Children.Add(new TextBlock { Text = message.Title, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.WrapWholeWords, Foreground = new SolidColorBrush(Colors.White) });
        contentPanel.Children.Add(new TextBlock { Text = message.Message, FontSize = 12, Opacity = 0.9, TextWrapping = TextWrapping.WrapWholeWords, Foreground = new SolidColorBrush(Colors.White) });

        var closeButton = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 12 }, Background = new SolidColorBrush(Colors.Transparent), BorderBrush = new SolidColorBrush(Colors.Transparent), Width = 32, Height = 32, Margin = new Thickness(0, -4, -4, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Foreground = new SolidColorBrush(Colors.White) };
        closeButton.Click += (s, e) => { try { NotificationPanel.Children.Remove(card); } catch { } };

        card.Children.Add(icon); card.Children.Add(contentPanel); card.Children.Add(closeButton);
        return card;
    }

    private void AnimateNotification(FrameworkElement element, double from, double to, int duration)
    {
        var animation = new DoubleAnimation { From = from, To = to, Duration = new Duration(TimeSpan.FromMilliseconds(duration)), EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(animation, element.RenderTransform);
        Storyboard.SetTargetProperty(animation, "X");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void SetupAutoDismiss(FrameworkElement card, int duration)
    {
        var timer = dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(duration);
        timer.Tick += (s, e) => { try { NotificationPanel.Children.Remove(card); } catch { } timer.Stop(); };
        timer.Start();
    }

    private Brush GetNotificationBrush(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => new SolidColorBrush(ColorHelper.FromArgb(255, 28, 175, 95)),
            NotificationType.Warning => new SolidColorBrush(ColorHelper.FromArgb(255, 255, 185, 0)),
            NotificationType.Error => new SolidColorBrush(ColorHelper.FromArgb(255, 232, 17, 35)),
            _ => new SolidColorBrush(ColorHelper.FromArgb(255, 0, 103, 192))
        };
    }

    private string GetNotificationIcon(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => "\uE930",
            NotificationType.Warning => "\uE7BA",
            NotificationType.Error => "\uE711",
            _ => "\uE946"
        };
    }

    private async Task LoadOverlayOpacityAsync()
    {
        try
        {
            var valueObj = await _localSettingsService.ReadSettingAsync("GlobalBackgroundOverlayOpacity");
            double opacity = 0.3;
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
            double opacity = 0.0;
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
}