/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher.Views;

public sealed partial class MainPage
{
    #region 游戏签到

    private void SyncCheckinState()
    {
        if (CheckinDataPanel == null || CheckinEmptyText == null) return;

        if (ViewModel.IsCheckinLoaded)
        {
            CheckinDataPanel.Opacity = 1.0;
            CheckinEmptyText.Opacity = 0.0;
            CheckinDataPanel.IsHitTestVisible = true;
            CheckinEmptyText.IsHitTestVisible = false;
        }
        else
        {
            CheckinDataPanel.Opacity = 0.0;
            CheckinEmptyText.Opacity = 0.8;
            CheckinDataPanel.IsHitTestVisible = false;
            CheckinEmptyText.IsHitTestVisible = true;
        }
    }

    private void AnimateCheckinTransition(bool isLoaded)
    {
        if (CheckinDataPanel == null || CheckinEmptyText == null) return;

        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(300));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        storyboard.Children.Add(CreateDoubleAnimation(CheckinDataPanel, "Opacity", isLoaded ? 1.0 : 0.0, duration,
            easing));
        storyboard.Children.Add(CreateDoubleAnimation(CheckinEmptyText, "Opacity", isLoaded ? 0.0 : 0.8, duration,
            easing));

        CheckinDataPanel.IsHitTestVisible = isLoaded;
        CheckinEmptyText.IsHitTestVisible = !isLoaded;

        storyboard.Begin();
    }

    #endregion
}