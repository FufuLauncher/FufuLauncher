/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services.GameServer;

namespace FufuLauncher.Views
{
    public sealed partial class GameUpdateWindow : Window
    {
        private readonly string _gameDir;
        private readonly GameUpdateOperationKind _kind;
        private readonly bool _autoStart;
        private readonly GameServerScheme? _schemeOverride;
        private string _descriptionText = string.Empty;
        private CancellationTokenSource? _cts;

        private readonly GameServerDownloadMonitor _downloadMonitor = new();
        private readonly RemainingChunksTracker _remainingChunksTracker = new();
        private DownloadSpeedChartController? _chartController;

        public GameUpdateWindow(string gameDir, GameUpdateOperationKind kind, bool autoStart = false,
            GameServerScheme? schemeOverride = null)
        {
            InitializeComponent();
            _gameDir = gameDir;
            _kind = kind;
            _autoStart = autoStart;
            _schemeOverride = schemeOverride;

            if (autoStart)
            {
                Activated += OnAutoStartActivated;
            }

            _chartController = new DownloadSpeedChartController(SpeedGraphChart, _downloadMonitor);
            SpeedGraphChart.NoDataText = "SpeedGraph_NoData".GetLocalized();
            RemainingChunksList.ItemsSource = _remainingChunksTracker.Chunks;

            ApplyKindTexts();

            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            ExtendsContentIntoTitleBar = true;

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var winId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(winId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(600, 640));
        }

        private void OnAutoStartActivated(object sender, WindowActivatedEventArgs args)
        {
            Activated -= OnAutoStartActivated;
            if (StartBtn.IsEnabled)
            {
                BeginOperation();
            }
        }

        private void ApplyKindTexts()
        {
            (string titleKey, string descriptionKey, string startKey) = _kind switch
            {
                GameUpdateOperationKind.Predownload => ("PreDownload_Title", "PreDownload_Description",
                    "PreDownload_Start"),
                GameUpdateOperationKind.Update => ("GameUpdate_UpdateTitle", "GameUpdate_UpdateDescription",
                    "GameUpdate_UpdateStart"),
                _ => ("GameUpdate_ApplyPredownloadTitle", "GameUpdate_ApplyPredownloadDescription",
                    "GameUpdate_ApplyPredownloadStart"),
            };

            Title = titleKey.GetLocalized();
            TitleTextBlock.Text = titleKey.GetLocalized();
            _descriptionText = descriptionKey.GetLocalized();
            DescriptionTextBlock.Text = _descriptionText;
            StartBtn.Content = startKey.GetLocalized();
        }

        private void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            BeginOperation();
        }

