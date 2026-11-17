/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models.GameServer;

internal sealed record CutsceneName(CutsceneChapter Chapter, IReadOnlyDictionary<string, string> Titles);

internal sealed record CutsceneVoiceQuestRef(int Id, int ChapterId);

internal sealed record CutsceneVoiceGroup(
    string GroupId,
    IReadOnlyList<string> Packages,
    IReadOnlyList<string> Videos,
    IReadOnlyList<CutsceneVoiceQuestRef> Quests);

internal sealed record CutsceneNameCatalog(
    Version GameVersion,
    int Revision,
    IReadOnlyDictionary<string, CutsceneName> Entries,
    IReadOnlyDictionary<int, CutsceneChapter> Chapters,
    IReadOnlyDictionary<int, CutsceneChapter> MainQuestChapters,
    IReadOnlyDictionary<string, CutsceneVoiceGroup> VoiceGroups)
{
    public bool IsNewerThan(CutsceneNameCatalog other) => GameVersion > other.GameVersion
        || (GameVersion == other.GameVersion && Revision > other.Revision);

    public CutsceneName? FindByFilename(string filename)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        return Entries.GetValueOrDefault(stem) ?? Entries.GetValueOrDefault(CutsceneNames.CanonicalSceneKey(filename));
    }

    public CutsceneChapter? FindChapter(CutsceneVoiceQuestRef quest) =>
        Chapters.GetValueOrDefault(quest.ChapterId) ?? MainQuestChapters.GetValueOrDefault(quest.Id);
}

internal static class CutsceneNames
{
    private sealed record Catalog(
        string GameVersion,
        ChapterEntry[] Chapters,
        int Revision = 0,
        Dictionary<string, VoiceGroupEntry>? VoiceGroups = null);

    private sealed record ChapterEntry(string Category, Dictionary<string, string> Titles,
        JsonElement Scenes, string? ReleaseVersion = null, int QuestChapterId = 0, int MainQuestId = 0);

    private sealed record VoiceGroupEntry(
        string[]? Packages,
        string[]? Videos,
        VoiceQuestEntry[]? Quests);

    private sealed record VoiceQuestEntry(int Id, int ChapterId = 0);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string[] TravelerSuffixes = ["_PlayerBoy", "_PlayerGirl", "_Boy", "_Girl"];

    internal static string CanonicalSceneKey(string filename)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        foreach (string suffix in TravelerSuffixes)
        {
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return stem[..^suffix.Length];
        }
        return stem;
    }

    public static string Localize(IReadOnlyDictionary<string, string> titles)
    {
        string culture = ResourceExtensions.CurrentCulture ?? "zh-CN";
        return titles.GetValueOrDefault(culture) ?? titles.GetValueOrDefault("en-US") ?? titles["zh-CN"];
    }

    public static CutsceneNameCatalog Read(Stream source)
    {
        Catalog catalog = JsonSerializer.Deserialize<Catalog>(source, JsonOptions)
            ?? throw new InvalidDataException("Cutscene name catalog is empty.");
        if (!Version.TryParse(catalog.GameVersion, out Version? version) || version.Build < 0
            || version.Revision >= 0 || catalog.Revision < 0 || catalog.Chapters is not { Length: > 0 })
            throw new InvalidDataException("Invalid cutscene catalog version or chapters.");

        var names = new Dictionary<string, CutsceneName>(StringComparer.OrdinalIgnoreCase);
        var chapters = new Dictionary<int, CutsceneChapter>();
        var mainQuestChapters = new Dictionary<int, CutsceneChapter>();
        foreach (ChapterEntry entry in catalog.Chapters)
        {
            if (entry is null || !Enum.TryParse(entry.Category, out CutsceneCategory category)
                || !Enum.IsDefined(category) || entry.QuestChapterId < 0 || entry.MainQuestId < 0
                || !HasTitles(entry.Titles)
                || entry.Scenes.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid cutscene chapter.");

            Version? releaseVersion = null;
            if (entry.ReleaseVersion is not null && !Version.TryParse(entry.ReleaseVersion, out releaseVersion))
                throw new InvalidDataException("Invalid cutscene chapter release version.");

            category = CutsceneChapter.GetCategoryOverride(entry.QuestChapterId) ?? category;
            var chapter = new CutsceneChapter(category, string.Empty, Titles: entry.Titles,
                ReleaseVersion: releaseVersion, QuestChapterId: entry.QuestChapterId);
            if (entry.QuestChapterId > 0)
                chapters.TryAdd(entry.QuestChapterId, chapter);
            if (entry.MainQuestId > 0)
                mainQuestChapters.TryAdd(entry.MainQuestId, chapter);
            foreach (JsonProperty scene in entry.Scenes.EnumerateObject())
            {
                if (scene.Name.Length == 0 || scene.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
                    throw new InvalidDataException("Invalid cutscene filename key.");
                var titles = scene.Value.Deserialize<Dictionary<string, string>>(JsonOptions);
                if (titles is null || !HasTitles(titles)
                    || !names.TryAdd(scene.Name, new CutsceneName(chapter, titles)))
                    throw new InvalidDataException("Invalid or duplicate cutscene entry.");
            }
        }
        if (names.Count == 0)
            throw new InvalidDataException("Cutscene name catalog is empty.");

        return new CutsceneNameCatalog(version, catalog.Revision, names, chapters, mainQuestChapters,
            ParseVoiceGroups(catalog.VoiceGroups));
    }

    private static IReadOnlyDictionary<string, CutsceneVoiceGroup> ParseVoiceGroups(
        Dictionary<string, VoiceGroupEntry>? rawGroups)
    {
        var result = new Dictionary<string, CutsceneVoiceGroup>(StringComparer.OrdinalIgnoreCase);
        if (rawGroups is null)
            return result;

        foreach (var (groupId, entry) in rawGroups)
        {
            if (string.IsNullOrWhiteSpace(groupId) || entry is null || entry.Packages is not { Length: > 0 })
                continue;

            string[] packages = entry.Packages
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim().Replace('\\', '/').TrimStart('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (packages.Length == 0)
                continue;

            string[] videos = (entry.Videos ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => Path.GetFileName(v.Trim().Replace('\\', '/')))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            CutsceneVoiceQuestRef[] quests = (entry.Quests ?? [])
                .Where(q => q is not null && q.Id > 0)
                .Select(q => new CutsceneVoiceQuestRef(q.Id, Math.Max(0, q.ChapterId)))
                .ToArray();

            result[groupId.Trim()] = new CutsceneVoiceGroup(groupId.Trim(), packages, videos, quests);
        }

        return result;
    }

    private static bool HasTitles(Dictionary<string, string>? titles) =>
        titles is not null && !string.IsNullOrWhiteSpace(titles.GetValueOrDefault("zh-CN"))
        && titles.Values.All(title => !string.IsNullOrWhiteSpace(title));
}
