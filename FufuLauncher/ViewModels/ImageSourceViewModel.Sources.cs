/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.ViewModels;

public partial class ImageSourceViewModel
{
    private async Task SelectCustomBackgroundAsync()
    {
        try
        {
            var path = await _filePickerService.PickImageOrVideoAsync();
            if (string.IsNullOrWhiteSpace(path)) return;

            var extension = Path.GetExtension(path).ToLowerInvariant();
            var isVideo = extension is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov";
            if (isVideo && !_devBuildDetectionService.IsDevBuild)
            {
                StatusText = "Settings_LiveWallpaperDevOnly".GetLocalized();
                return;
            }

            var requestVersion = ++_requestVersion;
            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.CancelPendingBackgroundLoad();
            CustomPath = path;
            HasCustomBackground = true;
            await _localSettingsService.SaveSettingAsync("CustomBackgroundPath", path);
            if (requestVersion != _requestVersion) return;
            await SaveSourceFlagsAsync(ImageSourceTab.Custom, requestVersion);
            if (requestVersion != _requestVersion) return;
            SelectedTab = ImageSourceTab.Custom;
            StartLoading(requestVersion);
            RefreshBackground();
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to select custom background: {ex.Message}");
            StatusText = ex.Message;
            IsPreviewLoading = false;
        }
    }

