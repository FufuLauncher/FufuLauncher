/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class AchievementWindow
{
    private static readonly JsonSerializerOptions _achievementReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions _achievementWriteOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private void EnsureDatabaseExists(string dbPath)
    {
        bool isNewDb = !File.Exists(dbPath);
        if (isNewDb)
        {
            string oldJsonPath = Path.Combine(Path.GetDirectoryName(dbPath)!, "achievements.json");
            if (File.Exists(oldJsonPath))
            {
                ImportJsonToDb(oldJsonPath, dbPath);
            }
            else if (File.Exists(_assetsFilePath))
            {
                ImportJsonToDb(_assetsFilePath, dbPath);
            }
        }
    }

    private void ImportJsonToDb(string jsonPath, string dbPath)
    {
        string jsonContent;
        try
        {
            jsonContent = File.ReadAllText(jsonPath);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[AchievementWindow] 读取成就文件失败 ({jsonPath}): {ex.Message}");
            return;
        }

        List<AchievementCategory>? rawCategories;
        try
        {
            rawCategories = JsonSerializer.Deserialize<List<AchievementCategory>>(jsonContent, _achievementReadOptions);
        }
        catch (JsonException ex)
        {
            Debug.WriteLine($"[AchievementWindow] 成就 JSON 解析失败 ({jsonPath}): {ex.Message}");
            return;
        }

        if (rawCategories == null) return;

        _achievementRepo.ChangeDatabase(dbPath);
        _achievementRepo.SynchronizeCatalog(
            rawCategories.Select(cat => (GetCategoryName(cat), cat.IconUrl)),
            CreateAchievementEntities(rawCategories));
    }

    private static List<AchievementEntity> CreateAchievementEntities(IEnumerable<AchievementCategory> categories)
    {
        var achievements = new List<AchievementEntity>();
        foreach (var category in categories)
        {
            if (category.Achievements == null) continue;

            var categoryName = GetCategoryName(category);
            foreach (var item in category.Achievements)
            {
                achievements.Add(new AchievementEntity
                {
                    Id = item.Id,
                    Title = item.Title ?? "",
                    CategoryName = categoryName,
                    RawJson = JsonSerializer.Serialize(item, _achievementWriteOptions),
                    IsCompleted = item.IsCompleted ? 1 : 0,
                    CurrentProgress = item.CurrentProgress,
                    MaxProgress = item.MaxProgress,
                    CompletionTimestamp = item.CompletionTimestamp
                });
            }
        }

        return achievements;
    }

    private async Task SyncWithAssetsDatabase()
    {
        if (_isBatchProcessing) return;
        _isBatchProcessing = true;
        ViewModel.IsLoading = true;
        ViewModel.StatusMessage = "正在对比数据库版本...";

        try
        {
            if (!File.Exists(_assetsFilePath))
            {
                await ShowDialogAsync("ErrorTitle".GetLocalized(), "找不到内置数据库文件");
                return;
            }

            var masterJson = await File.ReadAllTextAsync(_assetsFilePath);
            var masterCategories = JsonSerializer.Deserialize<List<AchievementCategory>>(
                masterJson, _achievementReadOptions) ?? throw new JsonException("成就列表为空或格式无效");

            EnsureDatabaseExists(_workFilePath);
            _achievementRepo.ChangeDatabase(_workFilePath);

            var result = _achievementRepo.SynchronizeCatalog(
                masterCategories.Select(cat => (GetCategoryName(cat), cat.IconUrl)),
                CreateAchievementEntities(masterCategories));

            LoadData();
            if (result.AddedCategories > 0 || result.AddedAchievements > 0 || result.UpdatedAchievements > 0)
            {
                await ShowDialogAsync("数据库更新",
                    $"同步成功！\n新增分类: {result.AddedCategories} 个\n新增成就: {result.AddedAchievements} 个\n更新成就信息: {result.UpdatedAchievements} 个");
            }
            else
            {
                ViewModel.StatusMessage = "当前已是最新数据库";
                await ShowDialogAsync("数据库更新", "您的存档已经是最新版本，无需更新。");
            }
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("更新失败", $"同步过程中发生错误：\n{ex.Message}");
        }
        finally
        {
            ViewModel.IsLoading = false;
            _isBatchProcessing = false;
            if (_isDataLoaded) CalculateGlobalStats();
        }
    }

    private static string GetCategoryName(AchievementCategory category) =>
        string.IsNullOrWhiteSpace(category.Name)
            ? "AchievementWindow_UnknownCategory".GetLocalized()
            : category.Name;

    private async void OnUpdateDbClick(object sender, RoutedEventArgs e)
    {
        var confirmDialog = new ContentDialog
        {
            Title = "AchievementWindow_UpdateDb".GetLocalized(),
            Content = "此操作将读取软件内置的最新成就列表，并将缺失的新成就添加到您当前的存档中。\n\n您的现有进度（已完成的成就）将保留不会丢失。\n\n是否继续？",
            PrimaryButtonText = "AchievementWindow_StartUpdate".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await confirmDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await SyncWithAssetsDatabase();
        }
    }
}
