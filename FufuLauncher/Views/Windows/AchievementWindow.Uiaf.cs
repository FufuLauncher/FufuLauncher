/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Yae;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class AchievementWindow
{
    private class UiafInfo
    {
        [JsonPropertyName("export_app")]
        public string ExportApp
        {
            get;
            set;
        } = "FufuLauncher";

        [JsonPropertyName("export_app_version")]
        public string ExportAppVersion
        {
            get;
            set;
        } = "1.0.0";

        [JsonPropertyName("uiaf_version")]
        public string UiafVersion
        {
            get;
            set;
        } = "v1.1";

        [JsonPropertyName("export_timestamp")]
        public long ExportTimestamp
        {
            get;
            set;
        }
    }

    private class UiafItem
    {
        [JsonPropertyName("id")]
        public int Id
        {
            get;
            set;
        }

        [JsonPropertyName("current")]
        public int Current
        {
            get;
            set;
        }

        [JsonPropertyName("status")]
        public int Status
        {
            get;
            set;
        }

        [JsonPropertyName("timestamp")]
        public long Timestamp
        {
            get;
            set;
        }
    }

    private class UiafData
    {
        [JsonPropertyName("info")]
        public UiafInfo Info
        {
            get;
            set;
        } = new();

        [JsonPropertyName("list")]
        public List<UiafItem> List
        {
            get;
            set;
        } = new();
    }

    private async void OnYaeImportClick(object sender, RoutedEventArgs e)
    {
        var contentPanel = new StackPanel { Spacing = 12, MaxWidth = 400 };

        contentPanel.Children.Add(new TextBlock
        {
            Text = "AchievementWindow_FollowSteps".GetLocalized(),
            TextWrapping = TextWrapping.Wrap
        });

        contentPanel.Children.Add(new TextBlock
        {
            Text = "AchievementWindow_YaeGuideSteps".GetLocalized(),
            TextWrapping = TextWrapping.Wrap,
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray)
        });

        var dialog = new ContentDialog
        {
            Title = "AchievementWindow_YaeGuideTitle".GetLocalized(),
            Content = contentPanel,
            CloseButtonText = "GotItBtn".GetLocalized(),
            XamlRoot = Content.XamlRoot
        };

        await dialog.ShowAsync();
    }

    private async void OnUiafImportClick(object sender, RoutedEventArgs e)
    {
        var path = await FilePickerService.PickOpenFileAsync(
            this,
            new[] { ("Common_JsonFile".GetLocalized(), new[] { ".json" }) },
            Windows.Storage.Pickers.PickerLocationId.Desktop,
            msg => _ = ShowDialogAsync("ErrorTitle".GetLocalized(), msg));
        if (string.IsNullOrEmpty(path)) return;

        if (_isBatchProcessing) return;
        _isBatchProcessing = true;
        ViewModel.StatusMessage = "AchievementWindow_ReadingUIAF".GetLocalized();

        try
        {
            string jsonContent = await File.ReadAllTextAsync(path);
            var uiafData = JsonSerializer.Deserialize<UiafData>(jsonContent);

            if (uiafData == null || uiafData.List == null)
            {
                await ShowDialogAsync("ErrorTitle".GetLocalized(), "AchievementWindow_InvalidUiafFormat".GetLocalized());
                return;
            }

            int updatedCount = ApplyUiafData(uiafData);

            ViewModel.StatusMessage = string.Format("AchievementWindow_UiafDoneStatus_Format".GetLocalized(), updatedCount);
            await ShowDialogAsync("AchievementWindow_ImportData".GetLocalized(), string.Format("AchievementWindow_UiafSuccessDialog_Format".GetLocalized(), updatedCount));
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("AchievementWindow_ImportFailed".GetLocalized(), string.Format("AchievementWindow_UiafParseException_Format".GetLocalized(), ex.Message));
        }
        finally
        {
            _isBatchProcessing = false;
        }
    }

    private int ApplyUiafData(UiafData uiafData)
    {
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

        int updatedCount = 0;

        foreach (var uiafItem in uiafData.List)
        {
            if (!idMap.TryGetValue(uiafItem.Id, out var targetItem)) continue;

            bool isCompleted = uiafItem.Status == 2 || uiafItem.Status == 3;
            bool needUpdate = uiafItem.Current > targetItem.CurrentProgress
                              || (isCompleted && !targetItem.IsCompleted);

            if (!needUpdate) continue;

            targetItem.CurrentProgress = uiafItem.Current;
            if (isCompleted)
            {
                targetItem.IsCompleted = true;

                if (uiafItem.Timestamp > 0)
                {
                    targetItem.CompletionTimestamp = uiafItem.Timestamp;
                }
                else if (targetItem.CompletionTimestamp == 0)
                {
                    targetItem.CompletionTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                }
            }

            updatedCount++;
        }

        foreach (var cat in ViewModel.Categories)
        {
            cat.RefreshProgress();
        }

        CalculateGlobalStats();
        SaveData();

        ApplyFilters();

        return updatedCount;
    }

    private async void OnYaeReadClick(object sender, RoutedEventArgs e)
    {
        if (_isBatchProcessing) return;

        var localSettingsService = App.GetService<ILocalSettingsService>();
        var configuredPath = await localSettingsService.ReadSettingAsync("GameInstallationPath") as string;
        string? gameExe = null;
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var directory = configuredPath.Trim().Trim('"');
            if (Directory.Exists(directory))
            {
                foreach (var exeName in await GameExeManager.GetExeNamesAsync())
                {
                    var candidate = Path.Combine(directory, exeName);
                    if (File.Exists(candidate))
                    {
                        gameExe = candidate;
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrEmpty(gameExe))
        {
            await ShowDialogAsync("ErrorTitle".GetLocalized(), "AchievementWindow_YaeNoGamePath".GetLocalized());
            return;
        }

        _isBatchProcessing = true;
        ViewModel.StatusMessage = "AchievementWindow_ReadingFromGame".GetLocalized();

        try
        {
            var result = await YaeAchievementReader.ReadAchievementsAsync(gameExe);
            if (result is null || result.List.Count == 0)
            {
                ViewModel.StatusMessage = "AchievementWindow_YaeEmptyResult".GetLocalized();
                await ShowDialogAsync("AchievementWindow_ImportResultTitle".GetLocalized(), "AchievementWindow_YaeEmptyResult".GetLocalized());
                return;
            }

            var uiafData = new UiafData
            {
                Info = new UiafInfo
                {
                    ExportApp = result.Info?.ExportApp ?? "FufuLauncher",
                    ExportAppVersion = result.Info?.ExportAppVersion ?? "1.0.0",
                    UiafVersion = "v1.1",
                    ExportTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds(),
                },
                List = result.List.Select(item => new UiafItem
                {
                    Id = item.Id,
                    Current = item.Current,
                    Status = item.Status,
                    Timestamp = item.Timestamp,
                }).ToList(),
            };

            int updatedCount = ApplyUiafData(uiafData);

            ViewModel.StatusMessage = string.Format("AchievementWindow_YaeReadDone".GetLocalized(), result.List.Count,
                updatedCount);
            await ShowDialogAsync("AchievementWindow_ImportSuccessTitle".GetLocalized(),
                string.Format("AchievementWindow_YaeReadDone".GetLocalized(), result.List.Count, updatedCount));
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = "AchievementWindow_YaeReadFailed".GetLocalized();
            await ShowDialogAsync("AchievementWindow_ImportFailed".GetLocalized(), ex.Message);
        }
        finally
        {
            _isBatchProcessing = false;
        }
    }

    private async void OnUiafExportClick(object sender, RoutedEventArgs e)
    {
        var path = await FilePickerService.PickSaveFileAsync(
            this,
            new[] { ("Common_JsonFile".GetLocalized(), new[] { ".json" }) },
            $"UIAF_Export_{DateTime.Now:yyyyMMdd_HHmmss}",
            Windows.Storage.Pickers.PickerLocationId.Desktop,
            msg => _ = ShowDialogAsync("ErrorTitle".GetLocalized(), msg));
        if (string.IsNullOrEmpty(path)) return;

        ViewModel.StatusMessage = "AchievementWindow_GeneratingUIAF".GetLocalized();

        try
        {
            long currentTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();

            var exportData = new UiafData
            {
                Info = new UiafInfo
                {
                    ExportApp = "FufuLauncher",
                    ExportAppVersion = $"{System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version}",
                    UiafVersion = "v1.1",
                    ExportTimestamp = currentTimestamp
                },
                List = new List<UiafItem>()
            };

            foreach (var cat in ViewModel.Categories)
            {
                foreach (var item in cat.Achievements)
                {
                    if (item.IsGroup)
                    {
                        foreach (var child in item.Children)
                        {
                            var uiafItem = CreateUiafItem(child, currentTimestamp);
                            if (uiafItem != null)
                            {
                                exportData.List.Add(uiafItem);
                            }
                        }
                    }
                    else
                    {
                        var uiafItem = CreateUiafItem(item, currentTimestamp);
                        if (uiafItem != null)
                        {
                            exportData.List.Add(uiafItem);
                        }
                    }
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            string jsonOutput = JsonSerializer.Serialize(exportData, options);

            await File.WriteAllTextAsync(path, jsonOutput);

            ViewModel.StatusMessage = "AchievementWindow_UIAFExportDone".GetLocalized();
            await ShowDialogAsync("AchievementWindow_ExportSuccess".GetLocalized(), "AchievementWindow_UiafExportSuccessMsg".GetLocalized());
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("AchievementWindow_ExportFailed".GetLocalized(), string.Format("AchievementWindow_UiafExportError_Format".GetLocalized(), ex.Message));
        }
    }

    private UiafItem CreateUiafItem(AchievementItem item, long defaultTimestamp)
    {
        int status = 1;

        if (item.IsCompleted)
        {
            status = 3;
        }
        else if (item.CurrentProgress >= item.MaxProgress && item.MaxProgress > 0)
        {
            status = 2;
        }

        if (status == 1 && item.CurrentProgress == 0)
        {
            return null;
        }

        return new UiafItem
        {
            Id = item.Id,
            Current = item.CurrentProgress,
            Status = status,
            Timestamp = item.CompletionTimestamp > 0 ? item.CompletionTimestamp : defaultTimestamp
        };
    }
}