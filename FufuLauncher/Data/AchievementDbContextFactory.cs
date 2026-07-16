using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FufuLauncher.Data;

public class AchievementDbContextFactory : IDesignTimeDbContextFactory<AchievementDbContext>
{
    public AchievementDbContext CreateDbContext(string[] args)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "ef_design_achievements.db");
        return new AchievementDbContext(dbPath);
    }
}
