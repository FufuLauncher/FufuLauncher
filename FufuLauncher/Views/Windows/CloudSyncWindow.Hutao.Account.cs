using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow
{
    private void OnHutaoAuthModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HutaoAuthModeSelector == null || HutaoPasswordInput == null || HutaoConfirmPasswordInput == null ||
            HutaoOtpRow == null || HutaoAuthSubmitButton == null)
        {
            return;
        }

        HutaoPasswordInput.MaskPassword();
        HutaoConfirmPasswordInput.MaskPassword();

        var mode = HutaoAuthModeSelector.SelectedIndex;
        if (mode == 0)
        {
            HutaoConfirmPasswordInput.Visibility = Visibility.Collapsed;
            HutaoOtpRow.Visibility = Visibility.Collapsed;
            HutaoAuthSubmitButton.Content = "HutaoSync_SubmitLogin".GetLocalized();
        }
        else if (mode == 1)
        {
            HutaoConfirmPasswordInput.Visibility = Visibility.Visible;
            HutaoOtpRow.Visibility = Visibility.Visible;
            HutaoAuthSubmitButton.Content = "HutaoSync_SubmitRegister".GetLocalized();
        }
        else
        {
            HutaoConfirmPasswordInput.Visibility = Visibility.Visible;
            HutaoOtpRow.Visibility = Visibility.Visible;
            HutaoAuthSubmitButton.Content = "HutaoSync_SubmitReset".GetLocalized();
        }
    }

    private void UpdateHutaoOtpButton()
    {
        if (_closed || HutaoSendOtpButton == null) return;

        var until = _hutaoCloud.VerifyCodeCooldownUntil;
        var remaining = until.HasValue
            ? (int)Math.Ceiling((until.Value - DateTimeOffset.UtcNow).TotalSeconds)
            : 0;

        if (remaining > 0)
        {
            HutaoSendOtpButton.IsEnabled = false;
            HutaoSendOtpButton.Content = string.Format("HutaoSync_SendOtpCooldownFormat".GetLocalized(), remaining);
            if (_hutaoOtpTimer is { IsRunning: false }) _hutaoOtpTimer.Start();
        }
        else
        {
            HutaoSendOtpButton.IsEnabled = !IsBusy;
            HutaoSendOtpButton.Content = "HutaoSync_SendOtpBtn".GetLocalized();
            if (_hutaoOtpTimer is { IsRunning: true }) _hutaoOtpTimer.Stop();
        }
    }

    private async void OnHutaoSendOtpClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        var email = HutaoEmailTextBox.Text.Trim();
        var isReset = HutaoAuthModeSelector.SelectedIndex == 2;

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.SendVerifyCodeAsync(email, isReset);
            if (res.Ok)
            {
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(), res.Message);
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
            }
        }
        finally
        {
            SetBusy(false);
            UpdateHutaoOtpButton();
        }
    }

    private async void OnHutaoAuthSubmitClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;

        var email = HutaoEmailTextBox.Text.Trim();
        var password = HutaoPasswordInput.Password;
        var confirm = HutaoConfirmPasswordInput.Password;
        var otp = HutaoOtpTextBox.Text.Trim();
        var mode = HutaoAuthModeSelector.SelectedIndex;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrEnterAccountPassword".GetLocalized());
            return;
        }

        if (mode != 0 && password != confirm)
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrPasswordMismatch".GetLocalized());
            return;
        }

        if (mode != 0 && string.IsNullOrWhiteSpace(otp))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrEnterCompleteAuth".GetLocalized());
            return;
        }

        HutaoPasswordInput.MaskPassword();
        HutaoConfirmPasswordInput.MaskPassword();

        SetBusy(true);
        try
        {
            var res = mode switch
            {
                1 => await _hutaoCloud.RegisterAsync(email, password, otp),
                2 => await _hutaoCloud.ResetPasswordAsync(email, password, otp),
                _ => await _hutaoCloud.LoginAsync(email, password)
            };

            if (res.Ok)
            {
                HutaoPasswordInput.Password = string.Empty;
                HutaoConfirmPasswordInput.Password = string.Empty;
                HutaoOtpTextBox.Text = string.Empty;
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(), res.Message);
                RefreshHutaoUiState();
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoRefreshUserInfoClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.RefreshUserInfoAsync();
            if (res.Ok)
            {
                await RefreshHutaoDevicesAsync(silent: true);
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                    "HutaoSync_MsgAccountRefreshed".GetLocalized());
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
            }
        }
        finally
        {
            SetBusy(false);
            RefreshHutaoUiState();
        }
    }

    private async void OnHutaoLogoutClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            await _hutaoCloud.LogoutAsync(revokeRemote: true);
            ShowMessage(InfoBarSeverity.Informational, "HutaoSync_SuccessTitle".GetLocalized(),
                "HutaoSync_MsgLogoutSuccess".GetLocalized());
        }
        finally
        {
            SetBusy(false);
            RefreshHutaoUiState();
        }
    }

    private async void OnHutaoRefreshDevicesClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !EnsureHutaoLoggedInForUiAction()) return;
        SetBusy(true);
        try
        {
            await RefreshHutaoDevicesAsync(silent: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshHutaoDevicesAsync(bool silent)
    {
        if (!_hutaoCloud.IsLoggedIn || _hutaoDevicesLoading) return;
        _hutaoDevicesLoading = true;
        try
        {
            var res = await _hutaoCloud.GetLoggedInDevicesAsync();
            if (_closed) return;
            if (!res.Ok)
            {
                if (!silent)
                {
                    ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                }

                return;
            }

            _hutaoDevicesLoadedForEmail = _hutaoCloud.CurrentEmail;
            _hutaoDevices.Clear();

            var unknownName = "HutaoSync_DeviceUnknownName".GetLocalized();
            var currentBadge = "HutaoSync_DeviceCurrentBadge".GetLocalized();
            var currentId = _hutaoCloud.CurrentDeviceId;

            foreach (var dev in res.Data ?? new List<HutaoLoggedInDevice>())
            {
                var devId = dev.EffectiveDeviceId;
                var devName = !string.IsNullOrWhiteSpace(dev.EffectiveDeviceName)
                    ? dev.EffectiveDeviceName
                    : unknownName;
                var os = dev.EffectiveOs;
                var isCurrent = !string.IsNullOrWhiteSpace(devId) &&
                                string.Equals(devId, currentId, StringComparison.OrdinalIgnoreCase);

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(os))
                {
                    parts.Add(os);
                }

                var formattedTime = FormatHutaoDateString(dev.EffectiveLoginTime);
                if (!string.IsNullOrWhiteSpace(formattedTime))
                {
                    parts.Add(string.Format("HutaoSync_DeviceLoginTimeFormat".GetLocalized(), formattedTime));
                }

                if (!string.IsNullOrWhiteSpace(devId))
                {
                    parts.Add(string.Format("HutaoSync_DeviceIdFormat".GetLocalized(), devId));
                }

                _hutaoDevices.Add(new HutaoDeviceListItem
                {
                    DeviceId = devId,
                    TitleText = devName,
                    SubtitleText = string.Join(" · ", parts),
                    BadgeText = isCurrent ? currentBadge : string.Empty,
                    IsCurrentDevice = isCurrent
                });
            }

            UpdateHutaoRepeaterEmptyStates();
        }
        finally
        {
            _hutaoDevicesLoading = false;
        }
    }

    private async void OnHutaoRevokeDeviceClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !EnsureHutaoLoggedInForUiAction()) return;
        if (sender is not FrameworkElement { Tag: string deviceId } || string.IsNullOrWhiteSpace(deviceId)) return;

        var matched = _hutaoDevices.FirstOrDefault(x =>
            string.Equals(x.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        var displayName = matched?.TitleText ?? deviceId;

        var dialog = new ContentDialog
        {
            Title = "HutaoSync_ConfirmRevokeDeviceTitle".GetLocalized(),
            Content = string.Format("HutaoSync_ConfirmRevokeDeviceMsgFormat".GetLocalized(), displayName),
            PrimaryButtonText = "HutaoSync_RevokeDeviceBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.RevokeDeviceAsync(deviceId);
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                "HutaoSync_MsgDeviceRevoked".GetLocalized());

            if (_hutaoCloud.IsLoggedIn)
            {
                await RefreshHutaoDevicesAsync(silent: true);
            }

            RefreshHutaoUiState();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoRevokeAllDevicesClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !EnsureHutaoLoggedInForUiAction()) return;

        var dialog = new ContentDialog
        {
            Title = "HutaoSync_ConfirmRevokeAllDevicesTitle".GetLocalized(),
            Content = "HutaoSync_ConfirmRevokeAllDevicesMsg".GetLocalized(),
            PrimaryButtonText = "HutaoSync_RevokeAllDevicesBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.RevokeAllDevicesAsync();
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            ShowMessage(InfoBarSeverity.Informational, "HutaoSync_SuccessTitle".GetLocalized(),
                "HutaoSync_MsgAllDevicesRevoked".GetLocalized());
            RefreshHutaoUiState();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnHutaoSponsorPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        if (_hutaoCloud.IsLoggedIn)
        {
            OnHutaoRefreshUserInfoClick(sender, e);
            return;
        }

        SelectHutaoSubTab("HutaoAccount");
    }

    private void OnHutaoOpenSponsorPageClick(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(ApiEndpoints.HutaoSponsorUrl));
    }

    private async void OnHutaoRedeemCodeClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !EnsureHutaoLoggedInForUiAction()) return;
        var code = HutaoRedeemCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrRedeemEmpty".GetLocalized());
            HutaoRedeemCodeBox.Focus(FocusState.Programmatic);
            return;
        }

        SetBusy(true);
        HutaoRedeemButton.IsEnabled = false;
        try
        {
            var res = await _hutaoCloud.RedeemCodeAsync(code);
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            HutaoRedeemCodeBox.Text = string.Empty;
            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(), res.Message);
            RefreshHutaoUiState();
        }
        finally
        {
            SetBusy(false);
            if (!_closed)
            {
                HutaoRedeemButton.IsEnabled = _hutaoCloud.IsLoggedIn;
            }
        }
    }

    private static string FormatHutaoRoleIdentity(HutaoUserInfo? info)
    {
        if (info == null) return "-";
        if (info.IsMaintainer) return "HutaoSync_RoleMaintainer".GetLocalized();
        if (info.IsLicensedDeveloper) return "HutaoSync_RoleDeveloper".GetLocalized();
        return "HutaoSync_RoleStandard".GetLocalized();
    }

    private static string FormatHutaoGachaPlan(HutaoUserInfo? info)
    {
        if (info == null) return "HutaoSync_PlanExpired".GetLocalized();
        if (info.IsMaintainer || info.IsLicensedDeveloper) return "HutaoSync_PlanDeveloper".GetLocalized();
        if (info.GachaLogExpireAt.HasValue && info.GachaLogExpireAt.Value > DateTimeOffset.UtcNow)
        {
            return string.Format(
                "HutaoSync_PlanActiveFormat".GetLocalized(),
                info.GachaLogExpireAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        }

        return "HutaoSync_PlanExpired".GetLocalized();
    }

    private static string FormatHutaoCdnPlan(HutaoUserInfo? info)
    {
        if (info == null) return "HutaoSync_AccelPlanExpired".GetLocalized();
        if (info.IsMaintainer || info.IsLicensedDeveloper) return "HutaoSync_PlanDeveloper".GetLocalized();
        if (info.CdnExpireAt.HasValue && info.CdnExpireAt.Value > DateTimeOffset.UtcNow)
        {
            return string.Format(
                "HutaoSync_PlanActiveFormat".GetLocalized(),
                info.CdnExpireAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        }

        return "HutaoSync_AccelPlanExpired".GetLocalized();
    }

    private void UpdateHutaoExtraFields(HutaoUserInfo? info)
    {
        _hutaoExtraFields.Clear();
        if (info?.ExtraFields != null)
        {
            foreach (var kv in info.ExtraFields)
            {
                var valueText = FormatExtraFieldValue(kv.Value);
                if (!string.IsNullOrWhiteSpace(valueText))
                {
                    _hutaoExtraFields.Add(new HutaoAccountExtraFieldItem
                    {
                        Label = kv.Key,
                        Value = valueText
                    });
                }
            }
        }

        if (HutaoExtraFieldsRepeater != null)
        {
            HutaoExtraFieldsRepeater.Visibility = _hutaoExtraFields.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private static string FormatExtraFieldValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return string.Empty;
            case JsonValueKind.True:
                return bool.TrueString;
            case JsonValueKind.False:
                return bool.FalseString;
            case JsonValueKind.String:
                var raw = element.GetString()?.Trim() ?? string.Empty;
                return FormatHutaoDateString(raw) is { Length: > 0 } dt ? dt : raw;
            default:
                return element.ToString();
        }
    }

    private static string FormatHutaoDateString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        if (DateTimeOffset.TryParse(raw, out var dto) && dto.Year > 2000)
        {
            return dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        return raw.Trim();
    }
}