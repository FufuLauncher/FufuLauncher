/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;

namespace FufuLauncher.Services.GameServer;

internal sealed record CutsceneAsset(
    AssetProperty Asset,
    CutsceneChapter Chapter,
    bool CanRemove,
    long LocalBytes,
    long PersistentBytes,
    CutsceneLocalState LocalState,
    CutsceneTraveler? Traveler,
    CutsceneName? Metadata,
    string? VoiceGroupId = null,
    IReadOnlyList<string>? LinkedAudioPackages = null)
{
    public string Name => Path.GetFileName(Asset.AssetName);
    public bool IsInstalled => LocalBytes == Asset.AssetSize || PersistentBytes == Asset.AssetSize;
    public bool IsStubbedLocked => !IsInstalled && LocalState == CutsceneLocalState.StubbedLocked;
    public long DiskBytes => LocalBytes + PersistentBytes;
    public long DownloadBytes => Asset.AssetChunks.Sum(chunk => chunk.ChunkSize);
}

internal sealed record CutsceneAudioAsset(
    AssetProperty Asset,
    string LanguageFolder,
    string LanguageKey,
    string RelativePath,
    CutsceneChapter Chapter,
    string VoiceGroupId,
    IReadOnlyList<string> LinkedVideoNames,
    IReadOnlyList<CutsceneChapter> LinkedChapters,
    bool IsSharedAcrossChapters,
    bool CanRemove,
    long LocalBytes,
    long PersistentBytes,
    CutsceneLocalState LocalState)
{
    public string Name => Path.GetFileName(Asset.AssetName);
    public bool IsInstalled => LocalBytes == Asset.AssetSize || PersistentBytes == Asset.AssetSize;
    public bool IsStubbedLocked => !IsInstalled && LocalState == CutsceneLocalState.StubbedLocked;
    public long DiskBytes => LocalBytes + PersistentBytes;
    public long DownloadBytes => Asset.AssetChunks.Sum(chunk => chunk.ChunkSize);
}

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
    bool HasHistory);

internal sealed record CutsceneChange(CutsceneAsset Video, bool Keep, bool RemoveUnusedTraveler = false);
internal sealed record CutsceneAudioChange(CutsceneAudioAsset Audio, bool Keep);

internal sealed record CutsceneVoicePackage(
    string Name,
    long Bytes,
    long PersistentBytes = 0,
    bool IsStubbedLocked = false,
    string? VoiceGroupId = null,
    string? LinkedSummary = null);

internal sealed record CutsceneVoicePack(string LanguageKey, IReadOnlyList<CutsceneVoicePackage> Packages);

public sealed partial class GameUpdateService
{
    private static readonly (string Directory, string LabelKey)[] VoiceLanguages =
    [
        ("Chinese", "Cutscene_VoiceChinese"),
        ("English(US)", "Cutscene_VoiceEnglish"),
        ("Japanese", "Cutscene_VoiceJapanese"),
        ("Korean", "Cutscene_VoiceKorean")
    ];

    private static readonly Dictionary<string, string> VoiceLanguageKeyMap =
        VoiceLanguages.ToDictionary(item => item.Directory, item => item.LabelKey, StringComparer.OrdinalIgnoreCase);

    internal async Task<CutsceneCatalog> ScanCutscenesAsync(string gameDir, CancellationToken token)
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
        ValidateExistingDirectoryChain(audioDir);
        ValidateExistingDirectoryChain(persistentAudioDir);

        SophonBranchPayload? branchPayload = null;
        try
        {
            branchPayload = await _sophonBuildClient.GetBranchPayloadAsync(scheme, isPreDownload: false, token)
                .ConfigureAwait(false);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
        }

        SophonBranchInfo source = await _sophonBuildClient.GetBranchInfoByTagAsync(scheme, version, token)
            .ConfigureAwait(false);
        SophonManifestProto manifest = await _sophonBuildClient.DownloadManifestAsync(source, token).ConfigureAwait(false);
        var (earlierVersion, earlierVideos, earlierAudios) = await LoadEarlierResourcesAsync(
                scheme, version, branchPayload, videoPrefix, audioPrefix, token)
            .ConfigureAwait(false);

        CutsceneNameCatalog activeCatalog = CutsceneNameCatalogStore.LoadCurrent();
        CutsceneNameCatalog builtInCatalog = CutsceneNames.BuiltIn;
        IReadOnlyDictionary<string, CutsceneVoiceGroup> voiceGroups = activeCatalog.VoiceGroups.Count > 0
            ? activeCatalog.VoiceGroups
            : builtInCatalog.VoiceGroups;

