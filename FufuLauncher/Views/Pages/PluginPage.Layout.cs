/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PluginPage
{
    #region 插件列表布局切换

    private const string LayoutPreferenceKey = "PluginPageIsGridLayout";

    private readonly CardEntranceAnimator _cardEntrance = new("PluginPage.Layout");
    private bool _isGridLayout;
    private bool _layoutInitialized;
    private bool _layoutPreferenceTouched;
    private int _layoutRestoreSession;
    private Task<bool>? _layoutPreferenceLoad;
    private Task _layoutPreferenceSave = Task.CompletedTask;

    private async Task PrepareLayoutModeAsync()
    {
        SyncLayoutTabs();

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
            _cardEntrance.Stop();
            return;
        }

        _cardEntrance.Begin(gridLayout ? (ItemsControl)PluginLayoutGridView : PluginListView);
    }

    private void SyncLayoutTabs()
    {
        ListViewModeButton.IsChecked = !_isGridLayout;
        GridViewModeButton.IsChecked = _isGridLayout;
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _layoutRestoreSession++;
        _cardEntrance.Stop();
    }

    private void OnPluginLayoutContainerChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        _cardEntrance.ObserveContainer(args);
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
