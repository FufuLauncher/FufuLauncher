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
            DebugLog.Info("AchievementWindow.Database",
                $"[AchievementWindow] Failed to read achievements file ({jsonPath}): {ex.Message}");
            return;
        }

        List<AchievementCategory>? rawCategories;
        try
        {
            rawCategories = JsonSerializer.Deserialize<List<AchievementCategory>>(jsonContent, _achievementReadOptions);
        }
        catch (JsonException ex)
        {
            DebugLog.Info("AchievementWindow.Database",
                $"[AchievementWindow] Failed to parse achievements JSON ({jsonPath}): {ex.Message}");
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
        ViewModel.StatusMessage = "AchievementWindow_ComparingDbVersion".GetLocalized();

        try
        {
            if (!File.Exists(_assetsFilePath))
            {
                await ShowDialogAsync("ErrorTitle".GetLocalized(), "AchievementWindow_DbNotFound".GetLocalized());
                return;
            }

            var masterJson = await File.ReadAllTextAsync(_assetsFilePath);
            var masterCategories = JsonSerializer.Deserialize<List<AchievementCategory>>(
                masterJson, _achievementReadOptions) ?? throw new JsonException("AchievementWindow_DbInvalidOrEmpty".GetLocalized());

            EnsureDatabaseExists(_workFilePath);
            _achievementRepo.ChangeDatabase(_workFilePath);

            var result = _achievementRepo.SynchronizeCatalog(
                masterCategories.Select(cat => (GetCategoryName(cat), cat.IconUrl)),
                CreateAchievementEntities(masterCategories));

            LoadData();
            if (result.AddedCategories > 0 || result.AddedAchievements > 0 || result.UpdatedAchievements > 0)
            {
                await ShowDialogAsync("AchievementWindow_DbUpdateTitle".GetLocalized(),
                    string.Format("AchievementWindow_DbSyncSuccess_Format".GetLocalized(), result.AddedCategories, result.AddedAchievements, result.UpdatedAchievements));
            }
            else
            {
                ViewModel.StatusMessage = "AchievementWindow_DbUpToDate".GetLocalized();
                await ShowDialogAsync("AchievementWindow_DbUpdateTitle".GetLocalized(), "AchievementWindow_AlreadyLatest".GetLocalized());
            }
        }
        catch (Exception ex)
        {
            await ShowDialogAsync("AchievementWindow_UpdateFailedTitle".GetLocalized(), string.Format("AchievementWindow_SyncError_Format".GetLocalized(), ex.Message));
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
            Content = "AchievementWindow_UpdateDbConfirmContent".GetLocalized(),
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