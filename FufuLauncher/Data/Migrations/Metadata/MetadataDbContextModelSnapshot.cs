using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using FufuLauncher.Data.Entities;

#nullable disable

namespace FufuLauncher.Data.Migrations.Metadata;

[DbContext(typeof(MetadataDbContext))]
partial class MetadataDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.28");

        modelBuilder.Entity("FufuLauncher.Data.Entities.MetadataEntity", b =>
        {
            b.Property<string>("Name")
                .HasColumnType("TEXT")
                .HasColumnName("Name");

            b.Property<string>("ImgSrc")
                .HasColumnType("TEXT")
                .HasColumnName("ImgSrc");

            b.Property<string>("ElementSrc")
                .HasColumnType("TEXT")
                .HasColumnName("ElementSrc");

            b.Property<string>("Type")
                .HasColumnType("TEXT")
                .HasColumnName("Type");

            b.Property<string>("Rank")
                .HasColumnType("TEXT")
                .HasColumnName("Rank");

            b.Property<string>("ItemId")
                .HasColumnType("TEXT")
                .HasColumnName("ItemId");

            b.HasKey("Name");

            b.ToTable("Metadata");
        });

        modelBuilder.Entity("FufuLauncher.Data.Entities.GachaLogEntity", b =>
        {
            b.Property<string>("Id")
                .HasColumnType("TEXT")
                .HasColumnName("Id");

            b.Property<string>("Uid")
                .HasColumnType("TEXT")
                .HasColumnName("Uid");

            b.Property<string>("GachaType")
                .HasColumnType("TEXT")
                .HasColumnName("GachaType");

            b.Property<string>("ItemId")
                .HasColumnType("TEXT")
                .HasColumnName("ItemId");

            b.Property<string>("Count")
                .HasColumnType("TEXT")
                .HasColumnName("Count");

            b.Property<string>("Time")
                .HasColumnType("TEXT")
                .HasColumnName("Time");

            b.Property<string>("Name")
                .HasColumnType("TEXT")
                .HasColumnName("Name");

            b.Property<string>("Lang")
                .HasColumnType("TEXT")
                .HasColumnName("Lang");

            b.Property<string>("ItemType")
                .HasColumnType("TEXT")
                .HasColumnName("ItemType");

            b.Property<string>("RankType")
                .HasColumnType("TEXT")
                .HasColumnName("RankType");

            b.HasKey("Id", "Uid");

            b.HasIndex("Uid");

            b.ToTable("GachaLogs");
        });

        modelBuilder.Entity("FufuLauncher.Data.Entities.GachaPoolMetadataEntity", b =>
        {
            b.Property<string>("Version")
                .HasColumnType("TEXT")
                .HasColumnName("Version");

            b.Property<string>("PoolType")
                .HasColumnType("TEXT")
                .HasColumnName("PoolType");

            b.Property<string>("StartTime")
                .HasColumnType("TEXT")
                .HasColumnName("StartTime");

            b.Property<string>("EndTime")
                .HasColumnType("TEXT")
                .HasColumnName("EndTime");

            b.Property<string>("UpItems")
                .HasColumnType("TEXT")
                .HasColumnName("UpItems");

            b.Property<string>("UpItemNames")
                .IsRequired()
                .HasColumnType("TEXT")
                .HasDefaultValue("[]")
                .HasColumnName("UpItemNames");

            b.HasKey("Version", "PoolType");

            b.ToTable("GachaPoolMetadata");
        });
    }
}
