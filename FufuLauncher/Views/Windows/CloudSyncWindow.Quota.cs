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

    private static double CalcPercent(long used, long limit)
    {
        if (limit <= 0) return 0;
        return Math.Clamp((double)used / limit * 100.0, 0, 100);
    }
}