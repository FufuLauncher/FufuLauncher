/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models.GameServer;

internal sealed record CutsceneName(CutsceneChapter Chapter, IReadOnlyDictionary<string, string> Titles,
    bool AllowRemoval, string? AllowRemovalInVersion);

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
    IReadOnlyDictionary<string, CutsceneVoiceGroup> VoiceGroups,
    string? VoiceSource = null,
    bool SelfMerged = false,
    Version? BuiltInBaseGameVersion = null,
    int BuiltInBaseRevision = 0)
{
    public string VersionLabel => $"{GameVersion} · r{Revision}";

    public bool IsNewerThan(CutsceneNameCatalog other) => GameVersion > other.GameVersion
        || (GameVersion == other.GameVersion && Revision > other.Revision);

    public CutsceneName? Find(string filename, CutsceneTraveler? traveler)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        if (traveler is not null)
        {
            int underscore = stem.LastIndexOf('_');
            if (underscore > 0)
                stem = stem[..underscore];
        }
        return Entries.GetValueOrDefault(stem);
    }

    public CutsceneName? FindByFilename(string filename)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        if (Entries.TryGetValue(stem, out CutsceneName? exact))
            return exact;

        int underscore = stem.LastIndexOf('_');
        if (underscore > 0)
        {
            string suffix = stem[underscore..];
            if (suffix.Equals("_Boy", StringComparison.OrdinalIgnoreCase)
                || suffix.Equals("_Girl", StringComparison.OrdinalIgnoreCase)
                || suffix.Equals("_PlayerBoy", StringComparison.OrdinalIgnoreCase)
                || suffix.Equals("_PlayerGirl", StringComparison.OrdinalIgnoreCase))
            {
                return Entries.GetValueOrDefault(stem[..underscore]);
            }
        }
        return null;
    }

    public CutsceneChapter? FindChapterByQuestChapterId(int questChapterId)
    {
        if (questChapterId <= 0)
            return null;

        foreach (CutsceneName entry in Entries.Values)
        {
            if (entry.Chapter.QuestChapterId == questChapterId)
                return entry.Chapter;
        }
        return null;
    }
}

internal static class CutsceneNames
{
    private sealed record Catalog(
        string GameVersion,
        ChapterEntry[] Chapters,
        int Revision = 0,
        string? VoiceSource = null,
        Dictionary<string, VoiceGroupEntry>? VoiceGroups = null,
        bool SelfMerged = false,
        string? BuiltInBaseGameVersion = null,
        int BuiltInBaseRevision = 0);

    private sealed record ChapterEntry(string Category, Dictionary<string, string> Titles,
        JsonElement Scenes, bool AllowRemoval = false,
        string? AllowRemovalInVersion = null, string? ReleaseVersion = null, int QuestChapterId = 0);

    private sealed record VoiceGroupEntry(
        string[]? Packages,
        string[]? Videos,
        VoiceQuestEntry[]? Quests);

    private sealed record VoiceQuestEntry(int Id, int ChapterId = 0);

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<CutsceneNameCatalog> Embedded = new(LoadEmbedded);
    public static CutsceneNameCatalog BuiltIn => Embedded.Value;

    public static CutsceneName? Find(string filename, CutsceneTraveler? traveler) => BuiltIn.Find(filename, traveler);

    public static string Localize(IReadOnlyDictionary<string, string> titles)
    {
        string culture = ResourceExtensions.CurrentCulture ?? "zh-CN";
        return titles.GetValueOrDefault(culture) ?? titles.GetValueOrDefault("en-US") ?? titles["zh-CN"];
    }

    public static CutsceneNameCatalog Read(Stream source, bool includeRemovalRules = false)
    {
        Catalog catalog = JsonSerializer.Deserialize<Catalog>(source, JsonOptions)
            ?? throw new InvalidDataException("Cutscene name catalog is empty.");
        if (!Version.TryParse(catalog.GameVersion, out Version? version) || version.Build < 0
            || version.Revision >= 0 || catalog.Revision < 0 || catalog.Chapters is not { Length: > 0 })
            throw new InvalidDataException("Invalid cutscene catalog version or chapters.");

        var names = new Dictionary<string, CutsceneName>(StringComparer.OrdinalIgnoreCase);
        foreach (ChapterEntry entry in catalog.Chapters)
        {
            if (entry is null || !Enum.TryParse(entry.Category, out CutsceneCategory category)
                || !Enum.IsDefined(category) || entry.QuestChapterId < 0 || !HasTitles(entry.Titles)
                || entry.Scenes.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid cutscene chapter.");

            Version? releaseVersion = null;
            if (entry.ReleaseVersion is not null && !Version.TryParse(entry.ReleaseVersion, out releaseVersion))
                throw new InvalidDataException("Invalid cutscene chapter release version.");

            var chapter = new CutsceneChapter(category, string.Empty, Titles: entry.Titles,
                ReleaseVersion: releaseVersion, QuestChapterId: entry.QuestChapterId);
            foreach (JsonProperty scene in entry.Scenes.EnumerateObject())
            {
                if (scene.Name.Length == 0 || scene.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-'))
                    throw new InvalidDataException("Invalid cutscene filename key.");
                var titles = scene.Value.Deserialize<Dictionary<string, string>>(JsonOptions);
                if (titles is null || !HasTitles(titles)
                    || !names.TryAdd(scene.Name, new CutsceneName(chapter, titles,
                        includeRemovalRules && entry.AllowRemoval, includeRemovalRules ? entry.AllowRemovalInVersion : null)))
                    throw new InvalidDataException("Invalid or duplicate cutscene entry.");
            }
        }
        if (names.Count == 0)
            throw new InvalidDataException("Cutscene name catalog is empty.");

        var voiceGroups = ParseVoiceGroups(catalog.VoiceGroups);
        Version? builtInBaseVersion = catalog.BuiltInBaseGameVersion is not null
            && Version.TryParse(catalog.BuiltInBaseGameVersion, out Version? parsedBase)
            ? parsedBase
            : null;
        return new CutsceneNameCatalog(version, catalog.Revision, names, voiceGroups, catalog.VoiceSource,
            catalog.SelfMerged, builtInBaseVersion, catalog.BuiltInBaseRevision);
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
                .Select(p => p.Replace('\\', '/').TrimStart('/'))
                .ToArray();
            if (packages.Length == 0)
                continue;

            string[] videos = (entry.Videos ?? [])
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => Path.GetFileName(v.Trim()))
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

    public static Stream OpenEmbeddedStream() =>
        typeof(CutsceneNames).Assembly.GetManifestResourceStream("FufuLauncher.Assets.CutsceneNames.json")
        ?? throw new InvalidDataException("Cutscene name catalog is missing.");

    private static CutsceneNameCatalog LoadEmbedded()
    {
        try
        {
            using Stream source = OpenEmbeddedStream();
            return Read(source, includeRemovalRules: true);
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneNames), ex.Message);
            return new CutsceneNameCatalog(
                new Version(0, 0, 0),
                0,
                new Dictionary<string, CutsceneName>(),
                new Dictionary<string, CutsceneVoiceGroup>());
        }
    }
}
