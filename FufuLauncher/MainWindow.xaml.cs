using System.Diagnostics;
using System.Security.Principal;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI.ViewManagement;
using CommunityToolkit.Mvvm.Messaging.Messages;
using FufuLauncher.ViewModels;
using FufuLauncher.Services.Background;
using Windows.Media.Playback;
using FufuLauncher.Services;
using FufuLauncher.Models;

namespace FufuLauncher;

public sealed partial class MainWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;
    private UISettings settings;
    private readonly IBackgroundRenderer _backgroundRenderer;
    private readonly ILocalSettingsService _localSettingsService;
    private MediaPlayer? _globalBackgroundPlayer;

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
        
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
        
        Title = "芙芙启动器";

        ExtendsContentIntoTitleBar = true;

        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged;
        _backgroundRenderer = App.GetService<IBackgroundRenderer>();
        _localSettingsService = App.GetService<ILocalSettingsService>();

        SetInitialWindowSize();

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
            // 初始化时执行一次
            UpdateBackgroundOverlayTheme();
        }
        
        Debug.WriteLine("[MainWindow] 开始注册消息监听...");
        WeakReferenceMessenger.Default.Register<ValueChangedMessage<WindowBackdropType>>(this, (r, m) =>
        {
            Debug.WriteLine($"[MainWindow] 收到消息! 准备切换背景为: {m.Value}");
            dispatcherQueue.TryEnqueue(() =>
            {
                ApplyBackdrop(m.Value);
            });
        });

        WeakReferenceMessenger.Default.Register<NotificationMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(() =>
            {
                try { ShowNotification(m); }
                catch (Exception ex) { Debug.WriteLine($"显示通知异常: {ex.Message}"); }
            });
        });

        WeakReferenceMessenger.Default.Register<BackgroundRefreshMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(async () => { await LoadGlobalBackgroundAsync(); });
        });

        this.Activated += OnWindowActivated;
    }
    
    private void UpdateBackgroundOverlayTheme()
    {
        // 确保在 UI 线程执行
        if (Content is FrameworkElement rootElement)
        {
            // 获取当前主题
            var currentTheme = rootElement.ActualTheme;

            // 如果是系统默认，则需要判断系统实际状态（通常 ActualTheme 会自动解析，但这里做个保险）
            if (currentTheme == ElementTheme.Default)
            {
                currentTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark 
                    ? ElementTheme.Dark 
                    : ElementTheme.Light;
            }

            //根据主题设置颜色
            if (currentTheme == ElementTheme.Dark)
            {
                // 深色模式：使用黑色滤镜，让背景变暗，确保白字清晰
                GlobalBackgroundOverlay.Fill = new SolidColorBrush(Colors.Black);
            }
            else
            {
                // 浅色模式：使用白色滤镜，让背景变亮，确保黑字清晰
                GlobalBackgroundOverlay.Fill = new SolidColorBrush(Colors.White);
            }
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
            {
                backdropType = (WindowBackdropType)Convert.ToInt32(backdropJson);
            }
            else
            {
                var acrylicEnabled = await localSettingsService.ReadSettingAsync("IsAcrylicEnabled");
                bool isEnabled = acrylicEnabled == null ? true : Convert.ToBoolean(acrylicEnabled);
                backdropType = isEnabled ? WindowBackdropType.Acrylic : WindowBackdropType.None;
            }

            ApplyBackdrop(backdropType);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载背景设置失败: {ex.Message}");
            ApplyBackdrop(WindowBackdropType.Acrylic);
        }
    }
    private async Task LoadGlobalBackgroundAsync()
    {
        try
        {
            var globalBgSetting = await _localSettingsService.ReadSettingAsync("UseGlobalBackground");
            bool useGlobalBg = globalBgSetting == null ? true : Convert.ToBoolean(globalBgSetting);
            if (!useGlobalBg)
            {
                await ClearGlobalBackgroundAsync();
                return;
            }

            var customPathObj = await _localSettingsService.ReadSettingAsync("CustomBackgroundPath");
            var customPath = customPathObj?.ToString();
            var hasCustom = !string.IsNullOrEmpty(customPath) && File.Exists(customPath);

            if (hasCustom)
            {
                var customResult = await _backgroundRenderer.GetCustomBackgroundAsync(customPath);
                await ApplyGlobalBackgroundAsync(customResult);
                return;
            }

            var enabledJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.IsBackgroundEnabledKey);
            bool isEnabled = enabledJson == null ? true : Convert.ToBoolean(enabledJson);
            if (!isEnabled)
            {
                await ClearGlobalBackgroundAsync();
                return;
            }

            var preferVideoSetting = await _localSettingsService.ReadSettingAsync("UserPreferVideoBackground");
            bool preferVideo = preferVideoSetting != null && Convert.ToBoolean(preferVideoSetting);

            var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            int serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
            var server = (ServerType)serverValue;

            var result = await _backgroundRenderer.GetBackgroundAsync(server, preferVideo);
            await ApplyGlobalBackgroundAsync(result);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow] 加载全局背景失败: {ex.Message}");
            await ClearGlobalBackgroundAsync();
        }
    }

    private Task ApplyGlobalBackgroundAsync(BackgroundRenderResult? result)
    {
        return RunOnUIThreadAsync(() =>
        {
            if (result == null)
            {
                GlobalBackgroundImage.Source = null;
                GlobalBackgroundImage.Visibility = Visibility.Collapsed;
                GlobalBackgroundVideo.Source = null;
                GlobalBackgroundVideo.Visibility = Visibility.Collapsed;
                _globalBackgroundPlayer?.Pause();
                _globalBackgroundPlayer = null;
                return;
            }

            if (result.IsVideo)
            {
                GlobalBackgroundImage.Source = null;
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
                _globalBackgroundPlayer?.Pause();
                _globalBackgroundPlayer = null;
                GlobalBackgroundVideo.Source = null;
                GlobalBackgroundVideo.Visibility = Visibility.Collapsed;

                GlobalBackgroundImage.Source = result.ImageSource;
                GlobalBackgroundImage.Visibility = Visibility.Visible;
            }
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
        Debug.WriteLine($"[MainWindow] 执行 ApplyBackdrop: {type}");
        try
        {
            this.SystemBackdrop = null;
            Debug.WriteLine("[MainWindow] 旧背景已清除");

            switch (type)
            {
                case WindowBackdropType.Mica:
                    Debug.WriteLine("[MainWindow] 创建 MicaBackdrop...");
                    this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                    break;
                case WindowBackdropType.Acrylic:
                    Debug.WriteLine("[MainWindow] 创建 DesktopAcrylicBackdrop...");
                    this.SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
                    break;
                default:
                    Debug.WriteLine("[MainWindow] 设置为纯色 (SystemBackdrop = null)");
                    break;
            }
            Debug.WriteLine("[MainWindow] 背景设置完成");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MainWindow]设置背景报错: {ex}");
        }
    }

    private void SetInitialWindowSize()
    {
        try
        {
            var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                this.AppWindow.Id,
                Microsoft.UI.Windowing.DisplayAreaFallback.Primary
            );

            if (displayArea != null)
            {
                var screenWidth = displayArea.WorkArea.Width;
                var screenHeight = displayArea.WorkArea.Height;

                if (screenWidth >= 2560 && screenHeight >= 1440)
                {
                    this.Width = 1750;
                    this.Height = 1000;
                }
                else
                {
                    this.Width = 1360;
                    this.Height = 768;
                }
            }
            else
            {
                this.Width = 1360;
                this.Height = 768;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"设置初始窗口大小失败: {ex.Message}");
            this.Width = 1360;
            this.Height = 768;
        }
    }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        try
        {
            this.SetTitleBar(AppTitleBar);

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
            }

            UpdateTitleBarWithAdminStatus();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"设置标题栏失败: {ex.Message}");
        }

        this.Activated -= OnWindowActivated;
    }

    private void UpdateTitleBarWithAdminStatus()
    {
        try
        {
            bool isAdmin = IsRunningAsAdministrator();
            // 使用硬编码的标题基础
            var baseTitle = "芙芙启动器";
            TitleBarText.Text = isAdmin ? $"{baseTitle} [管理员]" : baseTitle;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"更新管理员标题失败: {ex.Message}");
        }
    }

    private bool IsRunningAsAdministrator()
    {
        try
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch
        {
            return false;
        }
    }

    private async void NavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadAndApplyAcrylicSettingAsync();
            await LoadGlobalBackgroundAsync();
            await CheckUserAgreementAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"导航加载异常: {ex.Message}");
            ShowMainContent();
        }
    }


    private async Task CheckUserAgreementAsync()
    {
        try
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var accepted = await localSettingsService.ReadSettingAsync("UserAgreementAccepted");

            bool isAccepted = false;
            if (accepted != null)
            {
                try { isAccepted = Convert.ToBoolean(accepted); }
                catch { isAccepted = false; }
            }

            if (!isAccepted) ShowAgreementPage();
            else ShowMainContent();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"协议检查失败: {ex.Message}");
            ShowMainContent();
        }
    }

    private void ShowAgreementPage()
    {
        try
        {
            AgreementFrame.Visibility = Visibility.Visible;
            NavigationView.Visibility = Visibility.Collapsed;
            AgreementFrame.Navigate(typeof(Views.AgreementPage));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示协议页异常: {ex.Message}");
        }
    }

    private void ShowMainContent()
    {
        try
        {
            AgreementFrame.Visibility = Visibility.Collapsed;
            NavigationView.Visibility = Visibility.Visible;
            NavigationView.SelectedItem = NavigationView.MenuItems[0];

            if (ContentFrame.CurrentSourcePageType != typeof(Views.MainPage))
                ContentFrame.Navigate(typeof(Views.MainPage));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示主内容异常: {ex.Message}");
        }
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem selectedItem)
        {
            var viewModelTag = selectedItem.Tag?.ToString();

            if (viewModelTag == "FufuLauncher.ViewModels.SettingsViewModel")
            {
                PlaySettingsIconRotationAnimation();
            }

            if (!string.IsNullOrEmpty(viewModelTag)) NavigateToPage(viewModelTag);
        }
    }

    private void PlaySettingsIconRotationAnimation()
    {
        try
        {
            if (SettingsIcon != null)
            {
                if (SettingsIcon.RenderTransform == null || SettingsIcon.RenderTransform is not RotateTransform)
                {
                    SettingsIcon.RenderTransform = new RotateTransform { CenterX = 14, CenterY = 14 };
                }
                var rotateAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = TimeSpan.FromMilliseconds(300),
                    EasingFunction = new CircleEase { EasingMode = EasingMode.EaseInOut }
                };

                var storyboard = new Storyboard();
                Storyboard.SetTarget(rotateAnimation, SettingsIcon.RenderTransform);
                Storyboard.SetTargetProperty(rotateAnimation, "Angle");
                storyboard.Children.Add(rotateAnimation);
                storyboard.Begin();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"播放设置图标动画失败: {ex.Message}");
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
            _ => null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType) ContentFrame.Navigate(pageType);
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

            if (message.Duration > 0)
                SetupAutoDismiss(notificationCard, message.Duration);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"显示通知失败: {ex.Message}");
        }
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

        var icon = new FontIcon
        {
            FontFamily = new FontFamily("Segoe Fluent Icons"),
            FontSize = 16,
            Glyph = GetNotificationIcon(message.Type),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 0, 0),
            Foreground = new SolidColorBrush(Colors.White)
        };

        var contentPanel = new StackPanel { Spacing = 4, Margin = new Thickness(12, 0, 0, 0) };

        var titleText = new TextBlock
        {
            Text = message.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = new SolidColorBrush(Colors.White)
        };
        contentPanel.Children.Add(titleText);

        var messageText = new TextBlock
        {
            Text = message.Message,
            FontSize = 12,
            Opacity = 0.9,
            TextWrapping = TextWrapping.WrapWholeWords,
            Foreground = new SolidColorBrush(Colors.White)
        };
        contentPanel.Children.Add(messageText);

        var closeButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
            Background = new SolidColorBrush(Colors.Transparent),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            Width = 32,
            Height = 32,
            Padding = new Thickness(0),
            Margin = new Thickness(0, -4, -4, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = new SolidColorBrush(Colors.White),
            IsHitTestVisible = true,
        };

        closeButton.Click += (s, e) =>
        {
            Debug.WriteLine($"[通知系统] 用户点击关闭按钮");
            try
            {
                if (NotificationPanel.Children.Contains(card))
                {
                    NotificationPanel.Children.Remove(card);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[通知系统] 关闭异常: {ex.Message}");
            }
        };

        card.Children.Add(icon);
        card.Children.Add(contentPanel);
        card.Children.Add(closeButton);

        return card;
    }

    private void AnimateNotification(FrameworkElement element, double from, double to, int duration)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(duration)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, element.RenderTransform);
        Storyboard.SetTargetProperty(animation, "X");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void SetupAutoDismiss(FrameworkElement card, int duration)
    {
        if (duration <= 0) return;

        var timer = dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(duration);
        timer.Tick += (s, e) =>
        {
            try
            {
                if (NotificationPanel.Children.Contains(card))
                {
                    NotificationPanel.Children.Remove(card);
                }
            }
            catch { }
            timer.Stop();
        };
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
}