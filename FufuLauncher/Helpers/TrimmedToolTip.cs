/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Windows.Foundation;

namespace FufuLauncher.Helpers;

public static class TrimmedToolTip
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(TrimmedToolTip),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly ConditionalWeakTable<TextBlock, TrackState> States = new();

    private const double OverflowTolerance = 0.5;
    private const double ToolTipMaxWidth = 420;

    private sealed class TrackState
    {
        public long TextCallbackToken
        {
            get;
            set;
        }

        public bool UpdateQueued
        {
            get;
            set;
        }

        public ToolTip? OwnedToolTip
        {
            get;
            set;
        }
    }

    public static bool GetIsEnabled(DependencyObject element)
        => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value)
        => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock)
        {
            return;
        }

        if (e.NewValue is true)
        {
            var state = States.GetOrCreateValue(textBlock);
            if (state.TextCallbackToken == 0)
            {
                state.TextCallbackToken =
                    textBlock.RegisterPropertyChangedCallback(TextBlock.TextProperty, OnTextPropertyChanged);
            }

            textBlock.Loaded += OnTextBlockLoaded;
            textBlock.SizeChanged += OnTextBlockSizeChanged;
            ScheduleUpdate(textBlock);
            return;
        }

        textBlock.Loaded -= OnTextBlockLoaded;
        textBlock.SizeChanged -= OnTextBlockSizeChanged;

        if (States.TryGetValue(textBlock, out var existing) && existing.TextCallbackToken != 0)
        {
            textBlock.UnregisterPropertyChangedCallback(TextBlock.TextProperty, existing.TextCallbackToken);
            existing.TextCallbackToken = 0;
        }

        ClearToolTip(textBlock);
    }

    private static void OnTextBlockLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            ScheduleUpdate(textBlock);
        }
    }

    private static void OnTextBlockSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is TextBlock textBlock)
        {
            ScheduleUpdate(textBlock);
        }
    }

    private static void OnTextPropertyChanged(DependencyObject sender, DependencyProperty dp)
    {
        if (sender is TextBlock textBlock)
        {
            ScheduleUpdate(textBlock);
        }
    }

    private static void ScheduleUpdate(TextBlock textBlock)
    {
        var dispatcher = textBlock.DispatcherQueue;
        if (dispatcher is null || dispatcher.HasThreadAccess is false)
        {
            return;
        }

        var state = States.GetOrCreateValue(textBlock);
        if (state.UpdateQueued)
        {
            return;
        }

        state.UpdateQueued = true;
        _ = dispatcher.TryEnqueue(() => UpdateToolTip(textBlock));
    }

    private static void UpdateToolTip(TextBlock textBlock)
    {
        var state = States.GetOrCreateValue(textBlock);
        state.UpdateQueued = false;

        if (!GetIsEnabled(textBlock))
        {
            return;
        }

        var fullText = ResolveText(textBlock);
        if (fullText.Length == 0 || !IsTextOverflowing(textBlock, fullText))
        {
            ClearToolTip(textBlock);
            return;
        }

        ApplyToolTip(textBlock, fullText);
    }

    private static void ApplyToolTip(TextBlock textBlock, string text)
    {
        var state = States.GetOrCreateValue(textBlock);
        if (state.OwnedToolTip?.Content is TextBlock current && string.Equals(current.Text, text, StringComparison.Ordinal))
        {
            return;
        }

        var toolTip = new ToolTip
        {
            Content = new TextBlock
            {
                Text = text,
                MaxWidth = ToolTipMaxWidth,
                TextWrapping = TextWrapping.Wrap
            }
        };

        state.OwnedToolTip = toolTip;
        ToolTipService.SetToolTip(textBlock, toolTip);
    }

    private static void ClearToolTip(TextBlock textBlock)
    {
        if (!States.TryGetValue(textBlock, out var state) || state.OwnedToolTip is null)
        {
            return;
        }

        state.OwnedToolTip = null;
        ToolTipService.SetToolTip(textBlock, null);
    }

    private static string ResolveText(TextBlock textBlock)
    {
        if (!string.IsNullOrEmpty(textBlock.Text))
        {
            return textBlock.Text;
        }

        if (textBlock.Inlines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendInlines(textBlock.Inlines, builder);
        return builder.ToString();
    }

    private static void AppendInlines(IEnumerable<Inline> inlines, StringBuilder builder)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    builder.Append(run.Text);
                    break;
                case Span span:
                    AppendInlines(span.Inlines, builder);
                    break;
                case LineBreak:
                    builder.Append('\n');
                    break;
            }
        }
    }

    private static bool IsTextOverflowing(TextBlock textBlock, string fullText)
    {
        if (textBlock.IsTextTrimmed)
        {
            return true;
        }

        if (textBlock.TextWrapping is not (TextWrapping.Wrap or TextWrapping.WrapWholeWords) || textBlock.MaxLines <= 0)
        {
            return false;
        }

        var availableWidth = textBlock.ActualWidth - textBlock.Padding.Left - textBlock.Padding.Right;
        var availableHeight = textBlock.ActualHeight - textBlock.Padding.Top - textBlock.Padding.Bottom;
        if (availableWidth <= OverflowTolerance || availableHeight <= OverflowTolerance)
        {
            return false;
        }

        var probe = CreateProbe(textBlock, fullText);

        try
        {
            probe.Measure(new Size(availableWidth, double.PositiveInfinity));
        }
        catch (Exception ex)
        {
            DebugLog.Info("TrimmedToolTip", $"[TrimmedToolTip] Text measurement failed: {ex.Message}");
            return false;
        }

        return probe.DesiredSize.Height > availableHeight + OverflowTolerance;
    }

    private static TextBlock CreateProbe(TextBlock source, string text)
    {
        return new TextBlock
        {
            Text = text,
            FlowDirection = source.FlowDirection,
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontStyle = source.FontStyle,
            FontStretch = source.FontStretch,
            FontWeight = source.FontWeight,
            CharacterSpacing = source.CharacterSpacing,
            IsTextScaleFactorEnabled = source.IsTextScaleFactorEnabled,
            LineHeight = source.LineHeight,
            LineStackingStrategy = source.LineStackingStrategy,
            TextWrapping = source.TextWrapping,
            TextTrimming = TextTrimming.None,
            MaxLines = 0
        };
    }
}
