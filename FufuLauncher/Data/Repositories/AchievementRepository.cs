using System.Diagnostics;
using FufuLauncher.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data.Repositories;

public class AchievementRepository
{
    private string _dbPath;

    public AchievementRepository(string dbPath)
    {
        _dbPath = dbPath;
    }

    public void ChangeDatabase(string newDbPath)
    {
        _dbPath = newDbPath;
    }

    private static bool _migrated;

    private AchievementDbContext CreateContext()
    {
        var context = new AchievementDbContext(_dbPath);
        if (!_migrated)
        {
            try
            {
                context.Database.Migrate();
            }
            catch
            {
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
            }
            _migrated = true;
        }
        return context;
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

    public HashSet<int> GetExistingAchievementIds()
    {
        using var context = CreateContext();
        return context.Achievements.Select(a => a.Id).ToHashSet();
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

    public void UpdateAchievement(int uid, bool isCompleted, int currentProgress, int maxProgress, long completionTimestamp)
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

    public void UpdateAchievementsBatch(Dictionary<int, (bool IsCompleted, int CurrentProgress, int MaxProgress, long CompletionTimestamp)> updates)
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
