namespace FufuLauncher.Models;

internal static class AchievementFilter
{
    public static List<AchievementItem> Apply(
        IEnumerable<AchievementItem> achievements, string? searchText, bool hideCompleted, string? version)
    {
        var search = searchText?.Trim();
        var results = new List<AchievementItem>();
        foreach (var item in achievements)
        {
            var stages = item.IsGroup ? item.Children.AsEnumerable() : Enumerable.Repeat(item, 1);
            if (hideCompleted && stages.All(stage => stage.IsCompleted))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(version) && !stages.Any(stage => stage.Version == version))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(search) &&
                item.Title?.Contains(search, StringComparison.OrdinalIgnoreCase) != true &&
                !stages.Any(stage =>
                    stage.Title?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
                    stage.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) == true))
            {
                continue;
            }

            results.Add(item);
        }

        return results;
    }
}
