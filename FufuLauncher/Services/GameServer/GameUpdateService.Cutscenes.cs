/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;

namespace FufuLauncher.Services.GameServer;

internal sealed record CutsceneAsset(AssetProperty Asset, CutsceneChapter Chapter, bool CanRemove, long LocalBytes,
    CutsceneTraveler? Traveler, CutsceneName? Metadata)
{
    public string Name => Path.GetFileName(Asset.AssetName);
    public bool IsInstalled => LocalBytes == Asset.AssetSize;
    public long DownloadBytes => Asset.AssetChunks.Sum(chunk => chunk.ChunkSize);
}

internal sealed record CutsceneCatalog(string GameDirectory, string VideoDirectory, string Version,
    GameServerScheme Scheme, SophonBranchInfo Source, IReadOnlyList<CutsceneAsset> Videos, bool HasHistory);

internal sealed record CutsceneChange(CutsceneAsset Video, bool Keep, bool RemoveUnusedTraveler = false);

public sealed partial class GameUpdateService
{
    internal async Task<CutsceneCatalog> ScanCutscenesAsync(string gameDir, CancellationToken token)
    {
        gameDir = Path.GetFullPath(gameDir);
        GameServerScheme scheme = _configurationService.TryDetectCurrentScheme(gameDir)
            ?? throw new DirectoryNotFoundException("Err_GamePathNotFound".GetLocalized());
        string version = _configurationService.TryGetGameVersion(gameDir)
            ?? throw new InvalidOperationException("GameUpdate_UnknownLocalVersion".GetLocalized());
        string dataDir = scheme.IsOversea ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR;
        string prefix = $"{dataDir}/StreamingAssets/VideoAssets/StandaloneWindows64/";
        string videoDir = Path.GetFullPath(Path.Combine(gameDir, prefix));
        ValidateCutsceneDirectory(videoDir);

        SophonBranchInfo source = await _sophonBuildClient.GetBranchInfoByTagAsync(scheme, version, token)
            .ConfigureAwait(false);
        SophonManifestProto manifest = await _sophonBuildClient.DownloadManifestAsync(source, token).ConfigureAwait(false);
        Dictionary<string, string>? earlier = await LoadEarlierCutscenesAsync(scheme, version, prefix, token)
            .ConfigureAwait(false);
        AssetProperty[] assets = manifest.Assets.Where(asset => IsPackagedCutscene(asset, prefix)).ToArray();
        var shippedFiles = assets.Select(asset => Path.GetFileName(asset.AssetName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var videos = new List<CutsceneAsset>();
        foreach (AssetProperty asset in assets)
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(asset.AssetName);
            CutsceneTraveler? traveler = CutsceneTravelers.FindVariant(name, shippedFiles);
            CutsceneName? metadata = CutsceneNames.Find(name, traveler);
            CutsceneChapter chapter = metadata?.Chapter ?? CutsceneChapter.FromFileName(name);
            bool canRemove = metadata?.AllowRemoval == true
                || metadata?.AllowRemovalInVersion == version
                || (chapter.Category != CutsceneCategory.Other && earlier is not null
                    && earlier.TryGetValue(name, out string? hash)
                    && hash.Equals(asset.AssetHashMd5, StringComparison.OrdinalIgnoreCase));
            long localBytes = GetCutsceneFileSize(Path.Combine(videoDir, name));
            videos.Add(new CutsceneAsset(asset, chapter, canRemove, localBytes, traveler, metadata));
        }

        return new CutsceneCatalog(gameDir, videoDir, version, scheme, source, videos, earlier is not null);
    }

    internal CutsceneCatalog RefreshCutscenes(CutsceneCatalog catalog)
    {
        EnsureCutsceneInstallationUnchanged(catalog);
        return catalog with
        {
            Videos = catalog.Videos.Select(video => video with
            {
                LocalBytes = GetCutsceneFileSize(Path.Combine(catalog.VideoDirectory, video.Name))
            }).ToArray()
        };
    }

    internal async Task ApplyCutsceneChangesAsync(CutsceneCatalog catalog, IReadOnlyList<CutsceneChange> changes,
        IProgress<GameServerConversionProgress> progress, CancellationToken token)
    {
        using var lease = await GameServerOperation.EnterAsync(catalog.GameDirectory, token).ConfigureAwait(false);
        EnsureCutsceneInstallationUnchanged(catalog);

        if (changes.Where(change => change.RemoveUnusedTraveler).Select(change => change.Video.Traveler).Distinct()
            .Skip(1).Any())
            throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());

        foreach (var change in changes)
        {
            var video = change.Video;
            if (!catalog.Videos.Contains(video) || change.Keep == video.IsInstalled
                || (change.RemoveUnusedTraveler && (change.Keep || video.Traveler is null))
                || (!change.Keep && !video.CanRemove && !change.RemoveUnusedTraveler)
                || GetCutsceneFileSize(Path.Combine(catalog.VideoDirectory, video.Name)) != video.LocalBytes)
                throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());
        }

