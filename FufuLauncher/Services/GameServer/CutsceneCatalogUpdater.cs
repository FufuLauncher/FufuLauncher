/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Buffers;
using System.Buffers.Text;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;
using FufuLauncher.Services.PluginMirror;

namespace FufuLauncher.Services.GameServer;

/// <summary>
/// Pulls upstream open game data (AnimeGameData) and live official HYP/Sophon manifests
/// directly on the client, then autonomously merges them with the built-in curated catalog.
/// Never contacts any developer-hosted backend server or FufuLauncher GitHub endpoints.
/// </summary>
internal static class CutsceneCatalogUpdater
{
    private const string AnimeGameDataRepoApiCommitUrl =
        "https://api.github.com/repos/DimbreathBot/AnimeGameData/commits/master";

    private const string AnimeGameDataRawBase =
        "https://raw.githubusercontent.com/DimbreathBot/AnimeGameData";

    private const int MaxTableBytes = 24 * 1024 * 1024;
    private const int MaxTextMapBytes = 64 * 1024 * 1024;
    private static readonly TimeSpan EndpointTimeout = TimeSpan.FromSeconds(18);
    private static readonly TimeSpan TextMapTimeout = TimeSpan.FromSeconds(40);
    private static readonly JsonSerializerOptions ReadJsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonWriterOptions WriteJsonOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] TravelerSuffixes = ["_PlayerBoy", "_PlayerGirl", "_Boy", "_Girl"];

    internal sealed record QuestInfo(int Id, int ResId, int ChapterId, string? Type, ulong TitleTextMapHash);
    internal sealed record ChapterMeta(int Id, ulong TitleTextMapHash, ulong NumTextMapHash, string? QuestType);
    internal sealed record ResourceCollectionEntry(
        string GroupId,
        IReadOnlyList<string> Packages,
        IReadOnlyList<string> Videos);

    internal sealed record MergeOutcome(
        byte[] CatalogJsonBytes,
        CutsceneNameCatalog Catalog,
        bool Changed,
        string SourceSummary);

    public static async Task<MergeOutcome> PullAndMergeAsync(
        HttpClient httpClient,
        SophonBuildClient? sophonClient,
        GameServerScheme? preferredScheme,
        CutsceneNameCatalog currentCatalog,
        byte[]? existingCachedBytes,
        IProgress<string>? status,
        CancellationToken token)
    {
        // Step 1: Query official HYP/Sophon endpoints directly for live version & shipped .usm list.
        status?.Report("Cutscene_CatalogPullSophon".GetLocalized());
        var (liveVersion, liveUsmFiles) = await TryFetchLiveSophonAsync(sophonClient, preferredScheme, token)
            .ConfigureAwait(false);

        // Step 2: Resolve upstream AnimeGameData revision (best-effort commit SHA -> fallback 'master').
        status?.Report("Cutscene_CatalogPullUpstream".GetLocalized());
        string commitRef = await TryResolveUpstreamCommitAsync(httpClient, token).ConfigureAwait(false);
        string rawBase = $"{AnimeGameDataRawBase}/{commitRef}";
        string shortRef = commitRef.Length > 10 ? commitRef[..10] : commitRef;

        byte[] resCollectionBytes = await DownloadBytesWithMirrorsAsync(
            httpClient, $"{rawBase}/BinOutput/ResourceCollection/ConfigResourceCollection.json",
            MaxTableBytes, EndpointTimeout, token).ConfigureAwait(false);
        byte[] chapterExcelBytes = await DownloadBytesWithMirrorsAsync(
            httpClient, $"{rawBase}/ExcelBinOutput/ChapterExcelConfigData.json",
            MaxTableBytes, EndpointTimeout, token).ConfigureAwait(false);
        byte[] mainQuestBytes = await DownloadBytesWithMirrorsAsync(
            httpClient, $"{rawBase}/ExcelBinOutput/MainQuestExcelConfigData.json",
            MaxTableBytes, EndpointTimeout, token).ConfigureAwait(false);

        var resourceCollections = ParseResourceCollections(resCollectionBytes);
        var chapterMeta = ParseChapterExcel(chapterExcelBytes);
        var mainQuests = ParseMainQuests(mainQuestBytes);

        // Step 3: Load embedded curated baseline and check which new chapters/scenes need TextMap lookup.
        byte[] builtInBytes;
        using (Stream builtInStream = CutsceneNames.OpenEmbeddedStream())
        using (var ms = new MemoryStream())
        {
            await builtInStream.CopyToAsync(ms, token).ConfigureAwait(false);
            builtInBytes = ms.ToArray();
        }

        IReadOnlySet<ulong> neededHashes = CollectNeededTextMapHashes(
            builtInBytes, existingCachedBytes, liveUsmFiles, resourceCollections, mainQuests, chapterMeta);

        Dictionary<ulong, string> textChs = [];
        Dictionary<ulong, string> textCht = [];
        Dictionary<ulong, string> textEn = [];
        if (neededHashes.Count > 0)
        {
            status?.Report("Cutscene_CatalogPullTextMap".GetLocalized());
            textChs = await TryFetchFilteredTextMapAsync(
                httpClient, $"{rawBase}/TextMap/TextMapCHS.json", neededHashes, token).ConfigureAwait(false);
            textCht = await TryFetchFilteredTextMapAsync(
                httpClient, $"{rawBase}/TextMap/TextMapCHT.json", neededHashes, token).ConfigureAwait(false);
            textEn = await TryFetchFilteredTextMapAsync(
                httpClient, $"{rawBase}/TextMap/TextMapEN.json", neededHashes, token).ConfigureAwait(false);
        }

        // Step 4: Autonomously merge BuiltIn + Existing Cache + Live Sophon + AnimeGameData locally.
        status?.Report("Cutscene_CatalogMerging".GetLocalized());
        string voiceSourceUrl = $"{rawBase}/BinOutput/ResourceCollection/ConfigResourceCollection.json";
        string questSourceUrl = $"https://github.com/DimbreathBot/AnimeGameData/tree/{shortRef}";
        string sourceSummary = liveUsmFiles.Count > 0
            ? $"Sophon ({liveVersion}) + AnimeGameData ({shortRef})"
            : $"AnimeGameData ({shortRef})";

        return MergeCatalogs(
            builtInBytes,
            existingCachedBytes,
            currentCatalog,
            liveVersion,
            liveUsmFiles,
            resourceCollections,
            mainQuests,
            chapterMeta,
            textChs,
            textCht,
            textEn,
            questSourceUrl,
            voiceSourceUrl,
            sourceSummary);
    }

    internal static MergeOutcome MergeCatalogs(
        byte[] builtInBytes,
        byte[]? existingCachedBytes,
        CutsceneNameCatalog currentCatalog,
        Version? liveSophonVersion,
        IReadOnlySet<string> liveSophonVideos,
        IReadOnlyDictionary<string, ResourceCollectionEntry> resourceCollections,
        IReadOnlyList<QuestInfo> mainQuests,
        IReadOnlyDictionary<int, ChapterMeta> chapterMeta,
        IReadOnlyDictionary<ulong, string> textChs,
        IReadOnlyDictionary<ulong, string> textCht,
        IReadOnlyDictionary<ulong, string> textEn,
        string questSourceUrl,
        string voiceSourceUrl,
        string sourceSummary)
    {
        JsonNode? builtInNode = JsonNode.Parse(builtInBytes)
            ?? throw new InvalidDataException("Built-in cutscene catalog is invalid.");
        JsonObject builtInRoot = builtInNode.AsObject();
        CutsceneNameCatalog builtInCatalog = CutsceneNames.BuiltIn;

        var chaptersList = new List<JsonObject>();
        var seenScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chapterByQuestId = new Dictionary<int, JsonObject>();

        // 1. Copy curated chapters & scenes from BuiltIn (always authoritative).
        if (builtInRoot["chapters"] is JsonArray builtInChapters)
        {
            foreach (JsonNode? chNode in builtInChapters)
            {
                if (chNode is not JsonObject chObj)
                    continue;
                var clone = chObj.DeepClone().AsObject();
                NormalizeAndRegisterChapter(clone, chaptersList, seenScenes, chapterByQuestId);
            }
        }

        // 2. Carry forward non-builtIn chapters/scenes already persisted in local cache (if any).
        if (existingCachedBytes is { Length: > 0 })
        {
            TryOverlayCachedExtras(existingCachedBytes, chaptersList, seenScenes, chapterByQuestId);
        }

        // 3. Index quests by resId and build refreshed voiceGroups + video->group map.
        var questsByResId = new Dictionary<int, List<QuestInfo>>();
        foreach (QuestInfo q in mainQuests)
        {
            if (q.ResId <= 0)
                continue;
            if (!questsByResId.TryGetValue(q.ResId, out List<QuestInfo>? list))
                questsByResId[q.ResId] = list = [];
            list.Add(q);
        }

        var videoToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var voiceGroupsObj = new JsonObject();
        var mergedVoiceGroups = new Dictionary<string, CutsceneVoiceGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var (groupId, entry) in resourceCollections.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            foreach (string rawVideo in entry.Videos)
            {
                string fileName = Path.GetFileName(rawVideo.Replace('\\', '/'));
                if (fileName.Length > 0)
                    videoToGroup.TryAdd(CanonicalSceneKey(fileName), groupId);
            }

            if (entry.Packages.Count == 0)
                continue;

            int gidInt = int.TryParse(groupId, out int parsedGid) ? parsedGid : 0;
            List<QuestInfo> linkedQuests = gidInt > 0 && questsByResId.TryGetValue(gidInt, out List<QuestInfo>? qList)
                ? qList
                : [];

            var pkgArray = new JsonArray(entry.Packages.Select(p => JsonValue.Create(p)).ToArray<JsonNode?>());
            var vidArray = new JsonArray(entry.Videos.Select(v => JsonValue.Create(v)).ToArray<JsonNode?>());
            var questArray = new JsonArray(linkedQuests
                .Select(q => (JsonNode)new JsonObject
                {
                    ["id"] = q.Id,
                    ["chapterId"] = q.ChapterId
                })
                .ToArray());

            voiceGroupsObj[groupId] = new JsonObject
            {
                ["packages"] = pkgArray,
                ["videos"] = vidArray,
                ["quests"] = questArray
            };

            mergedVoiceGroups[groupId] = new CutsceneVoiceGroup(
                groupId,
                entry.Packages.ToArray(),
                entry.Videos.Select(v => Path.GetFileName(v.Trim())).ToArray(),
                linkedQuests.Select(q => new CutsceneVoiceQuestRef(q.Id, Math.Max(0, q.ChapterId))).ToArray());
        }

        // 4. Identify candidate .usm files and scaffold new chapters/scenes not already curated.
        var candidateFiles = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> rawVideoNames = liveSophonVideos.Count > 0
            ? liveSophonVideos
            : resourceCollections.Values.SelectMany(v => v.Videos);
        foreach (string rawName in rawVideoNames)
        {
            string fileName = Path.GetFileName(rawName.Replace('\\', '/').Trim());
            if (!fileName.EndsWith(".usm", StringComparison.OrdinalIgnoreCase))
                continue;
            string canonical = CanonicalSceneKey(fileName);
            if (!IsValidSceneKey(canonical))
                continue;
            candidateFiles.TryAdd(canonical, fileName);
        }

        Version baseVersion = builtInCatalog.GameVersion;
        Version targetVersion = MaxVersion(liveSophonVersion, MaxVersion(currentCatalog.GameVersion, baseVersion));
        string releaseVer = $"{targetVersion.Major}.{targetVersion.Minor}";

        // Pre-count how many new scenes share each MainQuest title so we disambiguate duplicates cleanly.
        var questSceneCounts = new Dictionary<int, int>();
        foreach (string canonical in candidateFiles.Keys)
        {
            if (seenScenes.Contains(canonical))
                continue;
            if (TryResolvePrimaryQuest(canonical, videoToGroup, questsByResId, out QuestInfo? q) && q is not null)
                questSceneCounts[q.Id] = questSceneCounts.GetValueOrDefault(q.Id) + 1;
        }

        var adhocChapterByTitleKey = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        int addedScenesCount = 0;

        foreach (var (canonical, fileName) in candidateFiles)
        {
            if (seenScenes.Contains(canonical))
                continue;

            TryResolvePrimaryQuest(canonical, videoToGroup, questsByResId, out QuestInfo? linkedQuest);
            int questChapterId = linkedQuest is { ChapterId: > 0 } ? linkedQuest.ChapterId : 0;

            JsonObject targetChapter;
            if (questChapterId > 0 && chapterByQuestId.TryGetValue(questChapterId, out JsonObject? existingChapter))
            {
                targetChapter = existingChapter;
            }
            else
            {
                targetChapter = CreateChapterObject(
                    questChapterId,
                    linkedQuest?.Type,
                    fileName,
                    releaseVer,
                    chapterMeta,
                    textChs,
                    textCht,
                    textEn,
                    adhocChapterByTitleKey,
                    chaptersList,
                    chapterByQuestId);
            }

            JsonObject sceneTitles = BuildSceneTitles(
                canonical, linkedQuest, questSceneCounts, textChs, textCht, textEn);
            var scenesObj = targetChapter["scenes"] as JsonObject ?? new JsonObject();
            targetChapter["scenes"] = scenesObj;
            scenesObj[canonical] = sceneTitles;
            seenScenes.Add(canonical);
            addedScenesCount++;
        }

        // 5. Compute version/revision and detect if anything materially changed vs currentCatalog.
        bool voiceGroupsChanged = !VoiceGroupsEqual(mergedVoiceGroups, currentCatalog.VoiceGroups);
        bool scenesAddedBeyondCurrent = candidateFiles.Keys.Any(k => !currentCatalog.Entries.ContainsKey(k));
        bool versionIncreased = targetVersion > currentCatalog.GameVersion;
        bool hasBuiltInUpgrade = currentCatalog.SelfMerged
            && currentCatalog.BuiltInBaseGameVersion is { } baseVer
            && (builtInCatalog.GameVersion > baseVer
                || (builtInCatalog.GameVersion == baseVer && builtInCatalog.Revision > currentCatalog.BuiltInBaseRevision));

        bool changed = versionIncreased || addedScenesCount > 0 || scenesAddedBeyondCurrent || voiceGroupsChanged || hasBuiltInUpgrade;

        int nextRevision;
        if (targetVersion > currentCatalog.GameVersion)
        {
            nextRevision = 1;
        }
        else if (changed)
        {
            nextRevision = Math.Max(currentCatalog.Revision + 1, builtInCatalog.Revision);
        }
        else
        {
            nextRevision = currentCatalog.Revision;
        }

        if (!changed)
            return new MergeOutcome([], currentCatalog, false, sourceSummary);

        // 6. Emit final self-merged CutsceneNames.json payload cleanly.
        var mergedRoot = new JsonObject
        {
            ["gameVersion"] = $"{targetVersion.Major}.{targetVersion.Minor}.{Math.Max(0, targetVersion.Build)}",
            ["revision"] = nextRevision,
            ["selfMerged"] = true,
            ["builtInBaseGameVersion"] = $"{builtInCatalog.GameVersion.Major}.{builtInCatalog.GameVersion.Minor}.{Math.Max(0, builtInCatalog.GameVersion.Build)}",
            ["builtInBaseRevision"] = builtInCatalog.Revision,
            ["questSource"] = questSourceUrl
        };

        if (builtInRoot["earlyCutsceneSource"]?.DeepClone() is JsonNode earlySource)
            mergedRoot["earlyCutsceneSource"] = earlySource;
        if (builtInRoot["releaseSource"]?.DeepClone() is JsonNode releaseSource)
            mergedRoot["releaseSource"] = releaseSource;

        mergedRoot["voiceSource"] = voiceSourceUrl;
        mergedRoot["chapters"] = new JsonArray(chaptersList.ToArray<JsonNode?>());
        mergedRoot["voiceGroups"] = voiceGroupsObj;

        byte[] outputBytes;
        using (var ms = new MemoryStream())
        {
            using (var writer = new Utf8JsonWriter(ms, WriteJsonOptions))
            {
                mergedRoot.WriteTo(writer);
            }
            ms.WriteByte((byte)'\n');
            outputBytes = ms.ToArray();
        }

        using var verifyStream = new MemoryStream(outputBytes, writable: false);
        CutsceneNameCatalog verified = CutsceneNames.Read(verifyStream);
        return new MergeOutcome(outputBytes, verified, true, sourceSummary);
    }

    /// <summary>
    /// When cached self-merged catalog is newer in gameVersion than an upgraded built-in release,
    /// overlays built-in curated chapter/scene titles and removal rules onto the active in-memory view.
    /// </summary>
    public static CutsceneNameCatalog OverlayBuiltInCurated(CutsceneNameCatalog cached, CutsceneNameCatalog builtIn)
    {
        var mergedEntries = new Dictionary<string, CutsceneName>(cached.Entries, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, builtInEntry) in builtIn.Entries)
            mergedEntries[key] = builtInEntry;

        IReadOnlyDictionary<string, CutsceneVoiceGroup> voiceGroups = cached.VoiceGroups.Count > 0
            ? cached.VoiceGroups
            : builtIn.VoiceGroups;

        return cached with
        {
            Entries = mergedEntries,
            VoiceGroups = voiceGroups
        };
    }

    private static async Task<(Version? LiveVersion, HashSet<string> VideoFiles)> TryFetchLiveSophonAsync(
        SophonBuildClient? sophonClient,
        GameServerScheme? preferredScheme,
        CancellationToken token)
    {
        var videos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Version? highestVersion = null;
        if (sophonClient is null)
            return (null, videos);

        var schemes = new List<GameServerScheme>();
        if (preferredScheme is not null)
            schemes.Add(preferredScheme);
        if (!schemes.Any(s => !s.IsOversea))
            schemes.Add(GameServerScheme.ChineseOfficialOfficial);
        if (!schemes.Any(s => s.IsOversea))
            schemes.Add(GameServerScheme.OverseaOfficialDefault);

        foreach (GameServerScheme scheme in schemes)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                SophonBranchPayload payload = await sophonClient
                    .GetBranchPayloadAsync(scheme, isPreDownload: false, token).ConfigureAwait(false);
                if (Version.TryParse(payload.Tag, out Version? tagVer) && (highestVersion is null || tagVer > highestVersion))
                    highestVersion = tagVer;

                SophonBranchInfo branchInfo = await sophonClient
                    .GetBranchInfoAsync(scheme, isPreDownload: false, token).ConfigureAwait(false);
                SophonManifestProto manifest = await sophonClient
                    .DownloadManifestAsync(branchInfo, token).ConfigureAwait(false);

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
                    $"Sophon live query failed for {scheme.DisplayName}: {ex.Message}");
            }
        }

        return (highestVersion, videos);
    }

    private static async Task<string> TryResolveUpstreamCommitAsync(HttpClient client, CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var request = new HttpRequestMessage(HttpMethod.Get, AnimeGameDataRepoApiCommitUrl);
            request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using HttpResponseMessage resp = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return "master";

            using Stream stream = await resp.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("sha", out JsonElement shaProp) && shaProp.ValueKind == JsonValueKind.String)
            {
                string? sha = shaProp.GetString();
                if (!string.IsNullOrWhiteSpace(sha))
                    return sha;
            }
        }
        catch
        {
        }

        return "master";
    }

    private static async Task<byte[]> DownloadBytesWithMirrorsAsync(
        HttpClient client, string rawUrl, int maxBytes, TimeSpan perAttemptTimeout, CancellationToken token)
    {
        Exception? lastError = null;
        foreach (var (sourceName, url) in BuildMirrorCandidates(rawUrl))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(perAttemptTimeout);
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
                    return ms.ToArray();
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
                DebugLog.Info(nameof(CutsceneCatalogUpdater), $"Fetch via {sourceName} failed: {ex.Message}");
            }
        }

        throw lastError ?? new InvalidOperationException($"Failed to fetch upstream resource: {rawUrl}");
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
                DebugLog.Info(nameof(CutsceneCatalogUpdater), $"TextMap via {sourceName} failed: {ex.Message}");
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
            (new Uri(ApiEndpoints.GhProxyBase).Host,
                ApiEndpoints.BuildMirrorUrl(new Uri(ApiEndpoints.GhProxyBase).Host, rawUrl)),
            ("GitHub", rawUrl)
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
        var result = new Dictionary<string, ResourceCollectionEntry>(StringComparer.Ordinal);
        if (!doc.RootElement.TryGetProperty("resourceCollectionMap", out JsonElement mapProp)
            || mapProp.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (JsonProperty groupProp in mapProp.EnumerateObject())
        {
            List<string> packages = [];
            if (groupProp.Value.TryGetProperty("audioResourceList", out JsonElement audioProp)
                && audioProp.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in audioProp.EnumerateArray())
                {
                    string? path = item.GetString()?.Replace('\\', '/').TrimStart('/');
                    if (!string.IsNullOrWhiteSpace(path))
                        packages.Add(path);
                }
            }

            List<string> videos = [];
            if (groupProp.Value.TryGetProperty("videoResourceList", out JsonElement videoProp)
                && videoProp.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in videoProp.EnumerateArray())
                {
                    string? path = item.GetString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(path))
                        videos.Add(path);
                }
            }

            result[groupProp.Name] = new ResourceCollectionEntry(groupProp.Name, packages, videos);
        }

        return result;
    }

    internal static Dictionary<int, ChapterMeta> ParseChapterExcel(byte[] jsonBytes)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonBytes);
        var result = new Dictionary<int, ChapterMeta>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out JsonElement idProp)
                || !idProp.TryGetInt32(out int id) || id <= 0)
            {
                continue;
            }

            ulong titleHash = TryReadUInt64(item, "chapterTitleTextMapHash");
            ulong numHash = TryReadUInt64(item, "chapterNumTextMapHash");
            string? questType = item.TryGetProperty("questType", out JsonElement qtProp) && qtProp.ValueKind == JsonValueKind.String
                ? qtProp.GetString()
                : null;
            result[id] = new ChapterMeta(id, titleHash, numHash, questType);
        }

        return result;
    }

    internal static List<QuestInfo> ParseMainQuests(byte[] jsonBytes)
    {
        using JsonDocument doc = JsonDocument.Parse(jsonBytes);
        var result = new List<QuestInfo>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (JsonElement item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out JsonElement idProp)
                || !idProp.TryGetInt32(out int id) || id <= 0)
            {
                continue;
            }

            int resId = item.TryGetProperty("resId", out JsonElement resProp) && resProp.TryGetInt32(out int r) ? r : 0;
            if (resId <= 0)
                continue;

            int chapterId = item.TryGetProperty("chapterId", out JsonElement chProp) && chProp.TryGetInt32(out int c) ? c : 0;
            string? type = item.TryGetProperty("type", out JsonElement typeProp) && typeProp.ValueKind == JsonValueKind.String
                ? typeProp.GetString()
                : null;
            ulong titleHash = TryReadUInt64(item, "titleTextMapHash");
            result.Add(new QuestInfo(id, resId, chapterId, type, titleHash));
        }

        return result;
    }

    private static ulong TryReadUInt64(JsonElement obj, string propertyName) =>
        obj.TryGetProperty(propertyName, out JsonElement prop) && prop.TryGetUInt64(out ulong val) ? val : 0UL;

    private static HashSet<ulong> CollectNeededTextMapHashes(
        byte[] builtInBytes,
        byte[]? existingCachedBytes,
        IReadOnlySet<string> liveUsmFiles,
        IReadOnlyDictionary<string, ResourceCollectionEntry> resourceCollections,
        IReadOnlyList<QuestInfo> mainQuests,
        IReadOnlyDictionary<int, ChapterMeta> chapterMeta)
    {
        var knownScenes = new HashSet<string>(CutsceneNames.BuiltIn.Entries.Keys, StringComparer.OrdinalIgnoreCase);
        var knownChapters = new HashSet<int>(
            CutsceneNames.BuiltIn.Entries.Values.Where(e => e.Chapter.QuestChapterId > 0).Select(e => e.Chapter.QuestChapterId));

        if (existingCachedBytes is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(existingCachedBytes, writable: false);
                CutsceneNameCatalog cached = CutsceneNames.Read(ms);
                foreach (var (key, val) in cached.Entries)
                {
                    knownScenes.Add(key);
                    if (val.Chapter.QuestChapterId > 0)
                        knownChapters.Add(val.Chapter.QuestChapterId);
                }
            }
            catch
            {
            }
        }

        var videoToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (gid, entry) in resourceCollections)
        {
            foreach (string rawVid in entry.Videos)
                videoToGroup.TryAdd(CanonicalSceneKey(Path.GetFileName(rawVid.Replace('\\', '/'))), gid);
        }

        var questsByResId = new Dictionary<int, List<QuestInfo>>();
        foreach (QuestInfo q in mainQuests)
        {
            if (!questsByResId.TryGetValue(q.ResId, out List<QuestInfo>? list))
                questsByResId[q.ResId] = list = [];
            list.Add(q);
        }

        IEnumerable<string> candidates = liveUsmFiles.Count > 0
            ? liveUsmFiles
            : resourceCollections.Values.SelectMany(v => v.Videos);

        var neededHashes = new HashSet<ulong>();
        foreach (string rawVid in candidates)
        {
            string fileName = Path.GetFileName(rawVid.Replace('\\', '/').Trim());
            if (!fileName.EndsWith(".usm", StringComparison.OrdinalIgnoreCase))
                continue;
            string canonical = CanonicalSceneKey(fileName);
            if (knownScenes.Contains(canonical))
                continue;

            if (TryResolvePrimaryQuest(canonical, videoToGroup, questsByResId, out QuestInfo? q) && q is not null)
            {
                if (q.TitleTextMapHash > 0)
                    neededHashes.Add(q.TitleTextMapHash);
                if (q.ChapterId > 0 && !knownChapters.Contains(q.ChapterId)
                    && chapterMeta.TryGetValue(q.ChapterId, out ChapterMeta? meta))
                {
                    if (meta.TitleTextMapHash > 0) neededHashes.Add(meta.TitleTextMapHash);
                    if (meta.NumTextMapHash > 0) neededHashes.Add(meta.NumTextMapHash);
                }
            }
        }

        return neededHashes;
    }

    private static void NormalizeAndRegisterChapter(
        JsonObject chapterObj,
        List<JsonObject> chaptersList,
        HashSet<string> seenScenes,
        Dictionary<int, JsonObject> chapterByQuestId)
    {
        var scenesObj = chapterObj["scenes"] as JsonObject ?? new JsonObject();
        var filteredScenes = new JsonObject();
        foreach (var kv in scenesObj)
        {
            if (!IsValidSceneKey(kv.Key) || !seenScenes.Add(kv.Key))
                continue;
            filteredScenes[kv.Key] = kv.Value?.DeepClone();
        }
        chapterObj["scenes"] = filteredScenes;
        chaptersList.Add(chapterObj);

        if (chapterObj["questChapterId"]?.GetValue<int>() is int qcid and > 0)
            chapterByQuestId.TryAdd(qcid, chapterObj);
    }

    private static void TryOverlayCachedExtras(
        byte[] existingCachedBytes,
        List<JsonObject> chaptersList,
        HashSet<string> seenScenes,
        Dictionary<int, JsonObject> chapterByQuestId)
    {
        try
        {
            JsonNode? cachedNode = JsonNode.Parse(existingCachedBytes);
            if (cachedNode?["chapters"] is not JsonArray cachedChapters)
                return;

            foreach (JsonNode? chNode in cachedChapters)
            {
                if (chNode is not JsonObject cachedChapter || cachedChapter["scenes"] is not JsonObject cachedScenes)
                    continue;

                var extraScenes = new List<(string Key, JsonNode? Value)>();
                foreach (var kv in cachedScenes)
                {
                    if (IsValidSceneKey(kv.Key) && !seenScenes.Contains(kv.Key))
                        extraScenes.Add((kv.Key, kv.Value?.DeepClone()));
                }
                if (extraScenes.Count == 0)
                    continue;

                int qcid = cachedChapter["questChapterId"]?.GetValue<int>() ?? 0;
                if (qcid > 0 && chapterByQuestId.TryGetValue(qcid, out JsonObject? targetChapter))
                {
                    JsonObject targetScenes = targetChapter["scenes"] as JsonObject ?? new JsonObject();
                    targetChapter["scenes"] = targetScenes;
                    foreach (var (key, val) in extraScenes)
                    {
                        if (seenScenes.Add(key))
                            targetScenes[key] = val;
                    }
                }
                else
                {
                    JsonObject clonedChapter = cachedChapter.DeepClone().AsObject();
                    var cleanScenes = new JsonObject();
                    foreach (var (key, val) in extraScenes)
                    {
                        if (seenScenes.Add(key))
                            cleanScenes[key] = val;
                    }
                    clonedChapter["scenes"] = cleanScenes;
                    chaptersList.Add(clonedChapter);
                    if (qcid > 0)
                        chapterByQuestId.TryAdd(qcid, clonedChapter);
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneCatalogUpdater), $"Ignoring cached extra chapters ({ex.GetType().Name}).");
        }
    }

    private static bool TryResolvePrimaryQuest(
        string canonicalSceneKey,
        IReadOnlyDictionary<string, string> videoToGroup,
        IReadOnlyDictionary<int, List<QuestInfo>> questsByResId,
        out QuestInfo? quest)
    {
        quest = null;
        if (!videoToGroup.TryGetValue(canonicalSceneKey, out string? gidStr) || !int.TryParse(gidStr, out int gid))
            return false;
        if (!questsByResId.TryGetValue(gid, out List<QuestInfo>? qList) || qList.Count == 0)
            return false;

        quest = qList.FirstOrDefault(q => q.ChapterId > 0) ?? qList[0];
        return true;
    }

    private static JsonObject CreateChapterObject(
        int questChapterId,
        string? questType,
        string fileName,
        string releaseVersion,
        IReadOnlyDictionary<int, ChapterMeta> chapterMeta,
        IReadOnlyDictionary<ulong, string> textChs,
        IReadOnlyDictionary<ulong, string> textCht,
        IReadOnlyDictionary<ulong, string> textEn,
        Dictionary<string, JsonObject> adhocByTitleKey,
        List<JsonObject> chaptersList,
        Dictionary<int, JsonObject> chapterByQuestId)
    {
        if (questChapterId == 0)
        {
            CutsceneChapter fallback = CutsceneChapter.FromFileName(fileName);
            string cacheKey = $"{fallback.Category}|{fallback.TitleKey}|{fallback.QuestId}";
            if (adhocByTitleKey.TryGetValue(cacheKey, out JsonObject? existingAdhoc))
                return existingAdhoc;

            string zhCnFallback = ResolveAdhocChapterTitle(fallback, "zh-CN");
            string zhTwFallback = ResolveAdhocChapterTitle(fallback, "zh-TW");
            string enUsFallback = ResolveAdhocChapterTitle(fallback, "en-US");

            var adhocObj = new JsonObject
            {
                ["category"] = fallback.Category.ToString(),
                ["releaseVersion"] = releaseVersion,
                ["questChapterId"] = 0,
                ["titles"] = new JsonObject
                {
                    ["zh-CN"] = zhCnFallback,
                    ["zh-TW"] = zhTwFallback,
                    ["en-US"] = enUsFallback
                },
                ["scenes"] = new JsonObject()
            };
            chaptersList.Add(adhocObj);
            adhocByTitleKey[cacheKey] = adhocObj;
            return adhocObj;
        }

        ChapterMeta? meta = chapterMeta.GetValueOrDefault(questChapterId);
        string category = InferCategory(questType ?? meta?.QuestType, questChapterId);

        string FormatTitle(IReadOnlyDictionary<ulong, string> map, string fallback)
        {
            if (meta is null)
                return fallback;
            string num = meta.NumTextMapHash > 0 ? map.GetValueOrDefault(meta.NumTextMapHash, string.Empty).Trim() : string.Empty;
            string title = meta.TitleTextMapHash > 0 ? map.GetValueOrDefault(meta.TitleTextMapHash, string.Empty).Trim() : string.Empty;
            if (num.Length > 0 && title.Length > 0)
                return $"{num} · {title}";
            return title.Length > 0 ? title : num.Length > 0 ? num : fallback;
        }

        string chsTitle = FormatTitle(textChs, $"章节 {questChapterId}");
        string chtTitle = FormatTitle(textCht, chsTitle);
        string enTitle = FormatTitle(textEn, $"Chapter {questChapterId}");

        var chapterObj = new JsonObject
        {
            ["category"] = category,
            ["releaseVersion"] = releaseVersion,
            ["questChapterId"] = questChapterId,
            ["titles"] = new JsonObject
            {
                ["zh-CN"] = chsTitle,
                ["zh-TW"] = chtTitle,
                ["en-US"] = enTitle
            },
            ["scenes"] = new JsonObject()
        };

        chaptersList.Add(chapterObj);
        chapterByQuestId[questChapterId] = chapterObj;
        return chapterObj;
    }

    private static JsonObject BuildSceneTitles(
        string canonical,
        QuestInfo? linkedQuest,
        IReadOnlyDictionary<int, int> questSceneCounts,
        IReadOnlyDictionary<ulong, string> textChs,
        IReadOnlyDictionary<ulong, string> textCht,
        IReadOnlyDictionary<ulong, string> textEn)
    {
        if (linkedQuest is { TitleTextMapHash: > 0 } q
            && textChs.TryGetValue(q.TitleTextMapHash, out string? questZhCn)
            && !string.IsNullOrWhiteSpace(questZhCn))
        {
            bool multiScenes = questSceneCounts.GetValueOrDefault(q.Id) > 1;
            string suffix = multiScenes ? $" · {ShortSceneSuffix(canonical)}" : string.Empty;
            string questZhTw = textCht.TryGetValue(q.TitleTextMapHash, out string? tw) && !string.IsNullOrWhiteSpace(tw)
                ? tw
                : questZhCn;
            string questEn = textEn.TryGetValue(q.TitleTextMapHash, out string? en) && !string.IsNullOrWhiteSpace(en)
                ? en
                : canonical;

            return new JsonObject
            {
                ["zh-CN"] = questZhCn + suffix,
                ["zh-TW"] = questZhTw + suffix,
                ["en-US"] = questEn + suffix
            };
        }

        return new JsonObject
        {
            ["zh-CN"] = canonical,
            ["zh-TW"] = canonical,
            ["en-US"] = canonical
        };
    }

    private static string ShortSceneSuffix(string canonical)
    {
        int idx = canonical.LastIndexOf('_');
        return idx > 0 && idx < canonical.Length - 1 ? canonical[(idx + 1)..] : canonical;
    }

    private static string ResolveAdhocChapterTitle(CutsceneChapter chapter, string locale)
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

        return chapter.QuestId.Length == 0 ? baseTitle : $"{baseTitle} · {chapter.QuestId}";
    }

    private static string InferCategory(string? questType, int chapterId) => questType switch
    {
        "AQ" => nameof(CutsceneCategory.Archon),
        "EQ" => nameof(CutsceneCategory.Event),
        "WQ" => nameof(CutsceneCategory.World),
        "LQ" => nameof(CutsceneCategory.Story),
        _ when chapterId is >= 1000 and < 2000 => nameof(CutsceneCategory.Archon),
        _ => nameof(CutsceneCategory.Other)
    };

    private static string CanonicalSceneKey(string fileName)
    {
        string stem = Path.GetFileNameWithoutExtension(fileName);
        foreach (string suffix in TravelerSuffixes)
        {
            if (stem.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return stem[..^suffix.Length];
        }
        return stem;
    }

    private static bool IsValidSceneKey(string key) =>
        key.Length > 0 && key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-');

    private static Version MaxVersion(Version? a, Version b) =>
        a is not null && a > b ? a : b;

    private static bool VoiceGroupsEqual(
        IReadOnlyDictionary<string, CutsceneVoiceGroup> left,
        IReadOnlyDictionary<string, CutsceneVoiceGroup> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach (var (gid, lg) in left)
        {
            if (!right.TryGetValue(gid, out CutsceneVoiceGroup? rg))
                return false;
            if (!lg.Packages.SequenceEqual(rg.Packages, StringComparer.Ordinal)
                || !lg.Videos.SequenceEqual(rg.Videos, StringComparer.Ordinal)
                || !lg.Quests.SequenceEqual(rg.Quests))
            {
                return false;
            }
        }
        return true;
    }
}
