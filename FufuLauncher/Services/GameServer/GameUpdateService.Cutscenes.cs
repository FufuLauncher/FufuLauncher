/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;

namespace FufuLauncher.Services.GameServer;

internal enum CutsceneRemovalProtection
{
    None,
    Unclassified,
    HistoryUnavailable,
    MissingInHistory,
    LinkedVideo,
    CurrentChapter
}

internal sealed record CutsceneAsset(
    AssetProperty Asset,
    CutsceneChapter Chapter,
    CutsceneRemovalProtection RemovalProtection,
    long LocalBytes,
    long PersistentBytes,
    CutsceneLocalState LocalState,
    CutsceneTraveler? Traveler,
    CutsceneName? Metadata)
{
    public bool CanRemove => RemovalProtection == CutsceneRemovalProtection.None;
    public string Name => Path.GetFileName(Asset.AssetName);
    public bool IsInstalled => LocalBytes == Asset.AssetSize || PersistentBytes == Asset.AssetSize;
    public bool IsStubbedLocked => !IsInstalled && LocalState == CutsceneLocalState.StubbedLocked;
    public long DiskBytes => LocalBytes + PersistentBytes;
    public long DownloadBytes => Asset.AssetChunks.Sum(chunk => chunk.ChunkSize);
}

internal sealed record CutsceneAudioAsset(
    AssetProperty Asset,
    SophonBranchInfo Source,
    string LanguageFolder,
    string LanguageKey,
    string RelativePath,
    IReadOnlyList<string> LinkedVideoNames,
    IReadOnlyList<CutsceneChapter> LinkedChapters,
    bool IsQuestVoice,
    CutsceneRemovalProtection RemovalProtection,
    long LocalBytes,
    long PersistentBytes,
    CutsceneLocalState LocalState)
{
    public bool CanRemove => RemovalProtection == CutsceneRemovalProtection.None;
    public bool IsSharedAcrossChapters => LinkedChapters.Count > 1;
    public string Name => Path.GetFileName(Asset.AssetName);
    public bool IsInstalled => LocalBytes == Asset.AssetSize || PersistentBytes == Asset.AssetSize;
    public bool IsStubbedLocked => !IsInstalled && LocalState == CutsceneLocalState.StubbedLocked;
    public long DiskBytes => LocalBytes + PersistentBytes;
    public long DownloadBytes => Asset.AssetChunks.Sum(chunk => chunk.ChunkSize);
}

internal sealed record CutsceneVoiceManifestAsset(
    AssetProperty Asset, SophonBranchInfo Source, string LanguageFolder, string RelativePath, string LanguageKey);

internal sealed record CutsceneCatalog(
    string GameDirectory,
    string VideoDirectory,
    string PersistentVideoDirectory,
    string AudioDirectory,
    string PersistentAudioDirectory,
    string PersistentRootDirectory,
    string Version,
    string? EarlierVersion,
    GameServerScheme Scheme,
    SophonBranchInfo Source,
    IReadOnlyList<CutsceneAsset> Videos,
    IReadOnlyList<CutsceneAudioAsset> Audios,
    IReadOnlyList<CutsceneVoiceManifestAsset> VoiceAssets,
    Dictionary<string, string>? EarlierVideoHashes,
    Dictionary<string, string>? EarlierAudioHashes,
    bool HasHistory);

internal sealed record CutsceneChange(CutsceneAsset Video, bool Keep, bool RemoveUnusedTraveler = false);
internal sealed record CutsceneAudioChange(CutsceneAudioAsset Audio, bool Keep);

internal sealed record CutsceneVoicePackage(
    long Bytes,
    long PersistentBytes,
    bool IsStubbedLocked,
    IReadOnlyList<CutsceneChapter> LinkedChapters);

internal sealed record CutsceneVoicePack(string LanguageKey, IReadOnlyList<CutsceneVoicePackage> Packages);

public sealed partial class GameUpdateService
{
    internal static IReadOnlyList<(string Directory, string LabelKey, string MatchingField)> VoiceLanguages { get; } =
    [
        ("Chinese", "Cutscene_VoiceChinese", "zh-cn"),
        ("English(US)", "Cutscene_VoiceEnglish", "en-us"),
        ("Japanese", "Cutscene_VoiceJapanese", "ja-jp"),
        ("Korean", "Cutscene_VoiceKorean", "ko-kr")
    ];

    private static readonly Dictionary<string, string> VoiceLanguageKeyMap =
        VoiceLanguages.ToDictionary(item => item.Directory, item => item.LabelKey, StringComparer.OrdinalIgnoreCase);

