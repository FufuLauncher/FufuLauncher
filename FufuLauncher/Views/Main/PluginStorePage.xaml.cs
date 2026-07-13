/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace FufuLauncher.Views;

public sealed partial class PluginStorePage : Page
{
    public PluginStoreViewModel ViewModel { get; }

    public PluginStorePage()
    {
        ViewModel = App.GetService<PluginStoreViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (FindName("EntranceStoryboard") is Storyboard sb)
            sb.Begin();

        if (ViewModel.Plugins.Count == 0)
        {
            await ViewModel.InitializeAsync();
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (ViewModel.Plugins.Count == 0)
        {
            await ViewModel.InitializeAsync();
        }
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await ViewModel.LoadPluginsAsync();
        }
    }
    
    private async void OnUploadPluginClick(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri("https://github.com/FufuLauncher/FufuLauncher/issues"));
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private async void OnSortClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string sortMode)
        {
            SortLabel.Text = item.Text;
            ViewModel.SortMode = sortMode;
            await ViewModel.LoadPluginsAsync();
        }
    }

    private async void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PluginStoreCategory category)
        {
            ViewModel.SelectedCategory = category;
            await ViewModel.LoadPluginsAsync();
        }
    }

    private async void OnPrevPageClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanGoPrev)
            await ViewModel.GoToPageAsync(ViewModel.CurrentPage - 1);
    }

    private async void OnNextPageClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanGoNext)
            await ViewModel.GoToPageAsync(ViewModel.CurrentPage + 1);
    }

    private void OnInstallButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PluginStoreItem item)
        {
            ViewModel.InstallCommand.Execute(item);
        }
    }

    private async void OnInstalledButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PluginStoreItem item)
        {
            await ShowPluginDetailDialogAsync(item);
        }
    }

    private async void OnPluginItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PluginStoreItem item)
        {
            await ShowPluginDetailDialogAsync(item);
        }
    }

    private async Task ShowPluginDetailDialogAsync(PluginStoreItem item)
    {
        var infoPanel = new StackPanel { Spacing = 16, Padding = new Thickness(0, 12, 0, 12) };

        infoPanel.Children.Add(new TextBlock
        {
            Text = item.Description,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Opacity = 0.9,
            LineHeight = 22,
            Margin = new Thickness(0, 0, 0, 8)
        });

        infoPanel.Children.Add(CreateInfoRow("PluginStoreVersion".GetLocalized(), item.VersionDisplay));
        infoPanel.Children.Add(CreateInfoRow("PluginStoreDeveloper".GetLocalized(), item.Developer));
        infoPanel.Children.Add(CreateInfoRow("PluginStoreSize".GetLocalized(), item.SizeDisplay));
        infoPanel.Children.Add(CreateInfoRow("PluginStoreRating".GetLocalized(), $"{item.RatingDisplay}"));
        infoPanel.Children.Add(CreateInfoRow("PluginStoreDownloads".GetLocalized(), item.DownloadsDisplay));

        if (!string.IsNullOrEmpty(item.LongDescription))
        {
            infoPanel.Children.Add(new TextBlock
            {
                Text = "PluginStoreDetails".GetLocalized(),
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 16, 0, -8)
            });

            infoPanel.Children.Add(new TextBlock
            {
                Text = item.LongDescription,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Opacity = 0.75,
                LineHeight = 20,
                Margin = new Thickness(0, 8, 0, 0),
                MaxHeight = 250
            });
        }

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 500,
            Content = infoPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var isInstalledOrUpdate = item.State == StorePluginState.Installed || item.State == StorePluginState.UpdateAvailable;
        var isUpdate = item.State == StorePluginState.UpdateAvailable;

        var dialog = new ContentDialog
        {
            Title = item.Name,
            Content = scrollViewer,
            PrimaryButtonText = isUpdate ? "PluginStoreUpdateNow".GetLocalized() : (isInstalledOrUpdate ? "PluginStoreUninstall".GetLocalized() : "PluginStoreInstallPlugin".GetLocalized()),
            SecondaryButtonText = "PluginStoreCancel".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            if (isUpdate)
            {
                ViewModel.InstallCommand.Execute(item);
            }
            else if (isInstalledOrUpdate)
            {
                ViewModel.UninstallCommand.Execute(item);
            }
            else
            {
                ViewModel.InstallCommand.Execute(item);
            }
        }
    }

    private static Grid CreateInfoRow(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var labelBlock = new TextBlock
        {
            Text = label,
            FontSize = 13,
            Opacity = 0.55,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(labelBlock, 0);

        var valueBlock = new TextBlock
        {
            Text = value,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(valueBlock, 1);

        grid.Children.Add(labelBlock);
        grid.Children.Add(valueBlock);

        return grid;
    }
}