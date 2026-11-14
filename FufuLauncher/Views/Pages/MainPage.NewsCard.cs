/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher.Views;

public sealed partial class MainPage
{
    #region 资讯卡与新闻骨架屏

    private bool _isInfoCardExpanded = true;
    private DispatcherQueueTimer _newsSkeletonTimer;
    private Storyboard _newsSkeletonShimmerStoryboard;
    private bool _isSkeletonLayerAVisible = true;
    private bool _isSkeletonRotating;
    private int _currentNewsTabIndex;
    private Storyboard? _newsTabTransitionStoryboard;

    private void InfoCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateInfoButtonOpacity(1.0);
        AnimateBannerArrowsOpacity(1.0);
    }

    private void InfoCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateInfoButtonOpacity(0.0);
        AnimateBannerArrowsOpacity(0.0);
    }

    private void OnInfoCardToggledRequested(bool isExpanded)
    {
        DispatcherQueue.TryEnqueue(() => AnimateInfoCardToggle(isExpanded));
    }

    private void AnimateInfoCardToggle(bool isExpanded)
    {
        _isInfoCardExpanded = isExpanded;
        var targetHeight = isExpanded ? ViewModel.InfoCardHeight : 157;
        var targetCornerRadius = new CornerRadius(12);


        if (NewsSkeletonPanel != null && !ViewModel.IsNewsLoaded)
        {
            NewsSkeletonPanel.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        var contentTargetOpacity = isExpanded && ViewModel.IsNewsLoaded ? 1.0 : 0.0;

        var sb = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(350));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var heightAnim = new DoubleAnimation
        {
            To = targetHeight,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(heightAnim, InfoCardContainer);
        Storyboard.SetTargetProperty(heightAnim, "Height");
        sb.Children.Add(heightAnim);

        if (InfoCardContentPanel != null)
        {
            var contentOpacityAnim = new DoubleAnimation
            {
                To = contentTargetOpacity,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(contentOpacityAnim, InfoCardContentPanel);
            Storyboard.SetTargetProperty(contentOpacityAnim, "Opacity");
            sb.Children.Add(contentOpacityAnim);
        }

        sb.Completed += (_, _) =>
        {
            if (InfoCardContentPanel != null)
            {
                InfoCardContentPanel.IsHitTestVisible = isExpanded && ViewModel.IsNewsLoaded;
                InfoCardContentPanel.Opacity = contentTargetOpacity;
            }

            BannerImageArea.CornerRadius = targetCornerRadius;
        };

        BannerImageArea.CornerRadius = targetCornerRadius;
        sb.Begin();
    }

    private void AnimateInfoButtonOpacity(double toOpacity)
    {
        if (InfoExpandButton == null) return;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, InfoExpandButton);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void InitializeNewsSkeleton()
    {
        if (NewsSkeletonPanel == null || InfoCardContentPanel == null) return;

        if (ViewModel.IsNewsLoaded)
        {
            NewsSkeletonPanel.Visibility = Visibility.Collapsed;
            InfoCardContentPanel.Opacity = _isInfoCardExpanded ? 1.0 : 0.0;
            InfoCardContentPanel.IsHitTestVisible = _isInfoCardExpanded;
            InfoCardContentTranslate.Y = 0;
            return;
        }

        InfoCardContentPanel.IsHitTestVisible = false;

        if (_isInfoCardExpanded)
        {
            NewsSkeletonPanel.Visibility = Visibility.Visible;
        }

        StartNewsSkeletonEffects();
    }

    private void StartNewsSkeletonEffects()
    {
        if (_newsSkeletonShimmerStoryboard == null)
        {
            var shimmerRows = new[]
            {
                SkeletonRowA1, SkeletonRowA2, SkeletonRowA3, SkeletonRowA4,
                SkeletonRowB1, SkeletonRowB2, SkeletonRowB3, SkeletonRowB4
            };

            _newsSkeletonShimmerStoryboard = new Storyboard();
            for (var i = 0; i < shimmerRows.Length; i++)
            {
                var shimmer = new DoubleAnimation
                {
                    From = 0.45,
                    To = 1.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(1400)),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds(120 * (i % 4)),
                    EnableDependentAnimation = true
                };
                Storyboard.SetTarget(shimmer, shimmerRows[i]);
                Storyboard.SetTargetProperty(shimmer, "Opacity");
                _newsSkeletonShimmerStoryboard.Children.Add(shimmer);
            }

            _newsSkeletonShimmerStoryboard.Begin();
        }

        if (_newsSkeletonTimer == null)
        {
            _newsSkeletonTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _newsSkeletonTimer.Interval = TimeSpan.FromSeconds(5);
            _newsSkeletonTimer.Tick += (_, _) => RotateNewsSkeleton();
            _newsSkeletonTimer.Start();
        }
    }

    private void RotateNewsSkeleton()
    {
        if (_isSkeletonRotating || ViewModel.IsNewsLoaded || NewsSkeletonPanel.Visibility != Visibility.Visible)
        {
            return;
        }

        var currentLayer = _isSkeletonLayerAVisible ? SkeletonLayerA : SkeletonLayerB;
        var incomingLayer = _isSkeletonLayerAVisible ? SkeletonLayerB : SkeletonLayerA;
        var currentTranslate = _isSkeletonLayerAVisible ? SkeletonLayerATranslate : SkeletonLayerBTranslate;
        var incomingTranslate = _isSkeletonLayerAVisible ? SkeletonLayerBTranslate : SkeletonLayerATranslate;

        var width = Math.Max(NewsSkeletonPanel.ActualWidth, 1);
        var offset = width * 0.18;

        incomingTranslate.X = offset;
        incomingLayer.Opacity = 0;
        currentTranslate.X = 0;
        currentLayer.Opacity = 1;

        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(BannerAnimationMs));
        var easing = new SineEase { EasingMode = EasingMode.EaseInOut };

        storyboard.Children.Add(CreateDoubleAnimation(currentTranslate, "X", -offset, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(currentLayer, "Opacity", 0, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(incomingTranslate, "X", 0, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(incomingLayer, "Opacity", 1, duration, easing));

        _isSkeletonRotating = true;
        storyboard.Completed += (_, _) =>
        {
            currentTranslate.X = 0;
            currentLayer.Opacity = 0;
            incomingTranslate.X = 0;
            incomingLayer.Opacity = 1;
            _isSkeletonLayerAVisible = !_isSkeletonLayerAVisible;
            _isSkeletonRotating = false;
        };
        storyboard.Begin();
    }

    private void StopNewsSkeletonEffects()
    {
        _newsSkeletonTimer?.Stop();
        _newsSkeletonShimmerStoryboard?.Stop();
    }

    private void OnNewsContentLoaded()
    {
        if (NewsSkeletonPanel == null || InfoCardContentPanel == null) return;

        StopNewsSkeletonEffects();

        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(360));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        storyboard.Children.Add(CreateDoubleAnimation(NewsSkeletonPanel, "Opacity", 0, duration, easing));
        storyboard.Children.Add(CreateDoubleAnimation(InfoCardContentTranslate, "Y", 0, duration, easing));

        if (_isInfoCardExpanded)
        {
            storyboard.Children.Add(CreateDoubleAnimation(InfoCardContentPanel, "Opacity", 1, duration, easing));
        }

        storyboard.Completed += (_, _) =>
        {
            NewsSkeletonPanel.Visibility = Visibility.Collapsed;
            InfoCardContentTranslate.Y = 0;
            if (_isInfoCardExpanded && InfoCardContentPanel != null)
            {
                InfoCardContentPanel.IsHitTestVisible = true;
                InfoCardContentPanel.Opacity = 1.0;
            }
        };
        storyboard.Begin();
    }

    private void InfoCardNewsViewport_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (InfoCardNewsViewport == null)
        {
            return;
        }

        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            InfoCardNewsViewport.Clip = new RectangleGeometry
            {
                Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height)
            };
        }
    }

    private void InfoCardNavView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (EventsListView == null || AnnouncementsListView == null || NewsListView == null)
        {
            return;
        }

        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            var targetIndex = tag switch
            {
                "Events" => 0,
                "Announcements" => 1,
                "News" => 2,
                _ => -1
            };

            if (targetIndex >= 0)
            {
                SwitchNewsTab(targetIndex);
            }
        }
    }

    private void SwitchNewsTab(int targetIndex)
    {
        var previousIndex = _currentNewsTabIndex;
        if (targetIndex == previousIndex)
        {
            return;
        }

        _newsTabTransitionStoryboard?.Stop();
        _currentNewsTabIndex = targetIndex;

        ListView? targetList = null;
        TranslateTransform? targetTranslate = null;

        for (var i = 0; i < 3; i++)
        {
            var (list, translate) = GetNewsTabParts(i);

            if (i == targetIndex)
            {
                targetList = list;
                targetTranslate = translate;
            }
            else
            {
                list.Visibility = Visibility.Collapsed;
                list.Opacity = 1.0;
                translate.X = 0;
            }
        }

        if (targetList == null || targetTranslate == null)
        {
            return;
        }

        var direction = targetIndex > previousIndex ? 1 : -1;
        var width = InfoCardNewsViewport?.ActualWidth ?? 0;
        var slideOffset = Math.Min(100.0, Math.Max(width * 0.28, 72.0)) * direction;

        targetList.Visibility = Visibility.Visible;
        targetList.Opacity = 0;
        targetTranslate.X = slideOffset;

        var storyboard = new Storyboard();
        var slideDuration = new Duration(TimeSpan.FromMilliseconds(280));
        var fadeDuration = new Duration(TimeSpan.FromMilliseconds(200));
        var slideEasing = new ExponentialEase { Exponent = 5, EasingMode = EasingMode.EaseOut };
        var fadeEasing = new CubicEase { EasingMode = EasingMode.EaseOut };

        storyboard.Children.Add(CreateDoubleAnimation(targetTranslate, "X", 0, slideDuration, slideEasing));
        storyboard.Children.Add(CreateDoubleAnimation(targetList, "Opacity", 1.0, fadeDuration, fadeEasing));

        _newsTabTransitionStoryboard = storyboard;
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_newsTabTransitionStoryboard, storyboard))
            {
                return;
            }

            targetTranslate.X = 0;
            targetList.Opacity = 1.0;
            _newsTabTransitionStoryboard = null;
        };
        storyboard.Begin();
    }

    private (ListView List, TranslateTransform Translate) GetNewsTabParts(int index) => index switch
    {
        0 => (EventsListView, EventsListTranslate),
        1 => (AnnouncementsListView, AnnouncementsListTranslate),
        _ => (NewsListView, NewsListTranslate)
    };

    #endregion
}