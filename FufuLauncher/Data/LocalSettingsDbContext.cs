using FufuLauncher.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data;

public class LocalSettingsDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<SettingEntity> Settings => Set<SettingEntity>();

    public LocalSettingsDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SettingEntity>(entity =>
        {
            entity.HasKey(e => e.Key);
        });
    }
}
