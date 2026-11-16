/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;
using FufuLauncher.ViewModels;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow : Window
{
    private static readonly TimeSpan DefaultAuthOtpCooldown = TimeSpan.FromSeconds(30);
    private static CloudSyncWindow? _current;

    private readonly ICloudSyncService _cloudSync;
    private readonly IHutaoCloudService _hutaoCloud;
    private readonly AccountManager? _accountManager;
    private readonly GameRoleService? _gameRoleService;
    private readonly ObservableCollection<CloudArchiveItem> _archives = new();
    private readonly DispatcherQueueTimer _authOtpTimer;
    private readonly Dictionary<(int Mode, string Email), DateTimeOffset> _authOtpResendAt = new();

    private enum SlideChannel
    {
        MainTab,
        HutaoSubTab,
        AccountAuth,
        HutaoAuth
    }

    private readonly Dictionary<SlideChannel, Storyboard> _slideStoryboards = new();
    private int _currentMainTabIndex;
    private bool _isSendingAuthOtp;
    private bool _statusIsAuthOtpFeedback;
    private bool _closed;
    private bool _isWindowContentLoaded;
    private int _busyOperations;
    private long _archiveRequestVersion;
    private bool _archiveLoadPending;
    private string? _archiveAccountEmail;
    private string? _displayedArchiveFilter;

    private bool IsBusy => _isSendingAuthOtp || _busyOperations > 0 || _archiveLoadPending;

    private static CloudSyncWindow GetOrCreateWindow(string? initialTabTag = null)
    {
        if (_current != null)
        {
            _current.SelectTab(initialTabTag);
            return _current;
        }

        var window = new CloudSyncWindow();
        window.Closed += (_, _) => _current = null;
        _current = window;
        if (!string.IsNullOrWhiteSpace(initialTabTag))
        {
            window.SelectTab(initialTabTag);
        }

        return window;
    }

    public static void ShowOrActivate(string? initialTabTag = null)
    {
        var window = GetOrCreateWindow(initialTabTag);
        window.Activate();
    }

    internal static void ShowFromSidebar()
    {
        var window = GetOrCreateWindow();
        if (window._closed) return;

        if (window.AppWindow?.Presenter is OverlappedPresenter presenter &&
            presenter.State == OverlappedPresenterState.Minimized)
        {
            presenter.Restore();
        }

        window.Activate();
        window.SetForegroundWindow();
    }

    public CloudSyncWindow() : this(
        App.GetService<ICloudSyncService>(),
        App.GetService<IHutaoCloudService>(),
        TryGetService<AccountManager>(),
        TryGetService<GameRoleService>())
    {
    }

    internal CloudSyncWindow(ICloudSyncService cloudSync)
        : this(
            cloudSync,
            App.GetService<IHutaoCloudService>(),
            TryGetService<AccountManager>(),
            TryGetService<GameRoleService>())
    {
    }

    internal CloudSyncWindow(
        ICloudSyncService cloudSync,
        IHutaoCloudService hutaoCloud,
        AccountManager? accountManager = null,
        GameRoleService? gameRoleService = null)
    {
        _cloudSync = cloudSync;
        _hutaoCloud = hutaoCloud;
        _accountManager = accountManager ?? TryGetService<AccountManager>();
        _gameRoleService = gameRoleService ?? TryGetService<GameRoleService>();
        InitializeComponent();
        RootGrid.Loaded += OnRootGridLoaded;
        _authOtpTimer = DispatcherQueue.CreateTimer();
        _authOtpTimer.Interval = TimeSpan.FromSeconds(1);
        _authOtpTimer.Tick += (_, _) => UpdateAuthOtpButton();
        EmailTextBox.TextChanged += (_, _) => ResetAuthOtpFeedback();
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                MaskAuthPasswords();
            }
        };

        AuthModeSelector.ItemsSource = new[]
        {
            "CloudSync_ModeLogin".GetLocalized(),
            "CloudSync_ModeRegister".GetLocalized(),
            "CloudSync_ModeReset".GetLocalized()
        };
        AuthModeSelector.SelectedIndex = 0;

        ArchivesRepeater.ItemsSource = _archives;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ConfigureWindow();
        ApplyThemeAndTitleBar();
        ApplyWindowBackdrop();

        _cloudSync.SessionChanged += OnSessionChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _authOtpTimer.Stop();
            _cloudSync.SessionChanged -= OnSessionChanged;
            StopAllSlideTransitions();
            CleanupHutaoBridge();
        };

        InitializeHutaoBridge();
        EmailTextBox.Text = _cloudSync.CurrentEmail;
        RefreshAccountUiState();
        if (_cloudSync.IsLoggedIn)
        {
            _ = InitializeLoggedInStateAsync();
        }
    }

    public void SelectTab(string? tabTag)
    {
        if (string.IsNullOrWhiteSpace(tabTag)) return;
        foreach (var item in MainNavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tabTag, StringComparison.OrdinalIgnoreCase))
            {
                MainNavView.SelectedItem = item;
                break;
            }
        }
    }

    private void ConfigureWindow()
    {
        try
        {
            var appWindow = AppWindow;
            if (appWindow == null) return;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var targetWidth = 1360;
            var targetHeight = 768;
            var shouldMaximize = false;

            if (App.MainWindow is MainWindow { IsAgreementShowing: false } mainWindow &&
                mainWindow.AppWindow?.Presenter is OverlappedPresenter mainPresenter)
            {
                if (mainPresenter.State == OverlappedPresenterState.Maximized)
                {
                    shouldMaximize = true;
                }
                else if (mainPresenter.State == OverlappedPresenterState.Restored &&
                         mainWindow.Width >= 900 && mainWindow.Height >= 580)
                {
                    targetWidth = (int)Math.Round(mainWindow.Width);
                    targetHeight = (int)Math.Round(mainWindow.Height);
                }
            }

            WindowManagerHelper.ResizeWithDpi(appWindow, this, targetWidth, targetHeight);
            WindowManagerHelper.CenterWindowOnScreen(appWindow, targetWidth, targetHeight);

            var scale = WindowManagerHelper.GetScaleFactor(this);
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = (int)Math.Round(1000 * scale);
                presenter.PreferredMinimumHeight = (int)Math.Round(620 * scale);
                if (shouldMaximize)
                {
                    presenter.Maximize();
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] ConfigureWindow failed: {ex.Message}");
        }
    }

    private void ApplyThemeAndTitleBar()
    {
        try
        {
            var theme = App.GetService<IThemeSelectorService>().Theme;
            RootGrid.RequestedTheme = theme;

            var isDark = theme == ElementTheme.Dark ||
                         (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
            var titleBar = AppWindow?.TitleBar;
            if (titleBar == null) return;

            titleBar.BackgroundColor = Colors.Transparent;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonInactiveForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverForegroundColor = isDark ? Colors.White : Colors.Black;
            titleBar.ButtonHoverBackgroundColor = isDark
                ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
            titleBar.ButtonPressedBackgroundColor = isDark
                ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(0x66, 0x00, 0x00, 0x00);
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] ApplyTheme failed: {ex.Message}");
        }
    }

    private void ApplyWindowBackdrop()
    {
        try
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop();
                return;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] MicaBackdrop failed: {ex.Message}");
        }

        try
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] DesktopAcrylicBackdrop fallback failed: {ex.Message}");
            var isDark = RootGrid.RequestedTheme == ElementTheme.Dark ||
                         (RootGrid.RequestedTheme == ElementTheme.Default &&
                          Application.Current.RequestedTheme == ApplicationTheme.Dark);
            RootGrid.Background = new SolidColorBrush(isDark
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 243, 243, 243));
        }
    }

    private void OnSessionChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RefreshAccountUiState);
    }

    private async Task InitializeLoggedInStateAsync()
    {
        SetBusy(true);
        try
        {
            if (await _cloudSync.EnsureAuthenticatedAsync())
            {
                await _cloudSync.GetAccountStatusAsync();
                RefreshAccountUiState();
                if (!_cloudSync.IsLocked)
                {
                    await RefreshQuotaInternalAsync(silent: true);
                    await RefreshArchivesInternalAsync(silent: true);
                }
            }
            else if (!string.IsNullOrWhiteSpace(_cloudSync.LastStartupMessage))
            {
                ShowMessage(InfoBarSeverity.Warning, "CloudSync_SessionExpiredTitle".GetLocalized(),
                    _cloudSync.LastStartupMessage);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (AccountTabPanel == null || QuotaTabPanel == null || ArchivesTabPanel == null ||
            HutaoTabPanel == null) return;
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;

        var targetIndex = tag switch
        {
            "Account" => 0,
            "Quota" => 1,
            "Archives" => 2,
            "Hutao" => 3,
            _ => -1
        };
        if (targetIndex < 0) return;

        var previousIndex = _currentMainTabIndex;
        var direction = targetIndex >= previousIndex ? 1 : -1;
        _currentMainTabIndex = targetIndex;

        if (tag != "Account") MaskAuthPasswords();
        if (targetIndex != previousIndex)
        {
            ContentScrollViewer?.ChangeView(null, 0, null, true);
        }

        UIElement[] mainPanels = [AccountTabPanel, QuotaTabPanel, ArchivesTabPanel, HutaoTabPanel];
        var animate = _isWindowContentLoaded && targetIndex != previousIndex;
        SwitchSlidePanel(SlideChannel.MainTab, mainPanels, targetIndex, direction, ContentViewport, animate);

        if (_cloudSync.IsLocked && tag != "Account")
        {
            ShowMessage(InfoBarSeverity.Warning, "CloudSync_LockedBannerTitle".GetLocalized(),
                "CloudSync_LockedBannerDetail".GetLocalized());
        }

        if (tag == "Hutao")
        {
            _ = RefreshHutaoTabOnSelectAsync();
            return;
        }

        if (!_cloudSync.IsLoggedIn || _cloudSync.IsLocked)
        {
            return;
        }

        if (tag == "Quota")
        {
            _ = RefreshQuotaInternalAsync(silent: false);
        }
        else if (tag == "Archives")
        {
            _ = RefreshArchivesInternalAsync(silent: false);
        }
    }

    private void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= OnRootGridLoaded;
        _isWindowContentLoaded = true;
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is UIElement element && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            element.Clip = new RectangleGeometry
            {
                Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
            };
        }
    }

    private void SwitchSlidePanel(SlideChannel channel, IReadOnlyList<UIElement> panels, int targetIndex, int direction,
        FrameworkElement? viewport, bool animate)
    {
        if (panels.Count == 0 || targetIndex < 0 || targetIndex >= panels.Count)
        {
            return;
        }

        if (_slideStoryboards.Remove(channel, out var existing))
        {
            existing.Stop();
        }

        for (var i = 0; i < panels.Count; i++)
        {
            if (i != targetIndex)
            {
                var hiddenPanel = panels[i];
                hiddenPanel.Visibility = Visibility.Collapsed;
                hiddenPanel.Opacity = 1;
                EnsureSlideTranslate(hiddenPanel).X = 0;
            }
        }

        var targetPanel = panels[targetIndex];
        var targetTranslate = EnsureSlideTranslate(targetPanel);

        if (!animate || _closed)
        {
            targetPanel.Visibility = Visibility.Visible;
            targetPanel.Opacity = 1;
            targetTranslate.X = 0;
            return;
        }

        var width = viewport?.ActualWidth > 0 ? viewport.ActualWidth : 640.0;
        var slideDirection = direction >= 0 ? 1 : -1;
        var slideOffset = Math.Min(100.0, Math.Max(width * 0.28, 72.0)) * slideDirection;

        targetPanel.Visibility = Visibility.Visible;
        targetPanel.Opacity = 0;
        targetTranslate.X = slideOffset;

        var slideDuration = TimeSpan.FromMilliseconds(280);
        var fadeDuration = TimeSpan.FromMilliseconds(200);
        var slideEasing = new ExponentialEase { Exponent = 5, EasingMode = EasingMode.EaseOut };
        var fadeEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateSlideDoubleAnimation(targetTranslate, "X", slideOffset, 0, slideDuration,
            slideEasing));
        storyboard.Children.Add(CreateSlideDoubleAnimation(targetPanel, "Opacity", 0, 1.0, fadeDuration, fadeEasing));

        _slideStoryboards[channel] = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (_slideStoryboards.TryGetValue(channel, out var current) && ReferenceEquals(current, storyboard))
            {
                _slideStoryboards.Remove(channel);
                storyboard.Stop();
                if (!_closed)
                {
                    targetPanel.Opacity = 1;
                    targetTranslate.X = 0;
                }
            }
        };
        storyboard.Begin();
    }

    private void StopAllSlideTransitions()
    {
        foreach (var storyboard in _slideStoryboards.Values.ToArray())
        {
            storyboard.Stop();
        }

        _slideStoryboards.Clear();
    }

    private static TranslateTransform EnsureSlideTranslate(UIElement target)
    {
        if (target.RenderTransform is TranslateTransform translate)
        {
            return translate;
        }

        translate = new TranslateTransform();
        target.RenderTransform = translate;
        return translate;
    }

    private static DoubleAnimation CreateSlideDoubleAnimation(DependencyObject target, string propertyPath,
        double from, double to, TimeSpan duration, EasingFunctionBase? easing)
    {
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(duration),
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, propertyPath);
        return animation;
    }

    private void SetBusy(bool busy)
    {
        _busyOperations += busy ? 1 : -1;
        UpdateBusyUi();
    }

    private void UpdateBusyUi()
    {
        if (_closed) return;
        GlobalProgressBar.Visibility = _busyOperations > 0 || _archiveLoadPending
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateAuthOtpButton();
    }

    private void ShowMessage(InfoBarSeverity severity, string title, string message, bool isAuthOtpFeedback = false)
    {
        _statusIsAuthOtpFeedback = isAuthOtpFeedback;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.Title = title;
        StatusInfoBar.Message = message;
        StatusInfoBar.IsOpen = true;
    }

    private static T? TryGetService<T>() where T : class
    {
        try
        {
            return App.GetService<T>();
        }
        catch
        {
            return null;
        }
    }
}