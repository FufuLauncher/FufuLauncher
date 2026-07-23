using System.Diagnostics;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data.Repositories;

public class MetadataRepository
{
    public MetadataRepository(string dbPath)
    {
        // The dbPath parameter is retained for backward compatibility with DI
        // registration, but the actual path is always resolved dynamically from
        // AppPaths.MetadataDb so that the repository stays in sync when
        // AppPaths.DataDir is changed during the first-run agreement flow.
    }

    private static readonly object _migrateLock = new();
    private static bool _migrated;

    private static string CurrentDbPath => AppPaths.MetadataDb;

    private MetadataDbContext CreateContext()
    {
        if (!_migrated)
        {
            lock (_migrateLock)
            {
                if (!_migrated)
                {
                    PerformMigration();
                    _migrated = true;
                }
            }
        }
        return new MetadataDbContext(CurrentDbPath);
    }

    /// <summary>
    /// Safely ensures the database is ready for use.
    /// For existing databases created by the old raw-SQLite version (which lack
    /// __EFMigrationsHistory), we skip Migrate() entirely and manually create the
    /// history record. This avoids a failed Migrate() transaction that can leave
    /// the SQLite connection in a broken state.
    /// </summary>
    private void PerformMigration()
    {
        var dbPath = CurrentDbPath;
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Check whether the Metadata table already exists (pre-EF database)
            bool tableExists = false;
            try
            {
                using var checkConn = new SqliteConnection($"Data Source={dbPath}");
                checkConn.Open();
                using var checkCmd = checkConn.CreateCommand();
                checkCmd.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Metadata';";
                tableExists = (long)checkCmd.ExecuteScalar()! > 0;
            }
            catch
            {
                // If we can't open the connection, let Migrate() handle it
            }

            if (tableExists)
            {
                using var context = new MetadataDbContext(dbPath);
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
                Debug.WriteLine("MetadataRepository: 检测到现有数据库，已跳过迁移");
            }
            else
            {
                using var context = new MetadataDbContext(dbPath);
                context.Database.Migrate();
                Debug.WriteLine("MetadataRepository: 已创建新数据库");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"MetadataRepository: 数据库迁移处理异常 - {ex.Message}");

            try
            {
                using var context = new MetadataDbContext(dbPath);
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
            }
            catch (Exception ex2)
            {
                Debug.WriteLine($"MetadataRepository: 迁移历史回退创建失败 - {ex2.Message}");
            }
        }
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
