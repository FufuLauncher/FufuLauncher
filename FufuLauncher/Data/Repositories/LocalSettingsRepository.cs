using System.Diagnostics;
using FufuLauncher.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data.Repositories;

public class LocalSettingsRepository
{
    private readonly string _dbPath;

    public LocalSettingsRepository(string dbPath)
    {
        _dbPath = dbPath;
    }

    private static bool _migrated;

    private LocalSettingsDbContext CreateContext()
    {
        var context = new LocalSettingsDbContext(_dbPath);
        if (!_migrated)
        {
            try
            {
                context.Database.Migrate();
            }
            catch
            {
                // Existing database before EF migrations — tables already exist.
                // Create migration history table and mark InitialCreate as applied.
                context.Database.ExecuteSqlRaw(
                    "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT PRIMARY KEY, ProductVersion TEXT);");
                context.Database.ExecuteSqlRaw(
                    "INSERT OR IGNORE INTO __EFMigrationsHistory VALUES ('20240716000000_InitialCreate', '8.0.28');");
            }
            _migrated = true;
        }
        return context;
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
