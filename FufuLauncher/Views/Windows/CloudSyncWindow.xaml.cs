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
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
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
    private bool _isSendingAuthOtp;
    private bool _statusIsAuthOtpFeedback;
    private bool _closed;
    private int _busyOperations;
    private long _archiveRequestVersion;
    private bool _archiveLoadPending;
    private string? _archiveAccountEmail;
    private string? _displayedArchiveFilter;

    private bool IsBusy => _isSendingAuthOtp || _busyOperations > 0 || _archiveLoadPending;

    public static void ShowOrActivate(string? initialTabTag = null)
    {
        if (_current != null)
        {
            _current.SelectTab(initialTabTag);
            _current.Activate();
            return;
        }

        var window = new CloudSyncWindow();
        window.Closed += (_, _) => _current = null;
        _current = window;
        if (!string.IsNullOrWhiteSpace(initialTabTag))
        {
            window.SelectTab(initialTabTag);
        }

        window.Activate();
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
        _ = ApplyBackdropFromSettingsAsync();

        _cloudSync.SessionChanged += OnSessionChanged;
        Closed += (_, _) =>
        {
            _closed = true;
            _authOtpTimer.Stop();
            _cloudSync.SessionChanged -= OnSessionChanged;
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

    private async Task ApplyBackdropFromSettingsAsync()
    {
        try
        {
            var settings = App.GetService<ILocalSettingsService>();
            var backdropJson = await settings.ReadSettingAsync("WindowBackdrop");
            var backdropType = backdropJson != null
                ? (WindowBackdropType)Convert.ToInt32(backdropJson)
                : WindowBackdropType.Acrylic;

            if (backdropType == WindowBackdropType.None)
            {
                SystemBackdrop = null;
                var isDark = RootGrid.ActualTheme == ElementTheme.Dark;
                RootGrid.Background = new SolidColorBrush(isDark
                    ? Color.FromArgb(255, 32, 32, 32)
                    : Color.FromArgb(255, 243, 243, 243));
                return;
            }

            SystemBackdrop = backdropType == WindowBackdropType.Mica
                ? new MicaBackdrop()
                : new DesktopAcrylicBackdrop();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] ApplyBackdrop failed: {ex.Message}");
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
        if (AccountTabPanel == null || QuotaTabPanel == null || ArchivesTabPanel == null || HutaoTabPanel == null) return;
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;

        if (tag != "Account") MaskAuthPasswords();
        AccountTabPanel.Visibility = tag == "Account" ? Visibility.Visible : Visibility.Collapsed;
        QuotaTabPanel.Visibility = tag == "Quota" ? Visibility.Visible : Visibility.Collapsed;
        ArchivesTabPanel.Visibility = tag == "Archives" ? Visibility.Visible : Visibility.Collapsed;
        HutaoTabPanel.Visibility = tag == "Hutao" ? Visibility.Visible : Visibility.Collapsed;

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
