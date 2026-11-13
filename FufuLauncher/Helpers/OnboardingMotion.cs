/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher.Helpers;

public sealed class OnboardingMotion
{
    public const double SlideOffset = 30;

    private static readonly TimeSpan EntranceDuration = TimeSpan.FromMilliseconds(380);
    private static readonly TimeSpan EntranceStagger = TimeSpan.FromMilliseconds(65);
    private static readonly TimeSpan ExitDuration = TimeSpan.FromMilliseconds(240);
    private static readonly TimeSpan ExitStagger = TimeSpan.FromMilliseconds(38);

    private readonly Dictionary<FrameworkElement, Storyboard> _running = new();
    private bool? _animationEnabled;

    public bool AnimationEnabled
    {
        get
        {
            _animationEnabled ??= ResolveAnimationEnabled();
            return _animationEnabled.Value;
        }
    }

    public static IReadOnlyList<FrameworkElement> CollectVisible(Panel panel)
    {
        var items = new List<FrameworkElement>();
        foreach (var child in panel.Children)
        {
            if (child is FrameworkElement element && element.Visibility == Visibility.Visible)
            {
                items.Add(element);
            }
        }

        return items;
    }

    public void Settle(IEnumerable<FrameworkElement> elements)
    {
        foreach (var element in elements)
        {
            StopRunning(element);
            element.Opacity = 1;
            EnsureTranslate(element).Y = 0;
        }
    }

    public void Prepare(IEnumerable<FrameworkElement> elements)
    {
        foreach (var element in elements)
        {
            StopRunning(element);
            element.Opacity = 0;
            EnsureTranslate(element).Y = SlideOffset;
        }
    }

    public Task EnterAsync(IReadOnlyList<FrameworkElement> elements)
    {
        return PlayAsync(elements, true);
    }

    public Task ExitAsync(IReadOnlyList<FrameworkElement> elements)
    {
        return PlayAsync(elements, false);
    }

    private static bool ResolveAnimationEnabled()
    {
        try
        {
            return new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        }
        catch
        {
            return true;
        }
    }

    private async Task PlayAsync(IReadOnlyList<FrameworkElement> elements, bool entrance)
    {
        if (elements.Count == 0)
        {
            return;
        }

        if (!AnimationEnabled)
        {
            ApplyFinalState(elements, entrance);
            return;
        }

        var storyboard = new Storyboard();
        var easing = entrance
            ? new CubicEase { EasingMode = EasingMode.EaseOut }
            : new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = new Duration(entrance ? EntranceDuration : ExitDuration);
        var stagger = entrance ? EntranceStagger : ExitStagger;

        for (var index = 0; index < elements.Count; index++)
        {
            var element = elements[index];
            StopRunning(element);

            var order = entrance ? index : elements.Count - 1 - index;
            var beginTime = TimeSpan.FromMilliseconds(stagger.TotalMilliseconds * order);
            var translate = EnsureTranslate(element);

            storyboard.Children.Add(
                CreateAnimation(element, "Opacity", entrance ? 1d : 0d, duration, beginTime, easing));
            storyboard.Children.Add(CreateAnimation(translate, "Y", entrance ? 0d : SlideOffset, duration, beginTime,
                easing));

            _running[element] = storyboard;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        storyboard.Completed += (_, _) => completion.TrySetResult(true);
        storyboard.Begin();

        await completion.Task;

        try
        {
            storyboard.Stop();
        }
        catch
        {
        }

        ApplyFinalState(elements, entrance);

        foreach (var element in elements)
        {
            _running.Remove(element);
        }
    }

    private static void ApplyFinalState(IReadOnlyList<FrameworkElement> elements, bool entrance)
    {
        foreach (var element in elements)
        {
            element.Opacity = entrance ? 1 : 0;
            EnsureTranslate(element).Y = entrance ? 0 : SlideOffset;
        }
    }

    private void StopRunning(FrameworkElement element)
    {
        if (!_running.Remove(element, out var storyboard))
        {
            return;
        }

        try
        {
            storyboard.Stop();
        }
        catch
        {
        }
    }

    private static TranslateTransform EnsureTranslate(FrameworkElement element)
    {
        if (element.RenderTransform is TranslateTransform translate)
        {
            return translate;
        }

        translate = new TranslateTransform();
        element.RenderTransform = translate;
        return translate;
    }

    private static DoubleAnimation CreateAnimation(DependencyObject target, string property, double to,
        Duration duration, TimeSpan beginTime, EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            BeginTime = beginTime,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        return animation;
    }
}