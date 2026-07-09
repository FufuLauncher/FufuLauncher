/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace FufuLauncher.Views;

public sealed partial class MainPage : Page
{
    private const double BannerSwipeThreshold = 42;
    private const double BannerAnimationMs = 520;
    private const double BannerInitialFadeMs = 560;
    private BannerItem _displayedBanner;
    private BannerItem _pendingBanner;
    private bool _isBannerTransitioning;
    private bool _isBannerPointerPressed;
    private Windows.Foundation.Point _bannerPointerPressedPoint;
    private bool _isInfoCardExpanded = true;
    private bool _isWidgetFlyoutEnabled = false;
    public MainViewModel ViewModel
    {
        get;
    }
    public XamlUICommand OpenLinkCommand
    {
        get;
    }

    private void Copyright_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateCopyrightOpacity(0.8);
    }

    private void Copyright_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateCopyrightOpacity(0.05);
    }
    
        private async void SwitchToBilibili_Click(object sender, RoutedEventArgs e)
        {
            await PrepareAndSwitchServer(true);
        }

        private async void SwitchToOfficial_Click(object sender, RoutedEventArgs e)
        {
            await PrepareAndSwitchServer(false);
        }

        private async Task PrepareAndSwitchServer(bool toBilibili)
        {
            try
            {
                var localSettingsService = App.GetService<ILocalSettingsService>();
                var gamePathSetting = await localSettingsService.ReadSettingAsync("GameInstallationPath");
                
                var gameDir = gamePathSetting as string;
                if (!string.IsNullOrEmpty(gameDir))
                {
                    gameDir = gameDir.Trim('"').Trim();
                }

                if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                {
                    await ShowDialog("ErrorTitle".GetLocalized(), "Home_ErrNoGamePath".GetLocalized());
                    return;
                }
                
                string configPath = Path.Combine(gameDir, "config.ini");
                if (!File.Exists(configPath))
                {
                    string parentDir = Directory.GetParent(gameDir)?.FullName ?? "";
                    string parentConfig = Path.Combine(parentDir, "config.ini");

                    if (File.Exists(parentConfig))
                    {
                        gameDir = parentDir;
                        configPath = parentConfig;
                    }
                    else
                    {
                        await ShowDialog("ErrorTitle".GetLocalized(), "Home_ErrConfigNotFound".GetLocalized());
                        return;
                    }
                }
                
                await PerformServerSwitch(gameDir, configPath, toBilibili);
            }
            catch (Exception ex)
            {
                await ShowDialog("ErrorTitle".GetLocalized(), $"{"Home_ErrSwitchException".GetLocalized()}: {ex.Message}");
            }
        }
        
        private void LaunchButton_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            AnimateLaunchButtonHoverOpacity(1.0);
        }

        private void LaunchButton_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            AnimateLaunchButtonHoverOpacity(0.0);
        }

        private void AnimateLaunchButtonHoverOpacity(double targetOpacity)
        {
            if (LaunchButtonHoverLayer == null) return;

            var storyboard = new Storyboard();
            var duration = new Duration(TimeSpan.FromMilliseconds(200));
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            storyboard.Children.Add(CreateDoubleAnimation(LaunchButtonHoverLayer, "Opacity", targetOpacity, duration, easing));

            storyboard.Begin();
        }
        
        private async void AnnouncementBell_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var announcementService = App.GetService<IAnnouncementService>();
                
                var announcementUrl = await announcementService.GetCurrentAnnouncementUrlAsync();
                
                if (string.IsNullOrEmpty(announcementUrl))
                {
                    var localSettings = App.GetService<ILocalSettingsService>();
                    
                    var lastUrlObj = await localSettings.ReadSettingAsync("LastAnnouncementUrl");
                    if (lastUrlObj is string lastUrl && !string.IsNullOrEmpty(lastUrl))
                    {
                        announcementUrl = lastUrl;
                    }
                }


                if (!string.IsNullOrEmpty(announcementUrl))
                {
                    var announcementWindow = new AnnouncementWindowL(announcementUrl);
                    announcementWindow.Activate();
                }
                else
                {
                    Debug.WriteLine("[Announcement] 手动获取公告失败：未获取到且无本地缓存");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Announcement] 手动触发公告异常: {ex.Message}");
            }
        }
        
        private double GetIconOpacity(bool isEnabled)
        {
            return isEnabled ? 1.0 : 0.4;
        }
        
        private async Task PerformServerSwitch(string gameDir, string configPath, bool toBilibili)
        {
            try
            {
                // Official: channel=1, sub_channel=1, cps=mihoyo
                // Bilibili: channel=14, sub_channel=0, cps=bilibili
                string channel = toBilibili ? "14" : "1";
                string subChannel = toBilibili ? "0" : "1";
                string cps = toBilibili ? "bilibili" : "mihoyo";

                string[] lines = await File.ReadAllLinesAsync(configPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].StartsWith("channel=")) lines[i] = $"channel={channel}";
                    else if (lines[i].StartsWith("sub_channel=")) lines[i] = $"sub_channel={subChannel}";
                    else if (lines[i].StartsWith("cps=")) lines[i] = $"cps={cps}";
                }
                await File.WriteAllLinesAsync(configPath, lines);
                
                await BilibiliSdkManager.EnsureSdkAndDeprecatedFilesAsync(gameDir, toBilibili);
                
                var serverName = toBilibili ? "Home_BilibiliServer".GetLocalized() : "Home_OfficialServer".GetLocalized();
                var action = toBilibili ? "Home_Deployed".GetLocalized() : "Home_Cleaned".GetLocalized();
                await ShowDialog("Home_SwitchSuccess".GetLocalized(), string.Format("Home_SwitchedTo_Format".GetLocalized(), serverName, action));
            }
            catch (Exception ex)
            {
                await ShowDialog("Home_SwitchFailed".GetLocalized(), ex.Message);
            }
        }
        
        private async Task ShowDialog(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
        }
    private void AnimateCopyrightOpacity(double toOpacity)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, CopyrightText);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
    private void ScreenshotButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {

    }

    private void ScreenshotButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {

    }

    private async void RefreshTokenButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var accountManager = App.GetService<AccountManager>();
            var activeId = accountManager.ActiveAccountId;
            if (activeId == null)
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage("Home_RefreshFailed".GetLocalized(), "Home_NoActiveAccount".GetLocalized(), NotificationType.Error));
                return;
            }

            var cookies = await accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0)
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage("Home_RefreshFailed".GetLocalized(), "Home_CannotLoadCredentials".GetLocalized(), NotificationType.Error));
                return;
            }

            var tokenService = new TokenRefreshService();
            var newCookies = await tokenService.RefreshCookieAsync(cookies, true);

            if (newCookies != null)
            {
                await accountManager.UpdateCookiesAsync(activeId, newCookies);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"手动刷新异常: {ex.Message}");
        }
    }

    private async void RefreshDailyNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.IsEnabled = false;
            try
            {
                await ViewModel.LoadDailyNoteAsync();
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }
    
