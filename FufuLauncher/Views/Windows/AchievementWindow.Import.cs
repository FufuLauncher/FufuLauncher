/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Net;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class AchievementWindow
{
    private async void StartLocalServer()
    {
        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(ApiEndpoints.AchievementImportListenerUrl);
            _listener.Start();

            await Task.Run(async () =>
            {
                while (_keepRunning)
                {
                    try
                    {
                        var context = await _listener.GetContextAsync();
                        _ = HandleIncomingFile(context).ContinueWith(t =>
                        {
                            if (t.IsFaulted)
                                DebugLog.Info("AchievementWindow.Import",
                                    $"Failed to handle request: {t.Exception?.InnerException?.Message}");
                        }, TaskContinuationOptions.OnlyOnFaulted);
                    }
                    catch
                    {
                        break;
                    }
                }
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("AchievementWindow.Import", "Failed to start port listener: " + ex.Message);
        }
    }

    private async Task HandleIncomingFile(HttpListenerContext context)
    {
        if (_isBatchProcessing)
        {
            context.Response.StatusCode = 503;
            context.Response.Close();
            return;
        }

        try
        {
            if (context.Request.HttpMethod == "POST")
            {
                string tempFile = Path.GetTempFileName();

                using (var input = context.Request.InputStream)
                using (var output = File.Create(tempFile))
                {
                    await input.CopyToAsync(output);
                }

                DispatcherQueue.TryEnqueue(async () =>
                {
                    await RunImportLogic(tempFile);

                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    {
                    }
                });

                byte[] b = "Import Started"u8.ToArray();
                context.Response.StatusCode = 200;
                context.Response.OutputStream.Write(b, 0, b.Length);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("AchievementWindow.Import", ex);
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var path = await FilePickerService.PickOpenFileAsync(
            this,
            new[] { ("AchievementWindow_CsvTextFilter".GetLocalized(), new[] { ".csv", ".txt" }) },
            Windows.Storage.Pickers.PickerLocationId.Desktop,
            msg => _ = ShowDialogAsync("ErrorTitle".GetLocalized(), msg));

        if (!string.IsNullOrEmpty(path))
        {
            await RunImportLogic(path);
        }
    }

    private async Task RunImportLogic(string filePath)
    {
        if (_isBatchProcessing) return;
        _isBatchProcessing = true;

        var progressBar = new ProgressBar
            { Value = 0, Maximum = 100, Height = 10, Margin = new Thickness(0, 15, 0, 5) };
        var statusText = new TextBlock
            { Text = "AchievementWindow_PreparingRead".GetLocalized(), FontSize = 13, Opacity = 0.8 };
        var stackPanel = new StackPanel { Width = 380, Spacing = 5 };
        stackPanel.Children.Add(statusText);
        stackPanel.Children.Add(progressBar);

        var progressDialog = new ContentDialog
        {
            Title = "AchievementWindow_Importing".GetLocalized(),
            Content = stackPanel,
            CloseButtonText = null,
            XamlRoot = Content.XamlRoot
        };

        var dialogTask = progressDialog.ShowAsync();

        try
        {
            var lines = new List<string>();
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = (await sr.ReadLineAsync())!) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                progressDialog.Hide();
                await ShowDialogAsync("AchievementWindow_ReadFailed".GetLocalized(), string.Format("AchievementWindow_FileUnreadable_Format".GetLocalized(), ex.Message));
                return;
            }

            if (lines.Count == 0)
            {
                progressDialog.Hide();
                await ShowDialogAsync("AchievementWindow_EmptyFile".GetLocalized(), "AchievementWindow_EmptyContent".GetLocalized());
                return;
            }

            var result = await Task.Run(() => ParseAndImport(lines, (percent, msg) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    progressBar.Value = percent;
                    statusText.Text = msg;
                });
            }));

            if (result.PendingUpdates.Count > 0)
            {
                statusText.Text = "AchievementWindow_ApplyingChanges".GetLocalized();

                foreach (var update in result.PendingUpdates)
                {
                    update.Item.CurrentProgress = update.Current;
                    update.Item.MaxProgress = update.Max;

                    if (update.ShouldComplete)
                    {
                        update.Item.IsCompleted = true;
                        if (update.Item.CompletionTimestamp == 0)
                        {
                            update.Item.CompletionTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                        }
                    }
                }

                foreach (var cat in ViewModel.Categories) cat.RefreshProgress();

                CalculateGlobalStats();
                SaveData();

                ApplyFilters();

                ViewModel.StatusMessage = string.Format("AchievementWindow_ImportSynced_Format".GetLocalized(), result.UpdatedCount);
            }
            else
            {
                ViewModel.StatusMessage = "AchievementWindow_ImportNoChanges".GetLocalized();
            }

            progressDialog.Hide();
            await dialogTask;

            string resultMsg = string.Format("AchievementWindow_ImportStats_Format".GetLocalized(),
                                   result.TotalScanned,
                                   result.SkippedIncomplete,
                                   result.UpdatedCount,
                                   result.AlreadyDone,
                                   result.FailedCount) +
                               (result.Errors.Any() ? "\n\n" + "AchievementWindow_ImportPartialUnrecognizedHeader".GetLocalized() + "\n" + string.Join("\n", result.Errors.Take(3)) : "");

            await ShowDialogAsync("AchievementWindow_ImportData".GetLocalized(), resultMsg);
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            await ShowDialogAsync("ErrorTitle".GetLocalized(), string.Format("AchievementWindow_ImportException_Format".GetLocalized(), ex.Message));
        }
        finally
        {
            _isBatchProcessing = false;
        }
    }

    private ImportStats ParseAndImport(List<string> lines, Action<double, string> reportProgress)
    {
        var stats = new ImportStats();
        var total = lines.Count;

        var idMap = new Dictionary<int, AchievementItem>();

        foreach (var cat in ViewModel.Categories)
        {
            foreach (var item in cat.Achievements)
            {
                if (item.IsGroup)
                {
                    foreach (var child in item.Children)
                    {
                        idMap[child.Id] = child;
                    }
                }
                else
                {
                    idMap[item.Id] = item;
                }
            }
        }

        reportProgress(10, "AchievementWindow_AnalyzingProgress".GetLocalized());

        for (int i = 0; i < total; i++)
        {
            if (i % 100 == 0) reportProgress(10 + (double)i / total * 80, string.Format("AchievementWindow_ProcessingLine_Format".GetLocalized(), i, total));

            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] parts;
            if (line.Contains('\t')) parts = line.Split('\t');
            else parts = line.Split(',');

            if (parts.Length < 7) continue;

            if (!int.TryParse(parts[0], out int gameId)) continue;

            stats.TotalScanned++;

            int.TryParse(parts[5], out int currentVal);
            int.TryParse(parts[6], out int maxVal);
            bool isCompletedInCsv = parts[1].Trim() == "已完成";

            if (idMap.TryGetValue(gameId, out var targetItem))
            {
                bool needUpdate = false;

                if (currentVal > targetItem.CurrentProgress) needUpdate = true;
                if (isCompletedInCsv && !targetItem.IsCompleted) needUpdate = true;

                if (needUpdate)
                {
                    stats.PendingUpdates.Add(new AchievementUpdateData
                    {
                        Item = targetItem,
                        ShouldComplete = isCompletedInCsv,
                        Current = currentVal,
                        Max = targetItem.MaxProgress > 0 ? targetItem.MaxProgress : maxVal
                    });
                    stats.UpdatedCount++;
                }
                else
                {
                    stats.AlreadyDone++;
                }
            }
            else
            {
                stats.FailedCount++;
                if (stats.Errors.Count < 5) stats.Errors.Add(string.Format("AchievementWindow_IdNotFoundInDb_Format".GetLocalized(), gameId));
            }
        }

        reportProgress(95, "AchievementWindow_RefreshingUI".GetLocalized());

        return stats;
    }

    private class ImportStats
    {
        public int TotalScanned
        {
            get;
            set;
        }

        public int SkippedIncomplete
        {
            get;
            set;
        }

        public int UpdatedCount
        {
            get;
            set;
        }

        public int AlreadyDone
        {
            get;
            set;
        }

        public int FailedCount
        {
            get;
            set;
        }

        public List<string> Errors
        {
            get;
            set;
        } = new();

        public List<AchievementUpdateData> PendingUpdates
        {
            get;
            set;
        } = new();
    }

    private class AchievementUpdateData
    {
        public AchievementItem Item
        {
            get;
            set;
        }

        public bool ShouldComplete
        {
            get;
            set;
        }

        public int Current
        {
            get;
            set;
        }

        public int Max
        {
            get;
            set;
        }
    }
}