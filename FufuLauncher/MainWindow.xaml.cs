using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Security.Principal;
using FufuLauncher.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI.ViewManagement;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher;

public sealed partial class MainWindow : WindowEx
{
    private Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue;
    private UISettings settings;

    public MainWindow()
    {
        InitializeComponent();

        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/WindowIcon.ico"));
    
        var localizedTitle = "AppDisplayName".GetLocalized();
        Title = string.IsNullOrWhiteSpace(localizedTitle) ? "芙芙启动器" : localizedTitle;

        ExtendsContentIntoTitleBar = true;

        dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        settings = new UISettings();
        settings.ColorValuesChanged += Settings_ColorValuesChanged;

        SetInitialWindowSize();

        WeakReferenceMessenger.Default.Register<AgreementAcceptedMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(() =>
            {
                try { ShowMainContent(); }
                catch (Exception ex) { Debug.WriteLine($"消息处理异常: {ex.Message}"); }
            });
        });

        WeakReferenceMessenger.Default.Register<AcrylicSettingChangedMessage>(this, (r, m) =>
        {
            dispatcherQueue.TryEnqueue(() =>
            {
                try { ApplyAcrylicSetting(m.IsEnabled); }
                catch (Exception ex) { Debug.WriteLine($"亚克力设置应用异常: {ex.Message}"); }
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

        this.Activated += OnWindowActivated;
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
        
            TitleBarIcon.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri("ms-appx:///Assets/WindowIcon.ico"));

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
            await CheckUserAgreementAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"导航加载异常: {ex.Message}");
            ShowMainContent();
        }
    }

    private async Task LoadAndApplyAcrylicSettingAsync()
    {
        try
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var acrylicEnabled = await localSettingsService.ReadSettingAsync("IsAcrylicEnabled");
            bool isEnabled = acrylicEnabled == null ? true : Convert.ToBoolean(acrylicEnabled);
            
            ApplyAcrylicSetting(isEnabled);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"加载亚克力设置失败: {ex.Message}");
            ApplyAcrylicSetting(true);
        }
    }

    private void ApplyAcrylicSetting(bool isEnabled)
    {
        try
        {
            if (isEnabled) this.SystemBackdrop = new DesktopAcrylicBackdrop();
            else this.SystemBackdrop = null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"应用亚克力效果失败: {ex.Message}");
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