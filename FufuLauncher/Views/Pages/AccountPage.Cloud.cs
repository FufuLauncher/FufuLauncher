/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class AccountPage
{
    private async void OnPlayTimeCloudSyncClicked(object sender, RoutedEventArgs e)
    {
        var cloudSync = App.GetService<ICloudSyncService>();
        if (!cloudSync.IsLoggedIn)
        {
            var loginPrompt = new ContentDialog
            {
                Title = "CloudSync_NotSignedInDialogTitle".GetLocalized(),
                Content = "CloudSync_PlayTimeNotSignedInMsg".GetLocalized(),
                PrimaryButtonText = "CloudSync_OpenCenterBtn".GetLocalized(),
                CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await loginPrompt.ShowAsync() == ContentDialogResult.Primary)
            {
                CloudSyncWindow.ShowOrActivate("Account");
            }
            return;
        }

        await cloudSync.EnsureAuthenticatedAsync();
        await cloudSync.GetAccountStatusAsync();

        if (cloudSync.IsLocked)
        {
            var lockDialog = new ContentDialog
            {
                Title = "CloudSync_LockedBannerTitle".GetLocalized(),
                Content = "CloudSync_ErrLocked".GetLocalized(),
                PrimaryButtonText = "CloudSync_GotoUnlockBtn".GetLocalized(),
                CloseButtonText = "CloudSync_CloseBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            if (await lockDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                CloudSyncWindow.ShowOrActivate("Account");
            }
            return;
        }

        await ShowCloudPlayTimeManagerDialogAsync(cloudSync);
    }

    private async Task ShowCloudPlayTimeManagerDialogAsync(ICloudSyncService cloudSync)
    {
        var playTimeStore = App.GetService<GamePlayTimeStore>();
        await playTimeStore.EnsureInitializedAsync();

        var hostWidth = XamlRoot?.Size.Width ?? 0.0;
        var hostHeight = XamlRoot?.Size.Height ?? 0.0;
        double dialogWidth = hostWidth > 0 ? Math.Max(480.0, Math.Min(760.0, hostWidth - 64.0)) : 700.0;
        double dialogMinWidth = Math.Min(640.0, dialogWidth);
        double innerMaxWidth = Math.Max(420.0, dialogWidth - 48.0);
        double listMaxHeight = hostHeight > 0 ? Math.Max(180.0, Math.Min(320.0, hostHeight - 420.0)) : 280.0;

        var feedbackBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var localSummaryText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        async Task UpdateLocalSummaryTextAsync()
        {
            var summary = await playTimeStore.GetSummaryAsync();
            localSummaryText.Text = string.Format(
                "CloudSync_PlayTimeLocalSummaryFormat".GetLocalized(),
                summary.TotalDays,
                GamePlayTimeStore.FormatSecondsReadable(summary.TotalSeconds),
                summary.SegmentCount);
        }

        await UpdateLocalSummaryTextAsync();

        var mergeTipText = new TextBlock
        {
            Text = "CloudSync_PlayTimeMergeTip".GetLocalized(),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        var slotBox = new TextBox
        {
            Header = "CloudSync_ArchiveNameHeader".GetLocalized(),
            Text = "playtime",
            MaxLength = 64,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "CloudSync_ArchiveNamePlaceholder".GetLocalized()
        };

        var overwriteCheck = new CheckBox
        {
            Content = "CloudSync_OverwriteExistingCheck".GetLocalized(),
            IsChecked = true
        };

        var uploadBtn = new Button
        {
            Content = "CloudSync_PlayTimeUploadBtn".GetLocalized(),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var quotaSummaryText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "CloudSync_LoadingArchivesText".GetLocalized(),
            VerticalAlignment = VerticalAlignment.Center
        };

        var listPanel = new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var listScroll = new ScrollViewer
        {
            Content = listPanel,
            MaxHeight = listMaxHeight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var refreshBtn = new Button { Content = "CloudSync_RefreshListBtn".GetLocalized() };
        var openCenterBtn = new Button { Content = "CloudSync_OpenCenterBtn".GetLocalized() };

        var headerRow = new Grid
        {
            ColumnSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(quotaSummaryText, 0);
        Grid.SetColumn(refreshBtn, 1);
        Grid.SetColumn(openCenterBtn, 2);
        headerRow.Children.Add(quotaSummaryText);
        headerRow.Children.Add(refreshBtn);
        headerRow.Children.Add(openCenterBtn);

        var rootPanel = new StackPanel
        {
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxWidth = innerMaxWidth
        };
        rootPanel.Children.Add(feedbackBar);
        rootPanel.Children.Add(localSummaryText);
        rootPanel.Children.Add(mergeTipText);
        rootPanel.Children.Add(slotBox);
        rootPanel.Children.Add(overwriteCheck);
        rootPanel.Children.Add(uploadBtn);
        rootPanel.Children.Add(new Border
        {
            Height = 1,
            Background = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
            Margin = new Thickness(0, 4, 0, 2)
        });
        rootPanel.Children.Add(headerRow);
        rootPanel.Children.Add(listScroll);

        var dialog = new ContentDialog
        {
            Title = "CloudSync_PlayTimeTitle".GetLocalized(),
            Content = rootPanel,
            CloseButtonText = "CloudSync_CloseBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            MaxWidth = dialogWidth,
            XamlRoot = XamlRoot
        };
        dialog.Resources["ContentDialogMinWidth"] = dialogMinWidth;
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;

        void ShowFeedback(InfoBarSeverity severity, string title, string msg)
        {
            feedbackBar.Severity = severity;
            feedbackBar.Title = title;
            feedbackBar.Message = msg;
            feedbackBar.IsOpen = true;
        }

        string GetAppVersionString()
        {
            var appVersion = !string.IsNullOrWhiteSpace(AppVersionHelper.NumericVersion)
                ? AppVersionHelper.NumericVersion
                : "1.7.2.3";
            return appVersion.Length > 64 ? appVersion[..64] : appVersion;
        }

        bool isWorking = false;

        async Task LoadCloudListAsync()
        {
            uploadBtn.IsEnabled = false;
            refreshBtn.IsEnabled = false;
            listPanel.Children.Clear();

            var listRes = await cloudSync.ListArchivesAsync("playtime");
            uploadBtn.IsEnabled = true;
            refreshBtn.IsEnabled = true;

            if (!listRes.Ok || listRes.Data == null)
            {
                quotaSummaryText.Text = "CloudSync_ArchivesLoadFailedTitle".GetLocalized();
                ShowFeedback(listRes.IsAccountLocked ? InfoBarSeverity.Warning : InfoBarSeverity.Error,
                    "CloudSync_ArchivesLoadFailedTitle".GetLocalized(),
                    cloudSync.FormatFriendlyError(listRes.Code, listRes.Message, listRes.HttpStatusCode));
                return;
            }

            quotaSummaryText.Text = string.Format(
                "CloudSync_PlayTimeListSummary".GetLocalized(),
                listRes.Data.Archives.Count,
                listRes.Data.FileCount,
                listRes.Data.FileCountLimit,
                CloudArchiveItem.FormatBytes(listRes.Data.R2BytesUsed),
                CloudArchiveItem.FormatBytes(listRes.Data.R2BytesLimit));

            if (listRes.Data.Archives.Count == 0)
            {
                listPanel.Children.Add(new TextBlock
                {
                    Text = "CloudSync_PlayTimeEmptyCloud".GetLocalized(),
                    FontSize = 12,
                    Opacity = 0.65,
                    Margin = new Thickness(0, 12, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            foreach (var archive in listRes.Data.Archives)
            {
                var card = new Border
                {
                    Padding = new Thickness(12, 10, 12, 10),
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                    BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
                };

                var cardStack = new StackPanel { Spacing = 8 };

                var topRow = new Grid { ColumnSpacing = 12 };
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var slotTitle = new TextBlock
                {
                    Text = archive.Slot,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ToolTipService.SetToolTip(slotTitle, archive.Slot);

                var metaText = new TextBlock
                {
                    Text = $"{archive.SizeSummary} · {archive.UpdatedAtLocalText}",
                    FontSize = 12,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    VerticalAlignment = VerticalAlignment.Center
                };

                Grid.SetColumn(slotTitle, 0);
                Grid.SetColumn(metaText, 1);
                topRow.Children.Add(slotTitle);
                topRow.Children.Add(metaText);

                var btnStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var mergeImportBtn = new Button
                {
                    Content = "CloudSync_PlayTimeMergeImportBtn".GetLocalized(),
                    Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                    FontSize = 12,
                    Padding = new Thickness(10, 4, 10, 4)
                };
                var overwriteBtn = new Button
                {
                    Content = "CloudSync_BtnOverwrite".GetLocalized(),
                    FontSize = 12,
                    Padding = new Thickness(10, 4, 10, 4)
                };
                var renameBtn = new Button
                {
                    Content = "CloudSync_BtnRename".GetLocalized(),
                    FontSize = 12,
                    Padding = new Thickness(10, 4, 10, 4)
                };
                var deleteBtn = new Button
                {
                    Content = "CloudSync_BtnDelete".GetLocalized(),
                    FontSize = 12,
                    Padding = new Thickness(10, 4, 10, 4),
                    Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                };

                mergeImportBtn.Click += async (_, _) =>
                {
                    if (isWorking) return;
                    isWorking = true;
                    mergeImportBtn.IsEnabled = false;
                    try
                    {
                        var pullRes = await cloudSync.PullArchiveAsync("playtime", archive.Slot);
                        if (!pullRes.Ok)
                        {
                            ShowFeedback(InfoBarSeverity.Error,
                                "CloudSync_DownloadFailedTitle".GetLocalized(),
                                cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
                            return;
                        }

                        var merge = await playTimeStore.MergeCloudPlayTimeJsonAsync(pullRes.JsonContent);
                        if (!merge.Ok)
                        {
                            ShowFeedback(InfoBarSeverity.Error,
                                "CloudSync_PlayTimeMergeFailedTitle".GetLocalized(),
                                "CloudSync_ErrInvalidFormat".GetLocalized());
                            return;
                        }

                        await UpdateLocalSummaryTextAsync();
                        ShowFeedback(InfoBarSeverity.Success,
                            "CloudSync_PlayTimeMergeSuccessTitle".GetLocalized(),
                            GamePlayTimeStore.FormatMergeSummary(archive.Slot, merge));
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("AccountPage", $"[CloudSync] PlayTime merge failed: {ex.Message}");
                        ShowFeedback(InfoBarSeverity.Error,
                            "CloudSync_PlayTimeMergeFailedTitle".GetLocalized(),
                            "CloudSync_ErrInvalidFormat".GetLocalized());
                    }
                    finally
                    {
                        mergeImportBtn.IsEnabled = true;
                        isWorking = false;
                    }
                };

                overwriteBtn.Click += (_, _) =>
                {
                    var confirmFlyoutPanel = new StackPanel { Spacing = 8, MaxWidth = 240 };
                    confirmFlyoutPanel.Children.Add(new TextBlock
                    {
                        Text = string.Format("CloudSync_PlayTimeConfirmOverwriteMsg".GetLocalized(), archive.Slot),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12
                    });
                    var confirmOverwriteBtn = new Button
                    {
                        Content = "CloudSync_ConfirmOverwriteBtn".GetLocalized(),
                        Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    confirmFlyoutPanel.Children.Add(confirmOverwriteBtn);
                    var flyout = new Flyout { Content = confirmFlyoutPanel };

                    confirmOverwriteBtn.Click += async (_, _) =>
                    {
                        flyout.Hide();
                        if (isWorking) return;
                        isWorking = true;
                        try
                        {
                            var json = await playTimeStore.BuildCloudPlayTimeJsonAsync(GetAppVersionString());
                            var res = await cloudSync.UploadArchiveAsync("playtime", archive.Slot, json, overwrite: true);
                            if (res.Ok)
                            {
                                ShowFeedback(InfoBarSeverity.Success,
                                    "CloudSync_OverwriteSuccessTitle".GetLocalized(),
                                    string.Format("CloudSync_MsgOverwritten".GetLocalized(), archive.Slot));
                                await LoadCloudListAsync();
                            }
                            else
                            {
                                ShowFeedback(InfoBarSeverity.Error,
                                    "CloudSync_OverwriteFailedTitle".GetLocalized(),
                                    cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
                            }
                        }
                        finally
                        {
                            isWorking = false;
                        }
                    };

                    flyout.ShowAt(overwriteBtn);
                };

                renameBtn.Click += (_, _) =>
                {
                    var newBox = new TextBox
                    {
                        Text = archive.Slot,
                        MaxLength = 64,
                        Width = 220,
                        Header = "CloudSync_RenameNewHeader".GetLocalized(),
                        PlaceholderText = "CloudSync_ArchiveNamePlaceholder".GetLocalized()
                    };
                    var doRenameBtn = new Button
                    {
                        Content = "CloudSync_RenameConfirmBtn".GetLocalized(),
                        Style = (Style)Application.Current.Resources["AccentButtonStyle"],
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    var renamePanel = new StackPanel { Spacing = 8, Children = { newBox, doRenameBtn } };
                    var flyout = new Flyout { Content = renamePanel };

                    doRenameBtn.Click += async (_, _) =>
                    {
                        var nextName = newBox.Text.Trim();
                        flyout.Hide();
                        if (string.IsNullOrWhiteSpace(nextName) || nextName == archive.Slot) return;

                        var res = await cloudSync.RenameArchiveAsync("playtime", archive.Slot, nextName, overwrite: false);
                        if (res.Ok)
                        {
                            ShowFeedback(InfoBarSeverity.Success,
                                "CloudSync_RenameSuccessTitle".GetLocalized(),
                                string.Format("CloudSync_MsgRenameSuccess".GetLocalized(), nextName));
                            await LoadCloudListAsync();
                        }
                        else
                        {
                            ShowFeedback(InfoBarSeverity.Error,
                                "CloudSync_RenameFailedTitle".GetLocalized(),
                                cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
                        }
                    };

                    flyout.ShowAt(renameBtn);
                };

                deleteBtn.Click += (_, _) =>
                {
                    var confirmDelPanel = new StackPanel { Spacing = 8, MaxWidth = 240 };
                    confirmDelPanel.Children.Add(new TextBlock
                    {
                        Text = string.Format("CloudSync_DeleteArchiveMsg".GetLocalized(), archive.Slot),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12
                    });
                    var confirmDelBtn = new Button
                    {
                        Content = "CloudSync_DeleteConfirmBtn".GetLocalized(),
                        Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    confirmDelPanel.Children.Add(confirmDelBtn);
                    var flyout = new Flyout { Content = confirmDelPanel };

                    confirmDelBtn.Click += async (_, _) =>
                    {
                        flyout.Hide();
                        var res = await cloudSync.DeleteArchiveAsync("playtime", archive.Slot);
                        if (res.Ok)
                        {
                            ShowFeedback(InfoBarSeverity.Success,
                                "CloudSync_DeleteSuccessTitle".GetLocalized(),
                                string.Format("CloudSync_MsgDeleteSuccess".GetLocalized(), archive.Slot));
                            await LoadCloudListAsync();
                        }
                        else
                        {
                            ShowFeedback(InfoBarSeverity.Error,
                                "CloudSync_DeleteFailedTitle".GetLocalized(),
                                cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
                        }
                    };

                    flyout.ShowAt(deleteBtn);
                };

                btnStack.Children.Add(mergeImportBtn);
                btnStack.Children.Add(overwriteBtn);
                btnStack.Children.Add(renameBtn);
                btnStack.Children.Add(deleteBtn);

                cardStack.Children.Add(topRow);
                cardStack.Children.Add(btnStack);
                card.Child = cardStack;
                listPanel.Children.Add(card);
            }
        }

        uploadBtn.Click += async (_, _) =>
        {
            if (isWorking) return;

            var slot = slotBox.Text.Trim();
            if (!cloudSync.IsValidSlotName(slot))
            {
                ShowFeedback(InfoBarSeverity.Warning,
                    "CloudSync_HintTitle".GetLocalized(),
                    cloudSync.FormatFriendlyError("INVALID_DATA_SLOT", null, 400));
                return;
            }

            isWorking = true;
            uploadBtn.IsEnabled = false;
            try
            {
                var json = await playTimeStore.BuildCloudPlayTimeJsonAsync(GetAppVersionString());
                var res = await cloudSync.UploadArchiveAsync("playtime", slot, json, overwriteCheck.IsChecked == true);
                if (res.Ok)
                {
                    var msgKey = res.Data?.Overwritten == true ? "CloudSync_MsgOverwritten" : "CloudSync_MsgUploadedNew";
                    ShowFeedback(InfoBarSeverity.Success,
                        "CloudSync_MsgUploadSuccess".GetLocalized(),
                        string.Format(msgKey.GetLocalized(), slot));
                    await LoadCloudListAsync();
                }
                else
                {
                    ShowFeedback(InfoBarSeverity.Error,
                        "CloudSync_MsgUploadFailedTitle".GetLocalized(),
                        cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
                }
            }
            finally
            {
                uploadBtn.IsEnabled = true;
                isWorking = false;
            }
        };

        refreshBtn.Click += async (_, _) => await LoadCloudListAsync();
        openCenterBtn.Click += (_, _) => CloudSyncWindow.ShowOrActivate("Archives");

        _ = LoadCloudListAsync();
        await dialog.ShowAsync();
    }
}
