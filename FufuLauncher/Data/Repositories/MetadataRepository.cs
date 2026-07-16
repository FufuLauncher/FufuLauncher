using System.Diagnostics;
using FufuLauncher.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data.Repositories;

public class MetadataRepository
{
    private readonly string _dbPath;

    public MetadataRepository(string dbPath)
    {
        _dbPath = dbPath;
    }

    private static bool _migrated;

    private MetadataDbContext CreateContext()
    {
        var context = new MetadataDbContext(_dbPath);
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

    // ---- Metadata (scraped items) ----

    public List<MetadataEntity> GetAllMetadata()
    {
        try
        {
            using var context = CreateContext();
            return context.Metadata.ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetadataRepo] 加载元数据失败: {ex.Message}");
            return new List<MetadataEntity>();
        }
    }

    public void UpsertMetadata(List<MetadataEntity> items)
    {
        using var context = CreateContext();
        foreach (var item in items)
        {
            var existing = context.Metadata.Find(item.Name);
            if (existing != null)
            {
                existing.ImgSrc = item.ImgSrc;
                existing.ElementSrc = item.ElementSrc;
                existing.Type = item.Type;
                existing.Rank = item.Rank;
                existing.ItemId = item.ItemId;
            }
            else
            {
                context.Metadata.Add(item);
            }
        }
        context.SaveChanges();
    }

    // ---- GachaLogs ----

    public List<string> GetDistinctUids()
    {
        try
        {
            using var context = CreateContext();
            return context.GachaLogs
                .Select(l => l.Uid)
                .Distinct()
                .OrderBy(u => u)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public List<GachaLogEntity> GetGachaLogsByUid(string uid)
    {
        try
        {
            using var context = CreateContext();
            return context.GachaLogs
                .Where(l => l.Uid == uid)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetadataRepo] 加载抽卡日志失败: {ex.Message}");
            return new List<GachaLogEntity>();
        }
    }

    public void ReplaceGachaLogs(string uid, List<GachaLogEntity> logs)
    {
        using var context = CreateContext();
        var existing = context.GachaLogs.Where(l => l.Uid == uid);
        context.GachaLogs.RemoveRange(existing);
        foreach (var log in logs)
        {
            log.Uid = uid;
            context.GachaLogs.Add(log);
        }
        context.SaveChanges();
    }

    public void InsertOrIgnoreGachaLogs(List<GachaLogEntity> logs)
    {
        using var context = CreateContext();
        var existingIds = context.GachaLogs
            .Where(l => logs.Select(x => x.Id).Contains(l.Id))
            .Select(l => l.Id)
            .ToHashSet();

        foreach (var log in logs)
        {
            if (!existingIds.Contains(log.Id))
            {
                context.GachaLogs.Add(log);
            }
        }
        context.SaveChanges();
    }

    public void DeleteGachaLogsByUid(string uid)
    {
        using var context = CreateContext();
        var logs = context.GachaLogs.Where(l => l.Uid == uid);
        context.GachaLogs.RemoveRange(logs);
        context.SaveChanges();
    }

    // ---- GachaPoolMetadata ----

    public bool HasPoolMetadata()
    {
        try
        {
            using var context = CreateContext();
            return context.GachaPoolMetadata.Any();
        }
        catch
        {
            return false;
        }
    }

    public List<GachaPoolMetadataEntity> GetPoolMetadataByType(string poolType)
    {
        try
        {
            using var context = CreateContext();
            return context.GachaPoolMetadata
                .Where(p => p.PoolType == poolType)
                .OrderByDescending(p => p.StartTime)
                .ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetadataRepo] 加载卡池元数据失败: {ex.Message}");
            return new List<GachaPoolMetadataEntity>();
        }
    }

    public void UpsertPoolMetadata(string poolType, List<GachaPoolMetadataEntity> pools)
    {
        using var context = CreateContext();
        foreach (var pool in pools)
        {
            pool.PoolType = poolType;
            var existing = context.GachaPoolMetadata.Find(pool.Version, pool.PoolType);
            if (existing != null)
            {
                existing.StartTime = pool.StartTime;
                existing.EndTime = pool.EndTime;
                existing.UpItems = pool.UpItems;
                existing.UpItemNames = pool.UpItemNames;
            }
            else
            {
                context.GachaPoolMetadata.Add(pool);
            }
        }
        context.SaveChanges();
    }
}
