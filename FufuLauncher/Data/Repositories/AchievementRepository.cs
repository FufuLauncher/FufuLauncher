/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data.Repositories;

public class AchievementRepository
{
    private string? _overridePath;
    private string DbPath => _overridePath ?? Path.Combine(Helpers.AppPaths.DataDir, "achievements.db");

    public AchievementRepository()
    {
    }

    public void ChangeDatabase(string? newDbPath)
    {
        _overridePath = newDbPath;
    }

    public void InvalidateMigrationCache(string dbPath)
    {
        _migratedPaths.TryRemove(dbPath, out _);
    }

    public static void ClearConnectionPool()
    {
        SqliteConnection.ClearAllPools();
    }

    private static readonly object _migrateLock = new();

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _migratedPaths
        = new(StringComparer.OrdinalIgnoreCase);

    private AchievementDbContext CreateContext()
    {
        var dbPath = DbPath;
        if (!_migratedPaths.ContainsKey(dbPath))
        {
            lock (_migrateLock)
            {
                if (!_migratedPaths.ContainsKey(dbPath))
                {
                    PerformMigration(dbPath);
                    _migratedPaths[dbPath] = true;
                }
            }
        }

        return new AchievementDbContext(dbPath);
    }

    private void PerformMigration(string dbPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            bool tableExists = false;
            try
            {
                using var checkConn = new SqliteConnection(SqlitePaths.BuildConnectionString(dbPath));
                checkConn.Open();
                using var checkCmd = checkConn.CreateCommand();
                checkCmd.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Categories';";
                tableExists = (long)checkCmd.ExecuteScalar()! > 0;
            }
            catch
            {
            }

            if (tableExists)
            {
                using var context = new AchievementDbContext(dbPath);
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
                Debug.WriteLine("AchievementRepository: 检测到现有数据库，已跳过迁移");
            }
            else
            {
                using var context = new AchievementDbContext(dbPath);
                context.Database.EnsureCreated();
                Debug.WriteLine("AchievementRepository: 已创建新数据库");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AchievementRepository: 数据库迁移处理异常 - {ex.Message}");

            try
            {
                using var context = new AchievementDbContext(dbPath);
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
            }
            catch (Exception ex2)
            {
                Debug.WriteLine($"AchievementRepository: 迁移历史回退创建失败 - {ex2.Message}");
            }
        }
    }

    // ---- Categories ----

