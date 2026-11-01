/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher.Views;

public sealed partial class MainPage
{
    #region 每日提醒

    private async void RefreshDailyNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.IsEnabled = false;
            try
            {
                await ViewModel.LoadDailyNoteAsync(force: true);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }
    }

    private void SyncDailyNoteState()
    {
        if (DailyNoteDataPanel == null || DailyNoteEmptyText == null) return;

        if (ViewModel.IsDailyNoteLoaded)
        {
            DailyNoteDataPanel.Opacity = 1.0;
            DailyNoteEmptyText.Opacity = 0.0;
            DailyNoteDataPanel.IsHitTestVisible = true;
            DailyNoteEmptyText.IsHitTestVisible = false;
        }
        else
        {
            DailyNoteDataPanel.Opacity = 0.0;
            DailyNoteEmptyText.Opacity = 0.8;
            DailyNoteDataPanel.IsHitTestVisible = false;
            DailyNoteEmptyText.IsHitTestVisible = true;
        }
    }

    private void AnimateDailyNoteTransition(bool isLoaded)
    {
        if (DailyNoteDataPanel == null || DailyNoteEmptyText == null) return;

        var storyboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(300));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        storyboard.Children.Add(CreateDoubleAnimation(DailyNoteDataPanel, "Opacity", isLoaded ? 1.0 : 0.0, duration,
            easing));
        storyboard.Children.Add(CreateDoubleAnimation(DailyNoteEmptyText, "Opacity", isLoaded ? 0.0 : 0.8, duration,
            easing));

        DailyNoteDataPanel.IsHitTestVisible = isLoaded;
        DailyNoteEmptyText.IsHitTestVisible = !isLoaded;

        storyboard.Begin();
    }

    #endregion
}