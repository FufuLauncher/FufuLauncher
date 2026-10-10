/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage
{
    private const double MarqueeDragThreshold = 4;

    private bool _isMarqueeActive;
    private bool _isMarqueeDragged;
    private bool _isBatchBarShown;
    private Point _marqueeStart;
    private readonly List<(PluginSettingItem Item, Rect Bounds)> _marqueeTargets = new();

    private void OnSettingsPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.IsSettingsInteractable || _settingPinAnimationCancellation != null) return;

        var source = e.OriginalSource as DependencyObject;
        if (IsInteractiveElement(source) || IsWithinElement(source, BatchActionBar)) return;

        _isMarqueeActive = true;
        _isMarqueeDragged = false;
        _marqueeStart = e.GetCurrentPoint(SelectionCanvas).Position;

        CacheMarqueeTargets();

        (sender as UIElement)?.CapturePointer(e.Pointer);
    }

    private void OnSettingsPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isMarqueeActive) return;

        var current = e.GetCurrentPoint(SelectionCanvas).Position;

        if (!_isMarqueeDragged &&
            (Math.Abs(current.X - _marqueeStart.X) > MarqueeDragThreshold ||
             Math.Abs(current.Y - _marqueeStart.Y) > MarqueeDragThreshold))
        {
            _isMarqueeDragged = true;
            SelectionRectangle.Visibility = Visibility.Visible;
            SelectionRectFadeIn.Begin();
        }

        if (!_isMarqueeDragged) return;

        var rect = UpdateMarquee(current);
        ApplyMarqueeSelection(rect);
    }

    private void OnSettingsPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isMarqueeActive) return;

        if (_isMarqueeDragged)
        {
            ApplyMarqueeSelection(UpdateMarquee(e.GetCurrentPoint(SelectionCanvas).Position));
            ViewModel.NotifySelectionChanged();
            e.Handled = true;
        }
        else
        {
            ViewModel.ClearSelection();
        }

        (sender as UIElement)?.ReleasePointerCapture(e.Pointer);
        EndMarquee();
    }

    private void OnSettingsPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_isMarqueeActive) return;

        ViewModel.NotifySelectionChanged();
        EndMarquee();
    }

    private void ResetSettingsSelection()
    {
        _isMarqueeActive = false;
        _isMarqueeDragged = false;
        _marqueeTargets.Clear();
        SettingsContent.ReleasePointerCaptures();
        SelectionRectFadeIn.Stop();
        SelectionRectFadeOut.Stop();
        SelectionRectangle.Visibility = Visibility.Collapsed;
        BatchActionBarFadeIn.Stop();
        BatchActionBarFadeOut.Stop();
        BatchActionBar.Visibility = Visibility.Collapsed;
        _isBatchBarShown = false;
    }

    private void EndMarquee()
    {
        _isMarqueeActive = false;
        _isMarqueeDragged = false;

        if (SelectionRectangle.Visibility == Visibility.Visible)
        {
            SelectionRectFadeOut.Begin();
        }
    }

    private void SelectionRectFadeOut_Completed(object sender, object e)
    {
        if (_isMarqueeDragged) return;

        SelectionRectangle.Visibility = Visibility.Collapsed;
        SelectionRectangle.Width = 0;
        SelectionRectangle.Height = 0;
        Canvas.SetLeft(SelectionRectangle, 0);
        Canvas.SetTop(SelectionRectangle, 0);
    }

    private Rect UpdateMarquee(Point current)
    {
        double x = Math.Min(_marqueeStart.X, current.X);
        double y = Math.Min(_marqueeStart.Y, current.Y);
        double width = Math.Abs(current.X - _marqueeStart.X);
        double height = Math.Abs(current.Y - _marqueeStart.Y);

        Canvas.SetLeft(SelectionRectangle, x);
        Canvas.SetTop(SelectionRectangle, y);
        SelectionRectangle.Width = width;
        SelectionRectangle.Height = height;

        return new Rect(x, y, width, height);
    }

    private void CacheMarqueeTargets()
    {
        _marqueeTargets.Clear();
        foreach (var container in EnumerateRealizedSettingContainers())
        {
            if (container.Content is PluginSettingItem item && container.ActualWidth > 0 && container.ActualHeight > 0)
            {
                var bounds = container.TransformToVisual(SelectionCanvas)
                    .TransformBounds(new Rect(0, 0, container.ActualWidth, container.ActualHeight));
                _marqueeTargets.Add((item, bounds));
            }
        }
    }

    private void ApplyMarqueeSelection(Rect rect)
    {
        foreach (var (item, bounds) in _marqueeTargets)
        {
            item.IsSelected = IntersectsWith(rect, bounds);
        }

        UpdateBatchBarPosition();
    }

    private void UpdateBatchBarPosition()
    {
        UpdateBatchBarVisibility();

        if (ViewModel.SelectionBarVisibility != Visibility.Visible) return;

        double minX = double.MaxValue;
        double minY = double.MaxValue;
        bool found = false;

        foreach (var (item, bounds) in _marqueeTargets)
        {
            if (!item.IsSelected) continue;

            found = true;
            if (bounds.X < minX) minX = bounds.X;
            if (bounds.Y < minY) minY = bounds.Y;
        }

        if (!found) return;

        BatchActionBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double barWidth = BatchActionBar.DesiredSize.Width;
        double barHeight = BatchActionBar.DesiredSize.Height;

        double horizontalLimit = Math.Max(0, SelectionCanvas.ActualWidth - barWidth);
        double verticalLimit = Math.Max(0, SelectionCanvas.ActualHeight - barHeight);

        Canvas.SetLeft(BatchActionBar, Math.Clamp(minX, 0, horizontalLimit));
        Canvas.SetTop(BatchActionBar, Math.Clamp(minY - barHeight - 6, 0, verticalLimit));
    }

    private void UpdateBatchBarVisibility()
    {
        bool shouldShow = ViewModel.SelectionBarVisibility == Visibility.Visible;

        if (shouldShow == _isBatchBarShown) return;

        _isBatchBarShown = shouldShow;

        if (shouldShow)
        {
            BatchActionBar.Visibility = Visibility.Visible;
            BatchActionBarFadeIn.Begin();
        }
        else
        {
            BatchActionBarFadeOut.Begin();
        }
    }

    private void BatchActionBarFadeOut_Completed(object sender, object e)
    {
        if (_isBatchBarShown) return;

        BatchActionBar.Visibility = Visibility.Collapsed;
    }

    private static bool IntersectsWith(Rect a, Rect b)
    {
        return a.X < b.X + b.Width &&
               b.X < a.X + a.Width &&
               a.Y < b.Y + b.Height &&
               b.Y < a.Y + a.Height;
    }

    private static bool IsInteractiveElement(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is Button or ToggleSwitch or CheckBox or RadioButton or ComboBox or NumberBox
                or TextBox or Slider or ScrollBar or Thumb)
            {
                return true;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static bool IsWithinElement(DependencyObject? source, DependencyObject ancestor)
    {
        while (source != null)
        {
            if (ReferenceEquals(source, ancestor)) return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void OnBatchEnableClick(object sender, RoutedEventArgs e) => ViewModel.BatchSetBoolValue(true);

    private async void OnBatchDisableClick(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || !ViewModel.IsSettingsInteractable || _settingDisableWarningDialog != null) return;

        var settings = ViewModel.SelectedSettings
            .Where(item => string.Equals(item.Type, "bool", StringComparison.OrdinalIgnoreCase) && item.BoolValue)
            .ToArray();

        if (!await ConfirmSettingDisableAsync(settings)) return;
        if (!IsLoaded || !ViewModel.IsSettingsInteractable) return;

        foreach (var item in settings)
        {
            if (IsCurrentSetting(item))
            {
                item.BoolValue = false;
            }
        }

        ViewModel.NotifySelectionChanged();
    }

    private async void OnBatchPinClick(object sender, RoutedEventArgs e) =>
        await AnimateSettingPinAsync(ViewModel.SelectedSettings.ToArray(), true, clearSelection: true);

    private async void OnBatchUnpinClick(object sender, RoutedEventArgs e) =>
        await AnimateSettingPinAsync(ViewModel.SelectedSettings.ToArray(), false, clearSelection: true);

    private void OnBatchClearSelectionClick(object sender, RoutedEventArgs e) => ViewModel.ClearSelection();
}