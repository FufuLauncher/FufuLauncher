/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class GachaAnalysisWindow
{
    private async void OnAboutButtonClick(object sender, RoutedEventArgs e)
    {
        var contentPanel = new StackPanel { Spacing = 8 };

        contentPanel.Children.Add(new TextBlock
        {
            Text = "GachaAnalysis_UigfSupportDesc".GetLocalized(),
            TextWrapping = TextWrapping.Wrap
        });

        contentPanel.Children.Add(new HyperlinkButton
        {
            Content = "UIGF-Org",
            NavigateUri = new Uri(ApiEndpoints.UigfHomeUrl)
        });

        var badgePanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 4, 0, 0)
        };

        badgePanel.Children.Add(new FontIcon
        {
            Glyph = "\uE734",
            FontSize = 16,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
        });

        badgePanel.Children.Add(new TextBlock
        {
            Text = "GachaAnalysis_UigfPartnerBadge".GetLocalized(),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        contentPanel.Children.Add(badgePanel);

        var dialog = new ContentDialog
        {
            Title = "GachaAnalysis_About".GetLocalized(),
            Content = contentPanel,
            CloseButtonText = "CloseBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async void OnDeleteGachaDataClick(object sender, RoutedEventArgs e)
    {
        var deleteUid = ViewModel.SelectedUid;
        if (string.IsNullOrEmpty(ViewModel.SelectedUid))
        {
            var noDataDialog = new ContentDialog
            {
                Title = "Common_Notice".GetLocalized(),
                Content = "GachaAnalysis_NoAccountDeleteHint".GetLocalized(),
                CloseButtonText = "Common_GotIt".GetLocalized(),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            await noDataDialog.ShowAsync();
            return;
        }

        ContentDialog deleteDialog = new()
        {
            Title = "Common_Warning".GetLocalized(),
            Content = string.Format("GachaAnalysis_ConfirmDeleteRecords_Format".GetLocalized(), ViewModel.SelectedUid),
            PrimaryButtonText = "Title_ConfirmDelete".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        ContentDialogResult result = await deleteDialog.ShowAsync();
        if (result == ContentDialogResult.Primary) await ViewModel.ClearGachaDataAsync(deleteUid);
    }

    private async void OnMiYouSheLoginClick(object sender, RoutedEventArgs e)
    {
        await PerformMiYouSheFetchAsync(incremental: false);
    }

    private async void OnMiYouSheFullUpdateClick(object sender, RoutedEventArgs e)
    {
        await PerformMiYouSheFetchAsync(incremental: false);
    }

    private async void OnMiYouSheIncrementalUpdateClick(object sender, RoutedEventArgs e)
    {
        await PerformMiYouSheFetchAsync(incremental: true);
    }

    private async Task PerformMiYouSheFetchAsync(bool incremental)
    {
        if (ViewModel.IsFetching || ViewModel.IsScraping) return;
        var accountManager = App.GetService<AccountManager>();
        var hasCnAccount = accountManager.GetAllAccounts().Any(a => a.ServerType == "cn");

        if (!hasCnAccount && accountManager.GetActiveAccountEntry()?.ServerType == "os")
        {
            var osDialog = new ContentDialog
            {
                Title = "GachaAnalysis_OsNotSupportedTitle".GetLocalized(),
                Content = "GachaAnalysis_OsNotSupportedDesc".GetLocalized(),
                CloseButtonText = "Common_GotIt".GetLocalized(),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            await osDialog.ShowAsync();
            return;
        }

        if (!hasCnAccount)
        {
            var dialog = new ContentDialog
            {
                Title = "GachaAnalysis_NotLoggedInTitle".GetLocalized(),
                Content = "GachaAnalysis_NotLoggedInDesc".GetLocalized(),
                CloseButtonText = "Common_GotIt".GetLocalized(),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();

            if (App.MainWindow is MainWindow mainWindow)
                await mainWindow.NavigateToAccountPageAsync();
            return;
        }

        await ViewModel.FetchFromMiYouSheCommand.ExecuteAsync(incremental);
    }

    private async void OnGameCacheFetchClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsFetching || ViewModel.IsScraping) return;
        var dialog = new ContentDialog
        {
            Title = "GachaAnalysis_GetViaGame".GetLocalized(),
            Content = new TextBlock
            {
                Text = "GachaAnalysis_CacheInstructions".GetLocalized(),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 440
            },
            PrimaryButtonText = "GachaAnalysis_CacheStart".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.FetchFromGameCacheCommand.ExecuteAsync(null);
    }

    private async void OnUrlFetchClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsFetching || ViewModel.IsScraping) return;
        var urlBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 120,
            Text = ViewModel.GachaUrl ?? "",
            PlaceholderText = "GachaAnalysis_UrlPlaceholder".GetLocalized()
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "GachaAnalysis_UrlInstruction".GetLocalized(),
            FontSize = 12,
            Foreground =
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(urlBox);

        var dialog = new ContentDialog
        {
            Title = "GachaAnalysis_UrlDialogTitle".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "GachaAnalysis_CacheStart".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            ViewModel.GachaUrl = urlBox.Text;
            ViewModel.FetchGachaDataCommand.Execute(null);
        }
    }
}