        var videoToGroup = new Dictionary<string, CutsceneVoiceGroup>(StringComparer.OrdinalIgnoreCase);
        var packageToGroup = new Dictionary<string, CutsceneVoiceGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (CutsceneVoiceGroup group in voiceGroups.Values)
        {
            foreach (string vid in group.Videos)
                videoToGroup.TryAdd(vid, group);
            foreach (string pkg in group.Packages)
                packageToGroup.TryAdd(pkg, group);
        }

        AssetProperty[] videoAssets = manifest.Assets.Where(asset => IsPackagedCutscene(asset, videoPrefix)).ToArray();
        var shippedFiles = videoAssets.Select(asset => Path.GetFileName(asset.AssetName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var videos = new List<CutsceneAsset>(videoAssets.Length);
        var videoByName = new Dictionary<string, CutsceneAsset>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetProperty asset in videoAssets)
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(asset.AssetName);
            CutsceneTraveler? traveler = CutsceneTravelers.FindVariant(name, shippedFiles);
            CutsceneName? metadata = builtInCatalog.Find(name, traveler);
            CutsceneChapter chapter = metadata?.Chapter ?? CutsceneChapter.FromFileName(name);
            bool canRemove = metadata?.AllowRemoval == true
                || metadata?.AllowRemovalInVersion == version
                || (chapter.Category != CutsceneCategory.Other && earlierVideos is not null
                    && earlierVideos.TryGetValue(name, out string? hash)
                    && hash.Equals(asset.AssetHashMd5, StringComparison.OrdinalIgnoreCase));

            string streamingPath = Path.Combine(videoDir, name);
            string persistentPath = Path.Combine(persistentVideoDir, name);
            long localBytes = GetCutsceneFileSize(streamingPath);
            long persistentBytes = GetCutsceneFileSize(persistentPath);
            CutsceneLocalState localState = CutsceneProtectionService.GetLocalState(streamingPath, persistentPath, asset.AssetSize);

            videoToGroup.TryGetValue(name, out CutsceneVoiceGroup? linkedGroup);
            var cutscene = new CutsceneAsset(
                asset,
                chapter,
                canRemove,
                localBytes,
                persistentBytes,
                localState,
                traveler,
                metadata,
                linkedGroup?.GroupId,
                linkedGroup?.Packages);
            videos.Add(cutscene);
            videoByName.TryAdd(name, cutscene);
        }

        List<CutsceneAudioAsset> audios = BuildTracedAudioAssets(
            manifest,
            audioPrefix,
            audioDir,
            persistentAudioDir,
            version,
            earlierAudios,
            packageToGroup,
            videoByName,
            activeCatalog,
            builtInCatalog,
            token);