private Dictionary<FrameworkElement, object> _cachedToolTips = new();

private async void OnWidgetSettingsClick(object sender, RoutedEventArgs e)
{
    if (App.MainWindow is MainWindow mainWindow)
    {
        await mainWindow.NavigateToSettingsPageAsync();
    }
}

private void OnToggleWidgetFlyoutModeClick(object sender, RoutedEventArgs e)
{
    _isWidgetFlyoutEnabled = !_isWidgetFlyoutEnabled;
    WidgetEyeIcon.Glyph = _isWidgetFlyoutEnabled ? "\uE8CB" : "\uE890";

    ToolTipService.SetToolTip(BtnWidgetGacha, _isWidgetFlyoutEnabled ? "Home_GachaTooltip".GetLocalized() : "Home_WidgetGacha".GetLocalized());
    ToolTipService.SetToolTip(BtnWidgetAchievement, _isWidgetFlyoutEnabled ? "Home_AchievementTooltip".GetLocalized() : "Home_WidgetAchievement".GetLocalized());
    ToolTipService.SetToolTip(BtnWidgetInventory, _isWidgetFlyoutEnabled ? "Home_InventoryTooltip".GetLocalized() : "Home_WidgetInventory".GetLocalized());
    ToolTipService.SetToolTip(BtnWidgetPlayerRole, _isWidgetFlyoutEnabled ? "Home_PlayerRoleTooltip".GetLocalized() : "Home_WidgetPlayerRole".GetLocalized());
    ToolTipService.SetToolTip(BtnWidgetDailyNote, _isWidgetFlyoutEnabled ? "Home_NotesTooltip".GetLocalized() : "Home_WidgetNotes".GetLocalized());
    ToolTipService.SetToolTip(BtnWidgetVideo, _isWidgetFlyoutEnabled ? "Home_VideoTooltip".GetLocalized() : "Home_WidgetVideo".GetLocalized());
    ToolTipService.SetToolTip(BtnWidgetBBS, _isWidgetFlyoutEnabled ? "Home_BBSTooltip".GetLocalized() : "Home_WidgetBBS".GetLocalized());
}

