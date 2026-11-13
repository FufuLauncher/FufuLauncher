/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class GachaAnalysisWindow
{
    private async void OnCloudArchiveManageClick(object sender, RoutedEventArgs e)
    {
        var cloudSync = App.GetService<ICloudSyncService>();
        if (!cloudSync.IsLoggedIn)
        {
            var loginPrompt = new ContentDialog
            {
                Title = "CloudSync_NotSignedInDialogTitle".GetLocalized(),
                Content = "CloudSync_GachaNotSignedInMsg".GetLocalized(),
                PrimaryButtonText = "CloudSync_OpenCenterBtn".GetLocalized(),
                CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };
            if (await loginPrompt.ShowAsync() == ContentDialogResult.Primary)
            {
                CloudSyncWindow.ShowOrActivate("Account");
            }

            return;
        }

        if (ViewModel.IsFetching)
        {
            await new ContentDialog
            {
                Title = "CloudSync_HintTitle".GetLocalized(),
                Content = "CloudSync_BusyWaitMsg".GetLocalized(),
                CloseButtonText = "CloudSync_CloseBtn".GetLocalized(),
                XamlRoot = Content.XamlRoot
            }.ShowAsync();
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
                XamlRoot = Content.XamlRoot
            };
            if (await lockDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                CloudSyncWindow.ShowOrActivate("Account");
            }

            return;
        }

        await ShowCloudGachaManagerDialogAsync(cloudSync);
    }

    private sealed class GachaUploadSourceOption
    {
        public string Label
        {
            get;
            init;
        } = string.Empty;

        public List<string> Uids
        {
            get;
            init;
        } = new();

        public string SuggestedSlot
        {
            get;
            init;
        } = string.Empty;
    }

    private async Task ShowCloudGachaManagerDialogAsync(ICloudSyncService cloudSync)
    {
        var localUids = ViewModel.GetAvailableLocalUids();
        var options = new List<GachaUploadSourceOption>();

        if (localUids.Count > 1)
        {
            options.Add(new GachaUploadSourceOption
            {
                Label = string.Format("CloudSync_GachaAllUidsOption".GetLocalized(), localUids.Count),
                Uids = localUids,
                SuggestedSlot = "wishes_all"
            });
        }

        foreach (var uid in localUids)
        {
            var count = ViewModel.GetLocalUidRecordCount(uid);
            options.Add(new GachaUploadSourceOption
            {
                Label = string.Format("CloudSync_GachaUidOptionFormat".GetLocalized(), uid, count),
                Uids = new List<string> { uid },
                SuggestedSlot = cloudSync.SuggestValidSlot($"uid_{uid}", "wishes")
            });
        }

        var defaultOptionIndex = 0;
        if (options.Count > 1 && !string.IsNullOrWhiteSpace(ViewModel.SelectedUid))
        {
            var matchIdx = options.FindIndex(o => o.Uids.Count == 1 && o.Uids[0] == ViewModel.SelectedUid);
            if (matchIdx >= 0) defaultOptionIndex = matchIdx;
        }

        var hostWidth = Content.XamlRoot?.Size.Width ?? 0.0;
        var hostHeight = Content.XamlRoot?.Size.Height ?? 0.0;
        double dialogWidth = hostWidth > 0 ? Math.Max(480.0, Math.Min(760.0, hostWidth - 64.0)) : 740.0;
        double dialogMinWidth = Math.Min(680.0, dialogWidth);
        double innerMaxWidth = Math.Max(420.0, dialogWidth - 52.0);
        double listMaxHeight = hostHeight > 0 ? Math.Max(180.0, Math.Min(340.0, hostHeight - 400.0)) : 290.0;

        var feedbackBar = new InfoBar
        {
            IsOpen = false,
            IsClosable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var sourceCombo = new ComboBox
        {
            Header = "CloudSync_GachaSourceHeader".GetLocalized(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            DisplayMemberPath = nameof(GachaUploadSourceOption.Label),
            ItemsSource = options,
            Visibility = options.Count > 0 ? Visibility.Visible : Visibility.Collapsed
        };
        if (options.Count > 0)
        {
            sourceCombo.SelectedIndex = defaultOptionIndex;
        }

        var noLocalHint = new TextBlock
        {
            Text = "CloudSync_GachaNoLocalDataHint".GetLocalized(),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Visibility = options.Count == 0 ? Visibility.Visible : Visibility.Collapsed
        };

        var slotBox = new TextBox
        {
            Header = "CloudSync_ArchiveNameHeader".GetLocalized(),
            Text = options.Count > 0 ? options[defaultOptionIndex].SuggestedSlot : "wishes",
            MaxLength = 64,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "CloudSync_ArchiveNamePlaceholder".GetLocalized(),
            Visibility = options.Count > 0 ? Visibility.Visible : Visibility.Collapsed
        };

        var slotEditedManually = false;
        var suppressSlotChange = false;
        slotBox.TextChanged += (_, _) =>
        {
            if (!suppressSlotChange) slotEditedManually = true;
        };

        sourceCombo.SelectionChanged += (_, _) =>
        {
            if (!slotEditedManually && sourceCombo.SelectedItem is GachaUploadSourceOption opt)
            {
                suppressSlotChange = true;
                slotBox.Text = opt.SuggestedSlot;
                suppressSlotChange = false;
            }
        };

        var overwriteCheck = new CheckBox
        {
            Content = "CloudSync_OverwriteExistingCheck".GetLocalized(),
            IsChecked = true,
            Visibility = options.Count > 0 ? Visibility.Visible : Visibility.Collapsed
        };

        var uploadBtn = new Button
        {
            Content = "CloudSync_GachaUploadBtn".GetLocalized(),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
            Visibility = options.Count > 0 ? Visibility.Visible : Visibility.Collapsed
        };

        var quotaSummaryText = new TextBlock
        {
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Text = "CloudSync_LoadingArchivesText".GetLocalized()
        };

        var listPanel = new StackPanel
        {
            Spacing = 6,
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
        quotaSummaryText.VerticalAlignment = VerticalAlignment.Center;
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
        rootPanel.Children.Add(noLocalHint);
        rootPanel.Children.Add(sourceCombo);
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
            Title = "CloudSync_GachaTitle".GetLocalized(),
            Content = rootPanel,
            CloseButtonText = "CloudSync_CloseBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            MaxWidth = dialogWidth,
            XamlRoot = Content.XamlRoot
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

        string BuildSelectedUigfJsonOrShowError()
        {
            var selectedUids = (sourceCombo.SelectedItem as GachaUploadSourceOption)?.Uids;
            if (selectedUids == null || selectedUids.Count == 0)
            {
                ShowFeedback(InfoBarSeverity.Warning, "CloudSync_GachaExportErrorTitle".GetLocalized(),
                    "CloudSync_GachaNoRecordsSelected".GetLocalized());
                return string.Empty;
            }

            var json = ViewModel.BuildStrictUigfV4Json(selectedUids);
            if (string.IsNullOrWhiteSpace(json))
            {
                ShowFeedback(InfoBarSeverity.Warning, "CloudSync_GachaExportErrorTitle".GetLocalized(),
                    "CloudSync_GachaNoRecordsSelected".GetLocalized());
                return string.Empty;
            }

            return json;
        }

        async Task LoadCloudListAsync()
        {
            uploadBtn.IsEnabled = false;
            refreshBtn.IsEnabled = false;
            listPanel.Children.Clear();

            var listRes = await cloudSync.ListArchivesAsync("uigf");
            uploadBtn.IsEnabled = options.Count > 0;
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
                "CloudSync_GachaListSummary".GetLocalized(),
                listRes.Data.Archives.Count,
                listRes.Data.FileCount,
                listRes.Data.FileCountLimit,
                CloudArchiveItem.FormatBytes(listRes.Data.R2BytesUsed),
                CloudArchiveItem.FormatBytes(listRes.Data.R2BytesLimit));

            if (listRes.Data.Archives.Count == 0)
            {
                listPanel.Children.Add(new TextBlock
                {
                    Text = "CloudSync_GachaEmptyCloud".GetLocalized(),
                    FontSize = 12,
                    Opacity = 0.65,
                    Margin = new Thickness(0, 12, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                return;
            }

            foreach (var archive in listRes.Data.Archives)
            {
                var row = new Grid
                {
                    ColumnSpacing = 10,
                    Padding = new Thickness(6, 6, 6, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    BorderBrush = (Brush)Application.Current.Resources["DividerStrokeColorDefaultBrush"],
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoStack = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    MinWidth = 0
                };
                var slotTitle = new TextBlock
                {
                    Text = archive.Slot,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                ToolTipService.SetToolTip(slotTitle, archive.Slot);
                infoStack.Children.Add(slotTitle);
                infoStack.Children.Add(new TextBlock
                {
                    Text = $"{archive.SizeSummary} · {archive.UpdatedAtLocalText}",
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                });

                var btnStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var importBtn = new Button
                {
                    Content = "CloudSync_GachaImportBtn".GetLocalized(), FontSize = 12,
                    Padding = new Thickness(8, 4, 8, 4)
                };
                var overwriteBtn = new Button
                {
                    Content = "CloudSync_BtnOverwrite".GetLocalized(), FontSize = 12,
                    Padding = new Thickness(8, 4, 8, 4), IsEnabled = options.Count > 0
                };
                var renameBtn = new Button
                {
                    Content = "CloudSync_BtnRename".GetLocalized(), FontSize = 12, Padding = new Thickness(8, 4, 8, 4)
                };
                var deleteBtn = new Button
                {
                    Content = "CloudSync_BtnDelete".GetLocalized(),
                    FontSize = 12,
                    Padding = new Thickness(8, 4, 8, 4),
                    Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"]
                };

                importBtn.Click += async (_, _) =>
                {
                    importBtn.IsEnabled = false;
                    try
                    {
                        var pullRes = await cloudSync.PullArchiveAsync("uigf", archive.Slot);
                        if (!pullRes.Ok)
                        {
                            ShowFeedback(InfoBarSeverity.Error,
                                "CloudSync_DownloadFailedTitle".GetLocalized(),
                                cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
                            return;
                        }

                        var (success, accounts, records, _) =
                            await ViewModel.ImportUigfJsonFromCloudAsync(pullRes.JsonContent);
                        if (!success)
                        {
                            ShowFeedback(InfoBarSeverity.Error,
                                "CloudSync_AchievementImportFailedTitle".GetLocalized(),
                                "CloudSync_ErrInvalidFormat".GetLocalized());
                            return;
                        }

                        ShowFeedback(InfoBarSeverity.Success,
                            "CloudSync_AchievementImportSuccessTitle".GetLocalized(),
                            string.Format("CloudSync_GachaImportedMsg".GetLocalized(), archive.Slot, accounts,
                                records));
                    }
                    finally
                    {
                        importBtn.IsEnabled = true;
                    }
                };

                overwriteBtn.Click += (_, _) =>
                {
                    var confirmFlyoutPanel = new StackPanel { Spacing = 8, MaxWidth = 240 };
                    confirmFlyoutPanel.Children.Add(new TextBlock
                    {
                        Text = string.Format("CloudSync_GachaConfirmOverwriteMsg".GetLocalized(), archive.Slot),
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
                        var json = BuildSelectedUigfJsonOrShowError();
                        if (string.IsNullOrEmpty(json)) return;

                        var res = await cloudSync.UploadArchiveAsync("uigf", archive.Slot, json, overwrite: true);
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

                        var res = await cloudSync.RenameArchiveAsync("uigf", archive.Slot, nextName, overwrite: false);
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
                    var delPanel = new StackPanel { Spacing = 8, MaxWidth = 220 };
                    delPanel.Children.Add(new TextBlock
                    {
                        Text = string.Format("CloudSync_DeleteArchiveMsg".GetLocalized(), archive.Slot),
                        TextWrapping = TextWrapping.Wrap,
                        FontSize = 12
                    });
                    var confirmDelBtn = new Button
                    {
                        Content = "CloudSync_DeleteConfirmBtn".GetLocalized(),
                        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 200, 50, 50)),
                        Foreground = new SolidColorBrush(Colors.White),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    delPanel.Children.Add(confirmDelBtn);
                    var flyout = new Flyout { Content = delPanel };

                    confirmDelBtn.Click += async (_, _) =>
                    {
                        flyout.Hide();
                        var res = await cloudSync.DeleteArchiveAsync("uigf", archive.Slot);
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

                btnStack.Children.Add(importBtn);
                btnStack.Children.Add(overwriteBtn);
                btnStack.Children.Add(renameBtn);
                btnStack.Children.Add(deleteBtn);

                Grid.SetColumn(infoStack, 0);
                Grid.SetColumn(btnStack, 1);
                row.Children.Add(infoStack);
                row.Children.Add(btnStack);
                listPanel.Children.Add(row);
            }
        }

        uploadBtn.Click += async (_, _) =>
        {
            var slot = slotBox.Text.Trim();
            if (!cloudSync.IsValidSlotName(slot))
            {
                ShowFeedback(InfoBarSeverity.Warning,
                    "CloudSync_HintTitle".GetLocalized(),
                    cloudSync.FormatFriendlyError("INVALID_DATA_SLOT", null, 400));
                return;
            }

            var json = BuildSelectedUigfJsonOrShowError();
            if (string.IsNullOrEmpty(json)) return;

            uploadBtn.IsEnabled = false;
            try
            {
                var res = await cloudSync.UploadArchiveAsync("uigf", slot, json, overwriteCheck.IsChecked == true);
                if (res.Ok)
                {
                    var msgKey = res.Data?.Overwritten == true
                        ? "CloudSync_MsgOverwritten"
                        : "CloudSync_MsgUploadedNew";
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
            }
        };

        refreshBtn.Click += async (_, _) => await LoadCloudListAsync();
        openCenterBtn.Click += (_, _) => CloudSyncWindow.ShowOrActivate("Archives");

        _ = LoadCloudListAsync();
        await dialog.ShowAsync();
    }
}