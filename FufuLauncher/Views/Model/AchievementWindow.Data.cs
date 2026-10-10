/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Views;

public sealed partial class AchievementWindow
{
    private void LoadData()
    {
        ViewModel.IsLoading = true;
        ViewModel.StatusMessage = "AchievementWindow_ReadingData".GetLocalized();
        _isDataLoaded = false;

        try
        {
            EnsureDatabaseExists(_workFilePath);
            _achievementRepo.ChangeDatabase(_workFilePath);

            var rawCategories = new List<AchievementCategory>();
            var categoryMap = new Dictionary<string, AchievementCategory>();

            var catEntities = _achievementRepo.GetAllCategories();
            foreach (var catEntity in catEntities)
            {
                var cat = new AchievementCategory
                {
                    Name = catEntity.Name,
                    IconUrl = catEntity.IconUrl,
                    Achievements = new ObservableCollection<AchievementItem>()
                };
                rawCategories.Add(cat);
                categoryMap[cat.Name] = cat;
            }

            _itemUids.Clear();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var achEntities = _achievementRepo.GetAllAchievements();
            foreach (var ach in achEntities)
            {
                string catName = ach.CategoryName ?? "";
                string rawJson = ach.RawJson ?? "";
                bool isCompleted = ach.IsCompleted == 1;
                int currentProgress = ach.CurrentProgress;
                int maxProgress = ach.MaxProgress;
                long completionTimestamp = ach.CompletionTimestamp;

                var item = JsonSerializer.Deserialize<AchievementItem>(rawJson, options);
                if (item != null && categoryMap.TryGetValue(catName, out var cat))
                {
                    item.Id = ach.Id;
                    item.IsCompleted = isCompleted;
                    item.CurrentProgress = currentProgress;
                    item.MaxProgress = maxProgress;
                    item.CompletionTimestamp = completionTimestamp;
                    cat.Achievements.Add(item);

                    _itemUids[item] = ach.Uid;
                }
            }

            ViewModel.Categories.Clear();

            foreach (var cat in rawCategories)
            {
                var groupedList = new ObservableCollection<AchievementItem>();
                var groups = cat.Achievements.GroupBy(x =>
                    !string.IsNullOrEmpty(x.SeriesId) ? x.SeriesId : Guid.NewGuid().ToString());

                foreach (var g in groups)
                {
                    var items = g.OrderBy(x => x.StageIndex).ToList();

                    if (items.Count == 1)
                    {
                        var item = items.First();
                        SetupItemEvents(cat, item);
                        groupedList.Add(item);
                    }
                    else
                    {
                        var firstChild = items.First();

                        var parentItem = new AchievementItem
                        {
                            Title = !string.IsNullOrEmpty(firstChild.SeriesMasterTitle)
                                ? firstChild.SeriesMasterTitle
                                : firstChild.Title,
                            Description = firstChild.Description,
                            Version = firstChild.Version,
                            ItemIconUrl = firstChild.ItemIconUrl,
                            SeriesId = firstChild.SeriesId,
                            Children = new ObservableCollection<AchievementItem>(items)
                        };

                        foreach (var child in parentItem.Children)
                        {
                            SetupItemEvents(cat, child, parentItem);
                        }

                        parentItem.RefreshGroupStatus();
                        groupedList.Add(parentItem);
                    }
                }

                cat.Achievements = groupedList;
                cat.RefreshProgress();
                ViewModel.Categories.Add(cat);
            }

            ViewModel.RefreshAvailableVersions("AchievementWindow_AllVersions".GetLocalized());
            ViewModel.SelectedCategory = ViewModel.Categories.FirstOrDefault();

            ApplyFilters();

            ViewModel.StatusMessage = $"共 {ViewModel.Categories.Sum(c => c.TotalCount)} 个成就";
            CalculateGlobalStats();

            ViewModel.StatusMessage = $"AchievementWindow_DataLoaded".GetLocalized();
            _isDataLoaded = true;
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = $"初始化失败: {ex.Message}";
            Debug.WriteLine(ex);
        }
        finally
        {
            ViewModel.IsLoading = false;
        }
    }

