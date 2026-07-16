using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FufuLauncher.Data.Migrations.Metadata;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Metadata",
            columns: table => new
            {
                Name = table.Column<string>(type: "TEXT", nullable: false),
                ImgSrc = table.Column<string>(type: "TEXT", nullable: true),
                ElementSrc = table.Column<string>(type: "TEXT", nullable: true),
                Type = table.Column<string>(type: "TEXT", nullable: true),
                Rank = table.Column<string>(type: "TEXT", nullable: true),
                ItemId = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Metadata", x => x.Name);
            });

        migrationBuilder.CreateTable(
            name: "GachaLogs",
            columns: table => new
            {
                Id = table.Column<string>(type: "TEXT", nullable: false),
                Uid = table.Column<string>(type: "TEXT", nullable: false),
                GachaType = table.Column<string>(type: "TEXT", nullable: false),
                ItemId = table.Column<string>(type: "TEXT", nullable: true),
                Count = table.Column<string>(type: "TEXT", nullable: true),
                Time = table.Column<string>(type: "TEXT", nullable: true),
                Name = table.Column<string>(type: "TEXT", nullable: true),
                Lang = table.Column<string>(type: "TEXT", nullable: true),
                ItemType = table.Column<string>(type: "TEXT", nullable: true),
                RankType = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GachaLogs", x => new { x.Id, x.Uid });
            });

        migrationBuilder.CreateIndex(
            name: "IX_GachaLogs_Uid",
            table: "GachaLogs",
            column: "Uid");

        migrationBuilder.CreateTable(
            name: "GachaPoolMetadata",
            columns: table => new
            {
                Version = table.Column<string>(type: "TEXT", nullable: false),
                PoolType = table.Column<string>(type: "TEXT", nullable: false),
                StartTime = table.Column<string>(type: "TEXT", nullable: false),
                EndTime = table.Column<string>(type: "TEXT", nullable: false),
                UpItems = table.Column<string>(type: "TEXT", nullable: false),
                UpItemNames = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "[]")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GachaPoolMetadata", x => new { x.Version, x.PoolType });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "GachaPoolMetadata");
        migrationBuilder.DropTable(name: "GachaLogs");
        migrationBuilder.DropTable(name: "Metadata");
    }
}