    private async Task ClearCustomBackgroundAsync()
    {
        try
        {
            await _localSettingsService.SaveSettingAsync<string>("CustomBackgroundPath", null);
            CustomPath = string.Empty;
            HasCustomBackground = false;
            UpdateSourceInfo();

            if (SelectedTab == ImageSourceTab.Custom)
                ClearPreview("ImageSource_CustomEmpty");
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to clear custom background: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task SelectSlideshowFolderAsync()
    {
        try
        {
            var folder = await _filePickerService.PickFolderAsync();
            if (string.IsNullOrWhiteSpace(folder)) return;

            var requestVersion = ++_requestVersion;
            if (App.MainWindow is MainWindow mainWindow)
                mainWindow.CancelPendingBackgroundLoad();
            SlideshowFolder = folder;
            HasSlideshowFolder = true;
            await _localSettingsService.SaveSettingAsync("BackgroundSlideshowFolder", folder);
            if (requestVersion != _requestVersion) return;
            await SaveSourceFlagsAsync(ImageSourceTab.Slideshow, requestVersion);
            if (requestVersion != _requestVersion) return;
            await _localSettingsService.SaveSettingAsync("BackgroundSlideshowInterval", SlideshowInterval);
            if (requestVersion != _requestVersion) return;
            SelectedTab = ImageSourceTab.Slideshow;
            StartLoading(requestVersion);
            RefreshBackground();
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to select carousel folder: {ex.Message}");
            StatusText = ex.Message;
            IsPreviewLoading = false;
        }
    }

    private async Task ClearSlideshowFolderAsync()
    {
        try
        {
            await _localSettingsService.SaveSettingAsync<string>("BackgroundSlideshowFolder", null);
            SlideshowFolder = string.Empty;
            HasSlideshowFolder = false;
            UpdateSourceInfo();

            if (SelectedTab == ImageSourceTab.Slideshow)
            {
                if (App.MainWindow is MainWindow mainWindow)
                    mainWindow.StopSlideshowBackground();

                ClearPreview("ImageSource_SlideshowEmpty");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to clear carousel folder: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task SaveBackgroundApiAsync()
    {
        try
        {
            var normalized = CustomBackgroundApiUrl.Trim();
            if (!string.IsNullOrEmpty(normalized) &&
                (!Uri.TryCreate(normalized, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                StatusText = "ImageSource_InvalidApiUrl".GetLocalized();
                return;
            }

            CustomBackgroundApiUrl = normalized;
            await _localSettingsService.SaveSettingAsync("CustomBackgroundApiUrl", normalized);
            CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(normalized)
                ? GetDefaultBackgroundApiUrl(SelectedServer)
                : normalized;
            await _localSettingsService.SaveSettingAsync("BackgroundJsonHash", string.Empty);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", string.Empty);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
            StatusText = string.Empty;

            if (SelectedTab == ImageSourceTab.Official)
            {
                var requestVersion = ++_requestVersion;
                StartLoading(requestVersion);
                await PickOfficialImageAsync(requestVersion);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to save official API: {ex.Message}");
            StatusText = ex.Message;
        }
    }

    private async Task ResetBackgroundApiAsync()
    {
        CustomBackgroundApiUrl = string.Empty;
        await SaveBackgroundApiAsync();
    }

    private async Task PickOfficialImageAsync(int requestVersion)
    {
        try
        {
            var server = SelectedServer;
            var backgrounds = await _hoyoverseBackgroundService.GetAvailableBackgroundsAsync(server);
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Official) return;

            var images = backgrounds
                .Where(item => !item.IsVideo && !string.IsNullOrWhiteSpace(item.Url))
                .ToList();
            if (images.Count == 0)
            {
                StatusText = "OfficialBgWindow_Empty".GetLocalized();
                RefreshBackground();
                return;
            }

            var currentUrl =
                (await _localSettingsService.ReadSettingAsync("SelectedOnlineBackgroundUrl"))?.ToString();
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Official) return;
            var candidates = string.IsNullOrWhiteSpace(currentUrl)
                ? images
                : images.Where(item => !string.Equals(item.Url, currentUrl, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            if (candidates.Count == 0) candidates = images;

            var selected = candidates[Random.Shared.Next(candidates.Count)];
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", selected.Url);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Official) return;
            RefreshBackground();
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion && SelectedTab == ImageSourceTab.Official)
            {
                DebugLog.Info("ImageSourceViewModel",
                    $"[ImageSource] Failed to fetch official backgrounds: {ex.Message}");
                StatusText = ex.Message;
                IsPreviewLoading = false;
            }
        }
    }

    private async Task PickBingImageAsync(int requestVersion)
    {
        try
        {
            var currentUrl = (await _localSettingsService.ReadSettingAsync("BingWallpaperUrl"))?.ToString();
            var wallpaper = await _bingWallpaperService.GetRandomWallpaperAsync(
                currentUrl,
                SelectedBingYearOption?.Year ?? 0,
                IsBingOfficial);
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Bing) return;

            if (wallpaper == null)
            {
                StatusText = "ImageSource_BingEmpty".GetLocalized();
                RefreshBackground();
                return;
            }

            await _localSettingsService.SaveSettingAsync("BingWallpaperUrl", wallpaper.Url);
            await _localSettingsService.SaveSettingAsync("BingWallpaperCopyright", wallpaper.Copyright);
            if (requestVersion != _requestVersion || SelectedTab != ImageSourceTab.Bing) return;
            BingCopyright = wallpaper.Copyright;
            UpdateSourceInfo();
            RefreshBackground();
        }
        catch (Exception ex)
        {
            if (requestVersion == _requestVersion && SelectedTab == ImageSourceTab.Bing)
            {
                DebugLog.Info("ImageSourceViewModel", $"[ImageSource] Failed to fetch Bing wallpaper: {ex.Message}");
                StatusText = ex.Message;
                IsPreviewLoading = false;
            }
        }
    }

    private async Task ReloadRandomAsync(bool forceNewImage, int requestVersion)
    {
        if (requestVersion != _requestVersion) return;

        if (App.MainWindow is MainWindow mainWindow)
            await mainWindow.ReloadRandomBackgroundAsync(forceNewImage);
        else
            RefreshBackground();
    }

    private static string GetDefaultBackgroundApiUrl(ServerType server) => server switch
    {
        ServerType.OS => ApiEndpoints.BackgroundOsApi,
        _ => ApiEndpoints.BackgroundCnApi
    };
}