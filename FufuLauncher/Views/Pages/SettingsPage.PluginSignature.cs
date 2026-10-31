/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    private async void OnVerifyPluginSignaturesClick(object sender, RoutedEventArgs e)
    {
        IReadOnlyList<PluginSignatureInfo> results;

        var progressBar = new ProgressBar
        {
            IsIndeterminate = true,
            Height = 20,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var statusText = new TextBlock
        {
            Text = "PluginSignature_Checking".GetLocalized(),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        var progressContent = new StackPanel();
        progressContent.Children.Add(statusText);
        progressContent.Children.Add(progressBar);

        var progressDialog = new ContentDialog
        {
            Title = "Settings_PluginSignature_Title".GetLocalized(),
            Content = progressContent,
            XamlRoot = XamlRoot
        };

        _ = progressDialog.ShowAsync();

        try
        {
            results = await Task.Run(PluginSignatureVerifier.VerifyInstalledPlugins);
        }
        catch (Exception ex)
        {
            progressDialog.Hide();

            var failDialog = new ContentDialog
            {
                Title = "ErrorTitle".GetLocalized(),
                Content = string.Format("PluginSignature_Failed".GetLocalized(), ex.Message),
                CloseButtonText = "CloseBtn".GetLocalized(),
                XamlRoot = XamlRoot
            };

            await failDialog.ShowAsync();
            return;
        }

        progressDialog.Hide();

        await ShowPluginSignatureDialogAsync(results);
    }

    private async Task ShowPluginSignatureDialogAsync(IReadOnlyList<PluginSignatureInfo> results)
    {
        var list = new StackPanel { Spacing = 8 };

        if (results.Count == 0)
        {
            list.Children.Add(new TextBlock
            {
                Text = "PluginSignature_NoPlugins".GetLocalized(),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8
            });
        }
        else
        {
            foreach (var info in results)
            {
                list.Children.Add(BuildPluginSignatureEntry(info));
            }
        }

        var content = new ScrollViewer
        {
            Content = list,
            MaxHeight = 420,
            MinWidth = 420,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollMode = ScrollMode.Auto
        };

        var dialog = new ContentDialog
        {
            Title = "PluginSignature_DialogTitle".GetLocalized(),
            Content = content,
            CloseButtonText = "CloseBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            Resources = { ["ContentDialogMaxWidth"] = 640.0 }
        };

        await dialog.ShowAsync();
    }

    private static FrameworkElement BuildPluginSignatureEntry(PluginSignatureInfo info)
    {
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };

        header.Children.Add(new TextBlock
        {
            Text = info.DisplayName,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });

        header.Children.Add(new TextBlock
        {
            Text = info.IsSigned ? "PluginSignature_Signed".GetLocalized() : "PluginSignature_Unsigned".GetLocalized(),
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Center
        });

        if (!info.IsSigned)
        {
            var unsignedPanel = new StackPanel { Spacing = 4 };
            unsignedPanel.Children.Add(header);
            unsignedPanel.Children.Add(new TextBlock
            {
                Text = info.TrustStatus,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75
            });
            unsignedPanel.Children.Add(new TextBlock
            {
                Text = Path.GetFileName(info.FilePath),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.5
            });

            return new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                BorderThickness = new Thickness(0),
                Child = unsignedPanel
            };
        }

        var detailsPanel = new StackPanel { Spacing = 6, Padding = new Thickness(0, 4, 0, 0) };
        foreach (var detail in info.Details)
        {
            detailsPanel.Children.Add(BuildDetailRow(detail));
        }

        return new Expander
        {
            FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["AppGlobalFont"],
            Header = header,
            Content = detailsPanel,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
    }

    private static FrameworkElement BuildDetailRow(PluginSignatureDetail detail)
    {
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = detail.Label,
            FontSize = 12,
            Opacity = 0.6,
            VerticalAlignment = VerticalAlignment.Top,
            TextWrapping = TextWrapping.Wrap
        };

        var value = new TextBlock
        {
            Text = detail.Value,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };

        Grid.SetColumn(value, 1);

        row.Children.Add(label);
        row.Children.Add(value);

        return row;
    }
}