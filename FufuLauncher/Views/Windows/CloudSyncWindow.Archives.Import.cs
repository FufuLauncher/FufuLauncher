using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow
{
    private async Task MergeUiafArchiveToLocalAsync(string slot, string jsonContent)
    {
        var summary = CloudArchiveLocalBridge.InspectUiafJson(jsonContent);
        if (!summary.Ok)
        {
            ShowMessage(InfoBarSeverity.Error,
                "CloudSync_UiafMergeFailedTitle".GetLocalized(),
                "CloudSync_ErrInvalidFormat".GetLocalized());
            return;
        }

        var profiles = CloudArchiveLocalBridge.GetLocalAchievementProfiles();
        var profileCombo = new ComboBox
        {
            Header = "CloudSync_UiafTargetHeader".GetLocalized(),
            ItemsSource = profiles,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var newNameBox = new TextBox
        {
            Header = "CloudSync_UiafNewProfileHeader".GetLocalized(),
            PlaceholderText = "CloudSync_UiafNewProfilePlaceholder".GetLocalized(),
            Visibility = Visibility.Collapsed,
            MaxLength = 64
        };

        profileCombo.SelectionChanged += (_, _) =>
        {
            var selected = profileCombo.SelectedItem as LocalAchievementProfileOption;
            newNameBox.Visibility = selected is { IsNewProfileOption: true }
                ? Visibility.Visible
                : Visibility.Collapsed;
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 400 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format("CloudSync_UiafImportSummaryFormat".GetLocalized(), summary.TotalItems, summary.CompletedItems),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });
        panel.Children.Add(profileCombo);
        panel.Children.Add(newNameBox);
        panel.Children.Add(new TextBlock
        {
            Text = "CloudSync_UiafImportTip".GetLocalized(),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        var dialog = new ContentDialog
        {
            Title = "CloudSync_UiafImportDialogTitle".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "CloudSync_ImportConfirmBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (profileCombo.SelectedItem is not LocalAchievementProfileOption chosen) return;

        SetBusy(true);
        try
        {
            var merge = await CloudArchiveLocalBridge.MergeUiafToLocalProfileAsync(
                jsonContent,
                chosen,
                newNameBox.Text);

            if (!merge.Ok)
            {
                ShowMessage(InfoBarSeverity.Error,
                    "CloudSync_UiafMergeFailedTitle".GetLocalized(),
                    merge.ErrorMessage ?? "CloudSync_UiafMergeFailed".GetLocalized());
                return;
            }

            ShowMessage(InfoBarSeverity.Success,
                "CloudSync_UiafMergeSuccessTitle".GetLocalized(),
                string.Format(
                    "CloudSync_UiafMergeSuccessFormat".GetLocalized(),
                    slot,
                    merge.TargetDisplayName,
                    merge.UpdatedCount));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task MergeUigfArchiveToLocalAsync(string slot, string jsonContent)
    {
        var uidItems = CloudArchiveLocalBridge.InspectUigfUids(jsonContent);
        if (uidItems.Count == 0)
        {
            ShowMessage(InfoBarSeverity.Error,
                "CloudSync_UigfMergeFailedTitle".GetLocalized(),
                "CloudSync_ErrInvalidFormat".GetLocalized());
            return;
        }

        var checkBoxes = new List<(string Uid, CheckBox Box)>();
        var listPanel = new StackPanel { Spacing = 6 };
        foreach (var entry in uidItems)
        {
            var cb = new CheckBox
            {
                IsChecked = true,
                Content = $"UID {entry.Uid}  ·  {entry.DetailText}"
            };
            checkBoxes.Add((entry.Uid, cb));
            listPanel.Children.Add(cb);
        }

        var panel = new StackPanel { Spacing = 10, MinWidth = 400 };
        panel.Children.Add(new TextBlock
        {
            Text = "CloudSync_UigfImportSummary".GetLocalized(),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 240,
            Content = listPanel
        });
        panel.Children.Add(new TextBlock
        {
            Text = "CloudSync_UigfImportTip".GetLocalized(),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        var dialog = new ContentDialog
        {
            Title = "CloudSync_UigfImportDialogTitle".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "CloudSync_ImportConfirmBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var selectedUids = checkBoxes
            .Where(x => x.Box.IsChecked == true)
            .Select(x => x.Uid)
            .ToHashSet(StringComparer.Ordinal);

        if (selectedUids.Count == 0)
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrSelectUidFirst".GetLocalized());
            return;
        }

        SetBusy(true);
        try
        {
            var result = await CloudArchiveLocalBridge.ImportUigfToLocalAsync(jsonContent, selectedUids);
            if (!result.Success)
            {
                ShowMessage(InfoBarSeverity.Error,
                    "CloudSync_UigfMergeFailedTitle".GetLocalized(),
                    "CloudSync_ErrInvalidFormat".GetLocalized());
                return;
            }

            ShowMessage(InfoBarSeverity.Success,
                "CloudSync_UigfMergeSuccessTitle".GetLocalized(),
                string.Format(
                    "CloudSync_UigfMergeSuccessFormat".GetLocalized(),
                    slot,
                    result.ImportedAccounts,
                    result.ImportedRecords,
                    result.DisplayUid ?? "-"));
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] Merge UIGF failed: {ex.Message}");
            ShowMessage(InfoBarSeverity.Error,
                "CloudSync_UigfMergeFailedTitle".GetLocalized(),
                "CloudSync_ErrInvalidFormat".GetLocalized());
        }
        finally
        {
            SetBusy(false);
        }
    }
}
