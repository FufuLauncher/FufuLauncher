using System.Reflection;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services.CycleChallenge;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class CycleChallengePage
{
    private const string ChallengeArchiveDataType = "challenge";

    private async void OnCloudArchiveManageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await ShowCloudChallengeManagerDialogAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CycleChallengePage", $"[CloudSync] Failed to open challenge cloud dialog: {ex.Message}");
        }
    }

    private async Task ShowCloudChallengeManagerDialogAsync()
    {
        var cloudSync = App.GetService<ICloudSyncService>();
        if (!cloudSync.IsLoggedIn)
        {
            var loginPrompt = new ContentDialog
            {
                Title = "CloudSync_NotSignedInDialogTitle".GetLocalized(),
                Content = "CloudSync_CycleChallengeNotSignedInMsg".GetLocalized(),
                PrimaryButtonText = "CloudSync_OpenCenterBtn".GetLocalized(),
                CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme
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
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme
            };

            if (await lockDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                CloudSyncWindow.ShowOrActivate("Account");
            }

            return;
        }

        var challengeService = App.GetService<CycleChallengeService>();
        var currentRoleUid = ViewModel.GetCurrentRoleUid();
        var initialSummary = await challengeService.GetCloudSummaryAsync();

        var isWorking = false;
        var slotEditedByUser = false;
        var updatingSlotProgrammatically = false;
        var suppressSourceSelectionChanged = false;

        var hostWidth = XamlRoot?.Size.Width > 0 ? XamlRoot.Size.Width : 900;
        var dialogWidth = Math.Min(560, Math.Max(360, hostWidth - 64));
        var dialogMinWidth = Math.Min(440, dialogWidth);

        var localSummaryText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        var sourceCombo = new ComboBox
        {
            Header = "CloudSync_CycleChallengeSourceHeader".GetLocalized(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var mergeTipText = new TextBlock
        {
            Text = "CloudSync_CycleChallengeMergeTip".GetLocalized(),
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };

        var slotBox = new TextBox
        {
            Text = "cycle_challenges",
            MaxLength = 64,
            Header = "CloudSync_ArchiveNameHeader".GetLocalized(),
            PlaceholderText = "CloudSync_ArchiveNamePlaceholder".GetLocalized()
        };

        slotBox.TextChanged += (_, _) =>
        {
            if (!updatingSlotProgrammatically)
            {
                slotEditedByUser = true;
            }
        };

        var overwriteCheck = new CheckBox
        {
            Content = "CloudSync_OverwriteExistingCheck".GetLocalized(),
            IsChecked = true
        };

        var uploadBtn = new Button
        {
            Content = "CloudSync_CycleChallengeUploadBtn".GetLocalized(),
            Style = (Style)Application.Current.Resources["AccentButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var quotaSummaryText = new TextBlock
        {
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap,
            Text = "CloudSync_LoadingArchivesText".GetLocalized()
        };

        var refreshBtn = new Button { Content = "CloudSync_RefreshListBtn".GetLocalized() };
        var openCenterBtn = new Button { Content = "CloudSync_OpenCenterBtn".GetLocalized() };
        var toolbarPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { refreshBtn, openCenterBtn }
        };

        var listPanel = new StackPanel { Spacing = 8 };

        var feedbackBar = new InfoBar
        {
            IsOpen = false,
            Visibility = Visibility.Collapsed,
            IsClosable = true,
            Margin = new Thickness(0, 8, 0, 0)
        };
        feedbackBar.Closed += (_, _) => feedbackBar.Visibility = Visibility.Collapsed;

        var scrollContent = new StackPanel
        {
            Spacing = 12,
            Padding = new Thickness(0, 0, 4, 0),
            Children =
            {
                localSummaryText,
                sourceCombo,
                mergeTipText,
                slotBox,
                overwriteCheck,
                uploadBtn,
                new Border
                {
                    Height = 1,
                    Margin = new Thickness(0, 4, 0, 4),
                    Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"]
                },
                quotaSummaryText,
                toolbarPanel,
                listPanel
            }
        };

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 460,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = scrollContent
        };

        var rootGrid = new Grid();
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scrollViewer, 0);
        Grid.SetRow(feedbackBar, 1);
        rootGrid.Children.Add(scrollViewer);
        rootGrid.Children.Add(feedbackBar);

        var dialog = new ContentDialog
        {
            Title = "CloudSync_CycleChallengeTitle".GetLocalized(),
            Content = rootGrid,
            CloseButtonText = "CloudSync_CloseBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme
        };
        dialog.Resources["ContentDialogMinWidth"] = dialogMinWidth;
        dialog.Resources["ContentDialogMaxWidth"] = dialogWidth;

        void ShowFeedback(InfoBarSeverity severity, string title, string msg)
        {
            feedbackBar.Severity = severity;
            feedbackBar.Title = title;
            feedbackBar.Message = msg;
            feedbackBar.Visibility = Visibility.Visible;
            feedbackBar.IsOpen = true;
        }

        string GetAppVersionString()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "1.0.0";
        }

        string? GetSelectedScopeUid()
        {
            return (sourceCombo.SelectedItem as ComboBoxItem)?.Tag as string;
        }

        IReadOnlyList<string>? GetSelectedTargetUids()
        {
            var selectedUid = GetSelectedScopeUid();
            return string.IsNullOrWhiteSpace(selectedUid) ? null : new[] { selectedUid };
        }

        void UpdateSummaryTextForCurrentSelection(CycleChallengeLocalSummary fullSummary)
        {
            var selectedUid = GetSelectedScopeUid();
            if (string.IsNullOrWhiteSpace(selectedUid))
            {
                localSummaryText.Text = string.Format(
                    "CloudSync_CycleChallengeLocalSummaryFormat".GetLocalized(),
                    fullSummary.UidCount,
                    fullSummary.TotalCount,
                    fullSummary.AbyssCount,
                    fullSummary.TheaterCount,
                    fullSummary.StygianCount);
                return;
            }

            var uidSummary =
                fullSummary.Uids.FirstOrDefault(x => string.Equals(x.Uid, selectedUid, StringComparison.Ordinal));
            if (uidSummary is null)
            {
                localSummaryText.Text = string.Format(
                    "CloudSync_CycleChallengeLocalSummaryFormat".GetLocalized(),
                    0, 0, 0, 0, 0);
                return;
            }

            localSummaryText.Text = string.Format(
                "CloudSync_CycleChallengeLocalSummaryFormat".GetLocalized(),
                1,
                uidSummary.TotalCount,
                uidSummary.AbyssCount,
                uidSummary.TheaterCount,
                uidSummary.StygianCount);
        }

        void PopulateSourceOptions(CycleChallengeLocalSummary summary, string? preferredUid)
        {
            suppressSourceSelectionChanged = true;
            try
            {
                sourceCombo.Items.Clear();

                var allItem = new ComboBoxItem
                {
                    Content = string.Format("CloudSync_CycleChallengeSourceAllUids".GetLocalized(), summary.TotalCount),
                    Tag = null
                };
                sourceCombo.Items.Add(allItem);

                ComboBoxItem? matchedItem = null;
                foreach (var uidInfo in summary.Uids)
                {
                    var item = new ComboBoxItem
                    {
                        Content = string.Format(
                            "CloudSync_CycleChallengeSourceSingleUid".GetLocalized(),
                            uidInfo.Uid,
                            uidInfo.AbyssCount,
                            uidInfo.TheaterCount,
                            uidInfo.StygianCount),
                        Tag = uidInfo.Uid
                    };
                    sourceCombo.Items.Add(item);

                    if (!string.IsNullOrWhiteSpace(preferredUid) &&
                        string.Equals(uidInfo.Uid, preferredUid, StringComparison.Ordinal))
                    {
                        matchedItem = item;
                    }
                }

                sourceCombo.SelectedItem = matchedItem ?? allItem;
            }
            finally
            {
                suppressSourceSelectionChanged = false;
            }

            UpdateSummaryTextForCurrentSelection(summary);
        }

        var currentSummary = initialSummary;
        PopulateSourceOptions(currentSummary, null);

        sourceCombo.SelectionChanged += (_, _) =>
        {
            if (suppressSourceSelectionChanged)
            {
                return;
            }

            UpdateSummaryTextForCurrentSelection(currentSummary);
            if (!slotEditedByUser)
            {
                var selectedUid = GetSelectedScopeUid();
                var autoSlot = string.IsNullOrWhiteSpace(selectedUid)
                    ? "cycle_challenges"
                    : $"challenge_{selectedUid}";
                if (autoSlot.Length > 64)
                {
                    autoSlot = autoSlot[..64];
                }

                updatingSlotProgrammatically = true;
                try
                {
                    slotBox.Text = autoSlot;
                }
                finally
                {
                    updatingSlotProgrammatically = false;
                }
            }
        };

        async Task RefreshLocalSummaryAsync()
        {
            var previousSelectedUid = GetSelectedScopeUid();
            currentSummary = await challengeService.GetCloudSummaryAsync();
            PopulateSourceOptions(currentSummary, previousSelectedUid ?? currentRoleUid);
            // Keep All UIDs selected if user had All UIDs selected
            if (previousSelectedUid is null && sourceCombo.Items.Count > 0)
            {
                suppressSourceSelectionChanged = true;
                try
                {
                    sourceCombo.SelectedIndex = 0;
                }
                finally
                {
                    suppressSourceSelectionChanged = false;
                }

                UpdateSummaryTextForCurrentSelection(currentSummary);
            }
        }

        async Task LoadCloudListAsync()
        {
            refreshBtn.IsEnabled = false;
            listPanel.Children.Clear();
            quotaSummaryText.Text = "CloudSync_LoadingArchivesText".GetLocalized();

            var listRes = await cloudSync.ListArchivesAsync(ChallengeArchiveDataType);
            refreshBtn.IsEnabled = true;

            if (!listRes.Ok || listRes.Data is null)
            {
                quotaSummaryText.Text = "CloudSync_ArchivesLoadFailedTitle".GetLocalized();
                ShowFeedback(
                    listRes.IsAccountLocked ? InfoBarSeverity.Warning : InfoBarSeverity.Error,
                    "CloudSync_ArchivesLoadFailedTitle".GetLocalized(),
                    cloudSync.FormatFriendlyError(listRes.Code, listRes.Message, listRes.HttpStatusCode));
                return;
            }

            quotaSummaryText.Text = string.Format(
                "CloudSync_CycleChallengeListSummary".GetLocalized(),
                listRes.Data.Archives.Count,
                listRes.Data.FileCount,
                listRes.Data.FileCountLimit,
                CloudArchiveItem.FormatBytes(listRes.Data.R2BytesUsed),
                CloudArchiveItem.FormatBytes(listRes.Data.R2BytesLimit));

            if (listRes.Data.Archives.Count == 0)
            {
                listPanel.Children.Add(new TextBlock
                {
                    Text = "CloudSync_CycleChallengeEmptyCloud".GetLocalized(),
                    FontSize = 12,
                    Opacity = 0.65,
                    Margin = new Thickness(0, 12, 0, 12),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center
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
                    Content = "CloudSync_CycleChallengeMergeImportBtn".GetLocalized(),
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
                        var pullRes = await cloudSync.PullArchiveAsync(ChallengeArchiveDataType, archive.Slot);
                        if (!pullRes.Ok)
                        {
                            ShowFeedback(
                                InfoBarSeverity.Error,
                                "CloudSync_DownloadFailedTitle".GetLocalized(),
                                cloudSync.FormatFriendlyError(pullRes.Code, pullRes.Message, pullRes.HttpStatusCode));
                            return;
                        }

                        var merge = await challengeService.MergeCloudChallengeJsonAsync(pullRes.JsonContent);
                        if (!merge.Ok)
                        {
                            ShowFeedback(
                                InfoBarSeverity.Error,
                                "CloudSync_CycleChallengeMergeFailedTitle".GetLocalized(),
                                "CloudSync_ErrInvalidFormat".GetLocalized());
                            return;
                        }

                        await RefreshLocalSummaryAsync();
                        await ViewModel.ReloadAfterCloudSyncAsync();
                        ShowFeedback(
                            InfoBarSeverity.Success,
                            "CloudSync_CycleChallengeMergeSuccessTitle".GetLocalized(),
                            CycleChallengeService.FormatMergeSummary(archive.Slot, merge));
                    }
                    catch (Exception ex)
                    {
                        DebugLog.Info("CycleChallengePage", $"[CloudSync] Challenge merge failed: {ex.Message}");
                        ShowFeedback(
                            InfoBarSeverity.Error,
                            "CloudSync_CycleChallengeMergeFailedTitle".GetLocalized(),
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
                        Text = string.Format("CloudSync_CycleChallengeConfirmOverwriteMsg".GetLocalized(),
                            archive.Slot),
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

                        var selectedUid = GetSelectedScopeUid();
                        var targetCount = string.IsNullOrWhiteSpace(selectedUid)
                            ? currentSummary.TotalCount
                            : (currentSummary.Uids
                                .FirstOrDefault(x => string.Equals(x.Uid, selectedUid, StringComparison.Ordinal))
                                ?.TotalCount ?? 0);
                        if (targetCount == 0)
                        {
                            ShowFeedback(
                                InfoBarSeverity.Warning,
                                "CloudSync_HintTitle".GetLocalized(),
                                "CloudSync_CycleChallengeNoLocalData".GetLocalized());
                            return;
                        }

                        isWorking = true;
                        try
                        {
                            var json = await challengeService.BuildCloudChallengeJsonAsync(
                                GetSelectedTargetUids(),
                                GetAppVersionString());
                            var res = await cloudSync.UploadArchiveAsync(
                                ChallengeArchiveDataType,
                                archive.Slot,
                                json,
                                overwrite: true);
                            if (res.Ok)
                            {
                                ShowFeedback(
                                    InfoBarSeverity.Success,
                                    "CloudSync_OverwriteSuccessTitle".GetLocalized(),
                                    string.Format("CloudSync_MsgOverwritten".GetLocalized(), archive.Slot));
                                await LoadCloudListAsync();
                            }
                            else
                            {
                                ShowFeedback(
                                    InfoBarSeverity.Error,
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

                        var res = await cloudSync.RenameArchiveAsync(
                            ChallengeArchiveDataType,
                            archive.Slot,
                            nextName,
                            overwrite: false);
                        if (res.Ok)
                        {
                            ShowFeedback(
                                InfoBarSeverity.Success,
                                "CloudSync_RenameSuccessTitle".GetLocalized(),
                                string.Format("CloudSync_MsgRenameSuccess".GetLocalized(), nextName));
                            await LoadCloudListAsync();
                        }
                        else
                        {
                            ShowFeedback(
                                InfoBarSeverity.Error,
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
                        var res = await cloudSync.DeleteArchiveAsync(ChallengeArchiveDataType, archive.Slot);
                        if (res.Ok)
                        {
                            ShowFeedback(
                                InfoBarSeverity.Success,
                                "CloudSync_DeleteSuccessTitle".GetLocalized(),
                                string.Format("CloudSync_MsgDeleteSuccess".GetLocalized(), archive.Slot));
                            await LoadCloudListAsync();
                        }
                        else
                        {
                            ShowFeedback(
                                InfoBarSeverity.Error,
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
                ShowFeedback(
                    InfoBarSeverity.Warning,
                    "CloudSync_HintTitle".GetLocalized(),
                    cloudSync.FormatFriendlyError("INVALID_DATA_SLOT", null, 400));
                return;
            }

            var selectedUid = GetSelectedScopeUid();
            var targetCount = string.IsNullOrWhiteSpace(selectedUid)
                ? currentSummary.TotalCount
                : (currentSummary.Uids.FirstOrDefault(x => string.Equals(x.Uid, selectedUid, StringComparison.Ordinal))
                    ?.TotalCount ?? 0);
            if (targetCount == 0)
            {
                ShowFeedback(
                    InfoBarSeverity.Warning,
                    "CloudSync_HintTitle".GetLocalized(),
                    "CloudSync_CycleChallengeNoLocalData".GetLocalized());
                return;
            }

            isWorking = true;
            uploadBtn.IsEnabled = false;
            try
            {
                var json = await challengeService.BuildCloudChallengeJsonAsync(
                    GetSelectedTargetUids(),
                    GetAppVersionString());
                var res = await cloudSync.UploadArchiveAsync(
                    ChallengeArchiveDataType,
                    slot,
                    json,
                    overwriteCheck.IsChecked == true);
                if (res.Ok)
                {
                    var msgKey = res.Data?.Overwritten == true
                        ? "CloudSync_MsgOverwritten"
                        : "CloudSync_MsgUploadedNew";
                    ShowFeedback(
                        InfoBarSeverity.Success,
                        "CloudSync_MsgUploadSuccess".GetLocalized(),
                        string.Format(msgKey.GetLocalized(), slot));
                    await LoadCloudListAsync();
                }
                else
                {
                    ShowFeedback(
                        InfoBarSeverity.Error,
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