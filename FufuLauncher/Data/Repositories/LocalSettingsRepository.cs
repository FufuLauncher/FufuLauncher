using System.Diagnostics;
using FufuLauncher.Data.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data.Repositories;

public class LocalSettingsRepository
{
    private readonly string _dbPath;

    public LocalSettingsRepository(string dbPath)
    {
        _dbPath = dbPath;
    }

    private static readonly object _migrateLock = new();
    private static bool _migrated;

    private LocalSettingsDbContext CreateContext()
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
        return new LocalSettingsDbContext(_dbPath);
    }

    /// <summary>
    /// Safely ensures the database is ready for use.
    /// For existing databases created by the old raw-SQLite version (which lack
    /// __EFMigrationsHistory), we skip Migrate() entirely and manually create the
    /// history record. This avoids a failed Migrate() transaction that can leave
    /// the SQLite connection in a broken state and cause data loss.
    /// </summary>
    private void PerformMigration()
    {
        try
        {
            // Ensure the directory exists so SQLite can create the DB file
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Check whether the Settings table already exists (pre-EF database
            // from v1.4.2.2 and earlier).  We use a short-lived connection so
            // we never pollute an EF context with a potentially failed migration.
            bool tableExists = false;
            try
            {
                using var checkConn = new SqliteConnection($"Data Source={_dbPath}");
                checkConn.Open();
                using var checkCmd = checkConn.CreateCommand();
                checkCmd.CommandText =
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Settings';";
                tableExists = (long)checkCmd.ExecuteScalar()! > 0;
            }
            catch
            {
                // If we can't even open the connection, let Migrate() handle it
            }

            if (tableExists)
            {
                // Pre-existing database — skip Migrate() to avoid a failed
                // CREATE TABLE.  Manually create the migration history so EF
                // knows the InitialCreate migration has been applied.
                using var context = new LocalSettingsDbContext(_dbPath);
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
                Debug.WriteLine("LocalSettingsRepository: 检测到现有数据库，已跳过迁移");
            }
            else
            {
                // Fresh database — let EF Migrate() create everything
                using var context = new LocalSettingsDbContext(_dbPath);
                context.Database.Migrate();
                Debug.WriteLine("LocalSettingsRepository: 已创建新数据库");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalSettingsRepository: 数据库迁移处理异常 - {ex.Message}");

            // Last-resort fallback: try to create the migration history manually
            // on a fresh context, so the app can at least start.
            try
            {
                using var context = new LocalSettingsDbContext(_dbPath);
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
            }
            catch (Exception ex2)
            {
                Debug.WriteLine($"LocalSettingsRepository: 迁移历史回退创建失败 - {ex2.Message}");
            }
        }
    }

    public async Task<Dictionary<string, string>> GetAllSettingsAsync()
    {
        try
        {
            using var context = CreateContext();
            var settings = await context.Settings.ToListAsync();
            return settings.ToDictionary(s => s.Key, s => s.Value ?? string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalSettingsRepository: 加载设置失败 - {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    public Dictionary<string, string> GetAllSettings()
    {
        try
        {
            using var context = CreateContext();
            return context.Settings.ToDictionary(s => s.Key, s => s.Value ?? string.Empty);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalSettingsRepository: 加载设置失败 - {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    public async Task UpsertSettingAsync(string key, string value)
    {
        try
        {
            using var context = CreateContext();
            var existing = await context.Settings.FindAsync(key);
            if (existing != null)
            {
                existing.Value = value;
            }
            else
            {
                context.Settings.Add(new SettingEntity { Key = key, Value = value });
            }
            await context.SaveChangesAsync();
            Debug.WriteLine($"LocalSettingsRepository: 已保存 '{key}'");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalSettingsRepository: 保存设置失败 - {ex.Message}");
        }
    }

    public async Task DeleteSettingAsync(string key)
    {
        try
        {
            using var context = CreateContext();
            var entity = await context.Settings.FindAsync(key);
            if (entity != null)
            {
                context.Settings.Remove(entity);
                await context.SaveChangesAsync();
                Debug.WriteLine($"LocalSettingsRepository: 已删除 '{key}'");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalSettingsRepository: 删除设置失败 - {ex.Message}");
        }
    }

    public async Task<List<SettingEntity>> GetAllSettingEntitiesAsync()
    {
        try
        {
            using var context = CreateContext();
            return await context.Settings.ToListAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalSettingsRepository: 加载实体失败 - {ex.Message}");
            return new List<SettingEntity>();
        }
    }

    public async Task ReplaceAllSettingsAsync(List<SettingEntity> settings)
    {
        using var context = CreateContext();
        context.Settings.RemoveRange(context.Settings);
        foreach (var setting in settings)
        {
            if (!string.IsNullOrWhiteSpace(setting.Key))
                context.Settings.Add(setting);
        }
        await context.SaveChangesAsync();
    }
}