    internal async Task<CutsceneCatalog> ScanCutscenesAsync(
        string gameDir, CutsceneNameCatalog activeCatalog, CancellationToken token)
    {
        gameDir = Path.GetFullPath(gameDir);
        GameServerScheme scheme = _configurationService.TryDetectCurrentScheme(gameDir)
            ?? throw new DirectoryNotFoundException("Err_GamePathNotFound".GetLocalized());
        string version = _configurationService.TryGetGameVersion(gameDir)
            ?? throw new InvalidOperationException("GameUpdate_UnknownLocalVersion".GetLocalized());
        string dataDir = scheme.IsOversea ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR;

        string videoPrefix = $"{dataDir}/StreamingAssets/VideoAssets/StandaloneWindows64/";
        string audioPrefix = $"{dataDir}/StreamingAssets/AudioAssets/";
        string videoDir = Path.GetFullPath(Path.Combine(gameDir, videoPrefix));
        string persistentRoot = CutsceneProtectionService.GetPersistentDirectory(gameDir, scheme.IsOversea);
        string persistentVideoDir = Path.GetFullPath(Path.Combine(persistentRoot, "VideoAssets", "StandaloneWindows64"));
        string audioDir = Path.GetFullPath(Path.Combine(gameDir, audioPrefix));
        string persistentAudioDir = Path.GetFullPath(Path.Combine(persistentRoot, "AudioAssets"));

        ValidateCutsceneDirectory(videoDir);
        ValidateExistingDirectoryChain(persistentVideoDir);

        SophonBranchPayload? branchPayload = null;
        try
        {
            branchPayload = await _sophonBuildClient.GetBranchPayloadAsync(scheme, isPreDownload: false, token)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
        }

        Dictionary<string, string> voiceFolders = GetInstalledVoiceFolders(audioDir, persistentAudioDir);
        var (source, manifest, voiceAssets) =
            await LoadCutsceneManifestsAsync(scheme, version, audioPrefix, voiceFolders, token).ConfigureAwait(false);
        var (earlierVersion, earlierVideos, earlierAudios) = await LoadEarlierResourcesAsync(
                scheme, version, branchPayload, videoPrefix, audioPrefix, voiceFolders, token)
            .ConfigureAwait(false);

        AssetProperty[] videoAssets = manifest.Assets.Where(asset => IsPackagedCutscene(asset, videoPrefix)).ToArray();
        var shippedFiles = videoAssets.Select(asset => Path.GetFileName(asset.AssetName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var videos = new List<CutsceneAsset>(videoAssets.Length);
        foreach (AssetProperty asset in videoAssets)
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(asset.AssetName);
            CutsceneTraveler? traveler = CutsceneTravelers.FindVariant(name, shippedFiles);
            string streamingPath = Path.Combine(videoDir, name);
            string persistentPath = Path.Combine(persistentVideoDir, name);
            videos.Add(new CutsceneAsset(
                asset,
                CutsceneChapter.FromFileName(name),
                CutsceneRemovalProtection.Unclassified,
                GetCutsceneFileSize(streamingPath),
                GetCutsceneFileSize(persistentPath),
                CutsceneProtectionService.GetLocalState(streamingPath, persistentPath, asset.AssetSize),
                traveler,
                null));
        }

        var catalog = new CutsceneCatalog(
            gameDir,
            videoDir,
            persistentVideoDir,
            audioDir,
            persistentAudioDir,
            persistentRoot,
            version,
            earlierVersion,
            scheme,
            source,
            videos,
            [],
            voiceAssets,
            earlierVideos,
            earlierAudios,
            earlierVideos is not null);
        return RebuildCutsceneAssociations(catalog, activeCatalog, token);
    }

    internal CutsceneCatalog RebuildCutsceneAssociations(
        CutsceneCatalog catalog, CutsceneNameCatalog activeCatalog, CancellationToken token)
    {
        EnsureCutsceneInstallationUnchanged(catalog);
        ValidateExistingDirectoryChain(catalog.AudioDirectory);
        ValidateExistingDirectoryChain(catalog.PersistentAudioDirectory);

        var videos = new List<CutsceneAsset>(catalog.Videos.Count);
        foreach (CutsceneAsset video in catalog.Videos)
        {
            token.ThrowIfCancellationRequested();
            CutsceneName? metadata = activeCatalog.FindByFilename(video.Name);
            CutsceneChapter chapter = metadata?.Chapter ?? CutsceneChapter.FromFileName(video.Name);
            CutsceneRemovalProtection protection = HasCutsceneRemovalException(video, catalog.Version)
                ? CutsceneRemovalProtection.None
                : chapter.Category == CutsceneCategory.Other
                    ? CutsceneRemovalProtection.Unclassified
                    : GetHistoricalProtection(catalog.EarlierVideoHashes, video.Name);

            videos.Add(video with { Chapter = chapter, Metadata = metadata, RemovalProtection = protection });
        }

        var audios = BuildTracedAudioAssets(catalog, videos, activeCatalog, token);
        return catalog with { Videos = videos, Audios = audios };
    }

    private static CutsceneRemovalProtection GetHistoricalProtection(
        Dictionary<string, string>? earlierHashes, string path)
    {
        if (earlierHashes is null)
            return CutsceneRemovalProtection.HistoryUnavailable;
        // A historical manifest establishes that the resource was already released.
        return earlierHashes.ContainsKey(path)
            ? CutsceneRemovalProtection.None
            : CutsceneRemovalProtection.MissingInHistory;
    }

    // Display catalogs can be imported. Keep exceptions for missing history tied to known official files.
    private static bool HasCutsceneRemovalException(CutsceneAsset video, string version) =>
        (video.Traveler is not null ? CutsceneNames.CanonicalSceneKey(video.Name) : Path.GetFileNameWithoutExtension(video.Name))
        .ToUpperInvariant() switch
        {
            "CS_BEYOND_BACKSTORY" or "VIDEO_REUNION_67_TEST" or "VIDEO_REUNION_72"
                or "BATTLEPASS" or "CHANGEWEATHER" or "CS_SHIELDINGRESOURCES_02" => true,
            "CS_ZD_AQ702318_SN" or "CS_ZD_AQ702323_OD" or "CS_ZD_AQ702514_SR"
                or "CS_ZD_AQ703109_ZS" => version == "7.2.0",
            _ => false
        };

    internal CutsceneCatalog RefreshCutscenes(CutsceneCatalog catalog)
    {
        EnsureCutsceneInstallationUnchanged(catalog);
        return catalog with
        {
            Videos = catalog.Videos.Select(video =>
            {
                string streamingPath = Path.Combine(catalog.VideoDirectory, video.Name);
                string persistentPath = Path.Combine(catalog.PersistentVideoDirectory, video.Name);
                return video with
                {
                    LocalBytes = GetCutsceneFileSize(streamingPath),
                    PersistentBytes = GetCutsceneFileSize(persistentPath),
                    LocalState = CutsceneProtectionService.GetLocalState(streamingPath, persistentPath, video.Asset.AssetSize)
                };
            }).ToArray(),
            Audios = catalog.Audios.Select(audio =>
            {
                string rel = audio.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                string streamingPath = Path.Combine(catalog.AudioDirectory, rel);
                string persistentPath = Path.Combine(catalog.PersistentAudioDirectory, rel);
                return audio with
                {
                    LocalBytes = GetCutsceneFileSize(streamingPath),
                    PersistentBytes = GetCutsceneFileSize(persistentPath),
                    LocalState = CutsceneProtectionService.GetLocalState(streamingPath, persistentPath, audio.Asset.AssetSize)
                };
            }).ToArray()
        };
    }

    internal IReadOnlyList<CutsceneVoicePack> ScanLocalVoicePacks(string gameDir, CutsceneNameCatalog names, CancellationToken token)
    {
        gameDir = Path.GetFullPath(gameDir);
        GameServerScheme scheme = _configurationService.TryDetectCurrentScheme(gameDir)
            ?? throw new InvalidOperationException("Err_GamePathNotFound".GetLocalized());
        string dataDir = scheme.IsOversea ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR;
        string streamingRoot = Path.Combine(gameDir, dataDir, "StreamingAssets", "AudioAssets");
        string persistentRoot = Path.Combine(gameDir, dataDir, "Persistent", "AudioAssets");

        var groupsByPackage = GetVoiceGroupsByPackage(names);
        var packs = new List<CutsceneVoicePack>();
        foreach (var (folder, labelKey, _) in VoiceLanguages)
        {
            token.ThrowIfCancellationRequested();
            string streamingPath = Path.Combine(streamingRoot, folder);
            string persistentPath = Path.Combine(persistentRoot, folder);
            bool hasStreaming = Directory.Exists(streamingPath);
            bool hasPersistent = Directory.Exists(persistentPath);
            if (!hasStreaming && !hasPersistent)
                continue;

            if (hasStreaming) ValidateExistingDirectoryChain(streamingPath);
            if (hasPersistent) ValidateExistingDirectoryChain(persistentPath);

            var fileNames = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (hasStreaming)
            {
                foreach (string file in Directory.EnumerateFiles(streamingPath, "*.pck", SearchOption.TopDirectoryOnly))
                    fileNames.Add(Path.GetFileName(file));
            }
            if (hasPersistent)
            {
                foreach (string file in Directory.EnumerateFiles(persistentPath, "*.pck", SearchOption.TopDirectoryOnly))
                    fileNames.Add(Path.GetFileName(file));
            }

            var packages = new List<CutsceneVoicePackage>();
            foreach (string name in fileNames)
            {
                token.ThrowIfCancellationRequested();
                string sFile = Path.Combine(streamingPath, name);
                string pFile = Path.Combine(persistentPath, name);
                long sBytes = GetCutsceneFileSize(sFile);
                long pBytes = GetCutsceneFileSize(pFile);
                bool locked = (sBytes == 0 && CutsceneProtectionService.IsStubbedAndLocked(sFile))
                    || (pBytes == 0 && CutsceneProtectionService.IsStubbedAndLocked(pFile));

                if (sBytes == 0 && pBytes == 0 && !locked)
                    continue;

                var (_, chapters) = ResolveVoiceLinks(groupsByPackage[$"{folder}/{name}"], names);
                packages.Add(new CutsceneVoicePackage(sBytes, pBytes, locked, chapters));
            }

            if (packages.Count > 0)
                packs.Add(new CutsceneVoicePack(labelKey, packages));
        }
        return packs;
    }

    internal async Task<string> CopyLocalCutsceneAsync(CutsceneCatalog catalog, CutsceneAsset video,
        string workspace, CancellationToken token)
    {
        using var lease = await GameServerOperation.EnterAsync(catalog.GameDirectory, token, requireClosed: false)
            .ConfigureAwait(false);
        EnsureCutsceneInstallationUnchanged(catalog);
        if (!video.IsInstalled || !catalog.Videos.Contains(video))
            throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());

        string path = ResolveInstalledAssetPath(
            Path.Combine(catalog.VideoDirectory, video.Name),
            Path.Combine(catalog.PersistentVideoDirectory, video.Name),
            video.Asset.AssetSize)
            ?? throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());

