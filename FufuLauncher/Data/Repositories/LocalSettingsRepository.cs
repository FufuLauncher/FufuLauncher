using System.Diagnostics;
using FufuLauncher.Data.Entities;
using FufuLauncher.Helpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data.Repositories;

public class LocalSettingsRepository
{
    private string DbPath => AppPaths.LocalSettingsDb;

    public LocalSettingsRepository() { }

    private static readonly object _migrateLock = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _migratedPaths
        = new(StringComparer.OrdinalIgnoreCase);

    private LocalSettingsDbContext CreateContext()
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
        return new LocalSettingsDbContext(dbPath);
    }

    private static void EnsureSettingsTable(string dbPath)
    {
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "CREATE TABLE IF NOT EXISTS \"Settings\" (\"Key\" TEXT NOT NULL PRIMARY KEY, \"Value\" TEXT);";
        cmd.ExecuteNonQuery();
    }

    private void PerformMigration(string dbPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Always ensure the Settings table exists via raw SQL first.
            // This is idempotent and avoids relying on EF Core's EnsureCreated(),
            // which can silently skip table creation when the database file
            // already exists but contains orphan tables (e.g. from a prior crash).
            EnsureSettingsTable(dbPath);

            using var context = new LocalSettingsDbContext(dbPath);
            context.Database.ExecuteSqlRaw(
                "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
            context.Database.ExecuteSqlRaw(
                "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
            Debug.WriteLine("LocalSettingsRepository: 数据库迁移完成");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LocalSettingsRepository: 数据库迁移处理异常 - {ex.Message}");

            try
            {
                EnsureSettingsTable(dbPath);
                using var context = new LocalSettingsDbContext(dbPath);
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
