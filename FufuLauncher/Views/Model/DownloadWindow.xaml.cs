/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
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

                    StatusText.Text = $"正在处理: {doneFiles}/{totalFiles} 文件";
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
                    StatusText.Text = "下载成功";
                    StatusText.Foreground = new SolidColorBrush(Colors.Green);
                    CancelButton.Content = "关闭";

                    var dialog = new ContentDialog
                    {
                        Title = "完成",
                        Content = "所有文件下载、校验、部署已完成。",
                        CloseButtonText = "确定",
                        XamlRoot = Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                });
            }
            catch (OperationCanceledException)
            {
                DispatcherQueue.TryEnqueue(() => StatusText.Text = "任务已取消");
            }
            catch (Exception ex)
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    StatusText.Text = "发生错误";
                    StatusText.Foreground = new SolidColorBrush(Colors.Red);
                    var dialog = new ContentDialog
                    {
                        Title = "错误",
                        Content = ex.Message,
                        CloseButtonText = "关闭",
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
            LogToggle.Content = LogToggle.IsChecked == true ? "隐藏详细日志" : "显示详细日志";
        }

        private void SetUIState(bool enabled)
        {
            StartButton.IsEnabled = enabled;
            LanguageCombo.IsEnabled = enabled;
            BaseGameCheck.IsEnabled = enabled;
            PathBox.IsEnabled = enabled;
            CancelButton.IsEnabled = !enabled;
            CancelButton.Content = enabled ? "关闭" : "取消";
        }
    }
}