        CutsceneChange[] restores = changes.Where(change => change.Keep).ToArray();
        if (restores.Length > 0)
        {
            var context = new GameUpdateContext(catalog.GameDirectory, catalog.Scheme, progress, _ => { }, token);
            EnsureFreeSpace(context, restores.Max(change => change.Video.DownloadBytes),
                restores.Sum(change => change.Video.Asset.AssetSize));
        }

        // Complete downloads before removing any working copies.
        CutsceneChange[] ordered = changes.OrderByDescending(change => change.Keep).ToArray();
        for (int index = 0; index < ordered.Length; index++)
        {
            CutsceneChange change = ordered[index];
            token.ThrowIfCancellationRequested();
            await GameServerOperation.EnsureGameClosedAsync().ConfigureAwait(false);
            EnsureCutsceneInstallationUnchanged(catalog);
            var video = change.Video;
            string path = Path.Combine(catalog.VideoDirectory, video.Name);
            if (GetCutsceneFileSize(path) != video.LocalBytes)
                throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());

            progress.Report(new GameServerConversionProgress(
                string.Format((change.Keep ? "Cutscene_Restoring" : "Cutscene_Removing").GetLocalized(), video.Name),
                changes.Count, index, 0, 0, null));

            if (change.Keep)
            {
                await RestoreCutsceneAsync(catalog, video, progress, token).ConfigureAwait(false);
                continue;
            }

            string hash = await HashUtility.Md5FileAsync(path, token).ConfigureAwait(false);
            if (!hash.Equals(video.Asset.AssetHashMd5, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(string.Format("Cutscene_FileChanged".GetLocalized(), video.Name));

            token.ThrowIfCancellationRequested();
            await GameServerOperation.EnsureGameClosedAsync().ConfigureAwait(false);
            ValidateCutsceneDirectory(catalog.VideoDirectory);
            if (GetCutsceneFileSize(path) != video.LocalBytes)
                throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());
            File.Delete(path);
        }
    }

    private async Task RestoreCutsceneAsync(CutsceneCatalog catalog, CutsceneAsset video,
        IProgress<GameServerConversionProgress> progress, CancellationToken token)
    {
        string temp = Path.Combine(catalog.VideoDirectory, $".fufucutscene-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var context = new GameUpdateContext(catalog.GameDirectory, catalog.Scheme, progress, _ => { }, token,
                chunksDirectory: temp);
            var op = GameUpdateAssetOperation.Add(catalog.Source.ChunkPrefix, catalog.Source.ChunkSuffix, video.Asset);
            await DownloadOperationChunksAsync(context, [op], "GameServer_StageDownloadChunks".GetLocalized())
                .ConfigureAwait(false);

            await GameServerOperation.EnsureGameClosedAsync().ConfigureAwait(false);
            EnsureCutsceneInstallationUnchanged(catalog);
            if (GetCutsceneFileSize(Path.Combine(catalog.VideoDirectory, video.Name)) != video.LocalBytes)
                throw new InvalidOperationException("Cutscene_RescanRequired".GetLocalized());
            if (!await MergeNewAssetAsync(context, video.Asset).ConfigureAwait(false))
                throw new IOException(string.Format("Cutscene_RestoreFailed".GetLocalized(), video.Name));
        }
        finally
        {
            ValidateCutsceneDirectory(temp);
            Directory.Delete(temp, recursive: true);
        }
    }

    private async Task<Dictionary<string, string>?> LoadEarlierCutscenesAsync(GameServerScheme scheme,
        string version, string prefix, CancellationToken token)
    {
        if (!Version.TryParse(version, out Version? installed) || installed.Minor == 0 && installed.Build <= 0)
            return null;

        string tag = installed.Build > 0 ? $"{installed.Major}.{installed.Minor}.{installed.Build - 1}"
            : $"{installed.Major}.{installed.Minor - 1}.0";
        try
        {
            SophonBranchInfo source = await _sophonBuildClient.GetBranchInfoByTagAsync(scheme, tag, token)
                .ConfigureAwait(false);
            SophonManifestProto manifest = await _sophonBuildClient.DownloadManifestAsync(source, token)
                .ConfigureAwait(false);
            return manifest.Assets.Where(asset => IsPackagedCutscene(asset, prefix)).ToDictionary(
                asset => Path.GetFileName(asset.AssetName), asset => asset.AssetHashMd5, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception) when (!token.IsCancellationRequested)
        {
            return null;
        }
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
    }
}
