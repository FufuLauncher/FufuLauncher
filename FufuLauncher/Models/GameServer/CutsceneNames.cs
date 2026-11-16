/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models.GameServer;

internal sealed record CutsceneName(CutsceneChapter Chapter, IReadOnlyDictionary<string, string> Titles,
    bool AllowRemoval, string? AllowRemovalInVersion);

internal static class CutsceneNames
{
    private sealed record Catalog(ChapterEntry[] Chapters);
    private sealed record ChapterEntry(string Category, Dictionary<string, string> Titles,
        Dictionary<string, Dictionary<string, string>> Scenes, bool AllowRemoval = false,
        string? AllowRemovalInVersion = null, string? ReleaseVersion = null, int QuestChapterId = 0);

    private static readonly Lazy<Dictionary<string, CutsceneName>> Names = new(Load);

    public static CutsceneName? Find(string filename, CutsceneTraveler? traveler)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        if (traveler is not null)
            stem = stem[..stem.LastIndexOf('_')];
        return Names.Value.GetValueOrDefault(stem);
    }

    public static string Localize(IReadOnlyDictionary<string, string> titles)
    {
        string culture = ResourceExtensions.CurrentCulture ?? "zh-CN";
        return titles.GetValueOrDefault(culture) ?? titles.GetValueOrDefault("en-US") ?? titles["zh-CN"];
    }

    private static Dictionary<string, CutsceneName> Load()
    {
        try
        {
            using Stream source = typeof(CutsceneNames).Assembly.GetManifestResourceStream("FufuLauncher.Assets.CutsceneNames.json")
                ?? throw new InvalidDataException("Cutscene name catalog is missing.");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            Catalog catalog = JsonSerializer.Deserialize<Catalog>(source, options)
                ?? throw new InvalidDataException("Cutscene name catalog is empty.");
            var names = new Dictionary<string, CutsceneName>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in catalog.Chapters)
            {
                Version.TryParse(entry.ReleaseVersion, out Version? releaseVersion);
                var chapter = new CutsceneChapter(Enum.Parse<CutsceneCategory>(entry.Category), string.Empty,
                    Titles: entry.Titles, ReleaseVersion: releaseVersion, QuestChapterId: entry.QuestChapterId);
                foreach (var (stem, titles) in entry.Scenes)
                    names.Add(stem, new CutsceneName(chapter, titles, entry.AllowRemoval, entry.AllowRemovalInVersion));
            }
            return names;
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneNames), ex.Message);
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