        string copy = Path.Combine(workspace, video.Name);
        await using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        await using (var output = new FileStream(copy, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous))
        {
            await source.CopyToAsync(output, token).ConfigureAwait(false);
        }
        if (!video.Asset.AssetHashMd5.Equals(await HashUtility.Md5FileAsync(copy, token).ConfigureAwait(false),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(string.Format("Cutscene_FileChanged".GetLocalized(), video.Name));
        return copy;
    }

    internal Task ApplyCutsceneChangesAsync(CutsceneCatalog catalog, IReadOnlyList<CutsceneChange> changes,
        IProgress<GameServerConversionProgress> progress, CancellationToken token) =>
        ApplyResourceChangesAsync(catalog, changes, [], useStubLockProtection: true, progress, token);

    internal async Task ApplyResourceChangesAsync(
        CutsceneCatalog catalog,
        IReadOnlyList<CutsceneChange> videoChanges,
        IReadOnlyList<CutsceneAudioChange> audioChanges,
        bool useStubLockProtection,
        IProgress<GameServerConversionProgress> progress,
        CancellationToken token)
    {
        using var lease = await GameServerOperation.EnterAsync(catalog.GameDirectory, token).ConfigureAwait(false);
        EnsureCutsceneInstallationUnchanged(catalog);

        if (videoChanges.Where(change => change.RemoveUnusedTraveler).Select(change => change.Video.Traveler).Distinct()
            .Skip(1).Any())
            throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());

        var pendingItems = new List<(
            AssetProperty Asset,
            SophonBranchInfo Source,
            string Name,
            string StreamingPath,
            string PersistentPath,
            long ExpectedLocalBytes,
            long ExpectedPersistentBytes,
            long DownloadBytes,
            bool Keep)>();

        foreach (var change in videoChanges)
        {
            var video = change.Video;
            string streamingPath = Path.Combine(catalog.VideoDirectory, video.Name);
            string persistentPath = Path.Combine(catalog.PersistentVideoDirectory, video.Name);
            if (!catalog.Videos.Contains(video) || change.Keep == video.IsInstalled
                || (change.RemoveUnusedTraveler && (change.Keep || video.Traveler is null))
                || (!change.Keep && !video.CanRemove && !change.RemoveUnusedTraveler)
                || GetCutsceneFileSize(streamingPath) != video.LocalBytes
                || GetCutsceneFileSize(persistentPath) != video.PersistentBytes)
            {
                throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());
            }

            pendingItems.Add((
                video.Asset,
                catalog.Source,
                video.Name,
                streamingPath,
                persistentPath,
                video.LocalBytes,
                video.PersistentBytes,
                video.DownloadBytes,
                change.Keep));
        }

