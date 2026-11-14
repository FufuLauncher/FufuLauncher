/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services.Background;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.ViewModels;

public partial class ImageSourceViewModel
{
    public void ApplyBackgroundResult(BackgroundRenderResult? result)
    {
        DebugLog.Info("ImageSourceViewModel", "VM.ApplyBackgroundResult",
            $"source={(result?.ImageSource == null ? "null" : "set")} fallback={result?.IsFallback} tab={SelectedTab}");
        if (SelectedTab == ImageSourceTab.Custom && !HasCustomBackground) return;
        if (SelectedTab == ImageSourceTab.Slideshow && !HasSlideshowFolder) return;

        if (result == null || result.IsFallback || result.ImageSource == null)
        {
            PreviewImage = null;
            IsPreviewFallback = true;
            IsPreviewVideo = result?.IsVideo == true;
            PreviewGlyph = IsPreviewVideo ? "\uE714" : "\uEB9F";
        }
        else
        {
            PreviewImage = result.ImageSource;
            IsPreviewFallback = false;
            IsPreviewVideo = false;
            PreviewGlyph = "\uEB9F";
        }

        if (result != null && !result.IsFallback)
            StatusText = string.Empty;

        IsPreviewLoading = false;
    }

    public void SetPreviewFailure()
    {
        PreviewImage = null;
        IsPreviewFallback = true;
        IsPreviewVideo = false;
        PreviewGlyph = "\uEB9F";
        IsPreviewLoading = false;
    }

    private void RefreshBackground()
    {
        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
    }

    private void StartLoading(int requestVersion)
    {
        IsPreviewLoading = true;
        _ = ClearLoadingAfterDelayAsync(requestVersion);
    }

    private async Task ClearLoadingAfterDelayAsync(int requestVersion)
    {
        await Task.Delay(TimeSpan.FromSeconds(15));
        if (requestVersion == _requestVersion && IsPreviewLoading)
            IsPreviewLoading = false;
    }

    private void ClearPreview(string statusKey)
    {
        DebugLog.Info("ImageSourceViewModel", "VM.ClearPreview", statusKey);
        PreviewImage = null;
        IsPreviewFallback = true;
        IsPreviewVideo = false;
        PreviewGlyph = "\uEB9F";
        IsPreviewLoading = false;
        StatusText = statusKey.GetLocalized();
    }

    private void UpdateSourceInfo()
    {
        SourceTitle = SelectedTab switch
        {
            ImageSourceTab.Official => "ImageSource_SourceOfficial".GetLocalized(),
            ImageSourceTab.RandomBlueArchive => "ImageSource_SourceRandom".GetLocalized(),
            ImageSourceTab.Bing => "ImageSource_SourceBing".GetLocalized(),
            ImageSourceTab.Custom => "ImageSource_SourceCustom".GetLocalized(),
            ImageSourceTab.Slideshow => "ImageSource_SourceSlideshow".GetLocalized(),
            _ => "ImageSource_Title".GetLocalized()
        };

        SourceCaption = SelectedTab switch
        {
            ImageSourceTab.Official => SelectedServer == ServerType.OS
                ? "ServerGlobal".GetLocalized()
                : "ServerCN".GetLocalized(),
            ImageSourceTab.RandomBlueArchive => RandomType switch
            {
                RandomBackgroundType.Home => "BgSourceRandomModeHome".GetLocalized(),
                RandomBackgroundType.Cg => "BgSourceRandomModeCg".GetLocalized(),
                _ => "BgSourceRandomModeAll".GetLocalized()
            },
            ImageSourceTab.Bing => string.IsNullOrWhiteSpace(BingCopyright)
                ? "ImageSource_SourceBing".GetLocalized()
                : BingCopyright,
            ImageSourceTab.Custom => HasCustomBackground
                ? CustomPath
                : "ImageSource_CustomEmpty".GetLocalized(),
            ImageSourceTab.Slideshow => HasSlideshowFolder
                ? SlideshowFolder
                : "ImageSource_SlideshowEmpty".GetLocalized(),
            _ => string.Empty
        };
    }
}
