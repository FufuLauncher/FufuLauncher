using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FufuLauncher.Data.Migrations.Achievement;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Categories",
            columns: table => new
            {
                Name = table.Column<string>(type: "TEXT", nullable: false),
                IconUrl = table.Column<string>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Categories", x => x.Name);
            });

        migrationBuilder.CreateTable(
            name: "Achievements",
            columns: table => new
            {
                Uid = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                Title = table.Column<string>(type: "TEXT", nullable: true),
                CategoryName = table.Column<string>(type: "TEXT", nullable: true),
                RawJson = table.Column<string>(type: "TEXT", nullable: true),
                IsCompleted = table.Column<int>(type: "INTEGER", nullable: false),
                CurrentProgress = table.Column<int>(type: "INTEGER", nullable: false),
                MaxProgress = table.Column<int>(type: "INTEGER", nullable: false),
                CompletionTimestamp = table.Column<long>(type: "INTEGER", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Achievements", x => x.Uid);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Achievements");
        migrationBuilder.DropTable(name: "Categories");
    }
}
