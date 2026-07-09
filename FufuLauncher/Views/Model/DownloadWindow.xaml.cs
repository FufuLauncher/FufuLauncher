/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;

namespace FufuLauncher.Views
{
    public sealed partial class DownloadWindow : Window
    {
        private readonly string _installPath;
        private CancellationTokenSource _cts;
        private bool _isDownloading = false;

        public DownloadWindow(string installPath)
        {
            InitializeComponent();
            _installPath = installPath;
            PathBox.Text = _installPath;

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var hWnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (appWindow != null)
            {
                appWindow.Resize(new Windows.Graphics.SizeInt32(900, 700));
            }

            Closed += (s, e) => { if (_isDownloading) _cts?.Cancel(); };
        }

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            _isDownloading = true;
            SetUIState(false);

            LogBlock.Text = ">>> 初始化任务...\n";
            MainProgressBar.Value = 0;
            StatusText.Foreground = (SolidColorBrush)Application.Current.Resources["TextFillColorPrimaryBrush"];

            _cts = new CancellationTokenSource();

            var downloader = new GenshinDownloader();

            downloader.Log += (msg) => DispatcherQueue.TryEnqueue(() =>
            {
                if (LogBorder.Visibility == Visibility.Visible)
                {
                    LogBlock.Text += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
                    LogScroller.ChangeView(null, LogScroller.ScrollableHeight, null);
                }
            });

            downloader.ProgressChanged += (downloaded, total, doneFiles, totalFiles) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (total <= 0) return;
                    double percent = (double)downloaded / total * 100;
                    MainProgressBar.Value = percent;

                    StatusText.Text = string.Format("DownloadWindow_Processing".GetLocalized(), doneFiles, totalFiles);
                    double dMB = downloaded / 1024.0 / 1024.0;
                    double tMB = total / 1024.0 / 1024.0;
                    ProgressText.Text = $"{dMB:F1} MB / {tMB:F1} MB ({percent:F1}%)";
                });
            };

            try
            {
                var lang = ((ComboBoxItem)LanguageCombo.SelectedItem).Tag.ToString();
                var downloadBase = BaseGameCheck.IsChecked == true;

                await Task.Run(() => downloader.StartDownloadAsync(_installPath, lang, downloadBase, 16, _cts.Token));

                DispatcherQueue.TryEnqueue(async () =>
                {
                    MainProgressBar.Value = 100;
                    StatusText.Text = "DownloadWindow_Success".GetLocalized();
                    StatusText.Foreground = new SolidColorBrush(Colors.Green);
                    CancelButton.Content = "CloseBtn".GetLocalized();

                    var dialog = new ContentDialog
                    {
                        Title = "DownloadWindow_Complete".GetLocalized(),
                        Content = "DownloadWindow_AllDone".GetLocalized(),
                        CloseButtonText = "OkBtn".GetLocalized(),
                        XamlRoot = Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                });
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() => StatusText.Text = "DownloadWindow_Cancelled".GetLocalized());
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    StatusText.Text = "DownloadWindow_Error".GetLocalized();
                    StatusText.Foreground = new SolidColorBrush(Colors.Red);
                    var dialog = new ContentDialog
                    {
                        Title = "ErrorTitle".GetLocalized(),
                        Content = ex.Message,
                        CloseButtonText = "CloseBtn".GetLocalized(),
                        XamlRoot = Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                });
            }
            finally
            {
                _isDownloading = false;
                _cts = null;
                SetUIState(true);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isDownloading) _cts?.Cancel();
            else Close();
        }

        private void LogToggle_Click(object sender, RoutedEventArgs e)
        {
            LogBorder.Visibility = LogToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            LogToggle.Content = LogToggle.IsChecked == true ? "DownloadWindow_HideDetailLog".GetLocalized() : "DownloadWindow_ShowDetailLog".GetLocalized();
        }

        private void SetUIState(bool enabled)
        {
            StartButton.IsEnabled = enabled;
            LanguageCombo.IsEnabled = enabled;
            BaseGameCheck.IsEnabled = enabled;
            PathBox.IsEnabled = enabled;
            CancelButton.IsEnabled = !enabled;
            CancelButton.Content = enabled ? "CloseBtn".GetLocalized() : "CancelBtn".GetLocalized();
        }
    }
}
