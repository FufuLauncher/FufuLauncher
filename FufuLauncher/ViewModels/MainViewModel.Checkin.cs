/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.ViewModels;

public partial class MainViewModel
{
    #region 游戏签到

    private bool _hasAttemptedAutoCheckin = false;
    private bool _isInternationalAccount = false;
    private int _checkinStatusVersion;

    [ObservableProperty] private string _checkinStatusText = "Checkin_LoadingStatus".GetLocalized();
    [ObservableProperty] private bool _isCheckinButtonEnabled = true;
    [ObservableProperty] private string _checkinButtonText = "Checkin_SignNow".GetLocalized();
    [ObservableProperty] private string _checkinSummary = "";

    [ObservableProperty] private string _checkinStateGlyph = "\uE730";
    [ObservableProperty] private SolidColorBrush _checkinStateBrush = new(Microsoft.UI.Colors.Gray);
    [ObservableProperty] private string _checkinStateTooltip = "\u6E38\u620F\u7B7E\u5230\u72B6\u6001\u52A0\u8F7D\u4E2D";

    public IAsyncRelayCommand ExecuteCheckinCommand
    {
        get;
    }

    private void UpdateCheckinIconState(string statusText)
    {
        bool isSigned = !string.IsNullOrEmpty(statusText) &&
                        (statusText.Contains("成功") || statusText.Contains("已"));

        if (isSigned)
        {
            CheckinStateGlyph = "";
            CheckinStateBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGreen);
            CheckinStateTooltip = "Checkin_Signed".GetLocalized();
        }
        else
        {
            CheckinStateGlyph = "";
            CheckinStateBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.8 };
            CheckinStateTooltip = "Checkin_Unsigned".GetLocalized();
        }

        IsCheckinButtonEnabled = true;
        CheckinButtonText = "Checkin_SignNow".GetLocalized();
    }

    private async Task LoadCheckinStatusAsync()
    {
        if (_localSettingsService == null) return;
        var version = ++_checkinStatusVersion;
        CheckinStatusText = "Checkin_LoadingStatus".GetLocalized();
        CheckinSummary = "";
        CheckinStateGlyph = "\uE730";
        CheckinStateBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);

        var isIntlRaw = await _localSettingsService.ReadSettingAsync("IsInternationalAccount");
        _isInternationalAccount = isIntlRaw != null && isIntlRaw.ToString().ToLower() == "true";

        try
        {
            var roleService = App.GetService<GameRoleService>();
            var accountId = App.GetService<AccountManager>().ActiveAccountId;
            // 自动签到先按社区账号执行，避免原神角色加载失败阻断其他游戏。
            await TryAutoCheckinAsync(version, accountId);
            if (version != _checkinStatusVersion) return;
            var selected = await roleService.GetCurrentAsync();
            if (version != _checkinStatusVersion) return;
            if (selected == null)
            {
                CheckinStatusText = "Checkin_NotLoggedIn".GetLocalized();
                CheckinSummary = "Checkin_PleaseLogin".GetLocalized();
                UpdateCheckinIconState("Fail");
                return;
            }

            var (status, summary) = await _checkinService.GetCheckinStatusAsync(
                selected.Role.game_uid, selected.Cookies, selected.ServerType);
            if (version != _checkinStatusVersion || !roleService.IsCurrent(selected)) return;

            CheckinStatusText = status;
            CheckinSummary = summary;
            UpdateCheckinIconState(status);

        }
        catch (Exception ex)
        {
            if (version != _checkinStatusVersion) return;
            CheckinStatusText = "Checkin_LoadFailed".GetLocalized();
            CheckinSummary = ex.Message;
            UpdateCheckinIconState("Fail");
        }
    }

    private async Task TryAutoCheckinAsync(int version, string? accountId)
    {
        if (_hasAttemptedAutoCheckin || accountId == null) return;
        var enabled = await _localSettingsService.ReadSettingAsync("IsAutoCheckinEnabled");
        if (version != _checkinStatusVersion ||
            App.GetService<AccountManager>().ActiveAccountId != accountId ||
            !bool.TryParse(enabled?.ToString(), out bool isEnabled) || !isEnabled) return;
        // 原神已签或未绑定原神，都不能代表其他游戏已完成签到。
        _hasAttemptedAutoCheckin = true;
        await ExecuteCheckinAsync();
    }

    private async Task ExecuteCheckinAsync()
    {
        IsCheckinButtonEnabled = false;
        CheckinButtonText = "Checkin_CheckingIn".GetLocalized();
        CheckinStatusText = "Checkin_CheckingIn".GetLocalized();
        CheckinSummary = "Checkin_Executing".GetLocalized();

        //await RefreshSettingsAsync();

        try
        {
            var progress = new Progress<string>(msg =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    CheckinButtonText = "Checkin_CheckingIn".GetLocalized();
                    CheckinSummary = msg;
                });
            });

            var unifiedResult = await _unifiedCheckinService.ExecuteAllCheckinsAsync(progress);

            CheckinStatusText = unifiedResult.OverallSuccess
                ? "Checkin_Complete".GetLocalized()
                : "Checkin_PartialFailed".GetLocalized();
            CheckinSummary = unifiedResult.SummaryMessage;
            UpdateCheckinIconState(unifiedResult.OverallSuccess ? "已签到" : "Fail");

            var notificationTitle = unifiedResult.NotificationType switch
            {
                NotificationType.Success => "Checkin_Complete".GetLocalized(),
                NotificationType.Warning => "Checkin_PartialFailed".GetLocalized(),
                _ => "Account_CheckinFailed".GetLocalized()
            };
            _notificationService.Show(notificationTitle, unifiedResult.GetDetailedSummary(),
                unifiedResult.NotificationType, 5000);
        }
        catch (Exception ex)
        {
            CheckinStatusText = "Checkin_ExecuteFailed".GetLocalized();
            CheckinSummary = ex.Message;
            UpdateCheckinIconState("Fail");
            _notificationService.Show("Account_CheckinException".GetLocalized(), ex.Message, NotificationType.Error,
                3000);
        }
        finally
        {
            await Task.Delay(2000);
            await LoadCheckinStatusAsync();
        }
    }

    #endregion
}
