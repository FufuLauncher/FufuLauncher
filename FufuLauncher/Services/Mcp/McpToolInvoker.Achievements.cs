/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Json;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Mcp;

public sealed partial class McpToolInvoker
{
    private const string AchievementProfileFileName = "current_profile.txt";
    private const string AchievementDatabaseFileName = "achievements.db";

    private sealed record AchievementEntry(
        int Id,
        string Title,
        string Description,
        string Category,
        string Version,
        string SeriesId,
        string SeriesMasterTitle,
        int StageIndex,
        int StageTotal,
        int Reward,
        bool IsCompleted,
        int CurrentProgress,
        int MaxProgress,
        long CompletionTimestamp);

    private sealed record AchievementSnapshot(
        bool DatabaseExists,
        bool Available,
        string? ProfileName,
        List<string> CategoryNames,
        List<AchievementEntry> Entries);

    private sealed record AchievementCategoryProgress(
        string Name,
        int Total,
        int Completed,
        int InProgress,
        int Primogems,
        int ObtainedPrimogems)
    {
        public double Percent => Total <= 0 ? 0 : Math.Round((double)Completed * 100 / Total, 1);

        public int Remaining => Total - Completed;
    }

    private async Task<McpToolResult> GetAchievementOverviewAsync(CancellationToken cancellationToken)
    {
        var snapshot = await LoadAchievementSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Available)
        {
            return McpToolResult.Success(new
            {
                available = false,
                profile = snapshot.ProfileName,
                totalAchievements = 0,
                completedAchievements = 0,
                message = DescribeAchievementUnavailable(snapshot)
            });
        }

        var entries = snapshot.Entries;
        var completed = entries.Count(entry => entry.IsCompleted);
        var totalPrimogems = entries.Sum(entry => entry.Reward);
        var obtainedPrimogems = entries.Where(entry => entry.IsCompleted).Sum(entry => entry.Reward);
        var latestCompletion = entries
            .Where(entry => entry.IsCompleted && entry.CompletionTimestamp > 0)
            .Select(entry => entry.CompletionTimestamp)
            .DefaultIfEmpty(0)
            .Max();

