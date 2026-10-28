/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Numerics;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace FufuLauncher.Views;

public sealed partial class PluginPage
{
    #region 插件列表布局切换

    private const string LayoutPreferenceKey = "PluginPageIsGridLayout";
    private const string LayoutTranslationProperty = "Translation";
    private const int LayoutEntranceStaggerMilliseconds = 26;
    private const int LayoutEntranceMaximumDelayMilliseconds = 210;
    private const int LayoutEntranceWindowMilliseconds = 700;
    private const float LayoutEntranceVerticalOffset = 16f;

    private readonly HashSet<UIElement> _layoutEntranceContainers = new();
    private bool _isGridLayout;
    private bool _layoutInitialized;
    private bool _layoutPreferenceTouched;
    private bool _layoutEntranceActive;
    private bool _layoutEntranceAnimationsEnabled;
    private int _layoutRestoreSession;
    private int _layoutEntranceSession;
    private long _layoutEntranceDeadline;
    private long _layoutEntranceNextAt;
    private Task<bool>? _layoutPreferenceLoad;
    private Task _layoutPreferenceSave = Task.CompletedTask;
    private UISettings? _layoutAnimationPreferences;
    private ScalarKeyFrameAnimation? _layoutEntranceFade;
    private Vector3KeyFrameAnimation? _layoutEntranceSlide;

    private async Task PrepareLayoutModeAsync()
    {
        SyncLayoutTabs();
        ReadLayoutAnimationPreference();

        if (_layoutInitialized || _layoutPreferenceTouched)
        {
            return;
        }

        _layoutInitialized = true;
        var session = ++_layoutRestoreSession;

        try
        {
            _layoutPreferenceLoad ??= ReadLayoutPreferenceAsync();
            var gridLayout = await _layoutPreferenceLoad;
            if (!IsLoaded || session != _layoutRestoreSession || _layoutPreferenceTouched)
            {
                return;
            }

            ApplyLayoutMode(gridLayout, animate: false, persist: false);
        }
        catch (Exception ex)
        {
            _layoutPreferenceLoad = null;
            DebugLog.Info("PluginPage.Layout", $"[PluginPage] Layout preference load failed: {ex}");
        }
    }

    private void OnListViewModeClick(object sender, RoutedEventArgs e)
    {
        if (!_isGridLayout)
        {
            ConfirmLayoutMode();
            return;
        }

        ApplyLayoutMode(false, animate: true, persist: true);
    }

    private void OnGridViewModeClick(object sender, RoutedEventArgs e)
    {
        if (_isGridLayout)
        {
            ConfirmLayoutMode();
            return;
        }

        ApplyLayoutMode(true, animate: true, persist: true);
    }

    private void ConfirmLayoutMode()
    {
        _layoutPreferenceTouched = true;
        _layoutPreferenceSave = SaveLayoutPreferenceAsync(_layoutPreferenceSave, _isGridLayout);
        SyncLayoutTabs();
    }

    private void ApplyLayoutMode(bool gridLayout, bool animate, bool persist)
    {
        _isGridLayout = gridLayout;
        PluginListView.Visibility = gridLayout ? Visibility.Collapsed : Visibility.Visible;
        PluginLayoutGridView.Visibility = gridLayout ? Visibility.Visible : Visibility.Collapsed;
        SyncLayoutTabs();

        if (persist)
        {
            _layoutPreferenceTouched = true;
            _layoutPreferenceSave = SaveLayoutPreferenceAsync(_layoutPreferenceSave, gridLayout);
        }

        if (!animate)
        {
            StopLayoutEntrance();
            return;
        }

        StartLayoutEntrance(gridLayout ? (ItemsControl)PluginLayoutGridView : PluginListView);
    }

    private void SyncLayoutTabs()
    {
        ListViewModeButton.IsChecked = !_isGridLayout;
        GridViewModeButton.IsChecked = _isGridLayout;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _layoutRestoreSession++;
        StopLayoutEntrance();
    }

    private void StartLayoutEntrance(ItemsControl target)
    {
        StopLayoutEntrance();

        if (!_layoutEntranceAnimationsEnabled || target.Items.Count == 0)
        {
            return;
        }

        _layoutEntranceActive = true;
        _layoutEntranceDeadline = Environment.TickCount64 + LayoutEntranceWindowMilliseconds;
        _layoutEntranceNextAt = 0;
        var session = ++_layoutEntranceSession;

        try
        {
            target.UpdateLayout();
            AnimateLayoutContainers(target, session);
        }
        catch (Exception ex)
        {
            _layoutEntranceActive = false;
            DebugLog.Info("PluginPage.Layout", $"[PluginPage] Layout entrance failed: {ex}");
        }
        finally
        {
            if (session == _layoutEntranceSession)
            {
                _layoutEntranceActive = false;
            }
        }
    }

    private void AnimateLayoutContainers(ItemsControl target, int session)
    {
        var count = target.Items.Count;

        for (var index = 0; index < count; index++)
        {
            if (session != _layoutEntranceSession || !_layoutEntranceActive)
            {
                return;
            }

            if (Environment.TickCount64 > _layoutEntranceDeadline)
            {
                return;
            }

            if (target.ContainerFromIndex(index) is not UIElement container)
            {
                return;
            }

            TryAnimateLayoutContainer(container);
        }
    }

