using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using FufuLauncher.Data.Entities;

#nullable disable

namespace FufuLauncher.Data.Migrations.Achievement;

[DbContext(typeof(AchievementDbContext))]
partial class AchievementDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.28");

        modelBuilder.Entity("FufuLauncher.Data.Entities.AchievementCategoryEntity", b =>
        {
            b.Property<string>("Name")
                .HasColumnType("TEXT")
                .HasColumnName("Name");

            b.Property<string>("IconUrl")
                .HasColumnType("TEXT")
                .HasColumnName("IconUrl");

            b.HasKey("Name");

            b.ToTable("Categories");
        });

        modelBuilder.Entity("FufuLauncher.Data.Entities.AchievementEntity", b =>
        {
            b.Property<int>("Uid")
                .ValueGeneratedOnAdd()
                .HasColumnType("INTEGER")
                .HasColumnName("Uid");

            b.Property<int>("Id")
                .HasColumnType("INTEGER")
                .HasColumnName("Id");

            b.Property<string>("Title")
                .HasColumnType("TEXT")
                .HasColumnName("Title");

            b.Property<string>("CategoryName")
                .HasColumnType("TEXT")
                .HasColumnName("CategoryName");

            b.Property<string>("RawJson")
                .HasColumnType("TEXT")
                .HasColumnName("RawJson");

            b.Property<int>("IsCompleted")
                .HasColumnType("INTEGER")
                .HasColumnName("IsCompleted");

            b.Property<int>("CurrentProgress")
                .HasColumnType("INTEGER")
                .HasColumnName("CurrentProgress");

            b.Property<int>("MaxProgress")
                .HasColumnType("INTEGER")
                .HasColumnName("MaxProgress");

            b.Property<long>("CompletionTimestamp")
                .ValueGeneratedOnAdd()
                .HasColumnType("INTEGER")
                .HasDefaultValue(0L)
                .HasColumnName("CompletionTimestamp");

            b.HasKey("Uid");

            b.ToTable("Achievements");
        });
    }
}
