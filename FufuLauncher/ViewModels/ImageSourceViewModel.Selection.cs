/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Services;

namespace FufuLauncher.ViewModels;

public partial class ImageSourceViewModel
{
    public async Task SelectTabAsync(ImageSourceTab tab, bool forceNewImage = false)
    {
        var started = Environment.TickCount64;
        DebugLog.Info("ImageSourceViewModel", "SelectTab.Start", $"{tab} force={forceNewImage}");
        var requestVersion = ++_requestVersion;
        if (App.MainWindow is MainWindow mainWindow)
            mainWindow.CancelPendingBackgroundLoad();
        _isStepping = true;
        SelectedTab = tab;
        StatusText = string.Empty;

        try
        {
            switch (tab)
            {
                case ImageSourceTab.Official:
                    await SaveSourceFlagsAsync(ImageSourceTab.Official, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    var officialUrl =
                        (await _localSettingsService.ReadSettingAsync("SelectedOnlineBackgroundUrl"))?.ToString();
                    if (requestVersion != _requestVersion) return;
                    if (forceNewImage || string.IsNullOrWhiteSpace(officialUrl))
                        await PickOfficialImageAsync(requestVersion);
                    else
                        RefreshBackground();
                    break;

                case ImageSourceTab.RandomBlueArchive:
                    await SaveSourceFlagsAsync(ImageSourceTab.RandomBlueArchive, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    await ReloadRandomAsync(forceNewImage, requestVersion);
                    break;

                case ImageSourceTab.Bing:
                    await SaveSourceFlagsAsync(ImageSourceTab.Bing, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    var bingUrl = (await _localSettingsService.ReadSettingAsync("BingWallpaperUrl"))?.ToString();
                    if (requestVersion != _requestVersion) return;
                    if (forceNewImage || string.IsNullOrWhiteSpace(bingUrl))
                        await PickBingImageAsync(requestVersion);
                    else
                        RefreshBackground();
                    break;

                case ImageSourceTab.Custom:
                    if (!HasCustomBackground)
                    {
                        ClearPreview("ImageSource_CustomEmpty");
                        return;
                    }

                    await SaveSourceFlagsAsync(ImageSourceTab.Custom, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    RefreshBackground();
                    break;

                case ImageSourceTab.Slideshow:
                    if (!HasSlideshowFolder)
                    {
                        ClearPreview("ImageSource_SlideshowEmpty");
                        return;
                    }

                    await SaveSourceFlagsAsync(ImageSourceTab.Slideshow, requestVersion);
                    if (requestVersion != _requestVersion) return;
                    await _localSettingsService.SaveSettingAsync("BackgroundSlideshowInterval", SlideshowInterval);
                    if (requestVersion != _requestVersion) return;
                    StartLoading(requestVersion);
                    RefreshBackground();
                    break;
            }
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion)
            {
                DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to switch image source: {ex.Message}");
                StatusText = ex.Message;
                IsPreviewLoading = false;
            }
        }
        finally
        {
            if (requestVersion == _requestVersion)
                _isStepping = false;
        }

        DebugLog.Info("ImageSourceViewModel", "SelectTab.End",
            $"{tab} in {Environment.TickCount64 - started}ms current={requestVersion == _requestVersion}");
    }

    private async Task StepAsync(int direction)
    {
        if (!CanStep || _isStepping) return;

        _isStepping = true;
        var requestVersion = ++_requestVersion;
        StatusText = string.Empty;

        try
        {
            switch (SelectedTab)
            {
                case ImageSourceTab.Official:
                    StartLoading(requestVersion);
                    await PickOfficialImageAsync(requestVersion);
                    break;

                case ImageSourceTab.RandomBlueArchive:
                    StartLoading(requestVersion);
                    await ReloadRandomAsync(true, requestVersion);
                    break;

                case ImageSourceTab.Bing:
                    StartLoading(requestVersion);
                    await PickBingImageAsync(requestVersion);
                    break;

                case ImageSourceTab.Slideshow:
                    StartLoading(requestVersion);
                    if (App.MainWindow is MainWindow mainWindow)
                    {
                        var moved = await mainWindow.StepSlideshowAsync(direction);
                        if (!moved && requestVersion == _requestVersion)
                            IsPreviewLoading = false;
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion)
            {
                DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to switch image: {ex.Message}");
                StatusText = ex.Message;
                IsPreviewLoading = false;
            }
        }
        finally
        {
            if (requestVersion == _requestVersion)
                _isStepping = false;
        }
    }

    private async Task SaveSourceFlagsAsync(ImageSourceTab tab, int requestVersion)
    {
        await _sourceFlagsGate.WaitAsync();
        try
        {
            if (requestVersion != _requestVersion) return;

            var random = tab == ImageSourceTab.RandomBlueArchive;
            var bing = tab == ImageSourceTab.Bing;
            var slideshow = tab == ImageSourceTab.Slideshow;
            var custom = tab is ImageSourceTab.Custom or ImageSourceTab.Slideshow;

            await _localSettingsService.SaveSettingAsync("IsRandomBackgroundEnabled", random);
            await _localSettingsService.SaveSettingAsync("IsBingBackgroundEnabled", bing);
            await _localSettingsService.SaveSettingAsync("IsBackgroundSlideshowEnabled", slideshow);
            await _localSettingsService.SaveSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, custom);
        }
        finally
        {
            _sourceFlagsGate.Release();
        }
    }
}