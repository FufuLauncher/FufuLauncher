using FufuLauncher.Helpers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    private bool _isVerifyingSsl;

    private async void OnVerifySslCertificatesClick(object sender, RoutedEventArgs e)
    {
        if (_isVerifyingSsl || XamlRoot == null) return;
        _isVerifyingSsl = true;

        try
        {
            var progressText = new TextBlock
            {
                Text = "SslCheck_CheckingInitial".GetLocalized(),
                TextWrapping = TextWrapping.Wrap
            };
            var progressBar = new ProgressBar
            {
                IsIndeterminate = true
            };
            var progressPanel = new StackPanel
            {
                Spacing = 12,
                MinWidth = 340
            };
            progressPanel.Children.Add(progressText);
            progressPanel.Children.Add(progressBar);

            using var cts = new CancellationTokenSource();
            var completed = false;

            var progressDialog = new ContentDialog
            {
                Title = "Settings_SslCheck_Title".GetLocalized(),
                Content = progressPanel,
                CloseButtonText = "CancelBtn".GetLocalized(),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
                RequestedTheme = ActualTheme
            };
            progressDialog.Closing += (_, _) =>
            {
                if (!completed)
                {
                    cts.Cancel();
                }
            };

            Task<ContentDialogResult>? dialogTask = null;
            try
            {
                dialogTask = progressDialog.ShowAsync().AsTask();
            }
            catch
            {
                // ignored
            }

            IReadOnlyList<SslCertificateInfo> results;
            try
            {
                results = await Task.Run(() => SslCertificateVerifier.VerifyAllAsync(
                    (done, total, host) =>
                    {
                        DispatcherQueue?.TryEnqueue(() =>
                        {
                            progressBar.IsIndeterminate = false;
                            progressBar.Maximum = total;
                            progressBar.Value = done;
                            progressText.Text = string.Format(
                                "SslCheck_CheckingProgress".GetLocalized(),
                                done,
                                total,
                                host);
                        });
                    },
                    cts.Token));
            }
            catch (OperationCanceledException)
            {
                completed = true;
                TryHideDialog(progressDialog);
                if (dialogTask != null)
                {
                    try { await dialogTask; } catch { }
                }

                return;
            }
            catch (Exception ex)
            {
                completed = true;
                TryHideDialog(progressDialog);
                if (dialogTask != null)
                {
                    try { await dialogTask; } catch { }
                }

                await ShowSafeDialogAsync(new ContentDialog
                {
                    Title = "ErrorTitle".GetLocalized(),
                    Content = string.Format("SslCheck_Failed".GetLocalized(), ex.Message),
                    CloseButtonText = "CloseBtn".GetLocalized(),
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = XamlRoot,
                    RequestedTheme = ActualTheme
                });
                return;
            }

            completed = true;
            TryHideDialog(progressDialog);
            if (dialogTask != null)
            {
                try { await dialogTask; } catch { }
            }

            if (cts.IsCancellationRequested)
            {
                return;
            }

            await ShowSslCheckDialogAsync(results);
        }
        finally
        {
            _isVerifyingSsl = false;
        }
    }

    private async Task ShowSslCheckDialogAsync(IReadOnlyList<SslCertificateInfo> results)
    {
        if (XamlRoot == null) return;

        object content;
        if (results.Count == 0)
        {
            content = new TextBlock
            {
                Text = "SslCheck_NoEndpoints".GetLocalized(),
                TextWrapping = TextWrapping.Wrap
            };
        }
        else
        {
            var totalEndpoints = results.Sum(r => r.StackedCount);
            var validCount = results.Count(r => r.IsValid);
            var invalidCount = results.Count - validCount;

            var rootPanel = new StackPanel { Spacing = 8 };
            rootPanel.Children.Add(new TextBlock
            {
                Text = string.Format(
                    "SslCheck_SummaryFormat".GetLocalized(),
                    totalEndpoints,
                    results.Count,
                    validCount,
                    invalidCount),
                FontSize = 12,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap
            });

            var list = new StackPanel { Spacing = 8 };
            foreach (var info in results)
            {
                list.Children.Add(BuildSslEntry(info));
            }

            rootPanel.Children.Add(list);

            content = new ScrollViewer
            {
                Content = rootPanel,
                MaxHeight = 420,
                MinWidth = 420,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
        }

        var dialog = new ContentDialog
        {
            Title = "SslCheck_DialogTitle".GetLocalized(),
            Content = content,
            CloseButtonText = "CloseBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme
        };
        dialog.Resources["ContentDialogMaxWidth"] = 640.0;

        await ShowSafeDialogAsync(dialog);
    }

    private static FrameworkElement BuildSslEntry(SslCertificateInfo info)
    {
        var title = info.StackedCount > 1
            ? string.Format("SslCheck_StackedHostFormat".GetLocalized(), info.DisplayHost, info.StackedCount)
            : info.DisplayHost;

        var subtitle = !info.HasCertificate
            ? (string.IsNullOrWhiteSpace(info.ErrorMessage)
                ? info.StatusSummary
                : $"{info.StatusSummary}  ·  {info.ErrorMessage}")
            : string.IsNullOrWhiteSpace(info.CertificateSummary)
                ? info.StatusSummary
                : $"{info.StatusSummary}  ·  {info.CertificateSummary}";

        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        header.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (info.Details.Count == 0)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(12),
                BorderThickness = new Thickness(0),
                Child = header
            };
        }

        var detailsPanel = new StackPanel
        {
            Spacing = 4,
            Padding = new Thickness(0, 4, 0, 4)
        };
        foreach (var detail in info.Details)
        {
            detailsPanel.Children.Add(BuildSslDetailRow(detail));
        }

        return new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Header = header,
            Content = detailsPanel
        };
    }

    private static FrameworkElement BuildSslDetailRow(SslCertificateDetail detail)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = detail.Label,
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetColumn(label, 0);

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

    private static void TryHideDialog(ContentDialog dialog)
    {
        try
        {
            dialog.Hide();
        }
        catch
        {
            // ignored
        }
    }
}
