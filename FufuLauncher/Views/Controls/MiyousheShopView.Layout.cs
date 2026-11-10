using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace FufuLauncher.Views;

public sealed partial class MiyousheShopView
{
    private const string ViewModeSetting = "MiyousheShopGridView";
    private const string DetailsWidthSetting = "MiyousheShopDetailsWidth";
    private const double MinimumCatalogWidth = 310;
    private const double MinimumDetailsWidth = 248;
    private const double MaximumDetailsWidth = 540;
    private const double DividerWidth = 12;
    private const double MinimumTileWidth = 144;
    private const double MaximumTileWidth = 184;
    private readonly ILocalSettingsService _settings = App.GetService<ILocalSettingsService>();
    private readonly InputSystemCursor _resizeCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    private Task? _layoutLoad;
    private double _preferredDetailsWidth = 320;
    private bool _gridView, _updatingSelection;

    private ListViewBase ActiveGoodsView => _gridView ? GoodsGrid : GoodsList;

    private async Task RestoreLayoutAsync()
    {
        try
        {
            var values = await Task.WhenAll(_settings.ReadSettingAsync(ViewModeSetting),
                _settings.ReadSettingAsync(DetailsWidthSetting));
            if (values[1] is double width && double.IsFinite(width))
                _preferredDetailsWidth = Math.Clamp(width, MinimumDetailsWidth, MaximumDetailsWidth);
            ApplyViewMode(values[0] is true);
            ApplyDetailsWidth();
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(MiyousheShopView), ex.GetType().Name);
        }
        finally
        {
            ListViewButton.IsEnabled = GridViewButton.IsEnabled = DetailDivider.IsEnabled = true;
        }
    }

    private async void OnViewModeClick(object sender, RoutedEventArgs e)
    {
        var button = (ToggleButton)sender;
        bool grid = button == GridViewButton;
        if (grid == _gridView)
        {
            button.IsChecked = true;
            return;
        }
        ApplyViewMode(grid);
        await _settings.TrySaveSettingAsync(ViewModeSetting, grid);
    }

    private void ApplyViewMode(bool grid)
    {
        var selected = ActiveGoodsView.SelectedItem;
        _updatingSelection = true;
        try
        {
            _gridView = grid;
            ListViewButton.IsChecked = !grid;
            GridViewButton.IsChecked = grid;
            GoodsList.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
            GoodsGrid.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
            ActiveGoodsView.SelectedItem = selected;
        }
        finally
        {
            _updatingSelection = false;
        }
        if (selected != null) ActiveGoodsView.ScrollIntoView(selected);
    }

    private void OnCatalogSizeChanged(object sender, SizeChangedEventArgs e) => ApplyDetailsWidth();

    private void OnGoodsGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (GoodsGrid.ItemsPanelRoot is not ItemsWrapGrid panel || e.NewSize.Width <= 0) return;
        double width = e.NewSize.Width - 16; // Leave room for the vertical scroll bar.
        int columns = Math.Max(1, (int)(width / MinimumTileWidth));
        panel.ItemWidth = Math.Clamp(Math.Floor(width / columns), MinimumTileWidth, MaximumTileWidth);
    }

    private double AvailableDetailsWidth => CatalogPane.ActualWidth > 0
        ? Math.Clamp(CatalogPane.ActualWidth - DividerWidth - MinimumCatalogWidth,
            MinimumDetailsWidth, MaximumDetailsWidth)
        : MaximumDetailsWidth;

    private void ApplyDetailsWidth()
    {
        double width = Math.Min(_preferredDetailsWidth, AvailableDetailsWidth);
        if (DetailsColumn.Width.Value != width) DetailsColumn.Width = new GridLength(width);
    }

    private bool ResizeDetails(double horizontalChange)
    {
        double current = DetailsColumn.Width.Value;
        double width = Math.Clamp(current - horizontalChange, MinimumDetailsWidth, AvailableDetailsWidth);
        if (width == current) return false;
        _preferredDetailsWidth = width;
        ApplyDetailsWidth();
        return true;
    }

    private void OnDetailsDragDelta(object sender, DragDeltaEventArgs e) => ResizeDetails(e.HorizontalChange);

    private async void OnDetailsDragCompleted(object sender, DragCompletedEventArgs e) =>
        await _settings.TrySaveSettingAsync(DetailsWidthSetting, _preferredDetailsWidth);

    private void OnDetailsSplitterEntered(object sender, PointerRoutedEventArgs e) => ProtectedCursor = _resizeCursor;

    private void OnDetailsSplitterExited(object sender, PointerRoutedEventArgs e) => ProtectedCursor = null;

    private async void OnDetailsSplitterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right)) return;
        e.Handled = true;
        if (ResizeDetails(e.Key == VirtualKey.Left ? -16 : 16))
            await _settings.TrySaveSettingAsync(DetailsWidthSetting, _preferredDetailsWidth);
    }
}