        foreach (var change in audioChanges)
        {
            var audio = change.Audio;
            string rel = audio.RelativePath.Replace('/', Path.DirectorySeparatorChar);
            string streamingPath = Path.Combine(catalog.AudioDirectory, rel);
            string persistentPath = Path.Combine(catalog.PersistentAudioDirectory, rel);
            if (!catalog.Audios.Contains(audio) || change.Keep == audio.IsInstalled
                || (!change.Keep && !audio.CanRemove)
                || GetCutsceneFileSize(streamingPath) != audio.LocalBytes
                || GetCutsceneFileSize(persistentPath) != audio.PersistentBytes)
            {
                throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());
            }

            pendingItems.Add((
                audio.Asset,
                audio.Source,
                audio.RelativePath,
                streamingPath,
                persistentPath,
                audio.LocalBytes,
                audio.PersistentBytes,
                audio.DownloadBytes,
                change.Keep));
        }

        var restores = pendingItems.Where(item => item.Keep).ToArray();
        if (restores.Length > 0)
        {
            var context = new GameUpdateContext(catalog.GameDirectory, catalog.Scheme, progress, _ => { }, token);
            EnsureFreeSpace(context, restores.Max(item => item.DownloadBytes),
                restores.Sum(item => item.Asset.AssetSize));
        }

        // Complete downloads/restores before removing or stub-locking any working copies.
        var ordered = pendingItems.OrderByDescending(item => item.Keep).ToArray();
        for (int index = 0; index < ordered.Length; index++)
        {
            var item = ordered[index];
            token.ThrowIfCancellationRequested();
            await GameServerOperation.EnsureGameClosedAsync().ConfigureAwait(false);
            EnsureCutsceneInstallationUnchanged(catalog);

            if (GetCutsceneFileSize(item.StreamingPath) != item.ExpectedLocalBytes
                || GetCutsceneFileSize(item.PersistentPath) != item.ExpectedPersistentBytes)
            {
                throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());
            }

            progress.Report(new GameServerConversionProgress(
                string.Format((item.Keep ? "Cutscene_Restoring" : "Cutscene_Removing").GetLocalized(), item.Name),
                ordered.Length, index, 0, 0, null));

            if (item.Keep)
            {
                CutsceneProtectionService.UnlockCleanedAsset(item.StreamingPath, item.PersistentPath, deleteStub: true);
                await RestoreAssetAsync(catalog, item.Asset, item.Source, item.Name, item.StreamingPath, progress, token)
                    .ConfigureAwait(false);
                continue;
            }

            string? intactPath = ResolveInstalledAssetPath(item.StreamingPath, item.PersistentPath, item.Asset.AssetSize);
            if (intactPath is null)
                throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());

            string hash = await HashUtility.Md5FileAsync(intactPath, token).ConfigureAwait(false);
            if (!hash.Equals(item.Asset.AssetHashMd5, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(string.Format("Cutscene_FileChanged".GetLocalized(), item.Name));

            token.ThrowIfCancellationRequested();
            await GameServerOperation.EnsureGameClosedAsync().ConfigureAwait(false);
            ValidateCutsceneDirectory(catalog.VideoDirectory);
            if (GetCutsceneFileSize(item.StreamingPath) != item.ExpectedLocalBytes
                || GetCutsceneFileSize(item.PersistentPath) != item.ExpectedPersistentBytes)
            {
                throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());
            }

            if (useStubLockProtection)
            {
                CutsceneProtectionService.LockCleanedAsset(
                    item.StreamingPath,
                    item.PersistentPath,
                    catalog.PersistentRootDirectory);
            }
            else
            {
                CutsceneProtectionService.UnlockCleanedAsset(item.StreamingPath, item.PersistentPath, deleteStub: true);
            }
        }
    }

    internal async Task<(int LockedCount, int UnlockedCount)> SyncCleanedProtectionLocksAsync(
        CutsceneCatalog catalog, bool lockCleaned, CancellationToken token)
    {
        using var lease = await GameServerOperation.EnterAsync(catalog.GameDirectory, token).ConfigureAwait(false);
        EnsureCutsceneInstallationUnchanged(catalog);

        int lockedCount = 0;
        int unlockedCount = 0;

        IEnumerable<(string StreamingPath, string PersistentPath, bool IsInstalled, long ExpectedSize)> targets =
            catalog.Videos.Select(v => (
                Path.Combine(catalog.VideoDirectory, v.Name),
                Path.Combine(catalog.PersistentVideoDirectory, v.Name),
                v.IsInstalled,
                v.Asset.AssetSize))
            .Concat(catalog.Audios.Select(a =>
            {
                string rel = a.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                return (
                    Path.Combine(catalog.AudioDirectory, rel),
                    Path.Combine(catalog.PersistentAudioDirectory, rel),
                    a.IsInstalled,
                    a.Asset.AssetSize);
            }));

        foreach (var (streamingPath, persistentPath, isInstalled, expectedSize) in targets)
        {
            token.ThrowIfCancellationRequested();
            if (isInstalled)
                continue;

            long sBytes = GetCutsceneFileSize(streamingPath);
            long pBytes = GetCutsceneFileSize(persistentPath);
            if (sBytes > 0 && sBytes != expectedSize)
                continue;
            if (pBytes > 0 && pBytes != expectedSize)
                continue;

            if (lockCleaned)
            {
                CutsceneProtectionService.LockCleanedAsset(streamingPath, persistentPath, catalog.PersistentRootDirectory);
                lockedCount++;
            }
            else if (CutsceneProtectionService.IsStubbedAndLocked(streamingPath)
                || CutsceneProtectionService.IsStubbedAndLocked(persistentPath))
            {
                CutsceneProtectionService.UnlockCleanedAsset(streamingPath, persistentPath, deleteStub: true);
                unlockedCount++;
            }
        }

        return (lockedCount, unlockedCount);
    }

    private async Task RestoreAssetAsync(CutsceneCatalog catalog, AssetProperty asset, SophonBranchInfo source,
        string displayName, string streamingPath, IProgress<GameServerConversionProgress> progress, CancellationToken token)
    {
        string tempParent = Path.GetDirectoryName(streamingPath) ?? catalog.VideoDirectory;
        Directory.CreateDirectory(tempParent);
        string temp = Path.Combine(tempParent, $".fufucutscene-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var context = new GameUpdateContext(catalog.GameDirectory, catalog.Scheme, progress, _ => { }, token,
                chunksDirectory: temp);
            var op = GameUpdateAssetOperation.Add(source.ChunkPrefix, source.ChunkSuffix, asset);
            await DownloadOperationChunksAsync(context, [op], "GameServer_StageDownloadChunks".GetLocalized())
                .ConfigureAwait(false);

            await GameServerOperation.EnsureGameClosedAsync().ConfigureAwait(false);
            EnsureCutsceneInstallationUnchanged(catalog);
            CutsceneProtectionService.UnlockFile(streamingPath, deleteFile: true);
            if (!await MergeNewAssetAsync(context, asset).ConfigureAwait(false))
                throw new IOException(string.Format("Cutscene_RestoreFailed".GetLocalized(), displayName));
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                ValidateExistingDirectoryChain(temp);
                Directory.Delete(temp, recursive: true);
            }
        }
    }

    private static List<CutsceneAudioAsset> BuildTracedAudioAssets(CutsceneCatalog catalog,
        IReadOnlyList<CutsceneAsset> videos, CutsceneNameCatalog names, CancellationToken token)
    {
        Version.TryParse(catalog.Version, out Version? installedVersion);
        var groupsByPackage = GetVoiceGroupsByPackage(names);
        var videosByName = videos.ToLookup(video => CutsceneNames.CanonicalSceneKey(video.Name),
            StringComparer.OrdinalIgnoreCase);
        var videosByChapter = videos.Where(video => video.Chapter.QuestChapterId > 0)
            .ToLookup(video => video.Chapter.QuestChapterId);
        var result = new List<CutsceneAudioAsset>();

        foreach (var (asset, source, languageFolder, relPath, labelKey) in catalog.VoiceAssets)
        {
            token.ThrowIfCancellationRequested();
            if (!groupsByPackage.Contains(relPath))
                continue;

            var groups = groupsByPackage[relPath];
            var (filenames, chapters) = ResolveVoiceLinks(groups, names);
            var matchingVideos = filenames.SelectMany(name => videosByName[CutsceneNames.CanonicalSceneKey(name)])
                .DistinctBy(video => video.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            string[] linkedVideoNames = matchingVideos.Select(video => video.Name).ToArray();
            bool protectedVideo = matchingVideos.Any(video => !video.CanRemove)
                || chapters.SelectMany(chapter => videosByChapter[chapter.QuestChapterId]).Any(video => !video.CanRemove);
            bool currentChapter = chapters.Any(chapter =>
                installedVersion is not null && chapter.ReleaseVersion is Version release
                && (release.Major > installedVersion.Major
                    || (release.Major == installedVersion.Major && release.Minor >= installedVersion.Minor)));

            // The fifth and sixth acts can be cleaned in the 7.2 release, by exact pack identity.
            bool removableChapterVoice = catalog.Version == "7.2.0"
                && (relPath.Equals($"{languageFolder}/1049.pck", StringComparison.OrdinalIgnoreCase)
                    || relPath.Equals($"{languageFolder}/1050.pck", StringComparison.OrdinalIgnoreCase));
            CutsceneRemovalProtection protection = CutsceneRemovalProtection.None;
            if (!removableChapterVoice)
            {
                protection = chapters.Count == 0 && matchingVideos.Length == 0
                    ? CutsceneRemovalProtection.Unclassified
                    : GetHistoricalProtection(catalog.EarlierAudioHashes, relPath);
                if (protection == CutsceneRemovalProtection.None)
                {
                    if (currentChapter)
                        protection = CutsceneRemovalProtection.CurrentChapter;
                    else if (protectedVideo)
                        protection = CutsceneRemovalProtection.LinkedVideo;
                }
            }

            string osRelPath = relPath.Replace('/', Path.DirectorySeparatorChar);
            string streamingPath = Path.Combine(catalog.AudioDirectory, osRelPath);
            string persistentPath = Path.Combine(catalog.PersistentAudioDirectory, osRelPath);
            result.Add(new CutsceneAudioAsset(
                asset,
                source,
                languageFolder,
                labelKey,
                relPath,
                linkedVideoNames,
                chapters.Count > 0 ? chapters : [CutsceneChapter.UnclassifiedVoice],
                filenames.Count == 0 && chapters.Count > 0 && groups.Any(group => group.Quests.Count > 0),
                protection,
                GetCutsceneFileSize(streamingPath),
                GetCutsceneFileSize(persistentPath),
                CutsceneProtectionService.GetLocalState(streamingPath, persistentPath, asset.AssetSize)));
        }

        return result;
    }

    private static ILookup<string, CutsceneVoiceGroup> GetVoiceGroupsByPackage(CutsceneNameCatalog names) =>
        names.VoiceGroups.Values.SelectMany(group => group.Packages.Select(package => (package, group)))
            .ToLookup(link => link.package, link => link.group, StringComparer.OrdinalIgnoreCase);

    private static (IReadOnlyList<string> Videos, IReadOnlyList<CutsceneChapter> Chapters)
        ResolveVoiceLinks(IEnumerable<CutsceneVoiceGroup> groups, CutsceneNameCatalog names)
    {
        var filenames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chapters = new List<CutsceneChapter>();
        var chapterIds = new HashSet<int>();
        foreach (CutsceneVoiceGroup group in groups)
        {
            foreach (CutsceneVoiceQuestRef quest in group.Quests)
                AddChapter(names.FindChapter(quest));
            foreach (string filename in group.Videos)
            {
                filenames.Add(filename);
                CutsceneChapter? chapter = names.FindByFilename(filename)?.Chapter;
                if (chapter?.Category != CutsceneCategory.Other)
                    AddChapter(chapter);
            }
        }

        return (filenames.ToArray(), chapters);

        void AddChapter(CutsceneChapter? chapter)
        {
            if (chapter is null)
                return;
            if (chapter.QuestChapterId > 0 ? chapterIds.Add(chapter.QuestChapterId) : !chapters.Contains(chapter))
                chapters.Add(chapter);
        }
    }

    private static Dictionary<string, string> GetInstalledVoiceFolders(string audioDir, string persistentAudioDir)
    {
        var folders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (folder, _, matchingField) in VoiceLanguages)
        {
            string streaming = Path.Combine(audioDir, folder);
            string persistent = Path.Combine(persistentAudioDir, folder);
            ValidateExistingDirectoryChain(streaming);
            ValidateExistingDirectoryChain(persistent);
            if (HasVoicePackages(streaming) || HasVoicePackages(persistent))
            {
                folders.Add(matchingField, folder);
            }
        }
        return folders;

        static bool HasVoicePackages(string path) =>
            Directory.Exists(path) && Directory.EnumerateFiles(path, "*.pck").Any();
    }

    private async Task<(SophonBranchInfo Source, SophonManifestProto Manifest, CutsceneVoiceManifestAsset[] Voices)>
        LoadCutsceneManifestsAsync(GameServerScheme scheme, string version, string audioPrefix,
            IReadOnlyDictionary<string, string> voiceFolders, CancellationToken token)
    {
        var (source, languageSources) = await _sophonBuildClient
            .GetBranchManifestSourcesByTagAsync(scheme, version, voiceFolders.Keys, token).ConfigureAwait(false);
        Task<SophonManifestProto> game = _sophonBuildClient.DownloadManifestAsync(source, token);
        var languages = Task.WhenAll(languageSources.Select(async pair =>
        {
            SophonManifestProto language = await _sophonBuildClient.DownloadManifestAsync(pair.Value, token)
                .ConfigureAwait(false);
            return (Source: pair.Value, Manifest: language, Folder: voiceFolders[pair.Key]);
        }));
        await Task.WhenAll(game, languages).ConfigureAwait(false);

        var voices = new Dictionary<string, CutsceneVoiceManifestAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in await languages.ConfigureAwait(false))
            AddVoiceAssets(language.Manifest, language.Source, language.Folder);

        // Some builds can include language assets in the main manifest as well.
        SophonManifestProto manifest = await game.ConfigureAwait(false);
        AddVoiceAssets(manifest, source, null);
        return (source, manifest, voices.Values.ToArray());

        void AddVoiceAssets(SophonManifestProto candidate, SophonBranchInfo candidateSource, string? languageFolder)
        {
            foreach (AssetProperty asset in candidate.Assets)
            {
                token.ThrowIfCancellationRequested();
                if (IsPackagedVoiceAudio(asset, audioPrefix, out string folder, out string path, out string label)
                    && (languageFolder is null
                        ? voiceFolders.Values.Contains(folder, StringComparer.OrdinalIgnoreCase)
                        : string.Equals(languageFolder, folder, StringComparison.OrdinalIgnoreCase)))
                {
                    voices.TryAdd(path, new CutsceneVoiceManifestAsset(asset, candidateSource, folder, path, label));
                }
            }
        }
    }

    private async Task<(string? VersionTag, Dictionary<string, string>? Videos, Dictionary<string, string>? Audios)>
        LoadEarlierResourcesAsync(
            GameServerScheme scheme,
            string version,
            SophonBranchPayload? branchPayload,
            string videoPrefix,
            string audioPrefix,
            IReadOnlyDictionary<string, string> voiceFolders,
            CancellationToken token)
    {
        if (!Version.TryParse(version, out Version? installed))
            return (null, null, null);

        var candidates = new List<(string Tag, Version Parsed)>();
        if (branchPayload?.DiffTags is { Count: > 0 })
        {
            foreach (string diffTag in branchPayload.DiffTags)
            {
                if (Version.TryParse(diffTag, out Version? diffVer) && diffVer < installed)
                    candidates.Add((diffTag, diffVer));
            }
        }

        var orderedTags = candidates
            .OrderByDescending(item => item.Parsed)
            .Select(item => item.Tag)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        void AddFallbackTag(string candidate)
        {
            if (!orderedTags.Contains(candidate, StringComparer.Ordinal))
                orderedTags.Add(candidate);
        }

        if (installed.Build > 0)
        {
            AddFallbackTag($"{installed.Major}.{installed.Minor}.{installed.Build - 1}");
        }
        else if (installed.Minor > 0)
        {
            AddFallbackTag($"{installed.Major}.{installed.Minor - 1}.0");
        }
        else if (installed.Major > 1)
        {
            for (int minor = 8; minor >= 0; minor--)
                AddFallbackTag($"{installed.Major - 1}.{minor}.0");
        }

        foreach (string tag in orderedTags)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var (_, manifest, voices) =
                    await LoadCutsceneManifestsAsync(scheme, tag, audioPrefix, voiceFolders, token).ConfigureAwait(false);

                var earlierVideos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var earlierAudios = voices.ToDictionary(voice => voice.RelativePath, voice => voice.Asset.AssetHashMd5,
                    StringComparer.OrdinalIgnoreCase);
                foreach (AssetProperty asset in manifest.Assets)
                {
                    if (IsPackagedCutscene(asset, videoPrefix))
                    {
                        earlierVideos[Path.GetFileName(asset.AssetName)] = asset.AssetHashMd5;
                    }
                }
                return (tag, earlierVideos, earlierAudios);
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
            }
        }

        return (null, null, null);
    }

    private static bool IsPackagedCutscene(AssetProperty asset, string prefix)
    {
        string path = asset.AssetName.Replace('\\', '/');
        if (asset.AssetSize <= 0 || asset.AssetHashMd5.Length != 32 || asset.AssetChunks.Count == 0
            || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string name = path[prefix.Length..];
        return name.EndsWith(".usm", StringComparison.OrdinalIgnoreCase)
            && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }

    private static bool IsPackagedVoiceAudio(AssetProperty asset, string audioPrefix,
        out string languageFolder, out string relativePath, out string labelKey)
    {
        languageFolder = string.Empty;
        relativePath = string.Empty;
        labelKey = string.Empty;

        string path = asset.AssetName.Replace('\\', '/');
        if (asset.AssetSize <= 0 || asset.AssetHashMd5.Length != 32 || asset.AssetChunks.Count == 0
            || !path.StartsWith(audioPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string rest = path[audioPrefix.Length..];
        int slash = rest.IndexOf('/');
        if (slash <= 0 || slash == rest.Length - 1)
            return false;

        string folder = rest[..slash];
        string file = rest[(slash + 1)..];
        if (!VoiceLanguageKeyMap.TryGetValue(folder, out string? key)
            || !file.EndsWith(".pck", StringComparison.OrdinalIgnoreCase)
            || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        languageFolder = folder;
        relativePath = $"{folder}/{file}";
        labelKey = key;
        return true;
    }

    private static string? ResolveInstalledAssetPath(string streamingPath, string persistentPath, long expectedSize)
    {
        if (GetCutsceneFileSize(streamingPath) == expectedSize)
            return streamingPath;
        if (GetCutsceneFileSize(persistentPath) == expectedSize)
            return persistentPath;
        return null;
    }

    private void EnsureCutsceneInstallationUnchanged(CutsceneCatalog catalog)
    {
        if (_configurationService.TryGetGameVersion(catalog.GameDirectory) != catalog.Version
            || !catalog.Scheme.Equals(_configurationService.TryDetectCurrentScheme(catalog.GameDirectory)))
            throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());
        ValidateCutsceneDirectory(catalog.VideoDirectory);
    }

    private static void ValidateCutsceneDirectory(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            if (!current.Exists || current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Cutscene_UnsafePath".GetLocalized());
        }
    }

    private static void ValidateExistingDirectoryChain(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Cutscene_UnsafePath".GetLocalized());
        }
    }

    private static long GetCutsceneFileSize(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                throw new IOException("Cutscene_UnsafePath".GetLocalized());
            return new FileInfo(path).Length;
        }
        catch (FileNotFoundException)
        {
            return 0;
        }
        catch (DirectoryNotFoundException)
        {
            return 0;
        }
    }
}
