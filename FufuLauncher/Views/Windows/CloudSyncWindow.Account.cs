/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow
{
    private void RefreshAccountUiState()
    {
        if (_closed) return;
        var loggedIn = _cloudSync.IsLoggedIn;
        if (loggedIn)
        {
            MaskAuthPasswords();
            DismissAuthOtpFeedback();
        }
        var authPanels = new UIElement[] { UnauthPanel, LoggedInAccountPanel };
        var targetIndex = loggedIn ? 1 : 0;
        var targetPanel = authPanels[targetIndex];
        var direction = loggedIn ? 1 : -1;
        var animateAccountAuth = _isWindowContentLoaded &&
                                 AccountTabPanel?.Visibility == Visibility.Visible &&
                                 targetPanel.Visibility != Visibility.Visible;
        SwitchSlidePanel(SlideChannel.AccountAuth, authPanels, targetIndex, direction, ContentViewport, animateAccountAuth);
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
            RefreshHutaoUiState();
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

        RefreshHutaoUiState();
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
}