    private void SetupItemEvents(AchievementCategory cat, AchievementItem item, AchievementItem parent = null)
    {
        item.PropertyChanged += (s, e) =>
        {
            if (_isBatchProcessing) return;

            if (e.PropertyName == nameof(AchievementItem.IsCompleted) ||
                e.PropertyName == nameof(AchievementItem.CurrentProgress) ||
                e.PropertyName == nameof(AchievementItem.MaxProgress))
            {
                if (e.PropertyName == nameof(AchievementItem.IsCompleted))
                {
                    if (item.IsCompleted && item.CompletionTimestamp == 0)
                    {
                        item.CompletionTimestamp = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
                    }
                    else if (!item.IsCompleted)
                    {
                        item.CompletionTimestamp = 0;
                    }
                }

                parent?.RefreshGroupStatus();
                cat.RefreshProgress();
                CalculateGlobalStats();
                if (ViewModel.HideCompleted) ApplyFilters();

                UpdateDbSingleItem(item);
            }
        };
    }

    private void UpdateDbSingleItem(AchievementItem item)
    {
        if (!_isDataLoaded || _isBatchProcessing) return;
        if (!_itemUids.TryGetValue(item, out int uid)) return;

        try
        {
            _achievementRepo.UpdateAchievement(uid, item.IsCompleted, item.CurrentProgress, item.MaxProgress,
                item.CompletionTimestamp);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    private void SaveData()
    {
        if (!_isDataLoaded) return;
        try
        {
            var updates =
                new Dictionary<int, (bool IsCompleted, int CurrentProgress, int MaxProgress, long CompletionTimestamp
                    )>();
            foreach (var uiCat in ViewModel.Categories)
            {
                foreach (var item in uiCat.Achievements)
                {
                    if (item.IsGroup)
                    {
                        foreach (var child in item.Children)
                        {
                            if (_itemUids.TryGetValue(child, out int uid))
                                updates[uid] = (child.IsCompleted, child.CurrentProgress, child.MaxProgress,
                                    child.CompletionTimestamp);
                        }
                    }
                    else
                    {
                        if (_itemUids.TryGetValue(item, out int uid))
                            updates[uid] = (item.IsCompleted, item.CurrentProgress, item.MaxProgress,
                                item.CompletionTimestamp);
                    }
                }
            }

            _achievementRepo.UpdateAchievementsBatch(updates);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = "AchievementWindow_SaveException".GetLocalized();
            Debug.WriteLine(ex);
        }
    }

    private void CalculateGlobalStats()
    {
        if (ViewModel.Categories == null) return;

        int totalPrimos = 0;
        int obtainedPrimos = 0;
        int totalCount = 0;
        int completedCount = 0;

        foreach (var cat in ViewModel.Categories)
        {
            foreach (var item in cat.Achievements)
            {
                if (item.IsGroup)
                {
                    foreach (var child in item.Children)
                    {
                        totalCount++;
                        totalPrimos += child.RewardValue;

                        if (child.IsCompleted)
                        {
                            completedCount++;
                            obtainedPrimos += child.RewardValue;
                        }
                    }
                }
                else
                {
                    totalCount++;
                    totalPrimos += item.RewardValue;

                    if (item.IsCompleted)
                    {
                        completedCount++;
                        obtainedPrimos += item.RewardValue;
                    }
                }
            }
        }

        ViewModel.PrimogemStatText = $"{obtainedPrimos} / {totalPrimos}";

        double percent = totalCount == 0 ? 0 : (double)completedCount / totalCount * 100;
        ViewModel.ProgressStatText = $"{completedCount} / {totalCount} ({percent:F1}%)";
        ViewModel.GlobalProgressPercent = percent;
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        ViewModel.StatusMessage = "AchievementWindow_Saving".GetLocalized();
        SaveData();
        ViewModel.StatusMessage = "AchievementWindow_SavedToDocs".GetLocalized();
    }
}