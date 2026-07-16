using FufuLauncher.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace FufuLauncher.Data;

public class MetadataDbContext : DbContext
{
    private readonly string _dbPath;

    public DbSet<MetadataEntity> Metadata => Set<MetadataEntity>();
    public DbSet<GachaLogEntity> GachaLogs => Set<GachaLogEntity>();
    public DbSet<GachaPoolMetadataEntity> GachaPoolMetadata => Set<GachaPoolMetadataEntity>();

    public MetadataDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MetadataEntity>(entity =>
        {
            entity.HasKey(e => e.Name);
        });

        modelBuilder.Entity<GachaLogEntity>(entity =>
        {
            entity.HasKey(e => new { e.Id, e.Uid });
            entity.HasIndex(e => e.Uid);
        });

        modelBuilder.Entity<GachaPoolMetadataEntity>(entity =>
        {
            entity.HasKey(e => new { e.Version, e.PoolType });
        });
    }
}