        var versions = entries
            .GroupBy(entry => string.IsNullOrWhiteSpace(entry.Version) ? "unknown" : entry.Version,
                StringComparer.Ordinal)
            .OrderBy(group => Version.TryParse(group.Key, out var parsed) ? parsed : null)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new
            {
                version = group.Key,
                total = group.Count(),
                completed = group.Count(entry => entry.IsCompleted),
                percent = FormatPercent(group.Count(entry => entry.IsCompleted), group.Count())
            })
            .ToList();

        return McpToolResult.Success(new
        {
            available = true,
            profile = snapshot.ProfileName,
            totalAchievements = entries.Count,
            completedAchievements = completed,
            remainingAchievements = entries.Count - completed,
            completionPercent = FormatPercent(completed, entries.Count),
            totalPrimogems,
            obtainedPrimogems,
            remainingPrimogems = totalPrimogems - obtainedPrimogems,
            categoryCount = snapshot.CategoryNames.Count,
            lastCompletionTime = latestCompletion > 0 ? FormatUnixTime(latestCompletion) : null,
            versions,
            dataSource = "launcher local achievement save (achievements.db)"
        });
    }

    private async Task<McpToolResult> GetAchievementCategoriesAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var categoryFilter = OptionalString(arguments, "category")?.Trim();
        var order = (OptionalString(arguments, "order")?.Trim() ?? "default").ToLowerInvariant();
        if (order is not ("default" or "progress_asc" or "progress_desc" or "name" or "total_desc"))
        {
            throw new McpToolException(
                $"Unknown order \"{order}\". Use default, progress_asc, progress_desc, name or total_desc.");
        }

        var snapshot = await LoadAchievementSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Available)
        {
            return McpToolResult.Success(new
            {
                available = false,
                profile = snapshot.ProfileName,
                totalCategories = 0,
                categories = Array.Empty<object>(),
                message = DescribeAchievementUnavailable(snapshot)
            });
        }

        var progress = BuildCategoryProgress(snapshot);
        var availableCategories = progress.Select(item => item.Name).ToList();
        var matched = string.IsNullOrWhiteSpace(categoryFilter)
            ? progress
            : progress.Where(item => ContainsText(item.Name, categoryFilter)).ToList();

        var ordered = order switch
        {
            "progress_asc" => matched.OrderBy(item => item.Percent).ThenBy(item => item.Name, StringComparer.Ordinal),
            "progress_desc" => matched.OrderByDescending(item => item.Percent).ThenBy(item => item.Name, StringComparer.Ordinal),
            "name" => matched.OrderBy(item => item.Name, StringComparer.Ordinal),
            "total_desc" => matched.OrderByDescending(item => item.Total).ThenBy(item => item.Name, StringComparer.Ordinal),
            _ => matched.AsEnumerable()
        };

        var categories = ordered
            .Select(item => (object)new
            {
                name = item.Name,
                total = item.Total,
                completed = item.Completed,
                remaining = item.Remaining,
                percent = FormatPercent(item.Completed, item.Total),
                inProgress = item.InProgress,
                primogems = item.Primogems,
                obtainedPrimogems = item.ObtainedPrimogems
            })
            .ToList();

        return McpToolResult.Success(new
        {
            available = true,
            profile = snapshot.ProfileName,
            totalCategories = progress.Count,
            matchedCategories = categories.Count,
            order,
            categories,
            message = categories.Count == 0
                ? "No category matches the filter. availableCategories lists every category name."
                : null,
            availableCategories = categories.Count == 0 ? availableCategories : null
        });
    }

    private async Task<McpToolResult> SearchAchievementsAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var idText = OptionalString(arguments, "id")?.Trim();
        int? id = null;
        if (!string.IsNullOrWhiteSpace(idText))
        {
            if (!int.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
            {
                throw new McpToolException($"Invalid achievement id \"{idText}\".");
            }

            id = parsedId;
        }

        var query = OptionalString(arguments, "query")?.Trim();
        var categoryFilter = OptionalString(arguments, "category")?.Trim();
        var versionFilter = OptionalString(arguments, "version")?.Trim();
        var status = (OptionalString(arguments, "status")?.Trim() ?? "all").ToLowerInvariant();
        if (status is not ("all" or "completed" or "incomplete"))
        {
            throw new McpToolException($"Unknown status \"{status}\". Use all, completed or incomplete.");
        }

        var page = OptionalInt(arguments, "page", 1, 1, 100000);
        var pageSize = OptionalInt(arguments, "pageSize", 20, 1, 100);

        var snapshot = await LoadAchievementSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (!snapshot.Available)
        {
            return McpToolResult.Success(new
            {
                available = false,
                profile = snapshot.ProfileName,
                totalMatched = 0,
                achievements = Array.Empty<object>(),
                message = DescribeAchievementUnavailable(snapshot)
            });
        }

        var categoryOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in snapshot.CategoryNames)
        {
            if (!categoryOrder.ContainsKey(name))
            {
                categoryOrder[name] = categoryOrder.Count;
            }
        }

        var matched = snapshot.Entries
            .Where(entry => id == null || entry.Id == id)
            .Where(entry => string.IsNullOrWhiteSpace(query) ||
                            ContainsText(entry.Title, query) ||
                            ContainsText(entry.Description, query))
            .Where(entry => string.IsNullOrWhiteSpace(categoryFilter) || ContainsText(entry.Category, categoryFilter))
            .Where(entry => string.IsNullOrWhiteSpace(versionFilter) || ContainsText(entry.Version, versionFilter))
            .Where(entry => status switch
            {
                "completed" => entry.IsCompleted,
                "incomplete" => !entry.IsCompleted,
                _ => true
            })
            .OrderBy(entry => categoryOrder.TryGetValue(entry.Category, out var order) ? order : int.MaxValue)
            .ThenBy(entry => string.IsNullOrWhiteSpace(entry.SeriesId) ? entry.Title : entry.SeriesId,
                StringComparer.Ordinal)
            .ThenBy(entry => entry.StageIndex)
            .ThenBy(entry => entry.Id)
            .ToList();

        var seriesProgress = snapshot.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.SeriesId))
            .GroupBy(entry => entry.SeriesId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (Total: group.Count(), Completed: group.Count(entry => entry.IsCompleted)),
                StringComparer.Ordinal);

        var achievements = matched
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => DescribeAchievement(entry, seriesProgress))
            .ToList();

        return McpToolResult.Success(new
        {
            available = true,
            profile = snapshot.ProfileName,
            totalMatched = matched.Count,
            page,
            pageSize,
            pageCount = PageCount(matched.Count, pageSize),
            hasMore = page * pageSize < matched.Count,
            achievements,
            message = matched.Count == 0
                ? "No achievement matches the given filters. Use get_achievement_categories to list the category names."
                : null
        });
    }

    private static object DescribeAchievement(AchievementEntry entry,
        IReadOnlyDictionary<string, (int Total, int Completed)> seriesProgress)
    {
        var hasSeries = !string.IsNullOrWhiteSpace(entry.SeriesId);
        var series = hasSeries ? seriesProgress[entry.SeriesId] : (Total: 0, Completed: 0);
        var progressPercent = entry.MaxProgress > 1
            ? Math.Round((double)entry.CurrentProgress * 100 / entry.MaxProgress, 1)
            : entry.IsCompleted
                ? 100d
                : 0d;

        return new
        {
            id = entry.Id,
            title = entry.Title,
            description = entry.Description,
            category = entry.Category,
            version = NullIfEmpty(entry.Version),
            seriesId = hasSeries ? entry.SeriesId : null,
            seriesMasterTitle = NullIfEmpty(entry.SeriesMasterTitle),
            stageIndex = hasSeries ? entry.StageIndex : (int?)null,
            stageTotal = hasSeries ? entry.StageTotal : (int?)null,
            seriesCompleted = hasSeries ? series.Completed : (int?)null,
            seriesTotal = hasSeries ? series.Total : (int?)null,
            reward = entry.Reward,
            isCompleted = entry.IsCompleted,
            currentProgress = entry.CurrentProgress,
            maxProgress = entry.MaxProgress,
            progressPercent,
            completedAt = entry.CompletionTimestamp > 0 ? FormatUnixTime(entry.CompletionTimestamp) : null
        };
    }

    private static List<AchievementCategoryProgress> BuildCategoryProgress(AchievementSnapshot snapshot)
    {
        var grouped = snapshot.Entries
            .GroupBy(entry => entry.Category, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var names = new List<string>();
        foreach (var name in snapshot.CategoryNames)
        {
            if (grouped.ContainsKey(name) && !names.Contains(name, StringComparer.Ordinal))
            {
                names.Add(name);
            }
        }

        foreach (var group in grouped)
        {
            if (!names.Contains(group.Key, StringComparer.Ordinal))
            {
                names.Add(group.Key);
            }
        }

        return names
            .Select(name =>
            {
                var entries = grouped[name];
                return new AchievementCategoryProgress(
                    name,
                    entries.Count,
                    entries.Count(entry => entry.IsCompleted),
                    entries.Count(entry => !entry.IsCompleted && entry.MaxProgress > 1 && entry.CurrentProgress > 0),
                    entries.Sum(entry => entry.Reward),
                    entries.Where(entry => entry.IsCompleted).Sum(entry => entry.Reward));
            })
            .ToList();
    }

    private Task<AchievementSnapshot> LoadAchievementSnapshotAsync(CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            var profileName = ReadAchievementProfileName();
            if (!AchievementDatabaseExists())
            {
                return new AchievementSnapshot(false, false, profileName, new List<string>(),
                    new List<AchievementEntry>());
            }

            var categories = _achievementRepository.GetAllCategories();
            var achievements = _achievementRepository.GetAllAchievements();
            var entries = new List<AchievementEntry>(achievements.Count);

            foreach (var entity in achievements)
            {
                cancellationToken.ThrowIfCancellationRequested();
                entries.Add(ToAchievementEntry(entity));
            }

            return new AchievementSnapshot(
                true,
                entries.Count > 0,
                profileName,
                categories.Select(category => category.Name).ToList(),
                entries);
        }, cancellationToken);

    private static bool AchievementDatabaseExists() =>
        File.Exists(Path.Combine(AppPaths.DataDir, AchievementDatabaseFileName));

    private static string DescribeAchievementUnavailable(AchievementSnapshot snapshot) =>
        snapshot.DatabaseExists
            ? "The achievement save exists but returned no records, or it is busy right now. Open the launcher achievements window once and retry."
            : "No achievement save is imported yet. Open the launcher achievements window and import or update the achievement data first.";

    private static AchievementEntry ToAchievementEntry(AchievementEntity entity)
    {
        var title = entity.Title ?? string.Empty;
        var description = string.Empty;
        var version = string.Empty;
        var seriesId = string.Empty;
        var seriesTitle = string.Empty;
        var stageIndex = 1;
        var stageTotal = 1;
        var reward = 0;
        var currentProgress = entity.CurrentProgress;
        var maxProgress = entity.MaxProgress;

        if (!string.IsNullOrWhiteSpace(entity.RawJson))
        {
            try
            {
                using var document = JsonDocument.Parse(entity.RawJson);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    title = ReadJsonString(root, "title") ?? title;
                    description = ReadJsonString(root, "description") ?? description;
                    version = ReadJsonString(root, "version") ?? version;
                    seriesId = ReadJsonString(root, "series_id") ?? seriesId;
                    seriesTitle = ReadJsonString(root, "series_master_title") ?? seriesTitle;
                    stageIndex = Math.Max(1, ReadJsonInt(root, "stage_index", stageIndex));
                    stageTotal = Math.Max(1, ReadJsonInt(root, "stage_total", stageTotal));
                    reward = ReadJsonInt(root, "reward", reward);
                    if (maxProgress <= 0)
                    {
                        maxProgress = ReadJsonInt(root, "total", maxProgress);
                    }

                    if (currentProgress <= 0)
                    {
                        currentProgress = ReadJsonInt(root, "current", currentProgress);
                    }
                }
            }
            catch (JsonException ex)
            {
                DebugLog.Info("McpToolInvoker", $"[Mcp] Achievement record JSON is invalid: {ex.Message}");
            }
        }

        return new AchievementEntry(
            entity.Id,
            title,
            description,
            entity.CategoryName ?? string.Empty,
            version,
            seriesId,
            seriesTitle,
            stageIndex,
            stageTotal,
            reward,
            entity.IsCompleted == 1,
            currentProgress,
            maxProgress,
            entity.CompletionTimestamp);
    }

    private static string? ReadJsonString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static int ReadJsonInt(JsonElement root, string name, int fallback)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static string? ReadAchievementProfileName()
    {
        try
        {
            var path = Path.Combine(AppPaths.DataDir, AchievementProfileFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            var name = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpToolInvoker", $"[Mcp] Failed to read the achievement profile name: {ex.Message}");
            return null;
        }
    }

    private static string FormatUnixTime(long timestamp) =>
        DateTimeOffset.FromUnixTimeSeconds(timestamp).ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
