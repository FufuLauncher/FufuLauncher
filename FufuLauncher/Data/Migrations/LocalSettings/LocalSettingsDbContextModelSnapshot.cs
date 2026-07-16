using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using FufuLauncher.Data.Entities;

#nullable disable

namespace FufuLauncher.Data.Migrations.LocalSettings;

[DbContext(typeof(LocalSettingsDbContext))]
partial class LocalSettingsDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "8.0.28");

        modelBuilder.Entity("FufuLauncher.Data.Entities.SettingEntity", b =>
        {
            b.Property<string>("Key")
                .HasColumnType("TEXT")
                .HasColumnName("Key");

            b.Property<string>("Value")
                .HasColumnType("TEXT")
                .HasColumnName("Value");

            b.HasKey("Key");

            b.ToTable("Settings");
        });
    }
}
