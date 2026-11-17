/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Buffers;
using System.Buffers.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;
using FufuLauncher.Services.PluginMirror;

namespace FufuLauncher.Services.GameServer;

internal static partial class CutsceneCatalogUpdater
{
    private const string PublishedCatalogUrl =
        "https://raw.githubusercontent.com/FufuLauncher/FufuLauncher/master/FufuLauncher/Assets/CutsceneNames.json";
    private const string AnimeGameDataRepoApiCommitUrl =
        "https://api.github.com/repos/DimbreathBot/AnimeGameData/commits/master";
    private const string AnimeGameDataRawBase =
        "https://raw.githubusercontent.com/DimbreathBot/AnimeGameData";
    private const string PublishedDataFallback = "d54f6f0d423ded9d055bb740a3e5f3aa8f2667f1";
    private const string ResourceTable = "BinOutput/ResourceCollection/ConfigResourceCollection.json";
    private const int MaxTableBytes = 24 * 1024 * 1024;
    private const int MaxTextMapBytes = 64 * 1024 * 1024;

    private static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds(18);
    private static readonly TimeSpan TextMapTimeout = TimeSpan.FromSeconds(40);
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal sealed record QuestInfo(int Id, int ResId, int ChapterId, int Series, string? Type, ulong TitleHash);
    internal sealed record ChapterMeta(int Id, string? QuestType, int CityId, ulong TitleHash, ulong NumHash, ulong ImageTitleHash);
    internal sealed record ResourceCollectionEntry(IReadOnlyList<string> Packages, IReadOnlyList<string> Videos);
    private sealed record QuestTables(Dictionary<string, ResourceCollectionEntry> Resources,
        Dictionary<int, ChapterMeta> Chapters, List<QuestInfo> Quests, Dictionary<int, SubquestInfo> Subquests);
    private sealed record SubquestInfo(int MainId, ulong TitleHash);
    private sealed record VideoQuestMatch(CutsceneVoiceQuestRef Quest, ulong TitleHash, ulong MainTitleHash,
        bool ConfirmedByResource);
    private sealed record PendingScene(string Filename, CutsceneVoiceQuestRef? Quest, JsonNode? CuratedTitles = null,
        ulong TitleHash = 0, ulong MainTitleHash = 0);

    public static async Task<(byte[]? Bytes, CutsceneNameCatalog Catalog)> PullAndMergeAsync(
        HttpClient httpClient,
        SophonBuildClient sophonClient,
        GameServerScheme? preferredScheme,
        CutsceneNameCatalog? currentCatalog,
        byte[]? existingCachedBytes,
        IProgress<string>? status,
        CancellationToken token)
    {
        status?.Report("Cutscene_CatalogPullUpstream".GetLocalized());
        var (publishedBytes, publishedCatalog) = await DownloadWithMirrorsAsync(
            httpClient, PublishedCatalogUrl, CutsceneNameCatalogStore.MaxCatalogBytes, ReadPublishedCatalog, token)
            .ConfigureAwait(false);
        if (currentCatalog is null && publishedCatalog.VoiceGroups.Count > 0)
            return (publishedBytes, publishedCatalog);

        JsonObject publishedRoot = ParseCatalogRoot(publishedBytes);
        string pinnedRef = GetPublishedDataRef(publishedRoot);
        var (version, videos) = await TryFetchLiveSophonAsync(sophonClient, preferredScheme, token)
            .ConfigureAwait(false);
        // The published revision is matched to its game version. Upstream master may omit some of its quests.
        string commitRef = version is not null && version > publishedCatalog.GameVersion
            ? await TryResolveUpstreamCommitAsync(httpClient, token).ConfigureAwait(false) ?? pinnedRef
            : pinnedRef;

        QuestTables tables;
        try
        {
            tables = await DownloadTablesAsync(httpClient, commitRef, token).ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested && commitRef != pinnedRef)
        {
            commitRef = pinnedRef;
            tables = await DownloadTablesAsync(httpClient, commitRef, token).ConfigureAwait(false);
        }

        var merge = new CatalogMerge(publishedRoot, existingCachedBytes, tables, videos);
        var neededHashes = merge.GetNeededTextHashes();
        Dictionary<ulong, string>[] textMaps = [[], [], []];
        if (neededHashes.Count > 0)
        {
            status?.Report("Cutscene_CatalogPullTextMap".GetLocalized());
            string rawBase = $"{AnimeGameDataRawBase}/{commitRef}";
            textMaps = await Task.WhenAll(
                FetchCatalogTextMapAsync(httpClient, rawBase, "CHS", merge, neededHashes, token),
                FetchCatalogTextMapAsync(httpClient, rawBase, "CHT", merge, neededHashes, token),
                FetchCatalogTextMapAsync(httpClient, rawBase, "EN", merge, neededHashes, token))
                .ConfigureAwait(false);
        }

        status?.Report("Cutscene_CatalogMerging".GetLocalized());
        return merge.Finish(currentCatalog, version, commitRef, textMaps, token);
    }

    private static string GetPublishedDataRef(JsonObject publishedRoot)
    {
        string? source = publishedRoot["voiceSource"]?.GetValue<string>();
        string prefix = $"{AnimeGameDataRawBase}/";
        string suffix = $"/{ResourceTable}";
        if (source is not null && source.StartsWith(prefix, StringComparison.Ordinal)
            && source.EndsWith(suffix, StringComparison.Ordinal) && source.Length == prefix.Length + suffix.Length + 40)
        {
            string commit = source.Substring(prefix.Length, 40);
            if (commit.All(char.IsAsciiHexDigit))
                return commit;
        }
        return PublishedDataFallback;
    }

