/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.ViewModels;

public partial class ImageSourceViewModel
{
    partial void OnSelectedTabChanged(ImageSourceTab value)
    {
        UpdateSourceInfo();
    }

    partial void OnBingCopyrightChanged(string value)
    {
        UpdateSourceInfo();
    }

    partial void OnBingChangeOnStartupChanged(bool value)
    {
        if (_isLoadingSettings) return;
        _ = _localSettingsService.SaveSettingAsync("IsBingBackgroundChangeOnStartupEnabled", value);
    }

    private void OnIsBingOfficialChanged(bool value)
    {
        if (_isLoadingSettings) return;
        _ = HandleBingOfficialChangedAsync(value);
    }

    private void OnSelectedBingYearOptionChanged(BingYearOption? value)
    {
        if (_isLoadingSettings) return;
        _ = HandleBingYearChangedAsync(value);
    }

    private async Task HandleBingOfficialChangedAsync(bool value)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync("IsBingWallpaperOfficial", value);
            if (SelectedTab == ImageSourceTab.Bing)
                await ReloadBingImageAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"Failed to change Bing official mode: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task HandleBingYearChangedAsync(BingYearOption? option)
    {
        if (option == null) return;

        try
        {
            await _localSettingsService.SaveSettingAsync("BingWallpaperYear", option.Year);
            if (!IsBingOfficial && SelectedTab == ImageSourceTab.Bing)
                await ReloadBingImageAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"Failed to change Bing wallpaper year: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task ReloadBingImageAsync()
    {
        var requestVersion = ++_requestVersion;
        StartLoading(requestVersion);
        await PickBingImageAsync(requestVersion);
    }

    partial void OnCustomPathChanged(string value)
    {
        UpdateSourceInfo();
    }

    partial void OnSlideshowFolderChanged(string value)
    {
        UpdateSourceInfo();
    }

    partial void OnHasCustomBackgroundChanged(bool value)
    {
        UpdateSourceInfo();
    }

    partial void OnHasSlideshowFolderChanged(bool value)
    {
        UpdateSourceInfo();
    }

    partial void OnSelectedServerChanged(ServerType value)
    {
        if (_isLoadingSettings) return;
        _ = HandleServerChangedAsync(value);
    }

    partial void OnRandomTypeChanged(RandomBackgroundType value)
    {
        if (_isLoadingSettings) return;
        _ = HandleRandomTypeChangedAsync(value);
    }

    private void OnIsRandomBackgroundPinnedChanged(bool value)
    {
        if (_isLoadingSettings || _isUpdatingRandomPin) return;
        _ = HandleRandomPinnedChangedAsync(value);
    }

    private async Task HandleRandomPinnedChangedAsync(bool value)
    {
        try
        {
            if (value && !await RandomBackgroundService.PinCurrentImageAsync())
            {
                _isUpdatingRandomPin = true;
                IsRandomBackgroundPinned = false;
                _isUpdatingRandomPin = false;
                StatusText = "ImageSource_RandomPinFailed".GetLocalized();
                return;
            }

            await _localSettingsService.SaveSettingAsync(RandomBackgroundService.PinnedSettingKey, value);

            if (value)
                StatusText = string.Empty;

            if (App.MainWindow is MainWindow mainWindow)
                await mainWindow.ReloadRandomBackgroundAsync(true);
            else
                WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"Failed to change random background pin state: {ex.Message}");
            _isUpdatingRandomPin = true;
            IsRandomBackgroundPinned = !value;
            _isUpdatingRandomPin = false;
            StatusText = "ImageSource_RandomPinFailed".GetLocalized();
        }
    }

    partial void OnSlideshowIntervalChanged(int value)
    {
        if (_isLoadingSettings) return;
        _ = HandleSlideshowIntervalChangedAsync(value);
    }

    private async Task HandleServerChangedAsync(ServerType server)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync(LocalSettingsService.BackgroundServerKey, (int)server);
            CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(CustomBackgroundApiUrl)
                ? GetDefaultBackgroundApiUrl(server)
                : CustomBackgroundApiUrl;
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", string.Empty);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
            await _localSettingsService.SaveSettingAsync("BackgroundJsonHash", string.Empty);
            UpdateSourceInfo();

            if (SelectedTab == ImageSourceTab.Official)
            {
                var requestVersion = ++_requestVersion;
                StartLoading(requestVersion);
                await PickOfficialImageAsync(requestVersion);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to switch official server: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task HandleRandomTypeChangedAsync(RandomBackgroundType type)
    {
        try
        {
            await _localSettingsService.SaveSettingAsync("RandomBackgroundType", type.ToString());
            UpdateSourceInfo();

            if (SelectedTab == ImageSourceTab.RandomBlueArchive)
            {
                var requestVersion = ++_requestVersion;
                StartLoading(requestVersion);
                await ReloadRandomAsync(true, requestVersion);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel",
                $"[ImageSource] Failed to save random background range: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task HandleSlideshowIntervalChangedAsync(int interval)
    {
        try
        {
            var normalized = Math.Max(1, interval);
            if (normalized != interval)
            {
                SlideshowInterval = normalized;
                return;
            }

            await _localSettingsService.SaveSettingAsync("BackgroundSlideshowInterval", normalized);

            if (SelectedTab == ImageSourceTab.Slideshow && HasSlideshowFolder)
            {
                StartLoading(++_requestVersion);
                RefreshBackground();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to save carousel interval: {ex.Message}");
            StatusText = ex.Message;
        }
    }
}