        private async void BeginOperation()
        {
            var configurationService = App.GetService<GameServerConfigurationService>();
            var currentScheme = _schemeOverride ?? configurationService.TryDetectCurrentScheme(_gameDir);
            if (currentScheme is null)
            {
                await ShowMessageAsync("AdvancedServerSwitch_UnknownCurrent".GetLocalized());
                return;
            }

            DescriptionTextBlock.Text = $"{_descriptionText} · {currentScheme.DisplayName}";

            StartBtn.IsEnabled = false;
            ProgressPanel.Visibility = Visibility.Visible;
            ProgressBar.IsIndeterminate = true;
            ProgressDetailText.Text = string.Empty;
            CancelBtn.IsEnabled = true;
            _chartController!.Start();
            _remainingChunksTracker.Reset();
            RemainingCountText.Text = string.Format("AdvancedServerSwitch_RemainingChunks".GetLocalized(), 0);

            _cts = new CancellationTokenSource();
            CancellationToken token = _cts.Token;
            var progress =
                new Progress<GameServerConversionProgress>(p => DispatcherQueue.TryEnqueue(() => UpdateProgress(p)));

            try
            {
                var updateService = App.GetService<GameUpdateService>();

                GameUpdateResult result = await Task.Run(() => _kind switch
                {
                    GameUpdateOperationKind.Predownload =>
                        updateService.PredownloadAsync(_gameDir, currentScheme, progress, UpdateStatus, token,
                            _downloadMonitor, ShowConfirmAsync),
                    GameUpdateOperationKind.Update =>
                        updateService.UpdateAsync(_gameDir, currentScheme, false, progress, UpdateStatus, token,
                            _downloadMonitor, ShowConfirmAsync),
                    _ =>
                        updateService.UpdateAsync(_gameDir, currentScheme, true, progress, UpdateStatus, token,
                            _downloadMonitor, ShowConfirmAsync),
                });

                _chartController.Stop();
                ProgressPanel.Visibility = Visibility.Collapsed;

                switch (result)
                {
                    case GameUpdateResult.Completed:
                    {
                        string successKey = _kind switch
                        {
                            GameUpdateOperationKind.Predownload => "PreDownload_Success",
                            GameUpdateOperationKind.Update => "GameUpdate_UpdateSuccess",
                            _ => "GameUpdate_ApplyPredownloadSuccess",
                        };
                        await ShowMessageAsync(successKey.GetLocalized(), "AdvancedServerSwitch_Done".GetLocalized());
                        break;
                    }

                    case GameUpdateResult.NothingToDo:
                    {
                        string nothingKey = _kind == GameUpdateOperationKind.Predownload
                            ? "GameUpdate_NoPredownloadNeeded"
                            : "GameUpdate_AlreadyLatest";
                        await ShowMessageAsync(nothingKey.GetLocalized(), Title);
                        break;
                    }

                    case GameUpdateResult.Cancelled:
                        UpdateStatus("AdvancedServerSwitch_Cancelled".GetLocalized());
                        break;

                    case GameUpdateResult.Failed:
                    {
                        _chartController.SetFailed();
                        string failureKey = _kind == GameUpdateOperationKind.Predownload
                            ? "PreDownload_Failed"
                            : "GameUpdate_UpdateFailed";
                        string message = string.IsNullOrWhiteSpace(StatusText.Text)
                            ? failureKey.GetLocalized()
                            : StatusText.Text;
                        await ShowMessageAsync(message, failureKey.GetLocalized());
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _chartController.SetPaused();
                ProgressPanel.Visibility = Visibility.Collapsed;
                UpdateStatus("AdvancedServerSwitch_Cancelled".GetLocalized());
            }
            catch (Exception ex)
            {
                _chartController.SetFailed();
                ProgressPanel.Visibility = Visibility.Collapsed;
                string failureKey = _kind == GameUpdateOperationKind.Predownload
                    ? "PreDownload_Failed"
                    : "GameUpdate_UpdateFailed";
                await ShowMessageAsync(ex.Message, failureKey.GetLocalized());
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                StartBtn.IsEnabled = true;
                CancelBtn.IsEnabled = false;
            }
        }

        private Task<bool> ShowConfirmAsync(GameUpdatePlan plan)
        {
            var tcs = new TaskCompletionSource<bool>();
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    string titleKey = plan.Kind switch
                    {
                        GameUpdateOperationKind.Predownload => "GameUpdate_ConfirmPredownloadTitle",
                        GameUpdateOperationKind.Update => "GameUpdate_ConfirmUpdateTitle",
                        _ => "GameUpdate_ConfirmApplyPredownloadTitle",
                    };

                    var dialog = new ContentDialog
                    {
                        Title = titleKey.GetLocalized(),
                        Content = string.Format("GameUpdate_ConfirmMessage".GetLocalized(),
                            ToSizeString(plan.DownloadTotalBytes), ToSizeString(plan.InstallTotalBytes),
                            plan.TargetTag),
                        PrimaryButtonText = "OkBtn".GetLocalized(),
                        CloseButtonText = "CloseBtn".GetLocalized(),
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = Content.XamlRoot
                    };

                    ContentDialogResult result = await dialog.ShowAsync();
                    tcs.TrySetResult(result == ContentDialogResult.Primary);
                }
                catch
                {
                    tcs.TrySetResult(false);
                }
            });

            return tcs.Task;
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            CancelBtn.IsEnabled = false;
            _cts?.Cancel();
        }

        private void UpdateProgress(GameServerConversionProgress p)
        {
            _chartController?.UpdateProgress(p);
            _remainingChunksTracker.Update(p);
            RemainingCountText.Text = string.Format("AdvancedServerSwitch_RemainingChunks".GetLocalized(),
                _remainingChunksTracker.Chunks.Count);

            StatusText.Text = p.ChunkName is null
                ? p.Stage
                : string.Format("AdvancedServerSwitch_Progress".GetLocalized(), p.DoneChunks, p.TotalChunks,
                    p.ChunkName);

            if (p.TotalChunks > 0)
            {
                ProgressBar.IsIndeterminate = false;
                ProgressBar.Maximum = p.TotalChunks;
                ProgressBar.Value = p.DoneChunks;
                ProgressDetailText.Text = p.TotalBytes > 0
                    ? $"{p.DoneBytes / 1048576.0:F1} MB / {p.TotalBytes / 1048576.0:F1} MB ({p.Percent:F1}%)"
                    : $"{p.Percent:F1}%";
            }
            else
            {
                ProgressBar.IsIndeterminate = true;
                ProgressDetailText.Text = string.Empty;
            }
        }

        private void UpdateStatus(string message)
        {
            DispatcherQueue.TryEnqueue(() => StatusText.Text = message);
        }

        private async Task ShowMessageAsync(string message, string? title = null)
        {
            var dialog = new ContentDialog
            {
                Title = title ?? "ErrorTitle".GetLocalized(),
                Content = message,
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private static string ToSizeString(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return $"{bytes / 1073741824.0:F1} GB";
            }

            if (bytes >= 1024L * 1024L)
            {
                return $"{bytes / 1048576.0:F1} MB";
            }

            return $"{bytes / 1024.0:F1} KB";
        }
    }
}