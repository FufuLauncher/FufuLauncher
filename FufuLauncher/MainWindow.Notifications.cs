/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using Windows.Foundation;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    #region Notifications

    private void ShowNotification(NotificationMessage message)
    {
        try
        {
            if (NotificationContainer.Visibility == Visibility.Collapsed ||
                (NotificationContainer.Tag is string state && state == "Closing"))
            {
                NotificationContainer.Tag = null;
                NotificationContainer.Visibility = Visibility.Visible;
                PlayContainerEntranceAnimation();
            }

            var infoBar = CreateInfoBar(message);
            NotificationPanel.Children.Insert(0, infoBar);
            PlayEntranceAnimation(infoBar);

            if (message.Duration > 0)
            {
                SetupAutoDismiss(infoBar, message.Duration);
            }
        }
        catch
        {
        }
    }

    private InfoBar CreateInfoBar(NotificationMessage message)
    {
        var slideOffset =
            (_notificationPosition == NotificationPosition.TopLeft ||
             _notificationPosition == NotificationPosition.BottomLeft)
                ? -380
                : 380;
        var infoBar = new InfoBar
        {
            FontFamily = (FontFamily)Application.Current.Resources["AppGlobalFont"],
            Title = message.Title,
            Message = message.Message,
            Severity = GetInfoBarSeverity(message.Type),
            IsOpen = true,
            IsClosable = true,
            Margin = new Thickness(0, 0, 0, 8),
            Width = 360,
            RenderTransform = new TranslateTransform { X = slideOffset },
            Opacity = 0
        };

        if (!string.IsNullOrEmpty(message.CopyText))
        {
            infoBar.ActionButton = CreateCopyActionButton(message.CopyText);
        }

        infoBar.Closing += (sender, args) =>
        {
            args.Cancel = true;

            if (infoBar.Tag is string state && state == "Closing")
            {
                return;
            }

            infoBar.Tag = "Closing";

            infoBar.IsHitTestVisible = false;

            DismissInfoBar(infoBar);
        };

        return infoBar;
    }

    private Button CreateCopyActionButton(string copyText)
    {
        var copyButton = new Button
        {
            Content = "CopyBtn".GetLocalized()
        };

        copyButton.Click += (_, _) =>
        {
            try
            {
                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                package.SetText(copyText);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            }
            catch
            {
            }

            copyButton.Content = "Btn_Copied".GetLocalized();
            copyButton.IsEnabled = false;

            Task.Delay(1000).ContinueWith(_ =>
            {
                dispatcherQueue.TryEnqueue(() =>
                {
                    copyButton.Content = "CopyBtn".GetLocalized();
                    copyButton.IsEnabled = true;
                });
            });
        };

        return copyButton;
    }

    private InfoBarSeverity GetInfoBarSeverity(NotificationType type)
    {
        return type switch
        {
            NotificationType.Success => InfoBarSeverity.Success,
            NotificationType.Warning => InfoBarSeverity.Warning,
            NotificationType.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
    }

    private void PlayEntranceAnimation(FrameworkElement element)
    {
        var slideOffset =
            (_notificationPosition == NotificationPosition.TopLeft ||
             _notificationPosition == NotificationPosition.BottomLeft)
                ? -380
                : 380;
        var transformAnim = new DoubleAnimation
        {
            From = slideOffset,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(transformAnim, element.RenderTransform);
        Storyboard.SetTargetProperty(transformAnim, "X");

        var opacityAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnim, element);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(transformAnim);
        storyboard.Children.Add(opacityAnim);
        storyboard.Begin();
    }

    private void DismissInfoBar(FrameworkElement element)
    {
        if (element is InfoBar infoBar && (infoBar.Title == "RedeemCodeExpired".GetLocalized() ||
                                           infoBar.Title == "RedeemCodeToday".GetLocalized() ||
                                           infoBar.Title == "RedeemCodeNew".GetLocalized() ||
                                           infoBar.Title == "RedeemCodeExpiring".GetLocalized()))
        {
            _ = _localSettingsService.SaveSettingAsync("LastRedeemCodeReminderDate",
                DateTime.Now.ToString("yyyy-MM-dd"));
            DebugLog.Info("MainWindow.Notifications", "[RedeemCodes] Dismissed state written to database");
        }

        var slideOffset =
            (_notificationPosition == NotificationPosition.TopLeft ||
             _notificationPosition == NotificationPosition.BottomLeft)
                ? -380
                : 380;
        var transformAnim = new DoubleAnimation
        {
            From = 0,
            To = slideOffset,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(transformAnim, element.RenderTransform);
        Storyboard.SetTargetProperty(transformAnim, "X");

        var opacityAnim = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(opacityAnim, element);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(transformAnim);
        storyboard.Children.Add(opacityAnim);

        var isLastNotification = NotificationPanel.Children
            .OfType<FrameworkElement>()
            .All(c => c.Tag is string state && state == "Closing");

        if (isLastNotification)
        {
            PlayContainerExitAnimation();
        }

        storyboard.Completed += (_, _) => CollapseNotification(element);

        storyboard.Begin();
    }

    private void CollapseNotification(FrameworkElement element)
    {
        try
        {
            var index = NotificationPanel.Children.IndexOf(element);
            if (index < 0)
            {
                return;
            }

            var isLastChild = index == NotificationPanel.Children.Count - 1;
            var spacing = isLastChild ? 0 : NotificationPanel.Spacing;
            var margin = element.Margin;
            var space = element.ActualHeight + margin.Top + margin.Bottom + spacing;

            NotificationPanel.Children.Remove(element);

            if (space <= 0)
            {
                return;
            }

            var placeholder = new Border
            {
                Height = Math.Max(0, space - spacing),
                Margin = new Thickness(0, 0, 0, -spacing),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsHitTestVisible = false
            };

            NotificationPanel.Children.Insert(index, placeholder);

            var heightAnim = new DoubleAnimation
            {
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(220)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(heightAnim, placeholder);
            Storyboard.SetTargetProperty(heightAnim, "Height");

            var storyboard = new Storyboard();
            storyboard.Children.Add(heightAnim);
            storyboard.Completed += (_, _) =>
            {
                try
                {
                    NotificationPanel.Children.Remove(placeholder);
                }
                catch
                {
                }
            };

            storyboard.Begin();
        }
        catch
        {
            try
            {
                NotificationPanel.Children.Remove(element);
            }
            catch
            {
            }
        }
    }

    private void ClearAllNotifications_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var currentNotifications = NotificationPanel.Children.ToList();

            foreach (var child in currentNotifications)
            {
                if (child is InfoBar infoBar)
                {
                    if (infoBar.Tag is string state && state == "Closing") continue;

                    infoBar.Tag = "Closing";
                    infoBar.IsHitTestVisible = false;
                    DismissInfoBar(infoBar);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Notifications", $"Clear-all notifications exception: {ex.Message}");
        }
    }

    private async void NotificationSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsNavItem = GetAllNavItems()
            .FirstOrDefault(i => i.Tag?.ToString() == "FufuLauncher.ViewModels.SettingsViewModel");
        if (settingsNavItem != null)
        {
            NavigationView.SelectedItem = settingsNavItem;
        }

        NavigateToPage("FufuLauncher.ViewModels.SettingsViewModel");

        await Task.Delay(500);

        if (ContentFrame.Content is Views.SettingsPage settingsPage)
        {
            await settingsPage.NavigateToNotificationPositionAsync();
        }
    }

    private void PlayContainerEntranceAnimation()
    {
        var origin = _notificationPosition switch
        {
            NotificationPosition.TopRight => new Point(1, 0),
            NotificationPosition.TopLeft => new Point(0, 0),
            NotificationPosition.BottomLeft => new Point(0, 1),
            _ => new Point(1, 1)
        };
        NotificationContainer.RenderTransformOrigin = origin;
        var scaleTransform = new ScaleTransform { ScaleX = 0.8, ScaleY = 0.8 };
        NotificationContainer.RenderTransform = scaleTransform;
        NotificationContainer.Opacity = 0;

        var scaleXAnim = new DoubleAnimation
        {
            From = 0.8,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(350)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleXAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");

        var scaleYAnim = new DoubleAnimation
        {
            From = 0.8,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(350)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleYAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");

        var opacityAnim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnim, NotificationContainer);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleXAnim);
        storyboard.Children.Add(scaleYAnim);
        storyboard.Children.Add(opacityAnim);
        storyboard.Begin();
    }

    private void PlayContainerExitAnimation()
    {
        if (NotificationContainer.Tag is string state && state == "Closing") return;

        NotificationContainer.Tag = "Closing";

        var origin = _notificationPosition switch
        {
            NotificationPosition.TopRight => new Point(1, 0),
            NotificationPosition.TopLeft => new Point(0, 0),
            NotificationPosition.BottomLeft => new Point(0, 1),
            _ => new Point(1, 1)
        };
        NotificationContainer.RenderTransformOrigin = origin;

        if (!(NotificationContainer.RenderTransform is ScaleTransform scaleTransform))
        {
            scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            NotificationContainer.RenderTransform = scaleTransform;
        }

        var scaleXAnim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.8,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleXAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleXAnim, "ScaleX");

        var scaleYAnim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.8,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleYAnim, scaleTransform);
        Storyboard.SetTargetProperty(scaleYAnim, "ScaleY");

        var opacityAnim = new DoubleAnimation
        {
            From = NotificationContainer.Opacity,
            To = 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EasingFunction = new CircleEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(opacityAnim, NotificationContainer);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(scaleXAnim);
        storyboard.Children.Add(scaleYAnim);
        storyboard.Children.Add(opacityAnim);

        storyboard.Completed += (_, _) =>
        {
            if (NotificationContainer.Tag is string finalState && finalState == "Closing")
            {
                NotificationContainer.Visibility = Visibility.Collapsed;
            }
        };

        storyboard.Begin();
    }

    private void SetupAutoDismiss(FrameworkElement element, int duration)
    {
        var timer = dispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(duration);
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (element.Tag is string state && state == "Closing")
            {
                return;
            }

            element.Tag = "Closing";
            element.IsHitTestVisible = false;

            DismissInfoBar(element);
        };
        timer.Start();
    }

    private async Task LoadNotificationPositionAsync()
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync("NotificationPosition");
            var position = value != null
                ? (NotificationPosition)Convert.ToInt32(value)
                : NotificationPosition.BottomRight;
            ApplyNotificationPosition(position);
        }
        catch
        {
            ApplyNotificationPosition(NotificationPosition.BottomRight);
        }
    }

    private void ApplyNotificationPosition(NotificationPosition position)
    {
        _notificationPosition = position;

        var navPaneWidth = NavigationView.IsPaneOpen
            ? NavigationView.OpenPaneLength
            : NavigationView.CompactPaneLength;
        var leftMargin = navPaneWidth + 24;

        switch (position)
        {
            case NotificationPosition.TopRight:
                NotificationContainer.HorizontalAlignment = HorizontalAlignment.Right;
                NotificationContainer.VerticalAlignment = VerticalAlignment.Top;
                NotificationContainer.Margin = new Thickness(0, 48, 24, 0);
                break;
            case NotificationPosition.TopLeft:
                NotificationContainer.HorizontalAlignment = HorizontalAlignment.Left;
                NotificationContainer.VerticalAlignment = VerticalAlignment.Top;
                NotificationContainer.Margin = new Thickness(leftMargin, 48, 0, 0);
                break;
            case NotificationPosition.BottomLeft:
                NotificationContainer.HorizontalAlignment = HorizontalAlignment.Left;
                NotificationContainer.VerticalAlignment = VerticalAlignment.Bottom;
                NotificationContainer.Margin = new Thickness(leftMargin, 0, 0, 24);
                break;
            case NotificationPosition.BottomRight:
            default:
                NotificationContainer.HorizontalAlignment = HorizontalAlignment.Right;
                NotificationContainer.VerticalAlignment = VerticalAlignment.Bottom;
                NotificationContainer.Margin = new Thickness(0, 0, 24, 24);
                break;
        }
    }

    #endregion
}