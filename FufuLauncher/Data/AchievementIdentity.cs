using System.Text.Json;
using FufuLauncher.Data.Entities;

namespace FufuLauncher.Data;

internal readonly record struct AchievementIdentity(
    string CategoryName, string SeriesId, int StageIndex, string Title, string Description)
{
    public static AchievementIdentity FromEntity(AchievementEntity entity)
    {
        if (string.IsNullOrWhiteSpace(entity.RawJson))
        {
            return new AchievementIdentity(entity.CategoryName ?? "", "", 1, entity.Title ?? "", "");
        }

        using var document = JsonDocument.Parse(entity.RawJson);
        var root = document.RootElement;
        var seriesId = GetString(root, "series_id");
        var stageIndex = 1;
        if (root.TryGetProperty("stage_index", out var stage))
        {
            if (stage.ValueKind == JsonValueKind.Number)
            {
                stage.TryGetInt32(out stageIndex);
            }
            else if (stage.ValueKind == JsonValueKind.String)
            {
                int.TryParse(stage.GetString(), out stageIndex);
            }
        }

        return new AchievementIdentity(
            entity.CategoryName ?? "",
            seriesId,
            Math.Max(1, stageIndex),
            string.IsNullOrEmpty(seriesId) ? entity.Title ?? GetString(root, "title") : "",
            string.IsNullOrEmpty(seriesId) ? GetString(root, "description") : "");
    }

    private static string GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
