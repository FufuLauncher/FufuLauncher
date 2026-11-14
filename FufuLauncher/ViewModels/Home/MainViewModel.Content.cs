/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using FufuLauncher.Helpers;

namespace FufuLauncher.ViewModels;

public partial class MainViewModel
{
    #region 资讯内容与轮播

    [ObservableProperty] private ObservableCollection<BannerItem> _banners = new();
    [ObservableProperty] private ObservableCollection<PostItem> _activityPosts = new();
    [ObservableProperty] private ObservableCollection<PostItem> _announcementPosts = new();
    [ObservableProperty] private ObservableCollection<PostItem> _infoPosts = new();
    [ObservableProperty] private ObservableCollection<SocialMediaItem> _socialMediaList = new();

    [ObservableProperty] private bool _isNewsLoaded;

    public Visibility ActivityListVisibility =>
        !IsNewsLoaded || ActivityPosts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ActivityEmptyVisibility =>
        IsNewsLoaded && ActivityPosts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AnnouncementListVisibility =>
        !IsNewsLoaded || AnnouncementPosts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AnnouncementEmptyVisibility =>
        IsNewsLoaded && AnnouncementPosts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InfoListVisibility =>
        !IsNewsLoaded || InfoPosts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility InfoEmptyVisibility =>
        IsNewsLoaded && InfoPosts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsNewsLoadedChanged(bool value)
    {
        NotifyNewsEmptyStates();
    }

    private void NotifyNewsEmptyStates()
    {
        OnPropertyChanged(nameof(ActivityListVisibility));
        OnPropertyChanged(nameof(ActivityEmptyVisibility));
        OnPropertyChanged(nameof(AnnouncementListVisibility));
        OnPropertyChanged(nameof(AnnouncementEmptyVisibility));
        OnPropertyChanged(nameof(InfoListVisibility));
        OnPropertyChanged(nameof(InfoEmptyVisibility));
    }

    private DispatcherQueueTimer _bannerTimer;

    private BannerItem _currentBanner;
    public string CurrentDayText => DateTime.Now.Day.ToString();

    public BannerItem CurrentBanner
    {
        get => _currentBanner;
        set
        {
            SetProperty(ref _currentBanner, value);
        }
    }

    private async Task LoadContentAsync()
    {
        if (Banners != null && Banners.Count > 0)
        {
            if (CurrentBanner == null)
            {
                CurrentBanner = Banners[0];
            }

            _bannerTimer?.Start();

            IsNewsLoaded = true;
            NotifyNewsEmptyStates();

            return;
        }

        try
        {
            var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            int serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
            var server = (Models.ServerType)serverValue;

            var content = await _contentService.GetGameContentAsync(server);

            if (content != null)
            {
                await UpdateUI(() =>
                {
                    _bannerTimer?.Stop();
                    CurrentBanner = null;

                    Banners.Clear();
                    foreach (var banner in content.Banners ?? Array.Empty<BannerItem>())
                    {
                        Banners.Add(banner);
                    }

                    var posts = content.Posts ?? Array.Empty<PostItem>();

                    ActivityPosts.Clear();
                    foreach (var post in posts.Where(p => p.Type == "POST_TYPE_ACTIVITY"))
                        ActivityPosts.Add(post);

                    AnnouncementPosts.Clear();
                    foreach (var post in posts.Where(p => p.Type == "POST_TYPE_ANNOUNCE"))
                        AnnouncementPosts.Add(post);

                    InfoPosts.Clear();
                    foreach (var post in posts.Where(p => p.Type == "POST_TYPE_INFO"))
                        InfoPosts.Add(post);

                    SocialMediaList.Clear();
                    foreach (var item in content.SocialMediaList ?? Array.Empty<SocialMediaItem>())
                    {
                        SocialMediaList.Add(item);
                    }

                    IsNewsLoaded = true;
                    NotifyNewsEmptyStates();

                    if (Banners.Count > 0)
                    {
                        _dispatcherQueue.TryEnqueue(async () =>
                        {
                            try
                            {
                                await Task.Delay(50);

                                if (Banners.Count > 0)
                                {
                                    CurrentBanner = Banners[0];
                                    _bannerTimer?.Start();
                                }
                            }
                            catch (Exception ex)
                            {
                                DebugLog.Info("MainViewModel.Content", $"Failed to set Banner selection: {ex.Message}");
                            }
                        });
                    }
                });
            }
            else
            {
                await UpdateUI(() =>
                {
                    IsNewsLoaded = true;
                    NotifyNewsEmptyStates();
                });
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainViewModel.Content", $"Content load failed: {ex.Message}");
            await UpdateUI(() =>
            {
                IsNewsLoaded = true;
                NotifyNewsEmptyStates();
            });
        }
    }

    private void RotateBanner()
    {
        if (Banners == null || Banners.Count < 2) return;

        if (CurrentBanner == null)
        {
            CurrentBanner = Banners[0];
            return;
        }

        try
        {
            var currentIndex = Banners.IndexOf(CurrentBanner);
            if (currentIndex == -1) currentIndex = 0;

            var nextIndex = (currentIndex + 1) % Banners.Count;
            CurrentBanner = Banners[nextIndex];
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainViewModel.Content", $"Carousel switch error: {ex.Message}");
        }
    }

    #endregion
}