        return new CutsceneCatalog(
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
            audios,
            earlierVideos is not null);
    }

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

    internal IReadOnlyList<CutsceneVoicePack> ScanLocalVoicePacks(string gameDir, CancellationToken token)
    {
        gameDir = Path.GetFullPath(gameDir);
        GameServerScheme scheme = _configurationService.TryDetectCurrentScheme(gameDir)
            ?? throw new InvalidOperationException("Err_GamePathNotFound".GetLocalized());
        string dataDir = scheme.IsOversea ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR;
        string streamingRoot = Path.Combine(gameDir, dataDir, "StreamingAssets", "AudioAssets");
        string persistentRoot = Path.Combine(gameDir, dataDir, "Persistent", "AudioAssets");

        CutsceneNameCatalog activeCatalog = CutsceneNameCatalogStore.LoadCurrent();
        CutsceneNameCatalog builtInCatalog = CutsceneNames.BuiltIn;
        IReadOnlyDictionary<string, CutsceneVoiceGroup> voiceGroups = activeCatalog.VoiceGroups.Count > 0
            ? activeCatalog.VoiceGroups
            : builtInCatalog.VoiceGroups;
        var packageToGroup = new Dictionary<string, CutsceneVoiceGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (CutsceneVoiceGroup group in voiceGroups.Values)
        {
            foreach (string pkg in group.Packages)
                packageToGroup.TryAdd(pkg, group);
        }

        var packs = new List<CutsceneVoicePack>();
        foreach (var (folder, labelKey) in VoiceLanguages)
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

                string relPath = $"{folder}/{name}";
                packageToGroup.TryGetValue(relPath, out CutsceneVoiceGroup? group);
                string? linkedSummary = null;
                if (group is not null)
                {
                    CutsceneChapter? linkedChapter = ResolvePrimaryChapter(group, null, activeCatalog, builtInCatalog);
                    linkedSummary = linkedChapter is not null
                        ? linkedChapter.Title
                        : string.Format("Cutscene_VoiceGroupLabel".GetLocalized(), group.GroupId);
                }

                packages.Add(new CutsceneVoicePackage(name, sBytes, pBytes, locked, group?.GroupId, linkedSummary));
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
                await RestoreAssetAsync(catalog, item.Asset, item.Name, item.StreamingPath, progress, token)
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

    private async Task RestoreAssetAsync(CutsceneCatalog catalog, AssetProperty asset, string displayName,
        string streamingPath, IProgress<GameServerConversionProgress> progress, CancellationToken token)
    {
        string tempParent = Path.GetDirectoryName(streamingPath) ?? catalog.VideoDirectory;
        Directory.CreateDirectory(tempParent);
        string temp = Path.Combine(tempParent, $".fufucutscene-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var context = new GameUpdateContext(catalog.GameDirectory, catalog.Scheme, progress, _ => { }, token,
                chunksDirectory: temp);
            var op = GameUpdateAssetOperation.Add(catalog.Source.ChunkPrefix, catalog.Source.ChunkSuffix, asset);
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

    private List<CutsceneAudioAsset> BuildTracedAudioAssets(
        SophonManifestProto manifest,
        string audioPrefix,
        string audioDir,
        string persistentAudioDir,
        string currentVersion,
        Dictionary<string, string>? earlierAudios,
        Dictionary<string, CutsceneVoiceGroup> packageToGroup,
        Dictionary<string, CutsceneAsset> videoByName,
        CutsceneNameCatalog activeCatalog,
        CutsceneNameCatalog builtInCatalog,
        CancellationToken token)
    {
        Version.TryParse(currentVersion, out Version? currentVer);
        var fallbackChapterCache = new Dictionary<string, CutsceneChapter>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CutsceneAudioAsset>();

        foreach (AssetProperty asset in manifest.Assets)
        {
            token.ThrowIfCancellationRequested();
            if (!IsPackagedVoiceAudio(asset, audioPrefix, out string languageFolder, out string relPath, out string labelKey))
                continue;

            if (!packageToGroup.TryGetValue(relPath, out CutsceneVoiceGroup? group))
                continue;

            var linkedVideos = new List<string>();
            var linkedChapters = new List<CutsceneChapter>();
            bool hasProtectedLinkedVideo = false;
            bool hasProtectedBuiltInChapter = false;

            foreach (string vidName in group.Videos)
            {
                if (videoByName.TryGetValue(vidName, out CutsceneAsset? cutscene))
                {
                    linkedVideos.Add(cutscene.Name);
                    if (!cutscene.CanRemove)
                        hasProtectedLinkedVideo = true;
                    AddDistinctChapter(linkedChapters, activeCatalog.Find(cutscene.Name, cutscene.Traveler)?.Chapter ?? cutscene.Chapter);
                }
                else
                {
                    linkedVideos.Add(vidName);
                    CutsceneName? builtInMeta = builtInCatalog.FindByFilename(vidName);
                    if (builtInMeta is not null)
                    {
                        CutsceneChapter displayCh = activeCatalog.FindByFilename(vidName)?.Chapter ?? builtInMeta.Chapter;
                        AddDistinctChapter(linkedChapters, displayCh);
                        if (IsChapterProtectedInVersion(builtInMeta, currentVersion, currentVer))
                            hasProtectedBuiltInChapter = true;
                    }
                }
            }

            foreach (CutsceneVoiceQuestRef quest in group.Quests)
            {
                if (quest.ChapterId <= 0)
                    continue;
                CutsceneChapter? chapter = activeCatalog.FindChapterByQuestChapterId(quest.ChapterId)
                    ?? builtInCatalog.FindChapterByQuestChapterId(quest.ChapterId);
                if (chapter is not null)
                {
                    AddDistinctChapter(linkedChapters, chapter);
                    CutsceneName? builtInScene = builtInCatalog.Entries.Values
                        .FirstOrDefault(e => e.Chapter.QuestChapterId == quest.ChapterId);
                    if (builtInScene is not null && IsChapterProtectedInVersion(builtInScene, currentVersion, currentVer))
                        hasProtectedBuiltInChapter = true;
                }
            }

            CutsceneChapter primaryChapter;
            if (linkedChapters.Count > 0)
            {
                primaryChapter = linkedChapters[0];
            }
            else
            {
                CutsceneChapter synthesized = CutsceneChapter.FromVoiceGroup(group.GroupId, group.Videos, group.Quests);
                string key = $"{synthesized.Category}:{synthesized.QuestChapterId}:{synthesized.QuestId}:{group.GroupId}";
                if (!fallbackChapterCache.TryGetValue(key, out CutsceneChapter? cached))
                {
                    cached = synthesized;
                    fallbackChapterCache[key] = cached;
                }
                primaryChapter = cached;
                linkedChapters.Add(primaryChapter);
            }

            bool hasTraceLink = group.Videos.Count > 0 || group.Quests.Count > 0;
            bool existsUnchangedInEarlier = earlierAudios is not null
                && earlierAudios.TryGetValue(relPath, out string? oldHash)
                && oldHash.Equals(asset.AssetHashMd5, StringComparison.OrdinalIgnoreCase);

            bool canRemove = hasTraceLink
                && existsUnchangedInEarlier
                && !hasProtectedLinkedVideo
                && !hasProtectedBuiltInChapter;

            string osRelPath = relPath.Replace('/', Path.DirectorySeparatorChar);
            string streamingPath = Path.Combine(audioDir, osRelPath);
            string persistentPath = Path.Combine(persistentAudioDir, osRelPath);
            long localBytes = GetCutsceneFileSize(streamingPath);
            long persistentBytes = GetCutsceneFileSize(persistentPath);
            CutsceneLocalState localState = CutsceneProtectionService.GetLocalState(streamingPath, persistentPath, asset.AssetSize);

            result.Add(new CutsceneAudioAsset(
                asset,
                languageFolder,
                labelKey,
                relPath,
                primaryChapter,
                group.GroupId,
                linkedVideos,
                linkedChapters,
                linkedChapters.Count > 1,
                canRemove,
                localBytes,
                persistentBytes,
                localState));
        }

        return result;
    }

    private static bool IsChapterProtectedInVersion(CutsceneName builtInMeta, string currentVersion, Version? currentVer)
    {
        if (builtInMeta.AllowRemoval || builtInMeta.AllowRemovalInVersion == currentVersion)
            return false;

        if (currentVer is not null && builtInMeta.Chapter.ReleaseVersion is Version release)
        {
            if (release.Major > currentVer.Major
                || (release.Major == currentVer.Major && release.Minor >= currentVer.Minor))
                return true;
        }
        return false;
    }

    private static CutsceneChapter? ResolvePrimaryChapter(
        CutsceneVoiceGroup group,
        CutsceneAsset? videoMatch,
        CutsceneNameCatalog activeCatalog,
        CutsceneNameCatalog builtInCatalog)
    {
        if (videoMatch is not null)
            return activeCatalog.Find(videoMatch.Name, videoMatch.Traveler)?.Chapter ?? videoMatch.Chapter;

        foreach (string vid in group.Videos)
        {
            CutsceneName? found = activeCatalog.FindByFilename(vid) ?? builtInCatalog.FindByFilename(vid);
            if (found is not null)
                return found.Chapter;
        }

        foreach (CutsceneVoiceQuestRef q in group.Quests)
        {
            if (q.ChapterId <= 0)
                continue;
            CutsceneChapter? ch = activeCatalog.FindChapterByQuestChapterId(q.ChapterId)
                ?? builtInCatalog.FindChapterByQuestChapterId(q.ChapterId);
            if (ch is not null)
                return ch;
        }
        return null;
    }

    private static void AddDistinctChapter(List<CutsceneChapter> list, CutsceneChapter chapter)
    {
        if (!list.Any(existing => ReferenceEquals(existing, chapter)
                || (existing.QuestChapterId > 0 && existing.QuestChapterId == chapter.QuestChapterId)
                || string.Equals(existing.Title, chapter.Title, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(chapter);
        }
    }

    private async Task<(string? VersionTag, Dictionary<string, string>? Videos, Dictionary<string, string>? Audios)>
        LoadEarlierResourcesAsync(
            GameServerScheme scheme,
            string version,
            SophonBranchPayload? branchPayload,
            string videoPrefix,
            string audioPrefix,
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
                SophonBranchInfo source = await _sophonBuildClient.GetBranchInfoByTagAsync(scheme, tag, token)
                    .ConfigureAwait(false);
                SophonManifestProto manifest = await _sophonBuildClient.DownloadManifestAsync(source, token)
                    .ConfigureAwait(false);

                var earlierVideos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var earlierAudios = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (AssetProperty asset in manifest.Assets)
                {
                    if (IsPackagedCutscene(asset, videoPrefix))
                    {
                        earlierVideos[Path.GetFileName(asset.AssetName)] = asset.AssetHashMd5;
                    }
                    else if (IsPackagedVoiceAudio(asset, audioPrefix, out _, out string relPath, out _))
                    {
                        earlierAudios[relPath] = asset.AssetHashMd5;
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