    private static JsonObject ParseCatalogRoot(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith("\uFEFF"u8))
            bytes = bytes[3..];
        return JsonNode.Parse(bytes, new JsonNodeOptions { PropertyNameCaseInsensitive = true })!.AsObject();
    }

    private static (byte[] Bytes, CutsceneNameCatalog Catalog) ReadPublishedCatalog(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return (bytes, CutsceneNames.Read(stream));
    }

    private static async Task<QuestTables> DownloadTablesAsync(
        HttpClient client, string commit, CancellationToken token)
    {
        string rawBase = $"{AnimeGameDataRawBase}/{commit}";
        var resources = DownloadWithMirrorsAsync(client, $"{rawBase}/{ResourceTable}", MaxTableBytes,
            ParseResourceCollections, token);
        var chapters = DownloadWithMirrorsAsync(client, $"{rawBase}/ExcelBinOutput/ChapterExcelConfigData.json",
            MaxTableBytes, ParseChapterExcel, token);
        var quests = DownloadWithMirrorsAsync(client, $"{rawBase}/ExcelBinOutput/MainQuestExcelConfigData.json",
            MaxTableBytes, ParseMainQuests, token);
        var subquests = TryDownloadSubquestsAsync(client, $"{rawBase}/ExcelBinOutput/QuestExcelConfigData.json", token);
        await Task.WhenAll(resources, chapters, quests, subquests).ConfigureAwait(false);
        return new QuestTables(await resources.ConfigureAwait(false), await chapters.ConfigureAwait(false),
            await quests.ConfigureAwait(false), await subquests.ConfigureAwait(false));
    }

    private static async Task<Dictionary<int, SubquestInfo>> TryDownloadSubquestsAsync(
        HttpClient client, string url, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(EndpointTimeout);
        try
        {
            return await DownloadWithMirrorsAsync(client, url, MaxTableBytes, ParseSubquests, bounded.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            DebugLog.Info(nameof(CutsceneCatalogUpdater), $"Optional subquest data unavailable ({ex.GetType().Name}).");
            return [];
        }
    }

    private sealed class CatalogMerge
    {
        private readonly JsonObject _published;
        private readonly JsonObject? _cached;
        private readonly Dictionary<int, ChapterMeta> _chapterMeta;
        private readonly Dictionary<int, QuestInfo> _questsById;
        private readonly ILookup<int, QuestInfo> _questsByChapter;
        private readonly List<JsonObject> _chapters = [];
        private readonly Dictionary<(int ChapterId, int QuestId), JsonObject> _chapterIndex = [];
        private readonly HashSet<string> _seenScenes = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<(int ChapterId, int QuestId), CutsceneVoiceQuestRef> _chaptersToResolve = [];
        private readonly Dictionary<string, PendingScene> _scenesToAdd =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly IReadOnlyList<CutsceneVoiceGroup> _groups;
        private readonly Dictionary<string, VideoQuestMatch> _videoQuests;

        public CatalogMerge(JsonObject published, byte[]? cachedBytes, QuestTables tables, IReadOnlySet<string> liveVideos)
        {
            _published = published;
            _cached = cachedBytes is not null ? ParseCatalogRoot(cachedBytes) : null;
            _chapterMeta = tables.Chapters;
            _questsById = tables.Quests.ToDictionary(q => q.Id);
            _questsByChapter = tables.Quests.ToLookup(quest => quest.ChapterId);
            _groups = BuildVoiceGroups(tables.Resources, tables.Quests);
            string[] files = liveVideos.Concat(_groups.SelectMany(group => group.Videos))
                .Order(StringComparer.OrdinalIgnoreCase).ToArray();
            _videoQuests = BuildVideoQuestIndex(files, _groups, _questsById, tables.Subquests);
            AddExistingChapters(_published, isPublished: true);
            if (_cached is not null)
                AddExistingChapters(_cached, isPublished: false);

            foreach (CutsceneVoiceGroup group in _groups.Where(group => group.Packages.Count > 0))
            {
                bool hasChapter = group.Quests.Any(quest => quest.ChapterId > 0);
                foreach (CutsceneVoiceQuestRef quest in group.Quests)
                {
                    if (quest.ChapterId > 0 || !hasChapter)
                        AddQuestChapter(quest);
                }
            }

            foreach (string filename in files)
            {
                string scene = CutsceneNames.CanonicalSceneKey(filename);
                if (!IsValidSceneKey(scene) || !filename.EndsWith(".usm", StringComparison.OrdinalIgnoreCase)
                    || !_seenScenes.Add(scene))
                    continue;

                VideoQuestMatch? match = _videoQuests.GetValueOrDefault(scene);
                _scenesToAdd.Add(scene, new PendingScene(filename, match?.Quest, TitleHash: match?.TitleHash ?? 0,
                    MainTitleHash: match?.MainTitleHash ?? 0));
            }

            foreach (PendingScene scene in _scenesToAdd.Values)
            {
                if (scene.Quest is { } quest)
                    AddQuestChapter(quest);
            }
        }

        private void AddExistingChapters(JsonObject root, bool isPublished)
        {
            if (root["chapters"] is not JsonArray chapters)
                return;

            foreach (JsonObject chapter in chapters.OfType<JsonObject>())
            {
                if (chapter["scenes"] is not JsonObject oldScenes)
                    continue;

                var key = (ChapterId: chapter["questChapterId"]?.GetValue<int>() ?? 0,
                    QuestId: chapter["mainQuestId"]?.GetValue<int>() ?? 0);
                bool verifiedTitle = chapter["titleFromChapter"] is JsonValue provenance
                    && provenance.TryGetValue<bool>(out bool verified) && verified;
                var scenes = new JsonObject();
                foreach (var (scene, titles) in oldScenes)
                {
                    if (!_seenScenes.Add(scene))
                        continue;
                    if (_videoQuests.TryGetValue(scene, out VideoQuestMatch? match)
                        && (key.ChapterId > 0 && match.ConfirmedByResource && match.Quest.ChapterId != key.ChapterId
                            || !isPublished && match.Quest.ChapterId > 0
                                && (key == (0, 0) || HasFallbackSceneTitles(scene, titles))))
                    {
                        JsonNode? curatedTitles = !isPublished && (key == (0, 0) || HasFallbackSceneTitles(scene, titles))
                            ? null : titles?.DeepClone();
                        _scenesToAdd.Add(scene, new PendingScene($"{scene}.usm", match.Quest, curatedTitles,
                            match.TitleHash, match.MainTitleHash));
                    }
                    else
                    {
                        scenes[scene] = titles?.DeepClone();
                    }
                }

                if (key != (0, 0) && _chapterIndex.TryGetValue(key, out JsonObject? existing))
                {
                    JsonObject target = existing["scenes"]!.AsObject();
                    foreach (var (scene, titles) in scenes)
                        target[scene] = titles?.DeepClone();
                }
                else if (scenes.Count > 0 || isPublished || verifiedTitle)
                {
                    var copy = chapter.DeepClone().AsObject();
                    copy["scenes"] = scenes;
                    if (CutsceneChapter.GetCategoryOverride(key.ChapterId) is { } category)
                        copy["category"] = category.ToString();
                    if (copy["titles"] is JsonObject titles)
                    {
                        foreach (string locale in titles.Select(title => title.Key).ToArray())
                        {
                            string[] parts = (titles[locale]?.GetValue<string>() ?? string.Empty).Split(" · ", 3);
                            if (parts.Length == 3 && HasChapterSubject(parts[1], parts[0]))
                                titles[locale] = $"{parts[1]} · {parts[2]}";
                        }
                    }
                    _chapters.Add(copy);
                    if (key != (0, 0))
                        _chapterIndex.Add(key, copy);
                    if (!isPublished && !verifiedTitle && key.ChapterId > 0
                        && _questsByChapter[key.ChapterId].FirstOrDefault() is { } quest)
                        _chaptersToResolve.TryAdd(key, new CutsceneVoiceQuestRef(quest.Id, quest.ChapterId));
                }
            }
        }

        private static bool HasFallbackSceneTitles(string scene, JsonNode? titles)
        {
            string fileSuffix = scene[(scene.LastIndexOf('_') + 1)..];
            return titles is JsonObject localized && localized.All(title =>
                title.Value is JsonValue value && value.TryGetValue<string>(out string? text)
                    && (string.Equals(text, scene, StringComparison.OrdinalIgnoreCase)
                        || text.EndsWith(" · " + fileSuffix, StringComparison.OrdinalIgnoreCase)));
        }

        private void AddQuestChapter(CutsceneVoiceQuestRef quest)
        {
            var key = GetChapterKey(quest);
            if (!_chapterIndex.ContainsKey(key))
                _chaptersToResolve.TryAdd(key, quest);
        }

        public HashSet<ulong> GetNeededTextHashes(IReadOnlyDictionary<ulong, string>? chapterText = null)
        {
            var hashes = new HashSet<ulong>();
            foreach (CutsceneVoiceQuestRef quest in _chaptersToResolve.Values)
            {
                if (_chapterMeta.TryGetValue(quest.ChapterId, out ChapterMeta? chapter))
                {
                    if (GetTitle(chapterText, chapter.TitleHash) is not null)
                        continue;

                    hashes.Add(chapter.TitleHash);
                    hashes.Add(chapter.NumHash);
                    hashes.Add(chapter.ImageTitleHash);
                }

                foreach (QuestInfo info in GetTitleQuests(quest))
                    hashes.Add(info.TitleHash);
            }

            foreach (var scene in _scenesToAdd.Values)
            {
                hashes.Add(scene.TitleHash);
                if (GetTitle(chapterText, scene.TitleHash) is null)
                    hashes.Add(scene.MainTitleHash);
            }

            hashes.Remove(0);
            if (chapterText is not null)
                hashes.RemoveWhere(hash => GetTitle(chapterText, hash) is not null);
            return hashes;
        }

        private IEnumerable<QuestInfo> GetTitleQuests(CutsceneVoiceQuestRef quest) => quest.ChapterId > 0
            ? _questsByChapter[quest.ChapterId].OrderBy(info => info.Id)
            : [_questsById[quest.Id]];

        public (byte[]? Bytes, CutsceneNameCatalog Catalog) Finish(CutsceneNameCatalog? currentCatalog,
            Version? liveVersion, string commit, IReadOnlyList<Dictionary<ulong, string>> maps, CancellationToken token)
        {
            foreach (var (key, quest) in _chaptersToResolve)
            {
                token.ThrowIfCancellationRequested();
                ChapterMeta? meta = _chapterMeta.GetValueOrDefault(quest.ChapterId);
                CutsceneCategory category = InferCategory(_questsById[quest.Id], meta);
                bool hasVerifiedTitle = maps.All(map => GetTitle(map, meta?.TitleHash) is not null);
                bool fromCache = _chapterIndex.TryGetValue(key, out JsonObject? chapter);
                if (fromCache && !hasVerifiedTitle)
                    continue;

                ulong[] questTitleHashes = GetTitleQuests(quest).Select(q => q.TitleHash).ToArray();
                chapter ??= new JsonObject { ["scenes"] = new JsonObject() };
                chapter["category"] = category.ToString();
                chapter["titles"] = GetLocalizedTitles(maps, (map, locale) =>
                {
                    return FormatChapterTitle(map, meta, category)
                        ?? questTitleHashes.Select(hash => GetTitle(map, hash))
                        .FirstOrDefault(value => value is not null)
                        ?? FallbackTitle(new CutsceneChapter(category, $"Cutscene_Category_{category}"), locale);
                });
                if (key.ChapterId > 0)
                {
                    chapter["questChapterId"] = key.ChapterId;
                    if (hasVerifiedTitle)
                        chapter["titleFromChapter"] = true;
                }
                else
                    chapter["mainQuestId"] = key.QuestId;
                if (!fromCache)
                {
                    _chapterIndex.Add(key, chapter);
                    _chapters.Add(chapter);
                }
            }

            var fallbackChapters = new Dictionary<CutsceneChapter, JsonObject>();
            var sameQuestScenes = _scenesToAdd.Where(pair => pair.Value.Quest is not null)
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).ToLookup(pair => pair.Value.Quest!.Id,
                    pair => pair.Key);
            foreach (var (key, scene) in _scenesToAdd)
            {
                token.ThrowIfCancellationRequested();
                JsonObject chapter;
                if (scene.Quest is { } quest)
                {
                    chapter = _chapterIndex[GetChapterKey(quest)];
                }
                else
                {
                    CutsceneChapter fallback = CutsceneChapter.FromFileName(scene.Filename);
                    if (!fallbackChapters.TryGetValue(fallback, out chapter!))
                    {
                        chapter = new JsonObject
                        {
                            ["category"] = fallback.Category.ToString(),
                            ["titles"] = GetLocalizedTitles(maps, (_, locale) => FallbackTitle(fallback, locale)),
                            ["scenes"] = new JsonObject()
                        };
                        fallbackChapters.Add(fallback, chapter);
                        _chapters.Add(chapter);
                    }
                }

                string[] related = scene.Quest is { } linked ? sameQuestScenes[linked.Id].ToArray() : [];
                string suffix = related.Length > 1 ? $" · {Array.IndexOf(related, key) + 1}" : string.Empty;
                chapter["scenes"]![key] = scene.CuratedTitles ?? GetLocalizedTitles(maps, (map, locale) =>
                    GetTitle(map, scene.TitleHash) is { } title ? title :
                        (GetTitle(map, scene.MainTitleHash)
                         ?? (scene.Quest is not null ? chapter["titles"]?[locale]?.GetValue<string>() : null)
                         ?? key) + suffix);
            }

            var publishedVersion = Version.Parse(_published["gameVersion"]!.GetValue<string>());
            Version version = liveVersion is not null && liveVersion > publishedVersion ? liveVersion : publishedVersion;
            if (currentCatalog is not null && currentCatalog.GameVersion > version)
                version = currentCatalog.GameVersion;

            var groupNodes = new JsonObject();
            foreach (CutsceneVoiceGroup group in _groups.Where(group => group.Packages.Count > 0))
            {
                var quests = new JsonArray(group.Quests.Select(quest => (JsonNode)new JsonObject
                {
                    ["id"] = quest.Id,
                    ["chapterId"] = quest.ChapterId
                }).ToArray());
                groupNodes[group.GroupId] = new JsonObject
                {
                    ["packages"] = ToStringArray(group.Packages),
                    ["videos"] = ToStringArray(group.Videos),
                    ["quests"] = quests
                };
            }

            var result = new JsonObject
            {
                ["gameVersion"] = version.ToString(3),
                ["questSource"] = $"https://github.com/DimbreathBot/AnimeGameData/tree/{commit}",
                ["earlyCutsceneSource"] = _published["earlyCutsceneSource"]?.DeepClone(),
                ["releaseSource"] = _published["releaseSource"]?.DeepClone(),
                ["voiceSource"] = $"{AnimeGameDataRawBase}/{commit}/{ResourceTable}",
                ["chapters"] = new JsonArray(_chapters
                    .OrderBy(chapter => chapter["questChapterId"]?.GetValue<int>() ?? 0)
                    .ThenBy(chapter => chapter["mainQuestId"]?.GetValue<int>() ?? 0)
                    .ThenBy(chapter => chapter["scenes"]!.AsObject().FirstOrDefault().Key, StringComparer.OrdinalIgnoreCase)
                    .ToArray<JsonNode?>()),
                ["voiceGroups"] = groupNodes
            };

            int revision = _published["revision"]?.GetValue<int>() ?? 0;
            if (currentCatalog is not null && version == currentCatalog.GameVersion)
            {
                result["revision"] = currentCatalog.Revision;
                if (JsonNode.DeepEquals(result, _cached))
                    return (null, currentCatalog);
                revision = Math.Max(revision, checked(currentCatalog.Revision + 1));
            }

            result["revision"] = revision;
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(result, WriteJsonOptions);
            using var stream = new MemoryStream(bytes, writable: false);
            return (bytes, CutsceneNames.Read(stream));
        }

        private static (int ChapterId, int QuestId) GetChapterKey(CutsceneVoiceQuestRef quest) =>
            quest.ChapterId > 0 ? (quest.ChapterId, 0) : (0, quest.Id);
    }

    private static Dictionary<string, VideoQuestMatch> BuildVideoQuestIndex(
        IEnumerable<string> files, IReadOnlyList<CutsceneVoiceGroup> groups,
        IReadOnlyDictionary<int, QuestInfo> quests, IReadOnlyDictionary<int, SubquestInfo> subquests)
    {
        var resourceQuests = groups.SelectMany(group =>
                group.Videos.SelectMany(video => group.Quests.Select(quest => (Video: video, Quest: quest))))
            .ToLookup(link => CutsceneNames.CanonicalSceneKey(link.Video), link => link.Quest,
                StringComparer.OrdinalIgnoreCase);
        var listedVideos = groups.SelectMany(group => group.Videos).Select(CutsceneNames.CanonicalSceneKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = new Dictionary<string, VideoQuestMatch>(StringComparer.OrdinalIgnoreCase);
        foreach (string scene in files.Select(CutsceneNames.CanonicalSceneKey).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            CutsceneVoiceQuestRef[] references = resourceQuests[scene].Distinct().ToArray();
            if (FindQuestInSceneName(scene, references, listedVideos.Contains(scene), quests, subquests)
                is { } resolved)
            {
                QuestInfo quest = resolved.Main;
                matches.Add(scene, new VideoQuestMatch(new CutsceneVoiceQuestRef(quest.Id, quest.ChapterId),
                    resolved.TitleHash, quest.TitleHash, references.Any(reference => reference.Id == quest.Id)));
                continue;
            }

            // Shared groups can mention several chapters. Only a single resolved chapter can place a video.
            var chapters = references.Where(quest => quest.ChapterId > 0).DistinctBy(quest => quest.ChapterId).ToArray();
            CutsceneVoiceQuestRef? fallback = chapters.Length == 1 ? chapters[0]
                : chapters.Length == 0 && references.Length == 1 ? references[0] : null;
            if (fallback is not null)
                matches.Add(scene, new VideoQuestMatch(fallback, 0, quests.GetValueOrDefault(fallback.Id)?.TitleHash ?? 0,
                    ConfirmedByResource: false));
        }
        return matches;
    }

    private static (QuestInfo Main, ulong TitleHash)? FindQuestInSceneName(
        string scene, IReadOnlyList<CutsceneVoiceQuestRef> references,
        bool listedVideo,
        IReadOnlyDictionary<int, QuestInfo> quests, IReadOnlyDictionary<int, SubquestInfo> subquests)
    {
        Match match = SceneQuestCode().Match(scene);
        if (!match.Success)
            return null;

        string kind = match.Groups["type"].Value.ToUpperInvariant();
        if (scene.StartsWith("CS_ACTIVITY_", StringComparison.OrdinalIgnoreCase))
            kind = "EQ";
        ReadOnlySpan<char> code = match.Groups["id"].ValueSpan;
        var exact = GetQuest(code);
        if (exact is { } named && (kind.Length > 0 || references.Any(reference => reference.Id == named.Main.Id)))
            return exact;

        // An archived resource can lack a quest in its resource group. Its typed clip name must then
        // resolve to an exact subquest recorded in the game before accepting its trailing take number.
        if (kind.Length > 0 && code.Length >= 7 && GetQuest(code[..^2]) is { } withTakeNumber
            && (references.Any(reference => reference.Id == withTakeNumber.Main.Id)
                || references.Count == 0 && listedVideo && int.TryParse(code[..^2], out int subquestId)
                    && subquests.ContainsKey(subquestId)))
            return withTakeNumber;
        return null;

        (QuestInfo Main, ulong TitleHash)? GetQuest(ReadOnlySpan<char> value)
        {
            if (!int.TryParse(value, out int id))
                return null;
            SubquestInfo? subquest = subquests.GetValueOrDefault(id);
            QuestInfo? quest = quests.GetValueOrDefault(subquest?.MainId ?? id);
            return quest is { ChapterId: > 0 }
                && (kind.Length == 0 || string.Equals(quest.Type ?? "AQ", kind, StringComparison.OrdinalIgnoreCase))
                ? (quest, subquest?.TitleHash ?? 0) : null;
        }
    }

    [GeneratedRegex(@"(?:^|_)(?<type>AQ|EQ|LQ|WQ)[A-Z_]{0,6}(?<id>[0-9]{5,12})(?=$|[_-])|^CS_(?:ACTIVITY_)?(?<id>[0-9]{6,8})(?=$|[_-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SceneQuestCode();

    internal static IReadOnlyList<CutsceneVoiceGroup> BuildVoiceGroups(
        IReadOnlyDictionary<string, ResourceCollectionEntry> resources, IEnumerable<QuestInfo> quests)
    {
        var questsByResource = quests.Where(quest => quest.ResId > 0)
            .ToLookup(quest => quest.ResId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var groups = new List<CutsceneVoiceGroup>();
        foreach (string groupId in resources.Keys.Concat(questsByResource.Select(group => group.Key))
                     .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            ResourceCollectionEntry? source = resources.GetValueOrDefault(groupId);
            var linked = questsByResource[groupId];
            var chapterIds = linked.Where(quest => quest.ChapterId > 0).Select(quest => quest.ChapterId).ToHashSet();
            CutsceneVoiceQuestRef[] refs = linked.Select(quest => new CutsceneVoiceQuestRef(quest.Id,
                quest.ChapterId > 0 ? quest.ChapterId : chapterIds.Contains(quest.Series) ? quest.Series : 0)).ToArray();

            IReadOnlyList<string> packages = source?.Packages ?? [];
            if (packages.Count == 0 && refs.Length > 0)
            {
                // Newer quests and hangouts may omit the paths in ResourceCollection. Only
                // matching packages from the official installed-version manifest are used.
                packages = GameUpdateService.VoiceLanguages.Select(language => $"{language.Directory}/{groupId}.pck").ToArray();
            }

            if (packages.Count > 0 || source?.Videos.Count > 0)
                groups.Add(new CutsceneVoiceGroup(groupId, packages, source?.Videos ?? [], refs));
        }
        return groups;
    }

    private static JsonArray ToStringArray(IEnumerable<string> values) =>
        new(values.Distinct(StringComparer.OrdinalIgnoreCase).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static string? GetTitle(IReadOnlyDictionary<ulong, string>? map, ulong? hash)
    {
        if (map is null || hash is not > 0 || !map.TryGetValue(hash.Value, out string? text))
            return null;

        text = text.Trim();
        return text.Length > 0 && !text.Contains("$HIDDEN", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("$UNRELEASED", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("(test)", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("（test）", StringComparison.OrdinalIgnoreCase) ? text : null;
    }

    private static string? FormatChapterTitle(
        IReadOnlyDictionary<ulong, string>? map, ChapterMeta? chapter, CutsceneCategory category)
    {
        string? title = GetTitle(map, chapter?.TitleHash);
        string? number = GetTitle(map, chapter?.NumHash);
        if (title is null && number is null)
            return null;

        // The hangout act string already contains the character's name.
        string? subject = category is CutsceneCategory.Archon or CutsceneCategory.Story or CutsceneCategory.Tribal
            ? GetTitle(map, chapter?.ImageTitleHash) : null;
        if (number is not null)
            number = string.Join(' ', number.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (subject is not null && number is not null && HasChapterSubject(number, subject))
            subject = null;
        return string.Join(" · ", new[] { subject, number, title }.OfType<string>().Distinct());
    }

    private static bool HasChapterSubject(string number, string subject) =>
        subject.Length > 0 && (number.StartsWith(subject + " ", StringComparison.Ordinal)
                              || number.StartsWith(subject + ":", StringComparison.Ordinal));

    private static JsonObject GetLocalizedTitles(IReadOnlyList<Dictionary<ulong, string>> maps,
        Func<IReadOnlyDictionary<ulong, string>?, string, string> getTitle) => new()
    {
        ["zh-CN"] = getTitle(maps[0], "zh-CN"),
        ["zh-TW"] = getTitle(maps[1], "zh-TW"),
        ["en-US"] = getTitle(maps[2], "en-US")
    };

    private static CutsceneCategory InferCategory(QuestInfo quest, ChapterMeta? chapter)
    {
        if (CutsceneChapter.GetCategoryOverride(quest.ChapterId) is { } category)
            return category;

        // Sunspray Summer Resort uses LQ for all four parts in the game tables.
        if (chapter is { Id: >= 10245 and <= 10248 })
            return CutsceneCategory.Event;

        return (chapter?.QuestType ?? quest.Type) switch
        {
            "AQ" => CutsceneCategory.Archon,
            "EQ" => CutsceneCategory.Event,
            "WQ" => CutsceneCategory.World,
            "LQ" when quest.ResId is >= 3000 and < 4000 => CutsceneCategory.Hangout,
            "LQ" when chapter is { CityId: 6, Id: >= 2052 and <= 2073 } => CutsceneCategory.Tribal,
            "LQ" => CutsceneCategory.Story,
            _ when quest.ChapterId is >= 1000 and < 2000 => CutsceneCategory.Archon,
            _ => CutsceneCategory.Other
        };
    }

    private static async Task<(Version? LiveVersion, HashSet<string> VideoFiles)> TryFetchLiveSophonAsync(
        SophonBuildClient sophonClient,
        GameServerScheme? preferredScheme,
        CancellationToken token)
    {
        var videos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Version? highestVersion = null;

        var schemes = new List<GameServerScheme>();
        if (preferredScheme is not null)
            schemes.Add(preferredScheme);
        if (!schemes.Any(s => !s.IsOversea))
            schemes.Add(GameServerScheme.ChineseOfficialOfficial);
        if (!schemes.Any(s => s.IsOversea))
            schemes.Add(GameServerScheme.OverseaOfficialDefault);

        using var lookup = CancellationTokenSource.CreateLinkedTokenSource(token);
        lookup.CancelAfter(EndpointTimeout);
        foreach (GameServerScheme scheme in schemes)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                SophonBranchPayload payload = await sophonClient
                    .GetBranchPayloadAsync(scheme, isPreDownload: false, lookup.Token).ConfigureAwait(false);
                if (Version.TryParse(payload.Tag, out Version? tagVer)
                    && tagVer.Build >= 0 && tagVer.Revision < 0 && (highestVersion is null || tagVer > highestVersion))
                    highestVersion = tagVer;

                SophonBranchInfo branchInfo = await sophonClient
                    .GetBranchInfoAsync(scheme, isPreDownload: false, lookup.Token).ConfigureAwait(false);
                SophonManifestProto manifest = await sophonClient
                    .DownloadManifestAsync(branchInfo, lookup.Token).ConfigureAwait(false);

                foreach (AssetProperty asset in manifest.Assets)
                {
                    string normalized = asset.AssetName.Replace('\\', '/');
                    if (normalized.Contains("/VideoAssets/", StringComparison.OrdinalIgnoreCase)
                        && normalized.EndsWith(".usm", StringComparison.OrdinalIgnoreCase))
                    {
                        videos.Add(Path.GetFileName(normalized));
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugLog.Info(nameof(CutsceneCatalogUpdater),
                    $"Sophon live query failed for {scheme.DisplayName} ({ex.GetType().Name}).");
            }
        }

        return (highestVersion, videos);
    }

    private static async Task<string?> TryResolveUpstreamCommitAsync(HttpClient client, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, AnimeGameDataRepoApiCommitUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using HttpResponseMessage resp = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            using Stream stream = await resp.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("sha", out JsonElement shaProp) && shaProp.ValueKind == JsonValueKind.String)
            {
                string? sha = shaProp.GetString();
                if (sha is { Length: 40 } && sha.All(char.IsAsciiHexDigit))
                    return sha;
            }
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
        }

        return null;
    }

    private static async Task<T> DownloadWithMirrorsAsync<T>(
        HttpClient client, string rawUrl, int maxBytes, Func<byte[], T> parse, CancellationToken token)
    {
        Exception? lastError = null;
        foreach (var (sourceName, url) in BuildMirrorCandidates(rawUrl))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(EndpointTimeout);
                using HttpResponseMessage response = await client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long len && len > maxBytes)
                    throw new InvalidDataException($"Upstream asset from {sourceName} exceeds size limit.");

                await using Stream content = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                using var ms = new MemoryStream();
                byte[] buffer = ArrayPool<byte>.Shared.Rent(32 * 1024);
                try
                {
                    int read;
                    while ((read = await content.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        if (ms.Length + read > maxBytes)
                            throw new InvalidDataException($"Upstream asset from {sourceName} exceeds size limit.");
                        ms.Write(buffer, 0, read);
                    }
                    return parse(ms.ToArray());
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                DebugLog.Info(nameof(CutsceneCatalogUpdater), $"Fetch via {sourceName} failed ({ex.GetType().Name}).");
            }
        }

        throw lastError ?? new InvalidOperationException($"Failed to fetch upstream resource: {rawUrl}");
    }

    private static async Task<Dictionary<ulong, string>> FetchCatalogTextMapAsync(
        HttpClient client, string rawBase, string language, CatalogMerge merge, IReadOnlySet<ulong> neededHashes,
        CancellationToken token)
    {
        // Chapter and act names live in the Medium archive. The regular archive provides task text.
        var texts = await TryFetchFilteredTextMapAsync(
            client, $"{rawBase}/TextMap/TextMap_Medium{language}.json", neededHashes, token).ConfigureAwait(false);
        var missingHashes = merge.GetNeededTextHashes(texts);
        var fallback = await TryFetchFilteredTextMapAsync(
            client, $"{rawBase}/TextMap/TextMap{language}.json", missingHashes, token).ConfigureAwait(false);
        foreach (var (hash, title) in fallback)
            texts[hash] = title;
        return texts;
    }

    private static async Task<Dictionary<ulong, string>> TryFetchFilteredTextMapAsync(
        HttpClient client, string rawUrl, IReadOnlySet<ulong> neededHashes, CancellationToken token)
    {
        if (neededHashes.Count == 0)
            return [];

        foreach (var (sourceName, url) in BuildMirrorCandidates(rawUrl))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TextMapTimeout);
                using HttpResponseMessage response = await client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is > MaxTextMapBytes)
                    continue;

                await using Stream stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                return await StreamFilteredTextMapAsync(stream, neededHashes, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DebugLog.Info(nameof(CutsceneCatalogUpdater), $"TextMap via {sourceName} failed ({ex.GetType().Name}).");
            }
        }

        return [];
    }

    internal static async Task<Dictionary<ulong, string>> StreamFilteredTextMapAsync(
        Stream stream, IReadOnlySet<ulong> neededHashes, CancellationToken token)
    {
        var result = new Dictionary<ulong, string>(Math.Min(neededHashes.Count, 256));
        if (neededHashes.Count == 0)
            return result;

        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        int validBytes = 0;
        long totalRead = 0;
        var state = new JsonReaderState();
        ulong? matchedKey = null;

        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (validBytes == buffer.Length)
                {
                    if (buffer.Length >= 4 * 1024 * 1024)
                        throw new InvalidDataException("TextMap JSON entry exceeded streaming buffer limit.");

                    byte[] grown = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                    buffer.AsSpan(0, validBytes).CopyTo(grown);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = grown;
                }

                int read = await stream.ReadAsync(buffer.AsMemory(validBytes), token).ConfigureAwait(false);
                totalRead += read;
                if (totalRead > MaxTextMapBytes)
                    throw new InvalidDataException("TextMap asset exceeds size limit.");

                validBytes += read;
                bool isFinalBlock = read == 0;

                if (ProcessTextMapChunk(buffer.AsSpan(0, validBytes), isFinalBlock, ref state,
                        ref matchedKey, neededHashes, result, out int consumed))
                {
                    return result;
                }

                int remaining = validBytes - consumed;
                if (remaining > 0 && consumed > 0)
                    buffer.AsSpan(consumed, remaining).CopyTo(buffer);
                validBytes = remaining;

                if (isFinalBlock)
                    break;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        return result;
    }

    private static bool ProcessTextMapChunk(
        ReadOnlySpan<byte> span,
        bool isFinalBlock,
        ref JsonReaderState state,
        ref ulong? matchedKey,
        IReadOnlySet<ulong> neededHashes,
        Dictionary<ulong, string> result,
        out int consumed)
    {
        var reader = new Utf8JsonReader(span, isFinalBlock, state);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.CurrentDepth == 1)
            {
                ReadOnlySpan<byte> rawKey = reader.HasValueSequence
                    ? reader.ValueSequence.ToArray()
                    : reader.ValueSpan;
                if (Utf8Parser.TryParse(rawKey, out ulong parsedHash, out int parsedBytes)
                    && parsedBytes == rawKey.Length
                    && neededHashes.Contains(parsedHash))
                {
                    matchedKey = parsedHash;
                }
                else
                {
                    matchedKey = null;
                }
            }
            else if (matchedKey is { } key)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    string? text = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        result[key] = text;
                }
                matchedKey = null;

                if (result.Count >= neededHashes.Count)
                {
                    state = reader.CurrentState;
                    consumed = (int)reader.BytesConsumed;
                    return true;
                }
            }
        }

        state = reader.CurrentState;
        consumed = (int)reader.BytesConsumed;
        return false;
    }

    private static IReadOnlyList<(string Name, string Url)> BuildMirrorCandidates(string rawUrl)
    {
        var candidates = new List<(string Name, string Url)>
        {
            ("GitHub", rawUrl),
            (new Uri(ApiEndpoints.GhProxyBase).Host,
                ApiEndpoints.BuildMirrorUrl(new Uri(ApiEndpoints.GhProxyBase).Host, rawUrl))
        };

        var mirrorConfig = new MirrorSiteProvider().LoadConfig();
        foreach (string domain in mirrorConfig.Mirrors.Take(10))
        {
            if (string.IsNullOrWhiteSpace(domain))
                continue;
            string trimmed = domain.Trim();
            if (!candidates.Any(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                candidates.Add((trimmed, ApiEndpoints.BuildMirrorUrl(trimmed, rawUrl)));
        }

        return candidates;
    }

    internal static Dictionary<string, ResourceCollectionEntry> ParseResourceCollections(byte[] jsonBytes)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonBytes);
        if (!doc.RootElement.TryGetProperty("resourceCollectionMap", out JsonElement map)
            || map.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing resource collection map.");

        return map.EnumerateObject().ToDictionary(group => group.Name, group => new ResourceCollectionEntry(
            ReadPaths(group.Value, "audioResourceList", ".pck").ToArray(),
            ReadPaths(group.Value, "videoResourceList", ".usm").Select(path => Path.GetFileName(path)).ToArray()));
    }

    private static IEnumerable<string> ReadPaths(JsonElement group, string propertyName, string extension)
    {
        if (!group.TryGetProperty(propertyName, out JsonElement list) || list.ValueKind != JsonValueKind.Array)
            return [];

        return list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!.Trim().Replace('\\', '/').TrimStart('/'))
            .Where(path => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    internal static Dictionary<int, ChapterMeta> ParseChapterExcel(byte[] jsonBytes)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonBytes);
        var result = new Dictionary<int, ChapterMeta>();
        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            int id = ReadInt(item, "id");
            if (id <= 0)
                continue;
            result[id] = new ChapterMeta(id, ReadString(item, "questType"), ReadInt(item, "cityId"),
                ReadHash(item, "chapterTitleTextMapHash"), ReadHash(item, "chapterNumTextMapHash"),
                ReadHash(item, "chapterImageTitleTextMapHash"));
        }

        return result;
    }

    internal static List<QuestInfo> ParseMainQuests(byte[] jsonBytes)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonBytes);
        var result = new List<QuestInfo>();
        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            int id = ReadInt(item, "id");
            if (id > 0)
                result.Add(new QuestInfo(id, ReadInt(item, "resId"), Math.Max(0, ReadInt(item, "chapterId")),
                    ReadInt(item, "series"), ReadString(item, "type"), ReadHash(item, "titleTextMapHash")));
        }
        return result;
    }

    private static Dictionary<int, SubquestInfo> ParseSubquests(byte[] jsonBytes)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonBytes);
        var parents = new Dictionary<int, SubquestInfo>();
        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            int subId = ReadInt(item, "subId");
            int mainId = ReadInt(item, "mainId");
            if (subId > 0 && mainId > 0)
                parents.TryAdd(subId, new SubquestInfo(mainId, ReadHash(item, "titleTextMapHash")));
        }
        return parents;
    }

    private static int ReadInt(JsonElement item, string key) =>
        item.TryGetProperty(key, out JsonElement property) && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out int value) ? value : 0;

    private static ulong ReadHash(JsonElement item, string key) =>
        item.TryGetProperty(key, out JsonElement property) && property.ValueKind == JsonValueKind.Number
        && property.TryGetUInt64(out ulong value) ? value : 0;

    private static string? ReadString(JsonElement item, string key) =>
        item.TryGetProperty(key, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool IsValidSceneKey(string key) =>
        key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-');

    private static string FallbackTitle(CutsceneChapter chapter, string locale)
    {
        string baseTitle = (chapter.TitleKey, locale) switch
        {
            ("Cutscene_Reunion", "zh-TW") => "星之歸還",
            ("Cutscene_Reunion", "en-US") => "Stellar Reunion",
            ("Cutscene_Reunion", _) => "星之归还",
            ("Cutscene_Region_Mondstadt", "zh-TW") => "序章 · 蒙德",
            ("Cutscene_Region_Mondstadt", "en-US") => "Mondstadt",
            ("Cutscene_Region_Mondstadt", _) => "序章 · 蒙德",
            ("Cutscene_Region_Liyue", "en-US") => "Liyue",
            ("Cutscene_Region_Liyue", _) => "璃月",
            ("Cutscene_Region_Inazuma", "zh-TW") => "稻妻",
            ("Cutscene_Region_Inazuma", "en-US") => "Inazuma",
            ("Cutscene_Region_Inazuma", _) => "稻妻",
            ("Cutscene_Region_Sumeru", "zh-TW") => "須彌",
            ("Cutscene_Region_Sumeru", "en-US") => "Sumeru",
            ("Cutscene_Region_Sumeru", _) => "须弥",
            ("Cutscene_Region_Fontaine", "zh-TW") => "楓丹",
            ("Cutscene_Region_Fontaine", "en-US") => "Fontaine",
            ("Cutscene_Region_Fontaine", _) => "枫丹",
            ("Cutscene_Region_Natlan", "zh-TW") => "納塔",
            ("Cutscene_Region_Natlan", "en-US") => "Natlan",
            ("Cutscene_Region_Natlan", _) => "纳塔",
            ("Cutscene_Region_NodKrai", "zh-TW") => "挪德卡萊",
            ("Cutscene_Region_NodKrai", "en-US") => "Nod-Krai",
            ("Cutscene_Region_NodKrai", _) => "挪德卡莱",
            ("Cutscene_Region_Snezhnaya", "en-US") => "Snezhnaya",
            ("Cutscene_Region_Snezhnaya", _) => "至冬",
            ("Cutscene_Category_Archon", "zh-TW") => "魔神任務",
            ("Cutscene_Category_Archon", "en-US") => "Archon Quests",
            ("Cutscene_Category_Archon", _) => "魔神任务",
            ("Cutscene_Category_Hangout", "zh-TW") => "邀約事件",
            ("Cutscene_Category_Hangout", "en-US") => "Hangout Events",
            ("Cutscene_Category_Hangout", _) => "邀约事件",
            ("Cutscene_Category_Story", "zh-TW") => "傳說任務",
            ("Cutscene_Category_Story", "en-US") => "Story Quests",
            ("Cutscene_Category_Story", _) => "传说任务",
            ("Cutscene_Category_Event", "zh-TW") => "活動任務",
            ("Cutscene_Category_Event", "en-US") => "Event Quests",
            ("Cutscene_Category_Event", _) => "活动任务",
            ("Cutscene_Category_Tribal", "zh-TW") => "部族紀聞",
            ("Cutscene_Category_Tribal", "en-US") => "Tribal Chronicles",
            ("Cutscene_Category_Tribal", _) => "部族纪闻",
            ("Cutscene_Category_World", "zh-TW") => "世界任務",
            ("Cutscene_Category_World", "en-US") => "World Quests",
            ("Cutscene_Category_World", _) => "世界任务",
            (_, "en-US") => "Other",
            _ => "其他"
        };

        return baseTitle;
    }

}
