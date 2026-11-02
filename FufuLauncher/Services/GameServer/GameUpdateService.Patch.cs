/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Helpers.Patch;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;
using ZstdSharp;

namespace FufuLauncher.Services.GameServer;

public sealed partial class GameUpdateService
{
    private async Task<SophonDecodedPatchBuild?> TryDecodePatchBuildAsync(GameServerScheme scheme,
        SophonBranchPayload payload,
        string localVersion, CancellationToken token)
    {
        if (!payload.DiffTags.Contains(localVersion, StringComparer.Ordinal))
        {
            return null;
        }

        SophonPatchBuildResponse? response;
        try
        {
            response = await _sophonBuildClient.GetPatchBuildAsync(scheme, payload, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        if (response is null || response.Manifests.Count == 0)
        {
            return null;
        }

        if (!string.Equals(response.Tag, payload.Tag, StringComparison.Ordinal))
        {
            return null;
        }

        var manifests = new List<SophonDecodedPatchManifest>();
        long downloadTotalBytes = 0;
        long downloadFileCount = 0;
        long uncompressedTotalBytes = 0;
        long installFileCount = 0;

        foreach (var item in response.Manifests)
        {
            if (!string.Equals(item.MatchingField, "game", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (item.DiffDownload is null || item.ManifestDownload is null ||
                !item.Stats.TryGetValue(localVersion, out var stats))
            {
                return null;
            }

            string manifestUrl =
                $"{item.ManifestDownload.UrlPrefix}/{item.Manifest.Id}{item.ManifestDownload.UrlSuffix ?? string.Empty}";
            try
            {
                PatchManifest data = await _sophonBuildClient
                    .DownloadPatchManifestAsync(manifestUrl, item.Manifest.Checksum, token).ConfigureAwait(false);

                downloadTotalBytes += stats.CompressedSize;
                downloadFileCount += stats.ChunkCount;
                uncompressedTotalBytes += stats.UncompressedSize;
                installFileCount += stats.FileCount;
                manifests.Add(new SophonDecodedPatchManifest(localVersion, response.Tag,
                    item.DiffDownload.UrlPrefix, item.DiffDownload.UrlSuffix ?? string.Empty, data));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return null;
            }
        }

        if (manifests.Count == 0)
        {
            return null;
        }

        return new SophonDecodedPatchBuild(localVersion, response.Tag, downloadTotalBytes, downloadFileCount,
            uncompressedTotalBytes, installFileCount, manifests);
    }

    private static List<SophonPatchAsset> EnumeratePatchAssets(SophonDecodedPatchBuild patchBuild)
    {
        var assets = new List<SophonPatchAsset>();
        foreach (var manifest in patchBuild.Manifests)
        {
            foreach (var fileData in manifest.Data.FileDatas)
            {
                PatchEntry? entry = fileData.PatchesEntries.FirstOrDefault(patchEntry =>
                    string.Equals(patchEntry.Key, manifest.OriginalTag, StringComparison.Ordinal));
                if (entry?.PatchInfo is { } patchInfo)
                {
                    assets.Add(new SophonPatchAsset(manifest.UrlPrefix, manifest.UrlSuffix, fileData, patchInfo));
                }
            }
        }

        return assets;
    }

    private async Task PredownloadPatchesAsync(GameUpdateContext context, List<SophonPatchAsset> assets,
        ChunkCacheScan? scan = null)
    {
        var distinct = new Dictionary<string, SophonPatchAsset>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            distinct.TryAdd(asset.PatchInfo.Id, asset);
        }

        if (distinct.Count == 0)
        {
            return;
        }

        var blocks = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var item in distinct)
        {
            blocks[item.Key] = item.Value.PatchInfo.PatchFileSize;
        }

        string stage = "GameServer_StageDownloadChunks".GetLocalized();
        ChunkCacheScan cacheScan = scan ?? await ScanChunkCacheAsync(context, blocks).ConfigureAwait(false);
        context.Progress.Report(new GameServerConversionProgress(stage, blocks.Count, cacheScan.CachedNames.Count,
            cacheScan.TotalBytes, cacheScan.CachedBytes, null, cacheScan.PendingNames));

        long doneChunks = cacheScan.CachedNames.Count;
        long doneBytes = cacheScan.CachedBytes;
        foreach (string name in cacheScan.PendingNames)
        {
            context.Token.ThrowIfCancellationRequested();
            await DownloadPatchAsync(context, distinct[name]).ConfigureAwait(false);
            long current = Interlocked.Increment(ref doneChunks);
            doneBytes += blocks[name];
            context.Progress.Report(new GameServerConversionProgress(stage, blocks.Count, (int)current,
                cacheScan.TotalBytes, doneBytes, name));
        }
    }

    private async Task DownloadPatchAsync(GameUpdateContext context, SophonPatchAsset asset)
    {
        if (context.ValidatedChunks.ContainsKey(asset.PatchInfo.Id))
        {
            return;
        }

        await _chunkDownloader.DownloadBlobAsync(asset.PatchInfo.Id, asset.PatchInfo.PatchFileSize,
            asset.PatchDownloadUrl,
            context.ChunksDir, context.ChunkLocks, context.Token,
            context.DownloadMonitor is null ? null : context.DownloadMonitor.AddBytes).ConfigureAwait(false);

        context.ValidatedChunks.TryAdd(asset.PatchInfo.Id, 0);
    }

    private async Task InstallOrPatchAssetsAsync(GameUpdateContext context, List<SophonPatchAsset> assets)
    {
        if (assets.Count == 0)
        {
            return;
        }

        string stage = "GameUpdate_StageApplyPatches".GetLocalized();
        long totalBytes = assets.Sum(asset => asset.FileData.FileSize);
        context.Progress.Report(GameServerConversionProgress.Reset(stage, assets.Count, totalBytes));

        long doneBytes = 0;
        for (int i = 0; i < assets.Count; i++)
        {
            context.Token.ThrowIfCancellationRequested();
            SophonPatchAsset asset = assets[i];
            bool installed = await InstallOrPatchAssetAsync(context, asset).ConfigureAwait(false);
            if (installed)
            {
                doneBytes += asset.FileData.FileSize;
            }
            else
            {
                context.Log(string.Format("GameUpdate_SkipAsset".GetLocalized(), asset.FileData.FileName));
            }

            context.Progress.Report(new GameServerConversionProgress(
                string.Format("GameUpdate_MergingFile".GetLocalized(), i + 1, assets.Count, asset.FileData.FileName),
                assets.Count, i + 1, totalBytes, doneBytes, null));
        }
    }

    private async Task<bool> InstallOrPatchAssetAsync(GameUpdateContext context, SophonPatchAsset asset)
    {
        PatchFileData fileData = asset.FileData;
        PatchInfo patchInfo = asset.PatchInfo;

        string assetPath = context.GetGameFilePath(fileData.FileName);
        if (File.Exists(assetPath)
            && new FileInfo(assetPath).Length == fileData.FileSize
            && fileData.FileHash.Equals(await HashUtility.Md5FileAsync(assetPath, context.Token).ConfigureAwait(false),
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? assetDirectory = Path.GetDirectoryName(assetPath);
        if (assetDirectory is null)
        {
            return false;
        }

        Directory.CreateDirectory(assetDirectory);
        string tempPath = GetTemporaryAssetPath(assetPath);

        try
        {
            string? oldAssetPath = null;
            if (!string.IsNullOrEmpty(patchInfo.OriginalFileName))
            {
                oldAssetPath = context.GetGameFilePath(patchInfo.OriginalFileName);
                if (!File.Exists(oldAssetPath) || new FileInfo(oldAssetPath).Length != patchInfo.OriginalFileSize ||
                    !patchInfo.OriginalFileHash.Equals(
                        await HashUtility.Md5FileAsync(oldAssetPath, context.Token).ConfigureAwait(false),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            await DownloadPatchAsync(context, asset).ConfigureAwait(false);
            string patchFilePath = context.GetChunkFilePath(patchInfo.Id);
            using (FileStream patchStream = new(patchFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                       ChunkDownloader.BufferSize, FileOptions.RandomAccess))
            {
                if (patchInfo.PatchStartOffset < 0 || patchInfo.PatchLength < 0 ||
                    patchInfo.PatchStartOffset > patchStream.Length ||
                    patchInfo.PatchLength > patchStream.Length - patchInfo.PatchStartOffset)
                {
                    return false;
                }

                if (oldAssetPath is null)
                {
                    using (FileStream target = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        await CopyRangeAsync(patchStream, patchInfo.PatchStartOffset, patchInfo.PatchLength, target,
                            context.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    using (FileStream oldStream = new(oldAssetPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (Stream diffView =
                           FufuPatch.CreateSubStream(patchStream, patchInfo.PatchStartOffset, patchInfo.PatchLength))
                    using (FileStream target = new(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        if (!FufuPatch.MergeZstd(oldStream, diffView, target))
                        {
                            return false;
                        }
                    }
                }
            }

            return await CommitAssetAsync(context, tempPath, assetPath, fileData.FileSize, fileData.FileHash)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or ZstdException)
        {
            context.Log($"{fileData.FileName}: {ex.Message}");
            return false;
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static void DeletePatchDeprecatedFiles(GameUpdateContext context, SophonDecodedPatchBuild patchBuild)
    {
        foreach (var manifest in patchBuild.Manifests)
        {
            PatchDeleteFilesEntry? entry = manifest.Data.DeleteFilesEntries.FirstOrDefault(item =>
                string.Equals(item.Key, manifest.OriginalTag, StringComparison.Ordinal));
            if (entry?.DeleteFiles is null)
            {
                continue;
            }

            foreach (var info in entry.DeleteFiles.Infos)
            {
                context.Token.ThrowIfCancellationRequested();
                string path = context.GetGameFilePath(info.Name);
                if (File.Exists(path))
                {
                    TryDeleteFile(path);
                }
            }
        }
    }

    private static async Task CopyRangeAsync(FileStream source, long offset, long length, FileStream target,
        CancellationToken token)
    {
        byte[] buffer = new byte[ChunkDownloader.BufferSize];
        source.Seek(offset, SeekOrigin.Begin);
        while (length > 0)
        {
            int toRead = (int)Math.Min(length, buffer.Length);
            int read = await source.ReadAsync(buffer.AsMemory(0, toRead), token).ConfigureAwait(false);
            if (read <= 0)
            {
                throw new EndOfStreamException("补丁段数据不完整");
            }

            await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            length -= read;
        }
    }
}