private void WidgetButton_PointerExited(object sender, PointerRoutedEventArgs e)
{
    if (sender is FrameworkElement element)
    {
        if (_cachedToolTips.TryGetValue(element, out var cachedTooltip))
        {
            ToolTipService.SetToolTip(element, cachedTooltip);
            _cachedToolTips.Remove(element);
        }
    }
}

private async void OpenCheckinSettings_Click(object sender, RoutedEventArgs e)
{
    if (App.MainWindow is MainWindow mainWindow)
    {
        await mainWindow.NavigateToSettingsPageAsync();
    }
}

private void OnOpenGachaAnalysisClick(object sender, RoutedEventArgs e)
    {
        var window = new GachaAnalysisWindow();
        window.Activate();
    }

    private void OnOpenAchievementsClick(object sender, RoutedEventArgs e)
    {
        var window = new AchievementWindow();
        window.Activate();
    }

    private void OnOpenInventoryClick(object sender, RoutedEventArgs e)
    {
        var window = new InventoryWindow();
        window.Activate();
    }

    private void OnOpenPlayerRolesClick(object sender, RoutedEventArgs e)
    {
        var window = new PlayerInfoWindow();
        window.Activate();
    }

    private void OnOpenDailyNoteClick(object sender, RoutedEventArgs e)
    {
        var window = new DailyNoteWindow();
        window.Activate();
    }

    private async void BBSButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog riskDialog = new()
        {
            Title = "Home_BBSSecurityTitle".GetLocalized(),
            Content = "Home_BBSSecurityMessage".GetLocalized(),
            PrimaryButtonText = "Home_BBSConfirm".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        
        ContentDialogResult result = await riskDialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary)
        {
            var bbsWindow = new BBSWindow();
            bbsWindow.Activate();
        }
    }

    private void OnOpenVideoResourcesClick(object sender, RoutedEventArgs e)
    {
        var window = new VideoResourcesWindow();
        window.Activate();
    }

    private void InfoCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateInfoButtonOpacity(1.0);
        AnimateBannerArrowsOpacity(1.0);
    }

    private void InfoCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateInfoButtonOpacity(0.0);
        AnimateBannerArrowsOpacity(0.0);
    }
    
    private void BackgroundGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BackgroundUrlInfo info)
        {
            ViewModel.SelectSpecificBackgroundCommand.Execute(info);
            
            BackgroundFlyout.Hide();
        }
    }

    private void InjectionModuleListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is InjectionModuleInfo module)
        {
            ViewModel.SelectInjectionModuleCommand.Execute(module);

            InjectionModuleFlyout.Hide();
        }
    }

    private void AnimateInfoButtonOpacity(double toOpacity)
    {
        if (InfoExpandButton == null) return;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, InfoExpandButton);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void AnimateBannerArrowsOpacity(double toOpacity)
    {
        if (BannerPrevButton == null || BannerNextButton == null) return;

        var sb = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(200));

        var prevAnim = new DoubleAnimation { To = toOpacity, Duration = duration, EnableDependentAnimation = true };
        Storyboard.SetTarget(prevAnim, BannerPrevButton);
        Storyboard.SetTargetProperty(prevAnim, "Opacity");
        sb.Children.Add(prevAnim);

        var nextAnim = new DoubleAnimation { To = toOpacity, Duration = duration, EnableDependentAnimation = true };
        Storyboard.SetTarget(nextAnim, BannerNextButton);
        Storyboard.SetTargetProperty(nextAnim, "Opacity");
        sb.Children.Add(nextAnim);

        BannerPrevButton.IsHitTestVisible = toOpacity > 0;
        BannerNextButton.IsHitTestVisible = toOpacity > 0;

        sb.Begin();
    }

    private void BannerPrev_Click(object sender, RoutedEventArgs e)
    {
        MoveBannerBy(-1);
    }

    private void BannerNext_Click(object sender, RoutedEventArgs e)
    {
        MoveBannerBy(1);
    }

    private void OnInfoCardToggledRequested(bool isExpanded)
    {
        DispatcherQueue.TryEnqueue(() => AnimateInfoCardToggle(isExpanded));
    }

    private void AnimateInfoCardToggle(bool isExpanded)
    {
        _isInfoCardExpanded = isExpanded;
        var targetHeight = isExpanded ? ViewModel.InfoCardHeight : 157;
        var targetCornerRadius = new CornerRadius(12);

        var sb = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(350));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var heightAnim = new DoubleAnimation
        {
            To = targetHeight,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(heightAnim, InfoCardContainer);
        Storyboard.SetTargetProperty(heightAnim, "Height");
        sb.Children.Add(heightAnim);

        if (InfoCardPivot != null)
        {
            var pivotOpacityAnim = new DoubleAnimation
            {
                To = isExpanded ? 1.0 : 0.0,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(pivotOpacityAnim, InfoCardPivot);
            Storyboard.SetTargetProperty(pivotOpacityAnim, "Opacity");
            sb.Children.Add(pivotOpacityAnim);
        }

        sb.Completed += (_, _) =>
        {
            if (InfoCardPivot != null)
            {
                InfoCardPivot.IsHitTestVisible = isExpanded;
                InfoCardPivot.Opacity = isExpanded ? 1.0 : 0.0;
            }
            BannerImageArea.CornerRadius = targetCornerRadius;
        };

        BannerImageArea.CornerRadius = targetCornerRadius;
        sb.Begin();
    }

    private bool _isInitialized;

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        Loaded += (_, _) =>
        {
            LaunchButtonOverlayBorder.Opacity = ViewModel.IsGameRunning ? 0.0 : 1.0;
        };

        ViewModel.InfoCardToggledRequested += OnInfoCardToggledRequested;

        OpenLinkCommand = new XamlUICommand();
        OpenLinkCommand.ExecuteRequested += (sender, args) =>
        {
            if (args.Parameter is string url)
            {
                OpenLink(url);
            }
        };
    }
    
    private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsGameRunning))
        {
            AnimateLaunchButtonOverlay(ViewModel.IsGameRunning ? 0.0 : 1.0);
        }
        else if (e.PropertyName == nameof(MainViewModel.CurrentBanner))
        {
            _ = DispatcherQueue.TryEnqueue(() => TransitionToBanner(ViewModel.CurrentBanner));
        }
        else if (e.PropertyName == nameof(MainViewModel.IsDailyNoteLoaded))
        {
            _ = DispatcherQueue.TryEnqueue(() => AnimateDailyNoteTransition(ViewModel.IsDailyNoteLoaded));
        }
    }
    
    private void SyncDailyNoteState()
    {
        if (DailyNoteDataPanel == null || DailyNoteEmptyText == null) return;

        if (ViewModel.IsDailyNoteLoaded)
        {
            DailyNoteDataPanel.Opacity = 1.0;
            DailyNoteEmptyText.Opacity = 0.0;
            DailyNoteDataPanel.IsHitTestVisible = true;
        }
        else
        {
            DailyNoteDataPanel.Opacity = 0.0;
            DailyNoteEmptyText.Opacity = 0.8;
            DailyNoteDataPanel.IsHitTestVisible = false;
        }
    }
    
    private void AnimateDailyNoteTransition(bool isLoaded)
    {
        if (DailyNoteDataPanel == null || DailyNoteEmptyText == null) return;

        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(300));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        
        storyboard.Children.Add(CreateDoubleAnimation(DailyNoteDataPanel, "Opacity", isLoaded ? 1.0 : 0.0, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(DailyNoteEmptyText, "Opacity", isLoaded ? 0.0 : 0.8, duration, easing));
    
        DailyNoteDataPanel.IsHitTestVisible = isLoaded;

        storyboard.Begin();
    }
    

    private void AnimateLaunchButtonOverlay(double toOpacity)
    {
        if (LaunchButtonOverlayBorder.Opacity == toOpacity) return;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromSeconds(1.5)), 
            
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        Storyboard.SetTarget(animation, LaunchButtonOverlayBorder);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_isInitialized)
        {
            _ = ViewModel.OnPageReturnedAsync();
        }
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();

        InitializeBannerDisplay();

        SyncDailyNoteState();

        if (!_isInitialized)
        {
            if (Helpers.AppPaths.IsFirstRun) return;
            await ViewModel.InitializeAsync();
            _isInitialized = true;
        }
    }
    
    private void OpenCheckinCalendar_Click(object sender, RoutedEventArgs e)
    {
        var calendarWindow = new CheckinCalendarWindow();
        calendarWindow.Activate();
    }

    private async void OpenLink(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                var uri = new Uri(url);
                await Windows.System.Launcher.LaunchUriAsync(uri);
                Debug.WriteLine($"打开链接: {url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开链接失败: {ex.Message}");
            }
        }
    }

    private void InitializeBannerDisplay()
    {
        if (ViewModel.Banners == null || ViewModel.Banners.Count == 0)
        {
            BannerCurrentImage.Source = null;
            BannerIncomingImage.Source = null;
            _displayedBanner = null;
            _pendingBanner = null;
            ResetBannerLayers();
            BannerCurrentLayer.Opacity = 0;
            return;
        }

        if (ViewModel.CurrentBanner == null)
        {
            ViewModel.CurrentBanner = ViewModel.Banners[0];
            return;
        }

        TransitionToBanner(ViewModel.CurrentBanner);
    }

    private void TransitionToBanner(BannerItem targetBanner)
    {
        if (ViewModel == null) 
        {
            return;
        }
        if (targetBanner == null)
        {
            return;
        }

        if (_isBannerTransitioning)
        {
            _pendingBanner = targetBanner;
            return;
        }

        if (ReferenceEquals(_displayedBanner, targetBanner) && BannerCurrentImage.Source != null)
        {
            return;
        }

        if (_displayedBanner == null || BannerCurrentImage.Source == null)
        {
            ShowInitialBanner(targetBanner);
            return;
        }

        var direction = ResolveBannerDirection(_displayedBanner, targetBanner);
        StartBannerTransition(targetBanner, direction);
    }

    private void ShowInitialBanner(BannerItem targetBanner)
    {
        _displayedBanner = targetBanner;
        ResetBannerLayers();
        BannerCurrentLayer.Opacity = 0;
        BannerCurrentScale.ScaleX = 1.015;
        BannerCurrentScale.ScaleY = 1.015;

        RoutedEventHandler imageOpened = null;
        ExceptionRoutedEventHandler imageFailed = null;
        imageOpened = (_, _) =>
        {
            BannerCurrentImage.ImageOpened -= imageOpened;
            BannerCurrentImage.ImageFailed -= imageFailed;
            FadeInInitialBanner();
        };
        imageFailed = (_, _) =>
        {
            BannerCurrentImage.ImageOpened -= imageOpened;
            BannerCurrentImage.ImageFailed -= imageFailed;
            ResetBannerLayers();
        };

        BannerCurrentImage.ImageOpened += imageOpened;
        BannerCurrentImage.ImageFailed += imageFailed;
        SetBannerImage(BannerCurrentImage, targetBanner);
    }

    private void FadeInInitialBanner()
    {
        BannerCurrentTranslate.X = 0;
        BannerIncomingTranslate.X = 0;
        BannerIncomingLayer.Opacity = 0;
        BannerCurrentLayer.Opacity = 0;
        BannerCurrentScale.ScaleX = 1.015;
        BannerCurrentScale.ScaleY = 1.015;

        var storyboard = new Storyboard();
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(BannerInitialFadeMs));

        storyboard.Children.Add(CreateDoubleAnimation(BannerCurrentLayer, "Opacity", 1, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(BannerCurrentScale, "ScaleX", 1, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(BannerCurrentScale, "ScaleY", 1, duration, easing));

        storyboard.Completed += (_, _) =>
        {
            if (!_isBannerTransitioning)
            {
                ResetBannerLayers();
            }
        };
        storyboard.Begin();
    }

    private int ResolveBannerDirection(BannerItem from, BannerItem to)
    {
        var count = ViewModel.Banners?.Count ?? 0;
        if (count < 2) return 1;

        var fromIndex = ViewModel.Banners.IndexOf(from);
        var toIndex = ViewModel.Banners.IndexOf(to);
        if (fromIndex < 0 || toIndex < 0) return 1;

        if ((fromIndex + 1) % count == toIndex) return 1;
        if ((fromIndex - 1 + count) % count == toIndex) return -1;

        return toIndex > fromIndex ? 1 : -1;
    }

    private void StartBannerTransition(BannerItem targetBanner, int direction)
    {
        var width = Math.Max(BannerViewport.ActualWidth, 1);
        var offset = width * 0.18 * direction;

        SetBannerImage(BannerIncomingImage, targetBanner);

        BannerIncomingTranslate.X = offset;
        BannerIncomingLayer.Opacity = 0;
        BannerIncomingScale.ScaleX = 1.015;
        BannerIncomingScale.ScaleY = 1.015;
        BannerCurrentTranslate.X = 0;
        BannerCurrentLayer.Opacity = 1;
        BannerCurrentScale.ScaleX = 1;
        BannerCurrentScale.ScaleY = 1;

        var storyboard = new Storyboard();
        var easing = new SineEase { EasingMode = EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(BannerAnimationMs));

        storyboard.Children.Add(CreateDoubleAnimation(BannerCurrentTranslate, "X", -offset, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(BannerCurrentLayer, "Opacity", 0, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(BannerCurrentScale, "ScaleX", 0.985, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(BannerCurrentScale, "ScaleY", 0.985, duration, easing));

        storyboard.Children.Add(CreateDoubleAnimation(BannerIncomingTranslate, "X", 0, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(BannerIncomingLayer, "Opacity", 1, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(BannerIncomingScale, "ScaleX", 1, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(BannerIncomingScale, "ScaleY", 1, duration, easing));

        _isBannerTransitioning = true;
        storyboard.Completed += (_, _) =>
        {
            SwapBannerLayers(targetBanner);
            _isBannerTransitioning = false;

            if (_pendingBanner != null && !ReferenceEquals(_pendingBanner, _displayedBanner))
            {
                var pendingBanner = _pendingBanner;
                _pendingBanner = null;
                TransitionToBanner(pendingBanner);
            }
            else
            {
                _pendingBanner = null;
            }
        };
        storyboard.Begin();
    }

    private static DoubleAnimation CreateDoubleAnimation(DependencyObject target, string property, double to, Duration duration, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }

    private void SwapBannerLayers(BannerItem displayedBanner)
    {
        BannerCurrentImage.Source = BannerIncomingImage.Source;
        BannerIncomingImage.Source = null;
        _displayedBanner = displayedBanner;
        ResetBannerLayers();
    }

    private void ResetBannerLayers()
    {
        BannerCurrentTranslate.X = 0;
        BannerIncomingTranslate.X = 0;
        BannerCurrentLayer.Opacity = 1;
        BannerIncomingLayer.Opacity = 0;
        BannerCurrentScale.ScaleX = 1;
        BannerCurrentScale.ScaleY = 1;
        BannerIncomingScale.ScaleX = 1;
        BannerIncomingScale.ScaleY = 1;
    }

    private static void SetBannerImage(Image imageControl, BannerItem banner)
    {
        if (banner?.Image?.Url == null)
        {
            imageControl.Source = null;
            return;
        }

        try
        {
            imageControl.Source = new BitmapImage(new Uri(banner.Image.Url));
        }
        catch
        {
            imageControl.Source = null;
        }
    }

    private void BannerButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ViewModel.CurrentBanner?.Image?.Link))
        {
            OpenLink(ViewModel.CurrentBanner.Image.Link);
        }
    }

    private void BannerViewport_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isBannerPointerPressed = true;
        _bannerPointerPressedPoint = e.GetCurrentPoint(BannerViewport).Position;
        BannerViewport.CapturePointer(e.Pointer);
    }

    private void BannerViewport_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isBannerPointerPressed)
        {
            return;
        }

        var releasedPoint = e.GetCurrentPoint(BannerViewport).Position;
        var deltaX = releasedPoint.X - _bannerPointerPressedPoint.X;
        _isBannerPointerPressed = false;
        BannerViewport.ReleasePointerCapture(e.Pointer);

        if (Math.Abs(deltaX) >= BannerSwipeThreshold)
        {
            MoveBannerBy(deltaX < 0 ? 1 : -1);
        }
    }

    private void BannerViewport_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _isBannerPointerPressed = false;
    }

    private void MoveBannerBy(int offset)
    {
        if (_isBannerTransitioning || ViewModel.Banners == null || ViewModel.Banners.Count < 2)
        {
            return;
        }

        var current = ViewModel.CurrentBanner ?? _displayedBanner ?? ViewModel.Banners[0];
        var currentIndex = ViewModel.Banners.IndexOf(current);
        if (currentIndex < 0) currentIndex = 0;

        var count = ViewModel.Banners.Count;
        var nextIndex = (currentIndex + offset + count) % count;
        ViewModel.CurrentBanner = ViewModel.Banners[nextIndex];
    }
}
