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
            _listener.Prefixes.Add("http://127.0.0.1:15655/");
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
                                Debug.WriteLine($"处理请求异常: {t.Exception?.InnerException?.Message}");
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
            Debug.WriteLine("端口开启失败: " + ex.Message);
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
            Debug.WriteLine(ex);
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
            new[] { ("CSV/文本文件", new[] { ".csv", ".txt" }) },
            Windows.Storage.Pickers.PickerLocationId.Desktop,
            msg => _ = ShowDialogAsync("错误", msg));

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
                await ShowDialogAsync("读取失败", $"文件可能被占用或无法读取。\n{ex.Message}");
                return;
            }

            if (lines.Count == 0)
            {
                progressDialog.Hide();
                await ShowDialogAsync("空文件", "文件中没有内容。");
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

                ViewModel.StatusMessage = $"导入成功，同步 {result.UpdatedCount} 个成就进度";
            }
            else
            {
                ViewModel.StatusMessage = $"导入结束，没有新的变动";
            }

            progressDialog.Hide();
            await dialogTask;

            string resultMsg = $"扫描行数: {result.TotalScanned}\n" +
                               $"跳过未完成: {result.SkippedIncomplete}\n" +
                               $"成功同步: {result.UpdatedCount}\n" +
                               $"已存在: {result.AlreadyDone}\n" +
                               $"无法识别: {result.FailedCount}\n\n" +
                               (result.Errors.Any() ? "部分未识别项:\n" + string.Join("\n", result.Errors.Take(3)) : "");

            await ShowDialogAsync("导入数据", resultMsg);
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            await ShowDialogAsync("ErrorTitle".GetLocalized(), $"导入过程中发生异常：\n{ex.Message}");
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

        reportProgress(10, "正在分析进度数据...");

        for (int i = 0; i < total; i++)
        {
            if (i % 100 == 0) reportProgress(10 + (double)i / total * 80, $"正在处理第 {i}/{total} 行...");

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
                if (stats.Errors.Count < 5) stats.Errors.Add($"[ID: {gameId}] 数据库中未找到该成就");
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