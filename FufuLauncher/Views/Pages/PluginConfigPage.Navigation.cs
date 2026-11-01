/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;

namespace FufuLauncher.Views;

public sealed partial class PluginConfigPage
{
    private void UpdateBackButtonState()
    {
        BackButton.IsEnabled = _isActive && !_isNavigatingBack && Frame != null;
    }

    private bool IsCurrentPage(Frame frame)
    {
        return _isActive && IsLoaded && ReferenceEquals(frame.Content, this);
    }

    private void RestorePageInteraction()
    {
        EntranceStoryboard.Stop();
        ExitStoryboard.Stop();
        RootLayoutGrid.Opacity = 1;
        PageTranslation.Y = 0;
        RootLayoutGrid.IsHitTestVisible = true;
        UpdateBackButtonState();
    }

    private void OnExitStoryboardCompleted(object sender, object e)
    {
        _exitAnimationCompletion?.TrySetResult(true);
    }

    private async Task<bool> PlayExitAnimationAsync()
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _exitAnimationCompletion = completion;
        try
        {
            var opacity = RootLayoutGrid.Opacity;
            var offset = PageTranslation.Y;
            EntranceStoryboard.Stop();
            ExitStoryboard.Stop();
            RootLayoutGrid.Opacity = opacity;
            PageTranslation.Y = offset;
            RootLayoutGrid.IsHitTestVisible = false;
            ExitStoryboard.Begin();
            return await completion.Task.WaitAsync(ExitAnimationTimeout);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginConfigPage.Navigation", $"[PluginConfig] Exit animation interrupted: {ex}");
            return _isActive && IsLoaded;
        }
        finally
        {
            if (ReferenceEquals(_exitAnimationCompletion, completion))
            {
                _exitAnimationCompletion = null;
            }
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isActive = true;
        UpdateBackButtonState();

        if (e.Parameter is PluginItem item)
        {
            _pluginItem = item;
            UpdatePluginHeader();
        }

        PluginContentTranslationService.Instance.EnabledChanged -= OnContentTranslationSettingChanged;
        PluginContentTranslationService.Instance.EnabledChanged += OnContentTranslationSettingChanged;

        await LoadConfigAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _isActive = false;
        StopGamepadWatcher();
        UpdateBackButtonState();
        _exitAnimationCompletion?.TrySetResult(false);
        PluginContentTranslationService.Instance.EnabledChanged -= OnContentTranslationSettingChanged;
        CancelContentTranslation();
        _loadCancellation?.Cancel();
        _autoSaveTimer.Stop();
        _ = SavePendingChangesAsync();
        base.OnNavigatedFrom(e);
    }

    private async void OnBackClick(object sender, RoutedEventArgs e)
    {
        await NavigateBackAsync();
    }

    private async Task NavigateBackAsync()
    {
        var frame = Frame;
        if (_isNavigatingBack || frame == null || !IsCurrentPage(frame))
        {
            return;
        }

        _isNavigatingBack = true;
        BackButton.Focus(FocusState.Programmatic);
        UpdateBackButtonState();
        try
        {
            if (!await SavePendingChangesAsync() || !IsCurrentPage(frame))
            {
                return;
            }

            if (!await PlayExitAnimationAsync() || !IsCurrentPage(frame))
            {
                return;
            }

            while (frame.BackStack.Count > 0 &&
                   frame.BackStack[frame.BackStack.Count - 1].SourcePageType == typeof(PluginConfigPage))
            {
                frame.BackStack.RemoveAt(frame.BackStack.Count - 1);
            }

            var transition = new SuppressNavigationTransitionInfo();
            if (frame.CanGoBack)
            {
                frame.GoBack(transition);
            }
            else
            {
                frame.NavigateToType(typeof(PluginPage), null, new FrameNavigationOptions
                {
                    IsNavigationStackEnabled = false,
                    TransitionInfoOverride = transition
                });
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginConfigPage.Navigation", $"[PluginConfig] Back navigation failed: {ex}");
            if (IsCurrentPage(frame))
            {
                ShowError("GameAnnouncement_Back".GetLocalized(), ex.Message, NavigateBackAsync);
            }
        }
        finally
        {
            _isNavigatingBack = false;
            if (IsCurrentPage(frame))
            {
                RestorePageInteraction();
            }

            UpdateBackButtonState();
        }
    }

    private async void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        await OpenPluginFolderAsync();
    }

    private async Task OpenPluginFolderAsync()
    {
        if (_pluginItem == null)
        {
            return;
        }

        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(_pluginItem.DirectoryPath);
            await Windows.System.Launcher.LaunchFolderAsync(folder);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginConfigPage.Navigation", $"[PluginConfig] Open folder failed: {ex}");
            if (_isActive)
            {
                ShowError("OpenFolderLabel".GetLocalized(), ex.Message, OpenPluginFolderAsync);
            }
        }
    }
}