using FufuLauncher.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data;

public class AchievementDbContext : DbContext
{
    private string _dbPath;

    public DbSet<AchievementCategoryEntity> Categories => Set<AchievementCategoryEntity>();
    public DbSet<AchievementEntity> Achievements => Set<AchievementEntity>();

    public AchievementDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    public void ChangeDatabase(string newDbPath)
    {
        _dbPath = newDbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AchievementCategoryEntity>(entity =>
        {
            entity.HasKey(e => e.Name);
        });

        modelBuilder.Entity<AchievementEntity>(entity =>
        {
            entity.HasKey(e => e.Uid);
            entity.Property(e => e.Uid).ValueGeneratedOnAdd();
        });
    }
}
