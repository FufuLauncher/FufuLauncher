using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FufuLauncher.Data;

public class LocalSettingsDbContextFactory : IDesignTimeDbContextFactory<LocalSettingsDbContext>
{
    public LocalSettingsDbContext CreateDbContext(string[] args)
    {
        // Use a temp path for design-time migrations — the actual path is determined at runtime.
        var dbPath = Path.Combine(Path.GetTempPath(), "ef_design_LocalSettings.db");
        return new LocalSettingsDbContext(dbPath);
    }
}
