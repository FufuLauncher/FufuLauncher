/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.CycleChallenge;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow
{
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
            var isAllFilter = string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase);
            if (isAllFilter)
            {
                _fufuUigfSlots.Clear();
                _fufuChallengeSlots.Clear();
            }

            foreach (var item in res.Data.Archives)
            {
                _archives.Add(item);
                if (!isAllFilter) continue;

                if (item.IsUigf)
                {
                    _fufuUigfSlots.Add(item.Slot);
                }
                else if (item.IsChallenge)
                {
                    _fufuChallengeSlots.Add(item.Slot);
                }
            }

            if (isAllFilter)
            {
                SyncHutaoArchivePickers();
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
                "CloudSync_UploadTypePlayTimeOption".GetLocalized(),
                "CloudSync_UploadTypeChallengeOption".GetLocalized()
            },
            SelectedIndex = detectedType switch
            {
                "uigf" => 1,
                "playtime" => 2,
                "challenge" => 3,
                _ => 0
            }
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

        var dataType = typeCombo.SelectedIndex switch
        {
            1 => "uigf",
            2 => "playtime",
            3 => "challenge",
            _ => "uiaf"
        };
        var slot = slotBox.Text.Trim();
        var overwrite = overwriteCheck.IsChecked == true;

        var uploadContent = TryPrepareArchiveUploadJson(dataType, jsonContent);
        if (uploadContent == null)
        {
            ShowMessage(InfoBarSeverity.Error,
                "CloudSync_MsgUploadFailedTitle".GetLocalized(),
                "CloudSync_ErrInvalidFormat".GetLocalized());
            return;
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

    private async void OnMergeArchiveToLocalClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CloudArchiveItem item } || !item.CanMergeToLocal) return;
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

        if (item.IsChallenge)
        {
            try
            {
                var challengeService = App.GetService<CycleChallengeService>();
                var merge = await challengeService.MergeCloudChallengeJsonAsync(pullRes.JsonContent);
                if (!merge.Ok)
                {
                    ShowMessage(InfoBarSeverity.Error,
                        "CloudSync_CycleChallengeMergeFailedTitle".GetLocalized(),
                        "CloudSync_ErrInvalidFormat".GetLocalized());
                    return;
                }

                ShowMessage(InfoBarSeverity.Success,
                    "CloudSync_CycleChallengeMergeSuccessTitle".GetLocalized(),
                    CycleChallengeService.FormatMergeSummary(item.Slot, merge));
            }
            catch (Exception ex)
            {
                DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] Merge Challenge failed: {ex.Message}");
                ShowMessage(InfoBarSeverity.Error,
                    "CloudSync_CycleChallengeMergeFailedTitle".GetLocalized(),
                    "CloudSync_ErrInvalidFormat".GetLocalized());
            }

            return;
        }

        if (item.IsUiaf)
        {
            await MergeUiafArchiveToLocalAsync(item.Slot, pullRes.JsonContent);
            return;
        }

        if (item.IsUigf)
        {
            await MergeUigfArchiveToLocalAsync(item.Slot, pullRes.JsonContent);
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
            var uploadContent = TryPrepareArchiveUploadJson(item.DataType, jsonContent);
            if (uploadContent == null)
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_OverwriteFailedTitle".GetLocalized(),
                    "CloudSync_ErrInvalidFormat".GetLocalized());
                return;
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
                    if (info.TryGetProperty("challenge_version", out _)) return "challenge";
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

    private static string? TryPrepareArchiveUploadJson(string dataType, string rawJson)
    {
        var appVersion = !string.IsNullOrWhiteSpace(AppVersionHelper.NumericVersion)
            ? AppVersionHelper.NumericVersion
            : "1.7.2.5";
        if (appVersion.Length > 64) appVersion = appVersion[..64];

        return dataType switch
        {
            "playtime" => GamePlayTimeStore.TryConvertPlayTimeJsonToCloudJson(rawJson, appVersion),
            "challenge" => CycleChallengeService.TryConvertChallengeJsonToCloudJson(rawJson, appVersion),
            _ => rawJson
        };
    }
}