/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow : Window
{
    private static readonly TimeSpan DefaultAuthOtpCooldown = TimeSpan.FromSeconds(30);
    private static CloudSyncWindow? _current;

    private readonly ICloudSyncService _cloudSync;
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

    public CloudSyncWindow() : this(App.GetService<ICloudSyncService>())
    {
    }

    internal CloudSyncWindow(ICloudSyncService cloudSync)
    {
        _cloudSync = cloudSync;
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
        };

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

    private void RefreshAccountUiState()
    {
        if (_closed) return;
        var loggedIn = _cloudSync.IsLoggedIn;
        if (loggedIn)
        {
            MaskAuthPasswords();
            DismissAuthOtpFeedback();
        }
        UnauthPanel.Visibility = loggedIn ? Visibility.Collapsed : Visibility.Visible;
        LoggedInAccountPanel.Visibility = loggedIn ? Visibility.Visible : Visibility.Collapsed;
        UpdateAuthOtpButton();

        if (!loggedIn)
        {
            QuotaResetInfoText.Text = "CloudSync_QuotaDefaultSubtitle".GetLocalized();
            QuotaDetailsPanel.Visibility = Visibility.Collapsed;
            QuotaNotReadyHintText.Text = "CloudSync_QuotaNotReadySignIn".GetLocalized();
            QuotaNotReadyHintText.Visibility = Visibility.Visible;
            ResetArchiveList();
            ArchivesSummaryText.Text = "CloudSync_ArchivesDefaultSubtitle".GetLocalized();
            ArchivesEmptyText.Text = "CloudSync_ArchivesSignInEmpty".GetLocalized();
            ArchivesEmptyText.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrWhiteSpace(EmailTextBox.Text) && !string.IsNullOrWhiteSpace(_cloudSync.CurrentEmail))
        {
            EmailTextBox.Text = _cloudSync.CurrentEmail;
        }

        var locked = _cloudSync.IsLocked;
        var statusBrush = locked
            ? (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
            : (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];

        AccountEmailValueText.Text = string.IsNullOrWhiteSpace(_cloudSync.CurrentEmail) ? "-" : _cloudSync.CurrentEmail;
        AccountStatusIndicator.Fill = statusBrush;
        AccountLockStatusText.Text = (locked ? "CloudSync_StatusLocked" : "CloudSync_StatusNormal").GetLocalized();
        AccountLockStatusText.Foreground = statusBrush;
        AccountLockedAtText.Text = locked
            ? CloudArchiveItem.FormatIsoTimestamp(_cloudSync.LockedAt)
            : "CloudSync_NotLockedText".GetLocalized();

        AccountLockActionTitleText.Text = (locked ? "CloudSync_UnlockTitle" : "CloudSync_LockTitle").GetLocalized();
        AccountLockActionDescText.Text = (locked ? "CloudSync_UnlockDesc" : "CloudSync_LockDesc").GetLocalized();
        LockAccountButton.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
        UnlockAccountButton.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
        DeleteAccountButton.IsEnabled = !locked;

        if (locked)
        {
            QuotaResetInfoText.Text = "CloudSync_QuotaDefaultSubtitle".GetLocalized();
            QuotaDetailsPanel.Visibility = Visibility.Collapsed;
            QuotaNotReadyHintText.Text = "CloudSync_QuotaNotReadyLocked".GetLocalized();
            QuotaNotReadyHintText.Visibility = Visibility.Visible;
            ResetArchiveList();
            ArchivesSummaryText.Text = "CloudSync_ArchivesLockedHint".GetLocalized();
            ArchivesEmptyText.Text = "CloudSync_ArchivesLockedEmpty".GetLocalized();
            ArchivesEmptyText.Visibility = Visibility.Visible;
        }
        else
        {
            if (_archiveAccountEmail != null && _archiveAccountEmail != _cloudSync.CurrentEmail)
            {
                ResetArchiveList();
            }
            QuotaNotReadyHintText.Visibility = Visibility.Collapsed;
            QuotaDetailsPanel.Visibility = Visibility.Visible;
        }
    }

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (AccountTabPanel == null || QuotaTabPanel == null || ArchivesTabPanel == null) return;
        if (args.SelectedItem is not NavigationViewItem item || item.Tag is not string tag) return;

        if (tag != "Account") MaskAuthPasswords();
        AccountTabPanel.Visibility = tag == "Account" ? Visibility.Visible : Visibility.Collapsed;
        QuotaTabPanel.Visibility = tag == "Quota" ? Visibility.Visible : Visibility.Collapsed;
        ArchivesTabPanel.Visibility = tag == "Archives" ? Visibility.Visible : Visibility.Collapsed;

        if (!_cloudSync.IsLoggedIn || _cloudSync.IsLocked)
        {
            if (_cloudSync.IsLocked && tag != "Account")
            {
                ShowMessage(InfoBarSeverity.Warning, "CloudSync_LockedBannerTitle".GetLocalized(),
                    "CloudSync_LockedBannerDetail".GetLocalized());
            }

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

    private void OnAuthModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AuthModeSelector == null || PasswordInput == null || ConfirmPasswordInput == null ||
            OtpInputRow == null || AuthSubmitButton == null || _authOtpTimer == null) return;
        var mode = AuthModeSelector.SelectedIndex;
        MaskAuthPasswords();
        ResetAuthOtpFeedback();

        if (mode == 0)
        {
            PasswordInput.Label = "CloudSync_PasswordHeader".GetLocalized();
            ConfirmPasswordInput.Visibility = Visibility.Collapsed;
            OtpInputRow.Visibility = Visibility.Collapsed;
            AuthSubmitButton.Content = "CloudSync_SubmitLogin".GetLocalized();
        }
        else if (mode == 1)
        {
            PasswordInput.Label = "CloudSync_PasswordSetHeader".GetLocalized();
            ConfirmPasswordInput.Label = "CloudSync_ConfirmPasswordHeader".GetLocalized();
            ConfirmPasswordInput.Visibility = Visibility.Visible;
            OtpInputRow.Visibility = Visibility.Visible;
            AuthSubmitButton.Content = "CloudSync_SubmitRegister".GetLocalized();
        }
        else
        {
            PasswordInput.Label = "CloudSync_PasswordNewHeader".GetLocalized();
            ConfirmPasswordInput.Label = "CloudSync_ConfirmNewPasswordHeader".GetLocalized();
            ConfirmPasswordInput.Visibility = Visibility.Visible;
            OtpInputRow.Visibility = Visibility.Visible;
            AuthSubmitButton.Content = "CloudSync_SubmitReset".GetLocalized();
        }
    }

    private void MaskAuthPasswords()
    {
        PasswordInput?.MaskPassword();
        ConfirmPasswordInput?.MaskPassword();
    }

    private async void OnSendAuthOtpClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || _closed || _cloudSync.IsLoggedIn) return;
        var context = GetAuthOtpContext();
        if (context.Mode is not (1 or 2) || GetAuthOtpSecondsRemaining(context) > 0) return;

        var email = EmailTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            ShowMessage(InfoBarSeverity.Warning, "CloudSync_HintTitle".GetLocalized(),
                "CloudSync_ErrInvalidEmail".GetLocalized(), isAuthOtpFeedback: true);
            EmailTextBox.Focus(FocusState.Programmatic);
            return;
        }

        _isSendingAuthOtp = true;
        DismissAuthOtpFeedback();
        EmailTextBox.IsReadOnly = true;
        AuthModeSelector.IsEnabled = false;
        AuthSubmitButton.IsEnabled = false;
        UpdateAuthOtpButton();
        try
        {
            var startRes = context.Mode == 1
                ? await _cloudSync.StartRegisterSendCodeAsync(email)
                : await _cloudSync.StartResetPasswordSendCodeAsync(email);
            if (_closed || _cloudSync.IsLoggedIn) return;

            if (startRes.RequiresCaptcha && startRes.Challenge is { } challenge)
            {
                var captchaRes = await CloudCaptchaWindow.ShowChallengeAsync<CloudOtpSentData>(_cloudSync, challenge);
                if (_closed || _cloudSync.IsLoggedIn) return;

                if (captchaRes == null)
                {
                    if (context == GetAuthOtpContext())
                    {
                        ShowMessage(InfoBarSeverity.Informational, "CloudSync_MsgCancelled".GetLocalized(),
                            "CloudSync_MsgCaptchaCancelled".GetLocalized(), isAuthOtpFeedback: true);
                    }
                    return;
                }

                ApplyAuthOtpResult(captchaRes, context, email);
                return;
            }

            ApplyAuthOtpResult(startRes, context, email);
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] Unable to send verification code: {ex.Message}");
            if (!_closed && !_cloudSync.IsLoggedIn && context == GetAuthOtpContext())
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_SendFailedTitle".GetLocalized(),
                    "CloudSync_ErrGeneric".GetLocalized(), isAuthOtpFeedback: true);
            }
        }
        finally
        {
            _isSendingAuthOtp = false;
            if (!_closed)
            {
                EmailTextBox.IsReadOnly = false;
                AuthModeSelector.IsEnabled = true;
                AuthSubmitButton.IsEnabled = true;
                UpdateAuthOtpButton();
            }
        }
    }

    private (int Mode, string Email) GetAuthOtpContext()
        => (AuthModeSelector.SelectedIndex, EmailTextBox.Text.Trim().ToLowerInvariant());

    private void ApplyAuthOtpResult<T>(CloudApiResult<T> result, (int Mode, string Email) context, string email)
    {
        var code = result.Code == "CAPTCHA_REQUIRED" ? "CHALLENGE_INVALID" : result.Code;
        var sent = result.Ok && code != "CHALLENGE_INVALID";
        var retryAfter = result.RetryAfter ?? (sent || code == "OTP_COOLDOWN" ? DefaultAuthOtpCooldown : TimeSpan.Zero);
        if (retryAfter > TimeSpan.Zero)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var entry in _authOtpResendAt.Where(entry => entry.Value <= now).ToArray())
            {
                _authOtpResendAt.Remove(entry.Key);
            }
            _authOtpResendAt[context] = now + retryAfter;
        }

        if (context != GetAuthOtpContext()) return;
        if (sent)
        {
            ShowMessage(InfoBarSeverity.Success, "CloudSync_OtpSentTitle".GetLocalized(),
                string.Format("CloudSync_EmailOtpSentDetail".GetLocalized(), email), isAuthOtpFeedback: true);
            if (AccountTabPanel.Visibility == Visibility.Visible) OtpTextBox.Focus(FocusState.Programmatic);
        }
        else
        {
            ShowMessage(InfoBarSeverity.Error, "CloudSync_SendFailedTitle".GetLocalized(),
                _cloudSync.FormatFriendlyError(code, result.Message, result.HttpStatusCode), isAuthOtpFeedback: true);
        }
    }

    private void DismissAuthOtpFeedback()
    {
        if (!_statusIsAuthOtpFeedback) return;
        _statusIsAuthOtpFeedback = false;
        StatusInfoBar.IsOpen = false;
    }

    private void ResetAuthOtpFeedback()
    {
        DismissAuthOtpFeedback();
        UpdateAuthOtpButton();
    }

    private double GetAuthOtpSecondsRemaining((int Mode, string Email) context)
        => _authOtpResendAt.TryGetValue(context, out var deadline)
            ? Math.Max(0, Math.Ceiling((deadline - DateTimeOffset.UtcNow).TotalSeconds))
            : 0;

    private void UpdateAuthOtpButton()
    {
        if (_closed) return;
        var context = GetAuthOtpContext();
        var seconds = context.Mode is 1 or 2 && !_cloudSync.IsLoggedIn ? GetAuthOtpSecondsRemaining(context) : 0;
        if (seconds > 0)
        {
            if (!_authOtpTimer.IsRunning) _authOtpTimer.Start();
        }
        else if (_authOtpTimer.IsRunning)
        {
            _authOtpTimer.Stop();
        }

        SendAuthOtpButton.IsEnabled = !IsBusy && !_cloudSync.IsLoggedIn && seconds == 0;
        SendAuthOtpButton.Content = _isSendingAuthOtp
            ? "CloudSync_OtpSendingBtn".GetLocalized()
            : seconds > 0
                ? string.Format("CloudSync_OtpResendIn".GetLocalized(), seconds)
                : "CloudSync_SendOtpBtn".GetLocalized();
    }

    private async void OnAuthSubmitClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        var email = EmailTextBox.Text.Trim();
        var password = PasswordInput.Password;
        var mode = AuthModeSelector.SelectedIndex;

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            ShowMessage(InfoBarSeverity.Warning, "CloudSync_HintTitle".GetLocalized(),
                "CloudSync_ErrInvalidEmail".GetLocalized());
            EmailTextBox.Focus(FocusState.Programmatic);
            return;
        }

        if (string.IsNullOrEmpty(password) || password.Length < 8)
        {
            ShowMessage(InfoBarSeverity.Warning, "CloudSync_HintTitle".GetLocalized(),
                "CloudSync_ErrPasswordLength".GetLocalized());
            PasswordInput.FocusInput();
            return;
        }

        var otp = OtpTextBox.Text.Trim();
        if (mode != 0)
        {
            if (password != ConfirmPasswordInput.Password)
            {
                ShowMessage(InfoBarSeverity.Warning, "CloudSync_HintTitle".GetLocalized(),
                    "CloudSync_ErrPasswordMismatch".GetLocalized());
                ConfirmPasswordInput.FocusInput();
                return;
            }

            if (string.IsNullOrWhiteSpace(otp))
            {
                ShowMessage(InfoBarSeverity.Warning, "CloudSync_HintTitle".GetLocalized(),
                    "CloudSync_ErrEnterCodeFirst".GetLocalized());
                OtpTextBox.Focus(FocusState.Programmatic);
                return;
            }
        }

        MaskAuthPasswords();
        SetBusy(true);
        try
        {
            if (mode == 0)
            {
                var startRes = await _cloudSync.StartLoginAsync(email, password);
                if (!startRes.RequiresCaptcha || startRes.Challenge == null)
                {
                    ShowMessage(InfoBarSeverity.Error, "CloudSync_LoginFailedTitle".GetLocalized(),
                        _cloudSync.FormatFriendlyError(startRes.Code, startRes.Message, startRes.HttpStatusCode));
                    return;
                }

                var captchaRes =
                    await CloudCaptchaWindow.ShowChallengeAsync<CloudLoginSessionData>(_cloudSync, startRes.Challenge);
                if (captchaRes == null)
                {
                    ShowMessage(InfoBarSeverity.Informational, "CloudSync_MsgCancelled".GetLocalized(),
                        "CloudSync_MsgCaptchaCancelled".GetLocalized());
                    return;
                }

                if (captchaRes.Ok && captchaRes.Data != null)
                {
                    _cloudSync.ApplyLoginSession(email, captchaRes.Data);
                    PasswordInput.Password = string.Empty;
                    ShowMessage(
                        captchaRes.Data.Locked ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                        (captchaRes.Data.Locked ? "CloudSync_MsgLoginLockedTitle" : "CloudSync_MsgLoginSuccess")
                        .GetLocalized(),
                        (captchaRes.Data.Locked ? "CloudSync_MsgLoginLockedDetail" : "CloudSync_MsgLoginSuccessDetail")
                        .GetLocalized());
                    RefreshAccountUiState();
                }
                else
                {
                    ShowMessage(InfoBarSeverity.Error, "CloudSync_LoginFailedTitle".GetLocalized(),
                        _cloudSync.FormatFriendlyError(captchaRes.Code, captchaRes.Message, captchaRes.HttpStatusCode));
                }
            }
            else if (mode == 1)
            {
                var regRes = await _cloudSync.ConfirmRegisterAsync(email, password, otp);
                if (regRes.Ok)
                {
                    PasswordInput.Password = string.Empty;
                    ConfirmPasswordInput.Password = string.Empty;
                    OtpTextBox.Text = string.Empty;
                    ShowMessage(InfoBarSeverity.Success, "CloudSync_MsgRegisterSuccess".GetLocalized(),
                        "CloudSync_MsgRegisterSuccessDetail".GetLocalized());
                    RefreshAccountUiState();
                }
                else
                {
                    ShowMessage(InfoBarSeverity.Error, "CloudSync_RegisterFailedTitle".GetLocalized(),
                        _cloudSync.FormatFriendlyError(regRes.Code, regRes.Message, regRes.HttpStatusCode));
                }
            }
            else
            {
                var resetRes = await _cloudSync.ConfirmResetPasswordAsync(email, otp, password);
                if (resetRes.Ok)
                {
                    PasswordInput.Password = string.Empty;
                    ConfirmPasswordInput.Password = string.Empty;
                    OtpTextBox.Text = string.Empty;
                    AuthModeSelector.SelectedIndex = 0;
                    ShowMessage(InfoBarSeverity.Success, "CloudSync_MsgResetSuccess".GetLocalized(),
                        "CloudSync_MsgResetSuccessDetail".GetLocalized());
                }
                else
                {
                    ShowMessage(InfoBarSeverity.Error, "CloudSync_ResetFailedTitle".GetLocalized(),
                        _cloudSync.FormatFriendlyError(resetRes.Code, resetRes.Message, resetRes.HttpStatusCode));
                }
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRefreshAccountStatusClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            var res = await _cloudSync.GetAccountStatusAsync();
            if (res.Ok)
            {
                var statusLabel = (_cloudSync.IsLocked ? "CloudSync_StatusLocked" : "CloudSync_StatusNormal")
                    .GetLocalized();
                ShowMessage(InfoBarSeverity.Success,
                    "CloudSync_RefreshDoneTitle".GetLocalized(),
                    string.Format("CloudSync_RefreshStatusResultFormat".GetLocalized(), statusLabel));
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_RefreshFailedTitle".GetLocalized(),
                    _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
            }
        }
        finally
        {
            SetBusy(false);
            RefreshAccountUiState();
        }
    }

    private async void OnLockAccountClick(object sender, RoutedEventArgs e)
    {
        await ShowLockOrUnlockDialogAsync(lockAccount: true);
    }

    private async void OnUnlockAccountClick(object sender, RoutedEventArgs e)
    {
        await ShowLockOrUnlockDialogAsync(lockAccount: false);
    }

    private async Task ShowLockOrUnlockDialogAsync(bool lockAccount)
    {
        var actionName = (lockAccount ? "CloudSync_LockTitle" : "CloudSync_UnlockTitle").GetLocalized();
        var description = (lockAccount ? "CloudSync_LockDesc" : "CloudSync_UnlockDesc").GetLocalized();

        var sendBtn = new Button { Content = "CloudSync_SendOtpBtn".GetLocalized() };
        var codeBox = new TextBox
        {
            Header = "CloudSync_OtpHeader".GetLocalized(),
            PlaceholderText = "CloudSync_OtpPlaceholder".GetLocalized(),
            MaxLength = 16
        };
        var feedbackText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, FontSize = 13 });
        panel.Children.Add(sendBtn);
        panel.Children.Add(codeBox);
        panel.Children.Add(feedbackText);

        var dialogClosed = false;
        var dialog = new ContentDialog
        {
            Title = actionName,
            Content = panel,
            PrimaryButtonText =
                (lockAccount ? "CloudSync_ConfirmLockBtn" : "CloudSync_ConfirmUnlockBtn").GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };
        dialog.Closed += (_, _) => dialogClosed = true;

        sendBtn.Click += async (_, _) =>
        {
            sendBtn.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            try
            {
                feedbackText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                feedbackText.Text = "CloudSync_SendingEmail".GetLocalized();
                var sendRes = lockAccount
                    ? await _cloudSync.SendLockCodeAsync()
                    : await _cloudSync.SendUnlockCodeAsync();

                if (dialogClosed)
                {
                    return;
                }

                if (sendRes.RequiresCaptcha && sendRes.Challenge != null)
                {
                    var captchaRes =
                        await CloudCaptchaWindow.ShowChallengeAsync<CloudOtpSentData>(_cloudSync, sendRes.Challenge);
                    if (dialogClosed)
                    {
                        return;
                    }

                    if (captchaRes == null)
                    {
                        feedbackText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
                        feedbackText.Text = "CloudSync_MsgCaptchaCancelled".GetLocalized();
                        return;
                    }

                    sendRes = captchaRes;
                }
                else if (!sendRes.Ok || string.Equals(sendRes.Code, "CAPTCHA_REQUIRED", StringComparison.Ordinal))
                {
                    feedbackText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                    feedbackText.Text = string.Equals(sendRes.Code, "CAPTCHA_REQUIRED", StringComparison.Ordinal)
                        ? _cloudSync.FormatFriendlyError("CHALLENGE_INVALID", sendRes.Message, sendRes.HttpStatusCode)
                        : _cloudSync.FormatFriendlyError(sendRes.Code, sendRes.Message, sendRes.HttpStatusCode);
                    return;
                }

                if (dialogClosed)
                {
                    return;
                }

                if (sendRes.Ok)
                {
                    feedbackText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                    feedbackText.Text = string.Format("CloudSync_EmailOtpSentDetail".GetLocalized(),
                        _cloudSync.CurrentEmail);
                    codeBox.Focus(FocusState.Programmatic);
                }
                else
                {
                    feedbackText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                    feedbackText.Text =
                        _cloudSync.FormatFriendlyError(sendRes.Code, sendRes.Message, sendRes.HttpStatusCode);
                }
            }
            finally
            {
                if (!dialogClosed)
                {
                    sendBtn.IsEnabled = true;
                    dialog.IsPrimaryButtonEnabled = true;
                }
            }
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var code = codeBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                args.Cancel = true;
                feedbackText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                feedbackText.Text = "CloudSync_InputOtpRequired".GetLocalized();
                return;
            }

            var deferral = args.GetDeferral();
            try
            {
                var res = lockAccount
                    ? await _cloudSync.ConfirmLockAsync(code)
                    : await _cloudSync.ConfirmUnlockAsync(code);

                if (!res.Ok)
                {
                    args.Cancel = true;
                    feedbackText.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
                    feedbackText.Text = _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode);
                }
                else
                {
                    ShowMessage(
                        lockAccount ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                        string.Format("CloudSync_ActionSuccessTitleFormat".GetLocalized(), actionName),
                        (lockAccount ? "CloudSync_MsgLockedSuccess" : "CloudSync_MsgUnlockedSuccess").GetLocalized());
                }
            }
            finally
            {
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
        RefreshAccountUiState();
    }

    private async void OnLogoutClick(object sender, RoutedEventArgs e)
    {
        var revokeCheck = new CheckBox
        {
            Content = "CloudSync_LogoutRevokeAllCheck".GetLocalized(),
            IsChecked = true
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "CloudSync_LogoutConfirmText".GetLocalized(),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(revokeCheck);

        var dialog = new ContentDialog
        {
            Title = "CloudSync_LogoutBtn".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "CloudSync_LogoutBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            await _cloudSync.LogoutAsync(revokeCheck.IsChecked == true);
            ShowMessage(InfoBarSeverity.Informational,
                "CloudSync_LogoutSuccessTitle".GetLocalized(),
                "CloudSync_LogoutSuccessDetail".GetLocalized());
            RefreshAccountUiState();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDeleteAccountClick(object sender, RoutedEventArgs e)
    {
        if (_cloudSync.IsLocked)
        {
            ShowMessage(InfoBarSeverity.Warning, "CloudSync_CannotOperateTitle".GetLocalized(),
                "CloudSync_LockBlockedDelete".GetLocalized());
            return;
        }

        var passwordInput = new CloudPasswordInput
        {
            Label = "CloudSync_PasswordHeader".GetLocalized(),
            PlaceholderText = "CloudSync_InputCurrentPwd".GetLocalized()
        };
        var errText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = "CloudSync_DeleteAccountWarning".GetLocalized(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
        });
        panel.Children.Add(passwordInput);
        panel.Children.Add(errText);

        var dialog = new ContentDialog
        {
            Title = "CloudSync_DeleteAccountTitle".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "CloudSync_ConfirmBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var password = passwordInput.Password;
            if (string.IsNullOrEmpty(password))
            {
                args.Cancel = true;
                errText.Text = "CloudSync_PasswordRequired".GetLocalized();
                passwordInput.FocusInput();
                return;
            }

            passwordInput.MaskPassword();
            var deferral = args.GetDeferral();
            try
            {
                var res = await _cloudSync.DeleteAccountAsync(password);
                if (!res.Ok)
                {
                    args.Cancel = true;
                    errText.Text = _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode);
                }
                else
                {
                    ShowMessage(InfoBarSeverity.Informational,
                        "CloudSync_DeleteAccountDoneTitle".GetLocalized(),
                        "CloudSync_DeleteAccountDoneDetail".GetLocalized());
                }
            }
            finally
            {
                deferral.Complete();
            }
        };

        void OnDeleteDialogWindowActivated(object sender, WindowActivatedEventArgs args)
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                passwordInput.MaskPassword();
            }
        }

        Activated += OnDeleteDialogWindowActivated;
        try
        {
            await dialog.ShowAsync();
        }
        finally
        {
            Activated -= OnDeleteDialogWindowActivated;
        }
        RefreshAccountUiState();
    }

    private async void OnRefreshQuotaClick(object sender, RoutedEventArgs e)
    {
        await RefreshQuotaInternalAsync(silent: false);
    }

    private async Task RefreshQuotaInternalAsync(bool silent)
    {
        if (!_cloudSync.IsLoggedIn)
        {
            RefreshAccountUiState();
            return;
        }

        SetBusy(true);
        try
        {
            var res = await _cloudSync.GetQuotaAsync();
            if (!res.Ok || res.Data == null)
            {
                if (!silent)
                {
                    ShowMessage(res.IsAccountLocked ? InfoBarSeverity.Warning : InfoBarSeverity.Error,
                        "CloudSync_QuotaFailedTitle".GetLocalized(),
                        _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
                }

                RefreshAccountUiState();
                return;
            }

            ApplyQuotaToUi(res.Data);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyQuotaToUi(CloudQuotaData q)
    {
        QuotaNotReadyHintText.Visibility = Visibility.Collapsed;
        QuotaDetailsPanel.Visibility = Visibility.Visible;

        var resetLocal = q.ResetsAt > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(q.ResetsAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : "-";
        QuotaResetInfoText.Text = string.Format("CloudSync_QuotaResetInfo".GetLocalized(), q.UtcDate, resetLocal);

        QuotaStorageValueText.Text = string.Format(
            "CloudSync_QuotaStorageValue".GetLocalized(),
            CloudArchiveItem.FormatBytes(q.Storage.R2Bytes.Used),
            CloudArchiveItem.FormatBytes(q.Storage.R2Bytes.Limit),
            CloudArchiveItem.FormatBytes(q.Storage.R2Bytes.Remaining));
        QuotaStorageBar.Value = CalcPercent(q.Storage.R2Bytes.Used, q.Storage.R2Bytes.Limit);

        QuotaFilesValueText.Text = string.Format(
            "CloudSync_QuotaFilesValue".GetLocalized(),
            q.Storage.Files.Used,
            q.Storage.Files.Limit,
            q.Storage.Files.Remaining);
        QuotaFilesBar.Value = CalcPercent(q.Storage.Files.Used, q.Storage.Files.Limit);

        var usedFmt = "CloudSync_QuotaUsedRemaining".GetLocalized();
        QuotaOpsValueText.Text = string.Format(usedFmt, q.Daily.Ops.Used, q.Daily.Ops.Limit, q.Daily.Ops.Remaining);
        QuotaOpsBar.Value = CalcPercent(q.Daily.Ops.Used, q.Daily.Ops.Limit);

        QuotaUploadsValueText.Text =
            string.Format(usedFmt, q.Daily.Uploads.Used, q.Daily.Uploads.Limit, q.Daily.Uploads.Remaining);
        QuotaUploadsBar.Value = CalcPercent(q.Daily.Uploads.Used, q.Daily.Uploads.Limit);

        QuotaDeletesValueText.Text =
            string.Format(usedFmt, q.Daily.Deletes.Used, q.Daily.Deletes.Limit, q.Daily.Deletes.Remaining);
        QuotaDeletesBar.Value = CalcPercent(q.Daily.Deletes.Used, q.Daily.Deletes.Limit);

        if (q.Upgrade.Active)
        {
            var expLocal = CloudArchiveItem.FormatIsoTimestamp(q.Upgrade.ExpiresAt);
            var delta = q.Upgrade.ExpandedBy;
            var deltaSummary = delta == null
                ? string.Empty
                : string.Format(
                    "CloudSync_UpgradeDeltaFormat".GetLocalized(),
                    CloudArchiveItem.FormatBytes(delta.MaxUserR2Bytes),
                    delta.MaxUserFiles,
                    delta.UserDailyMaxUploads,
                    delta.UserDailyMaxDeletes,
                    delta.UserDailyMaxOps);
            UpgradeStatusText.Text =
                string.Format("CloudSync_UpgradeActiveFormat".GetLocalized(), expLocal, deltaSummary);
            UpgradeStatusText.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
        }
        else
        {
            UpgradeStatusText.Text = "CloudSync_UpgradeInactive".GetLocalized();
            UpgradeStatusText.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        }
    }

    private async void OnActivateCodeClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        if (!_cloudSync.IsLoggedIn)
        {
            ShowMessage(InfoBarSeverity.Warning, "CloudSync_HintTitle".GetLocalized(),
                "CloudSync_MsgLoginRequiredFirst".GetLocalized());
            return;
        }

        var code = ActivationCodeTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowMessage(InfoBarSeverity.Warning, "CloudSync_HintTitle".GetLocalized(),
                "CloudSync_ActivatePromptEmpty".GetLocalized());
            return;
        }

        SetBusy(true);
        try
        {
            var res = await _cloudSync.ActivateCodeAsync(code);
            if (res.Ok)
            {
                ActivationCodeTextBox.Text = string.Empty;
                ShowMessage(InfoBarSeverity.Success, "CloudSync_MsgActivateSuccess".GetLocalized(),
                    "CloudSync_MsgActivateSuccessDetail".GetLocalized());
                await RefreshQuotaInternalAsync(silent: true);
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_ActivateFailedTitle".GetLocalized(),
                    _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnArchiveFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArchivesRepeater != null && _cloudSync.IsLoggedIn && !_cloudSync.IsLocked)
        {
            _ = RefreshArchivesInternalAsync(silent: false);
        }
    }

    private async void OnRefreshArchivesClick(object sender, RoutedEventArgs e)
    {
        await RefreshArchivesInternalAsync(silent: false);
    }

    private async Task RefreshArchivesInternalAsync(bool silent)
    {
        if (_closed) return;
        if (!_cloudSync.IsLoggedIn || _cloudSync.IsLocked)
        {
            RefreshAccountUiState();
            return;
        }

        var filter = (ArchiveFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var email = _cloudSync.CurrentEmail;
        var accountChanged = _archiveAccountEmail != email;
        _archiveAccountEmail = email;
        var version = ++_archiveRequestVersion;
        _archiveLoadPending = true;
        UpdateBusyUi();

        if (accountChanged || _displayedArchiveFilter != filter)
        {
            _displayedArchiveFilter = null;
            _archives.Clear();
            ArchivesSummaryText.Text = "CloudSync_ArchivesSubtitle".GetLocalized();
        }

        ArchivesEmptyText.Text = "CloudSync_LoadingArchivesText".GetLocalized();
        ArchivesEmptyText.Visibility = _archives.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        try
        {
            var res = await _cloudSync.ListArchivesAsync(filter);
            if (_closed || version != _archiveRequestVersion) return;
            if (!_cloudSync.IsLoggedIn || _cloudSync.IsLocked || email != _cloudSync.CurrentEmail)
            {
                ResetArchiveList();
                RefreshAccountUiState();
                return;
            }

            if (!res.Ok || res.Data == null)
            {
                if (!silent || _archives.Count == 0)
                {
                    ArchivesEmptyText.Text = _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode);
                    ArchivesEmptyText.Visibility = Visibility.Visible;
                }
                return;
            }

            _displayedArchiveFilter = filter;
            _archives.Clear();
            foreach (var item in res.Data.Archives)
            {
                _archives.Add(item);
            }

            ArchivesSummaryText.Text = string.Format(
                "CloudSync_ArchivesSummary".GetLocalized(),
                res.Data.FileCount,
                res.Data.FileCountLimit,
                CloudArchiveItem.FormatBytes(res.Data.R2BytesUsed),
                CloudArchiveItem.FormatBytes(res.Data.R2BytesLimit));
            ArchivesEmptyText.Text = (filter == "all" ? "CloudSync_ArchivesEmpty" : "CloudSync_ArchivesFilteredEmpty")
                .GetLocalized();
            ArchivesEmptyText.Visibility = _archives.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            if (version == _archiveRequestVersion)
            {
                _archiveLoadPending = false;
                UpdateBusyUi();
            }
        }
    }

    private void ResetArchiveList()
    {
        ++_archiveRequestVersion;
        _archiveLoadPending = false;
        _archiveAccountEmail = null;
        _displayedArchiveFilter = null;
        _archives.Clear();
        ArchivesSummaryText.Text = "CloudSync_ArchivesSubtitle".GetLocalized();
        ArchivesEmptyText.Visibility = Visibility.Collapsed;
        UpdateBusyUi();
    }

    private async void OnUploadLocalJsonClick(object sender, RoutedEventArgs e)
    {
        if (!_cloudSync.IsLoggedIn || _cloudSync.IsLocked)
        {
            ShowMessage(InfoBarSeverity.Warning,
                "CloudSync_CannotOperateTitle".GetLocalized(),
                (_cloudSync.IsLocked ? "CloudSync_ErrLocked" : "CloudSync_MsgLoginRequiredFirst").GetLocalized());
            return;
        }

        var path = await FilePickerService.PickOpenFileAsync(
            this,
            new[] { ("CloudSync_FileFilterJson".GetLocalized(), new[] { ".json" }) },
            PickerLocationId.DocumentsLibrary,
            msg =>
            {
                DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] PickOpenFile failed: {msg}");
                ShowMessage(InfoBarSeverity.Error, "CloudSync_OpenFileFailedTitle".GetLocalized(),
                    "CloudSync_OpenFileFailedTitle".GetLocalized());
            });
        if (string.IsNullOrEmpty(path)) return;

        string jsonContent;
        try
        {
            jsonContent = await File.ReadAllTextAsync(path);
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] ReadAllText failed: {ex.Message}");
            ShowMessage(InfoBarSeverity.Error, "CloudSync_ReadFileFailedTitle".GetLocalized(),
                "CloudSync_ReadFileFailedTitle".GetLocalized());
            return;
        }

        var detectedType = DetectArchiveType(jsonContent);
        var typeCombo = new ComboBox
        {
            Header = "CloudSync_UploadDataTypeHeader".GetLocalized(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items =
            {
                "CloudSync_UploadTypeUiafOption".GetLocalized(),
                "CloudSync_UploadTypeUigfOption".GetLocalized(),
                "CloudSync_UploadTypePlayTimeOption".GetLocalized()
            },
            SelectedIndex = detectedType == "uigf" ? 1 : detectedType == "playtime" ? 2 : 0
        };
        var slotBox = new TextBox
        {
            Header = "CloudSync_ArchiveNameHeader".GetLocalized(),
            Text = _cloudSync.SuggestValidSlot(Path.GetFileNameWithoutExtension(path), detectedType),
            MaxLength = 64,
            PlaceholderText = "CloudSync_ArchiveNamePlaceholder".GetLocalized()
        };
        var overwriteCheck = new CheckBox
        {
            Content = "CloudSync_OverwriteExistingCheck".GetLocalized(),
            IsChecked = true
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format("CloudSync_SelectedFileFormat".GetLocalized(), Path.GetFileName(path)),
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        panel.Children.Add(typeCombo);
        panel.Children.Add(slotBox);
        panel.Children.Add(overwriteCheck);

        var dialog = new ContentDialog
        {
            Title = "CloudSync_UploadFileDialogTitle".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "CloudSync_StartUploadBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var dataType = typeCombo.SelectedIndex == 1
            ? "uigf"
            : typeCombo.SelectedIndex == 2
                ? "playtime"
                : "uiaf";
        var slot = slotBox.Text.Trim();
        var overwrite = overwriteCheck.IsChecked == true;

        var uploadContent = jsonContent;
        if (dataType == "playtime")
        {
            uploadContent = TryPreparePlayTimeUploadJson(jsonContent);
            if (uploadContent == null)
            {
                ShowMessage(InfoBarSeverity.Error,
                    "CloudSync_MsgUploadFailedTitle".GetLocalized(),
                    "CloudSync_ErrInvalidFormat".GetLocalized());
                return;
            }
        }

        SetBusy(true);
        try
        {
            var res = await _cloudSync.UploadArchiveAsync(dataType, slot, uploadContent, overwrite);
            if (res.Ok)
            {
                var detailKey = res.Data?.Overwritten == true ? "CloudSync_MsgOverwritten" : "CloudSync_MsgUploadedNew";
                ShowMessage(InfoBarSeverity.Success,
                    "CloudSync_MsgUploadSuccess".GetLocalized(),
                    string.Format(detailKey.GetLocalized(), slot));
                await RefreshArchivesInternalAsync(silent: true);
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_MsgUploadFailedTitle".GetLocalized(),
                    _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnMergePlayTimeArchiveToLocalClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CloudArchiveItem item } || !item.IsPlayTime) return;
        if (!_cloudSync.IsLoggedIn || _cloudSync.IsLocked)
        {
            ShowMessage(InfoBarSeverity.Warning,
                "CloudSync_CannotOperateTitle".GetLocalized(),
                (_cloudSync.IsLocked ? "CloudSync_ErrLocked" : "CloudSync_MsgLoginRequiredFirst").GetLocalized());
            return;
        }

        SetBusy(true);
        CloudPullResult pullRes;
        try
        {
            pullRes = await _cloudSync.PullArchiveAsync(item.DataType, item.Slot);
        }
        finally
        {
            SetBusy(false);
        }

        if (!pullRes.Ok)
        {
            ShowMessage(InfoBarSeverity.Error, "CloudSync_DownloadFailedTitle".GetLocalized(),
                _cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
            return;
        }

        try
        {
            var store = App.GetService<GamePlayTimeStore>();
            await store.EnsureInitializedAsync();
            var merge = await store.MergeCloudPlayTimeJsonAsync(pullRes.JsonContent);
            if (!merge.Ok)
            {
                ShowMessage(InfoBarSeverity.Error,
                    "CloudSync_PlayTimeMergeFailedTitle".GetLocalized(),
                    "CloudSync_ErrInvalidFormat".GetLocalized());
                return;
            }

            ShowMessage(InfoBarSeverity.Success,
                "CloudSync_PlayTimeMergeSuccessTitle".GetLocalized(),
                GamePlayTimeStore.FormatMergeSummary(item.Slot, merge));
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] Merge PlayTime failed: {ex.Message}");
            ShowMessage(InfoBarSeverity.Error,
                "CloudSync_PlayTimeMergeFailedTitle".GetLocalized(),
                "CloudSync_ErrInvalidFormat".GetLocalized());
        }
    }

    private async void OnSaveArchiveToDiskClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CloudArchiveItem item }) return;

        SetBusy(true);
        CloudPullResult pullRes;
        try
        {
            pullRes = await _cloudSync.PullArchiveAsync(item.DataType, item.Slot);
        }
        finally
        {
            SetBusy(false);
        }

        if (!pullRes.Ok)
        {
            ShowMessage(InfoBarSeverity.Error, "CloudSync_DownloadFailedTitle".GetLocalized(),
                _cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
            return;
        }

        var suggestedName = item.Slot.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(item.Slot)
            : item.Slot;

        var savePath = await FilePickerService.PickSaveFileAsync(
            this,
            new[] { ("CloudSync_FileFilterJson".GetLocalized(), new[] { ".json" }) },
            suggestedName,
            PickerLocationId.DocumentsLibrary,
            msg =>
            {
                DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] PickSaveFile failed: {msg}");
                ShowMessage(InfoBarSeverity.Error, "CloudSync_SaveFileFailedTitle".GetLocalized(),
                    "CloudSync_SaveFileFailedTitle".GetLocalized());
            });
        if (string.IsNullOrEmpty(savePath)) return;

        try
        {
            await File.WriteAllTextAsync(savePath, pullRes.JsonContent);
            ShowMessage(InfoBarSeverity.Success,
                "CloudSync_SaveSuccessTitle".GetLocalized(),
                string.Format("CloudSync_SaveSuccessDetail".GetLocalized(), item.Slot, Path.GetFileName(savePath)));
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] WriteAllText failed: {ex.Message}");
            ShowMessage(InfoBarSeverity.Error, "CloudSync_SaveFileFailedTitle".GetLocalized(),
                "CloudSync_SaveFileFailedTitle".GetLocalized());
        }
    }

    private async void OnOverwriteArchiveFromDiskClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CloudArchiveItem item }) return;

        var path = await FilePickerService.PickOpenFileAsync(
            this,
            new[] { ("CloudSync_FileFilterJson".GetLocalized(), new[] { ".json" }) },
            PickerLocationId.DocumentsLibrary,
            msg =>
            {
                DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] PickOpenFile failed: {msg}");
                ShowMessage(InfoBarSeverity.Error, "CloudSync_OpenFileFailedTitle".GetLocalized(),
                    "CloudSync_OpenFileFailedTitle".GetLocalized());
            });
        if (string.IsNullOrEmpty(path)) return;

        var confirm = new ContentDialog
        {
            Title = "CloudSync_ConfirmOverwriteTitle".GetLocalized(),
            Content = string.Format("CloudSync_ConfirmOverwriteDiskMsg".GetLocalized(), Path.GetFileName(path),
                item.Slot),
            PrimaryButtonText = "CloudSync_ConfirmOverwriteBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            var jsonContent = await File.ReadAllTextAsync(path);
            var uploadContent = jsonContent;
            if (item.IsPlayTime)
            {
                uploadContent = TryPreparePlayTimeUploadJson(jsonContent);
                if (uploadContent == null)
                {
                    ShowMessage(InfoBarSeverity.Error, "CloudSync_OverwriteFailedTitle".GetLocalized(),
                        "CloudSync_ErrInvalidFormat".GetLocalized());
                    return;
                }
            }

            var res = await _cloudSync.UploadArchiveAsync(item.DataType, item.Slot, uploadContent, overwrite: true);
            if (res.Ok)
            {
                ShowMessage(InfoBarSeverity.Success,
                    "CloudSync_OverwriteSuccessTitle".GetLocalized(),
                    string.Format("CloudSync_MsgOverwritten".GetLocalized(), item.Slot));
                await RefreshArchivesInternalAsync(silent: true);
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_OverwriteFailedTitle".GetLocalized(),
                    _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] Overwrite failed: {ex.Message}");
            ShowMessage(InfoBarSeverity.Error, "CloudSync_ReadFileFailedTitle".GetLocalized(),
                "CloudSync_ReadFileFailedTitle".GetLocalized());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnRenameArchiveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CloudArchiveItem item }) return;

        var newNameBox = new TextBox
        {
            Header = "CloudSync_RenameNewHeader".GetLocalized(),
            PlaceholderText = "CloudSync_ArchiveNamePlaceholder".GetLocalized(),
            Text = item.Slot,
            MaxLength = 64
        };
        var overwriteCheck = new CheckBox
        {
            Content = "CloudSync_RenameOverwriteCheck".GetLocalized(),
            IsChecked = false
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format("CloudSync_RenameOldName".GetLocalized(), item.Slot),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });
        panel.Children.Add(newNameBox);
        panel.Children.Add(overwriteCheck);

        var dialog = new ContentDialog
        {
            Title = "CloudSync_RenameTitle".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "CloudSync_RenameConfirmBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var newSlot = newNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(newSlot) || newSlot == item.Slot) return;

        SetBusy(true);
        try
        {
            var res = await _cloudSync.RenameArchiveAsync(item.DataType, item.Slot, newSlot,
                overwriteCheck.IsChecked == true);
            if (res.Ok)
            {
                ShowMessage(InfoBarSeverity.Success,
                    "CloudSync_RenameSuccessTitle".GetLocalized(),
                    string.Format("CloudSync_MsgRenameSuccess".GetLocalized(), newSlot));
                await RefreshArchivesInternalAsync(silent: true);
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_RenameFailedTitle".GetLocalized(),
                    _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDeleteSingleArchiveClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CloudArchiveItem item }) return;

        var dialog = new ContentDialog
        {
            Title = "CloudSync_DeleteArchiveTitle".GetLocalized(),
            Content = string.Format("CloudSync_DeleteArchiveMsg".GetLocalized(), item.Slot),
            PrimaryButtonText = "CloudSync_DeleteConfirmBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            var res = await _cloudSync.DeleteArchiveAsync(item.DataType, item.Slot);
            if (res.Ok)
            {
                ShowMessage(InfoBarSeverity.Success,
                    "CloudSync_DeleteSuccessTitle".GetLocalized(),
                    string.Format("CloudSync_MsgDeleteSuccess".GetLocalized(), item.Slot));
                await RefreshArchivesInternalAsync(silent: true);
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_DeleteFailedTitle".GetLocalized(),
                    _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnDeleteAllDataClick(object sender, RoutedEventArgs e)
    {
        if (!_cloudSync.IsLoggedIn || _cloudSync.IsLocked)
        {
            ShowMessage(InfoBarSeverity.Warning,
                "CloudSync_CannotOperateTitle".GetLocalized(),
                (_cloudSync.IsLocked ? "CloudSync_ErrLocked" : "CloudSync_MsgLoginRequiredFirst").GetLocalized());
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "CloudSync_DeleteAllTitle".GetLocalized(),
            Content = "CloudSync_DeleteAllMsg".GetLocalized(),
            PrimaryButtonText = "CloudSync_DeleteAllConfirmBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            var res = await _cloudSync.DeleteAllDataAsync();
            if (res.Ok)
            {
                ShowMessage(InfoBarSeverity.Success,
                    "CloudSync_DeleteAllSuccessTitle".GetLocalized(),
                    string.Format("CloudSync_MsgDeleteAllSuccess".GetLocalized(), res.Data?.DeletedFiles ?? 0));
                await RefreshArchivesInternalAsync(silent: true);
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_DeleteAllFailedTitle".GetLocalized(),
                    _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
            }
        }
        finally
        {
            SetBusy(false);
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

    private static double CalcPercent(long used, long limit)
    {
        if (limit <= 0) return 0;
        return Math.Clamp((double)used / limit * 100.0, 0, 100);
    }

    private static string DetectArchiveType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
                {
                    if (info.TryGetProperty("uiaf_version", out _)) return "uiaf";
                    if (info.TryGetProperty("version", out _)) return "uigf";
                    if (info.TryGetProperty("playtime_version", out _)) return "playtime";
                }

                if (doc.RootElement.TryGetProperty("storage_key", out var sk) &&
                    sk.ValueKind == JsonValueKind.String &&
                    string.Equals(sk.GetString(), "fufu.game-play-time", StringComparison.Ordinal))
                {
                    return "playtime";
                }

                if (doc.RootElement.TryGetProperty("segments", out _) &&
                    doc.RootElement.TryGetProperty("daily_seconds", out _))
                {
                    return "playtime";
                }
            }
        }
        catch
        {
        }

        return "uiaf";
    }

    private static string? TryPreparePlayTimeUploadJson(string rawJson)
    {
        var appVersion = !string.IsNullOrWhiteSpace(AppVersionHelper.NumericVersion)
            ? AppVersionHelper.NumericVersion
            : "1.7.2.3";
        if (appVersion.Length > 64) appVersion = appVersion[..64];
        return GamePlayTimeStore.TryConvertPlayTimeJsonToCloudJson(rawJson, appVersion);
    }
}