    private void OnPluginLayoutContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Phase != 0)
        {
            return;
        }

        if (args.InRecycleQueue)
        {
            if (args.ItemContainer is UIElement recycled && _layoutEntranceContainers.Remove(recycled))
            {
                RestoreLayoutContainer(recycled);
            }

            return;
        }

        if (!_layoutEntranceActive)
        {
            return;
        }

        if (Environment.TickCount64 > _layoutEntranceDeadline)
        {
            _layoutEntranceActive = false;
            return;
        }

        if (args.ItemContainer is UIElement container)
        {
            TryAnimateLayoutContainer(container);
        }
    }

    private void TryAnimateLayoutContainer(UIElement container)
    {
        if (!_layoutEntranceContainers.Add(container))
        {
            return;
        }

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(container, true);
            var visual = ElementCompositionPreview.GetElementVisual(container);
            PrepareLayoutEntranceAnimations(visual.Compositor);

            var now = Environment.TickCount64;
            var delayMilliseconds =
                Math.Clamp(_layoutEntranceNextAt - now, 0, LayoutEntranceMaximumDelayMilliseconds);
            _layoutEntranceNextAt = now + delayMilliseconds + LayoutEntranceStaggerMilliseconds;

            var delay = TimeSpan.FromMilliseconds(delayMilliseconds);
            var fade = _layoutEntranceFade!;
            var slide = _layoutEntranceSlide!;
            fade.DelayTime = delay;
            slide.DelayTime = delay;

            visual.Properties.InsertVector3(LayoutTranslationProperty,
                new Vector3(0, LayoutEntranceVerticalOffset, 0));
            visual.Opacity = 0;
            visual.StartAnimation(nameof(Visual.Opacity), fade);
            visual.Properties.StartAnimation(LayoutTranslationProperty, slide);
        }
        catch (Exception ex)
        {
            _layoutEntranceContainers.Remove(container);
            _layoutEntranceActive = false;
            _layoutEntranceAnimationsEnabled = false;
            RestoreLayoutContainer(container);
            DebugLog.Info("PluginPage.Layout", $"[PluginPage] Layout entrance animation failed: {ex}");
        }
    }

    private void PrepareLayoutEntranceAnimations(Compositor compositor)
    {
        if (_layoutEntranceFade != null && _layoutEntranceSlide != null &&
            _layoutEntranceFade.Compositor.Equals(compositor))
        {
            return;
        }

        _layoutEntranceFade?.Dispose();
        _layoutEntranceSlide?.Dispose();

        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));

        _layoutEntranceFade = compositor.CreateScalarKeyFrameAnimation();
        _layoutEntranceFade.InsertKeyFrame(0, 0);
        _layoutEntranceFade.InsertKeyFrame(1, 1, easing);
        _layoutEntranceFade.Duration = TimeSpan.FromMilliseconds(240);
        _layoutEntranceFade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        _layoutEntranceFade.StopBehavior = AnimationStopBehavior.SetToFinalValue;

        _layoutEntranceSlide = compositor.CreateVector3KeyFrameAnimation();
        _layoutEntranceSlide.InsertKeyFrame(0, new Vector3(0, LayoutEntranceVerticalOffset, 0));
        _layoutEntranceSlide.InsertKeyFrame(1, Vector3.Zero, easing);
        _layoutEntranceSlide.Duration = TimeSpan.FromMilliseconds(340);
        _layoutEntranceSlide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        _layoutEntranceSlide.StopBehavior = AnimationStopBehavior.SetToFinalValue;
    }

    private void StopLayoutEntrance()
    {
        _layoutEntranceActive = false;
        _layoutEntranceSession++;

        if (_layoutEntranceContainers.Count > 0)
        {
            foreach (var container in _layoutEntranceContainers)
            {
                RestoreLayoutContainer(container);
            }

            _layoutEntranceContainers.Clear();
        }

        _layoutEntranceFade?.Dispose();
        _layoutEntranceSlide?.Dispose();
        _layoutEntranceFade = null;
        _layoutEntranceSlide = null;
    }

    private static void RestoreLayoutContainer(UIElement container)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(container);
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.Properties.StopAnimation(LayoutTranslationProperty);
            visual.Properties.InsertVector3(LayoutTranslationProperty, Vector3.Zero);
            visual.Opacity = (float)container.Opacity;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginPage.Layout", $"[PluginPage] Layout animation reset failed: {ex.Message}");
        }
    }

    private void ReadLayoutAnimationPreference()
    {
        try
        {
            _layoutAnimationPreferences ??= new UISettings();
            _layoutEntranceAnimationsEnabled = _layoutAnimationPreferences.AnimationsEnabled;
        }
        catch (Exception ex)
        {
            _layoutEntranceAnimationsEnabled = false;
            DebugLog.Info("PluginPage.Layout", $"[PluginPage] Animation preference read failed: {ex}");
        }
    }

    private static async Task<bool> ReadLayoutPreferenceAsync()
    {
        var value = await App.GetService<ILocalSettingsService>().ReadSettingAsync(LayoutPreferenceKey);
        return value != null && Convert.ToBoolean(value);
    }

    private static async Task SaveLayoutPreferenceAsync(Task previous, bool gridLayout)
    {
        try
        {
            await previous;
            await App.GetService<ILocalSettingsService>().SaveSettingAsync(LayoutPreferenceKey, gridLayout);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginPage.Layout", $"[PluginPage] Layout preference save failed: {ex}");
        }
    }

    #endregion
}
