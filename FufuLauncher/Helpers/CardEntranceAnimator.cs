/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace FufuLauncher.Helpers;

internal sealed class CardEntranceAnimator
{
    private const string TranslationProperty = "Translation";
    private const int StaggerMilliseconds = 26;
    private const int MaximumDelayMilliseconds = 210;
    private const int WindowMilliseconds = 700;
    private const float VerticalOffset = 16f;

    private readonly string _module;
    private readonly HashSet<UIElement> _containers = new();

    private bool _active;
    private bool _failed;
    private int _session;
    private long _deadline;
    private long _nextAt;
    private UISettings? _preferences;
    private ScalarKeyFrameAnimation? _fade;
    private Vector3KeyFrameAnimation? _slide;

    public CardEntranceAnimator(string module)
    {
        _module = module;
    }

    public void Begin(ItemsControl target)
    {
        Stop();

        if (!AnimationsEnabled() || target.Items.Count == 0)
        {
            return;
        }

        _active = true;
        _deadline = Environment.TickCount64 + WindowMilliseconds;
        _nextAt = 0;
        var session = ++_session;

        try
        {
            target.UpdateLayout();
            AnimateContainers(target, session);
        }
        catch (Exception ex)
        {
            _active = false;
            DebugLog.Info(_module, $"[CardEntrance] Entrance failed: {ex}");
        }
        finally
        {
            if (session == _session)
            {
                _active = false;
            }
        }
    }

    public void ObserveContainer(ContainerContentChangingEventArgs args)
    {
        if (args.Phase != 0)
        {
            return;
        }

        if (args.InRecycleQueue)
        {
            if (args.ItemContainer is UIElement recycled && _containers.Remove(recycled))
            {
                RestoreContainer(recycled);
            }

            return;
        }

        if (!_active)
        {
            return;
        }

        if (Environment.TickCount64 > _deadline)
        {
            _active = false;
            return;
        }

        if (args.ItemContainer is UIElement container)
        {
            TryAnimateContainer(container);
        }
    }

    public void Stop()
    {
        _active = false;
        _session++;

        if (_containers.Count > 0)
        {
            foreach (var container in _containers)
            {
                RestoreContainer(container);
            }

            _containers.Clear();
        }

        _fade?.Dispose();
        _slide?.Dispose();
        _fade = null;
        _slide = null;
    }

    private void AnimateContainers(ItemsControl target, int session)
    {
        var count = target.Items.Count;

        for (var index = 0; index < count; index++)
        {
            if (session != _session || !_active)
            {
                return;
            }

            if (Environment.TickCount64 > _deadline)
            {
                return;
            }

            if (target.ContainerFromIndex(index) is not UIElement container)
            {
                return;
            }

            TryAnimateContainer(container);
        }
    }

    private void TryAnimateContainer(UIElement container)
    {
        if (!_containers.Add(container))
        {
            return;
        }

        try
        {
            ElementCompositionPreview.SetIsTranslationEnabled(container, true);
            var visual = ElementCompositionPreview.GetElementVisual(container);
            PrepareAnimations(visual.Compositor);

            var now = Environment.TickCount64;
            var delayMilliseconds =
                Math.Clamp(_nextAt - now, 0, MaximumDelayMilliseconds);
            _nextAt = now + delayMilliseconds + StaggerMilliseconds;

            var delay = TimeSpan.FromMilliseconds(delayMilliseconds);
            var fade = _fade!;
            var slide = _slide!;
            fade.DelayTime = delay;
            slide.DelayTime = delay;

            visual.Properties.InsertVector3(TranslationProperty, new Vector3(0, VerticalOffset, 0));
            visual.Opacity = 0;
            visual.StartAnimation(nameof(Visual.Opacity), fade);
            visual.Properties.StartAnimation(TranslationProperty, slide);
        }
        catch (Exception ex)
        {
            _containers.Remove(container);
            _active = false;
            _failed = true;
            RestoreContainer(container);
            DebugLog.Info(_module, $"[CardEntrance] Entrance animation failed: {ex}");
        }
    }

    private void PrepareAnimations(Compositor compositor)
    {
        if (_fade != null && _slide != null && _fade.Compositor.Equals(compositor))
        {
            return;
        }

        _fade?.Dispose();
        _slide?.Dispose();

        var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.16f, 1), new Vector2(0.3f, 1));

        _fade = compositor.CreateScalarKeyFrameAnimation();
        _fade.InsertKeyFrame(0, 0);
        _fade.InsertKeyFrame(1, 1, easing);
        _fade.Duration = TimeSpan.FromMilliseconds(240);
        _fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        _fade.StopBehavior = AnimationStopBehavior.SetToFinalValue;

        _slide = compositor.CreateVector3KeyFrameAnimation();
        _slide.InsertKeyFrame(0, new Vector3(0, VerticalOffset, 0));
        _slide.InsertKeyFrame(1, Vector3.Zero, easing);
        _slide.Duration = TimeSpan.FromMilliseconds(340);
        _slide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        _slide.StopBehavior = AnimationStopBehavior.SetToFinalValue;
    }

    private bool AnimationsEnabled()
    {
        if (_failed)
        {
            return false;
        }

        try
        {
            _preferences ??= new UISettings();
            return _preferences.AnimationsEnabled;
        }
        catch (Exception ex)
        {
            _failed = true;
            DebugLog.Info(_module, $"[CardEntrance] Animation preference read failed: {ex}");
            return false;
        }
    }

    private static void RestoreContainer(UIElement container)
    {
        try
        {
            var visual = ElementCompositionPreview.GetElementVisual(container);
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.Properties.StopAnimation(TranslationProperty);
            visual.Properties.InsertVector3(TranslationProperty, Vector3.Zero);
            visual.Opacity = (float)container.Opacity;
        }
        catch (Exception ex)
        {
            DebugLog.Info("CardEntranceAnimator", $"[CardEntrance] Reset failed: {ex.Message}");
        }
    }
}
