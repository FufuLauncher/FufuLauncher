/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Reflection;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace FufuLauncher.Views;

public sealed partial class PluginStorePage : Page
{
    public PluginStoreViewModel ViewModel
    {
        get;
    }

    public IReadOnlyList<int> SkeletonItems
    {
        get;
    } = new[] { 0, 1, 2, 3, 4, 5 };

    private static readonly string CurrentAppVersion =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0.0";

    private bool _isSubscribed;

    public PluginStorePage()
    {
        ViewModel = App.GetService<PluginStoreViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();

        SyncSortUi();
        SubscribeViewModel();
        UpdateLoadingAnimations();

        if (ViewModel.Plugins.Count == 0)
        {
            await ViewModel.InitializeAsync();
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeViewModel();
        SkeletonPulseStoryboard.Stop();
        RefreshSpinStoryboard.Stop();
        ResultsFadeOutStoryboard.Stop();
        ResultsFadeInStoryboard.Stop();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        UnsubscribeViewModel();
        base.OnNavigatedFrom(e);
    }

    private void SubscribeViewModel()
    {
        if (_isSubscribed) return;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _isSubscribed = true;
    }

    private void UnsubscribeViewModel()
    {
        if (!_isSubscribed) return;
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _isSubscribed = false;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PluginStoreViewModel.IsLoading)
            or nameof(PluginStoreViewModel.ShowSkeleton)
            or nameof(PluginStoreViewModel.IsRefreshing))
        {
            UpdateLoadingAnimations();
        }
    }

    private void UpdateLoadingAnimations()
    {
        if (ViewModel.ShowSkeleton)
            SkeletonPulseStoryboard.Begin();
        else
            SkeletonPulseStoryboard.Stop();

        if (ViewModel.IsLoading)
        {
            RefreshSpinStoryboard.Begin();
        }
        else
        {
            RefreshSpinStoryboard.Stop();
            RefreshIconRotate.Angle = 0;
        }

        if (ViewModel.IsRefreshing)
        {
            ResultsFadeOutStoryboard.Begin();
        }
        else if (!ViewModel.IsLoading)
        {
            ResultsFadeInStoryboard.Begin();
        }
    }

    private void OnContentScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var horizontalMargin = ContentPanel.Margin.Left + ContentPanel.Margin.Right;
        var available = e.NewSize.Width - horizontalMargin;

        ContentPanel.Width = Math.Max(320, Math.Min(ContentPanel.MaxWidth, available));
    }

    private void SyncSortUi()
    {
        var isNewest = string.Equals(ViewModel.SortMode, "newest", StringComparison.OrdinalIgnoreCase);
        SortNewestItem.IsChecked = isNewest;
        SortPopularItem.IsChecked = !isNewest;
        SortLabel.Text = isNewest ? SortNewestItem.Text : SortPopularItem.Text;
    }

    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ViewModel.CurrentPage = 1;
        await ViewModel.LoadPluginsAsync();
    }

    private async void OnUploadPluginClick(object sender, RoutedEventArgs e)
    {
        await Windows.System.Launcher.LaunchUriAsync(new Uri(ApiEndpoints.DeveloperPortalUrl));
    }

    private void OnAddPrivatePluginClick(object sender, RoutedEventArgs e)
    {
        ViewModel.AddPrivatePluginCommand.Execute(null);
    }

    private void OnLuaTestClick(object sender, RoutedEventArgs e)
    {
        ViewModel.LuaTestCommand.Execute(null);
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }

    private async void OnResetFiltersClick(object sender, RoutedEventArgs e)
    {
        ViewModel.SearchText = string.Empty;
        ViewModel.SelectedCategory = ViewModel.Categories.FirstOrDefault();
        ViewModel.CurrentPage = 1;
        await ViewModel.LoadPluginsAsync();
    }

    private async void OnSortClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is string sortMode)
        {
            SortLabel.Text = item.Text;
            ViewModel.SortMode = sortMode;
            ViewModel.CurrentPage = 1;
            await ViewModel.LoadPluginsAsync();
        }
    }

    private async void OnCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton btn && btn.Tag is PluginStoreCategory category)
        {
            ViewModel.SelectedCategory = category;
            ViewModel.CurrentPage = 1;
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
        var content = new StackPanel { Spacing = 16, Padding = new Thickness(0, 4, 0, 4) };

        content.Children.Add(BuildDetailHeader(item));
        content.Children.Add(BuildDetailDivider());

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            content.Children.Add(new TextBlock
            {
                Text = item.Description,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Opacity = 0.9,
                LineHeight = 22
            });
        }

        var infoPanel = new StackPanel { Spacing = 10 };
        infoPanel.Children.Add(CreateInfoRow("PluginStoreVersion".GetLocalized(), item.VersionDisplay));
        infoPanel.Children.Add(CreateInfoRow("PluginStoreDeveloper".GetLocalized(), item.Developer));
        infoPanel.Children.Add(CreateInfoRow("PluginStoreSize".GetLocalized(), item.SizeDisplay));
        infoPanel.Children.Add(CreateInfoRow("PluginStoreDownloads".GetLocalized(), item.DownloadsDisplay));

        if (item.HasCategory)
        {
            infoPanel.Children.Add(CreateInfoRow("PluginStoreCategoryLabel".GetLocalized(), item.CategoryDisplay));
        }

        if (item.HasUpdateType)
        {
            infoPanel.Children.Add(CreateInfoRow("PluginStoreUpdateType".GetLocalized(), item.UpdateTypeDisplay));
        }

        if (!string.IsNullOrWhiteSpace(item.MinAppVersion))
        {
            var versionRow = CreateInfoRow("PluginStoreMinAppVersion".GetLocalized(), $"v{item.MinAppVersion}");
            if (!IsVersionSatisfied(CurrentAppVersion, item.MinAppVersion)
                && versionRow.Children[1] is TextBlock valueBlock)
            {
                valueBlock.Text = string.Format(
                    "PluginStoreVersionTooLow".GetLocalized(), item.MinAppVersion, CurrentAppVersion);
                valueBlock.Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
                valueBlock.TextWrapping = TextWrapping.Wrap;
                valueBlock.TextTrimming = TextTrimming.None;
            }

            infoPanel.Children.Add(versionRow);
        }

        if (item.IsPrivate)
        {
            infoPanel.Children.Add(CreateInfoRow(
                "PluginStoreVisibility".GetLocalized(), "PluginStorePrivatePlugin".GetLocalized()));
        }

        content.Children.Add(infoPanel);

        if (item.HasDependencies)
        {
            content.Children.Add(BuildDetailDivider());
            content.Children.Add(CreateSubHeader("PluginStoreDependencies".GetLocalized()));

            var depsPanel = new StackPanel { Spacing = 6 };
            foreach (var dep in item.Dependencies.Where(d => !d.IsEmpty))
            {
                depsPanel.Children.Add(new TextBlock
                {
                    Text = dep.ToString(),
                    FontSize = 13,
                    Opacity = 0.75,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            content.Children.Add(depsPanel);
        }

        if (!string.IsNullOrEmpty(item.LongDescription))
        {
            content.Children.Add(BuildDetailDivider());
            content.Children.Add(CreateSubHeader("PluginStoreLongDescription".GetLocalized()));

            content.Children.Add(new TextBlock
            {
                Text = item.LongDescription,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Opacity = 0.75,
                LineHeight = 20
            });
        }

        var scrollViewer = new ScrollViewer
        {
            MaxHeight = 520,
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled
        };

        var isInstalledOrUpdate =
            item.State == StorePluginState.Installed || item.State == StorePluginState.UpdateAvailable;
        var isUpdate = item.State == StorePluginState.UpdateAvailable;

        var dialog = new ContentDialog
        {
            Title = item.Name,
            Content = scrollViewer,
            PrimaryButtonText = isUpdate
                ? "PluginStoreUpdateNow".GetLocalized()
                : (isInstalledOrUpdate
                    ? "PluginStoreUninstall".GetLocalized()
                    : "PluginStoreInstallPlugin".GetLocalized()),
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

    private static Grid BuildDetailHeader(PluginStoreItem item)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconHost = new Border
        {
            Width = 56,
            Height = 56,
            CornerRadius = new CornerRadius(12),
            VerticalAlignment = VerticalAlignment.Top,
            Background = TryGetBrush("CardBackgroundFillColorDefaultBrush")
        };

        var iconLayers = new Grid();
        iconLayers.Children.Add(new FontIcon { Glyph = "", FontSize = 22, Opacity = 0.6 });

        if (!string.IsNullOrWhiteSpace(item.IconUrl)
            && Uri.TryCreate(item.IconUrl, UriKind.Absolute, out var iconUri))
        {
            iconLayers.Children.Add(new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(iconUri),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
            });
        }

        iconHost.Child = iconLayers;
        Grid.SetColumn(iconHost, 0);
        grid.Children.Add(iconHost);

        var textPanel = new StackPanel
        {
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 4
        };

        textPanel.Children.Add(new TextBlock
        {
            Text = item.Name,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });

        textPanel.Children.Add(new TextBlock
        {
            Text = item.DeveloperVersionDisplay,
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        });

        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        return grid;
    }

    private static Border BuildDetailDivider() => new()
    {
        Height = 1,
        Opacity = 0.5,
        Background = TryGetBrush("DividerStrokeColorDefaultBrush")
    };

    private static Microsoft.UI.Xaml.Media.Brush? TryGetBrush(string key)
        => Application.Current.Resources.TryGetValue(key, out var value)
            ? value as Microsoft.UI.Xaml.Media.Brush
            : null;

    private static TextBlock CreateSubHeader(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
    };

    private static Grid CreateInfoRow(string label, string value)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
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

    private static bool IsVersionSatisfied(string currentVersion, string minVersion)
    {
        if (!AppVersionHelper.TryParseVersion(currentVersion, out var cur) ||
            !AppVersionHelper.TryParseVersion(minVersion, out var min))
        {
            return true;
        }

        return cur >= min;
    }
}