    public List<AchievementCategoryEntity> GetAllCategories()
    {
        try
        {
            using var context = CreateContext();
            return context.Categories.ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AchievementRepo] 加载分类失败: {ex.Message}");
            return new List<AchievementCategoryEntity>();
        }
    }

    public void InsertOrIgnoreCategory(string name, string? iconUrl)
    {
        using var context = CreateContext();
        if (!context.Categories.Any(c => c.Name == name))
        {
            context.Categories.Add(new AchievementCategoryEntity { Name = name, IconUrl = iconUrl });
            context.SaveChanges();
        }
    }

    public int InsertOrIgnoreCategories(IEnumerable<(string Name, string? IconUrl)> categories)
    {
        using var context = CreateContext();
        var existingNames = context.Categories.Select(c => c.Name).ToHashSet();
        int count = 0;
        foreach (var (name, iconUrl) in categories)
        {
            if (!existingNames.Contains(name))
            {
                context.Categories.Add(new AchievementCategoryEntity { Name = name, IconUrl = iconUrl });
                existingNames.Add(name);
                count++;
            }
        }

        context.SaveChanges();
        return count;
    }

    // ---- Achievements ----

    public List<AchievementEntity> GetAllAchievements()
    {
        try
        {
            using var context = CreateContext();
            return context.Achievements.ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AchievementRepo] 加载成就失败: {ex.Message}");
            return new List<AchievementEntity>();
        }
    }

    public (int AddedCategories, int AddedAchievements, int UpdatedAchievements) SynchronizeCatalog(
        IEnumerable<(string Name, string? IconUrl)> categories, IReadOnlyCollection<AchievementEntity> achievements)
    {
        using var context = CreateContext();
        using var transaction = context.Database.BeginTransaction();

        var existingCategories = context.Categories.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var addedCategories = 0;
        foreach (var (name, iconUrl) in categories)
        {
            if (existingCategories.TryGetValue(name, out var category))
            {
                category.IconUrl = iconUrl;
            }
            else
            {
                category = new AchievementCategoryEntity { Name = name, IconUrl = iconUrl };
                context.Categories.Add(category);
                existingCategories.Add(name, category);
                addedCategories++;
            }
        }

        var existingAchievements = context.Achievements.OrderBy(a => a.Uid).ToList();
        var identities = existingAchievements.ToDictionary(a => a, AchievementIdentity.FromEntity);
        var byId = existingAchievements.Where(a => a.Id > 0).ToLookup(a => a.Id);
        var byIdentity = existingAchievements.ToLookup(a => identities[a]);
        var matchedAchievements = new HashSet<AchievementEntity>();
        var seenAchievements = new HashSet<(int Id, AchievementIdentity Identity)>();
        var addedAchievements = 0;
        var updatedAchievements = 0;

        foreach (var achievement in achievements)
        {
            var identity = AchievementIdentity.FromEntity(achievement);
            if (!seenAchievements.Add((achievement.Id, identity)))
            {
                continue;
            }

            AchievementEntity? existing = null;
            if (achievement.Id > 0)
            {
                var candidates = byId[achievement.Id].Where(a => !matchedAchievements.Contains(a)).ToList();
                existing = candidates.FirstOrDefault(a => identities[a] == identity)
                           ?? candidates.FirstOrDefault(a => identities[a].StageIndex == identity.StageIndex)
                           ?? candidates.FirstOrDefault();
            }

            existing ??= byIdentity[identity].FirstOrDefault(a =>
                !matchedAchievements.Contains(a) &&
                (!string.IsNullOrEmpty(identity.SeriesId) || achievement.Id <= 0 || a.Id <= 0 ||
                 a.Id == achievement.Id));

            if (existing == null)
            {
                context.Achievements.Add(achievement);
                addedAchievements++;
                continue;
            }

            matchedAchievements.Add(existing);
            var id = achievement.Id > 0 ? achievement.Id : existing.Id;
            var maxProgress = achievement.MaxProgress > 0 ? achievement.MaxProgress : existing.MaxProgress;
            if (existing.Id == id && existing.Title == achievement.Title &&
                existing.CategoryName == achievement.CategoryName && existing.RawJson == achievement.RawJson &&
                existing.MaxProgress == maxProgress)
            {
                continue;
            }

            existing.Id = id;
            existing.Title = achievement.Title;
            existing.CategoryName = achievement.CategoryName;
            existing.RawJson = achievement.RawJson;
            existing.MaxProgress = maxProgress;
            updatedAchievements++;
        }

        context.SaveChanges();
        transaction.Commit();
        return (addedCategories, addedAchievements, updatedAchievements);
    }

    public void InsertAchievement(AchievementEntity achievement)
    {
        using var context = CreateContext();
        context.Achievements.Add(achievement);
        context.SaveChanges();
    }

    public void InsertAchievements(List<AchievementEntity> achievements)
    {
        using var context = CreateContext();
        context.Achievements.AddRange(achievements);
        context.SaveChanges();
    }

    public void UpdateAchievement(int uid, bool isCompleted, int currentProgress, int maxProgress,
        long completionTimestamp)
    {
        using var context = CreateContext();
        var entity = context.Achievements.Find(uid);
        if (entity != null)
        {
            entity.IsCompleted = isCompleted ? 1 : 0;
            entity.CurrentProgress = currentProgress;
            entity.MaxProgress = maxProgress;
            entity.CompletionTimestamp = completionTimestamp;
            context.SaveChanges();
        }
    }

    public void UpdateAchievementsBatch(
        Dictionary<int, (bool IsCompleted, int CurrentProgress, int MaxProgress, long CompletionTimestamp)> updates)
    {
        using var context = CreateContext();
        foreach (var (uid, (isCompleted, currentProgress, maxProgress, completionTimestamp)) in updates)
        {
            var entity = context.Achievements.Find(uid);
            if (entity != null)
            {
                entity.IsCompleted = isCompleted ? 1 : 0;
                entity.CurrentProgress = currentProgress;
                entity.MaxProgress = maxProgress;
                entity.CompletionTimestamp = completionTimestamp;
            }
        }

        context.SaveChanges();
    }
}