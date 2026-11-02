/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.Win32.SafeHandles;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;
using ZstdSharp;

namespace FufuLauncher.Services.GameServer;

public sealed partial class GameUpdateService
{
    private async Task UpdateDiffAssetsAsync(GameUpdateContext context, List<GameUpdateAssetOperation> operations)
    {
        List<GameUpdateAssetOperation> mergeOperations = operations
            .Where(operation =>
                operation.Kind is GameUpdateAssetOperationKind.AddOrRepair or GameUpdateAssetOperationKind.Modify)
            .ToList();
        List<GameUpdateAssetOperation> deleteOperations = operations
            .Where(operation => operation.Kind == GameUpdateAssetOperationKind.Delete)
            .ToList();

        if (mergeOperations.Count > 0)
        {
            InitializeDuplicatedChunkNames(context, mergeOperations);
            Directory.CreateDirectory(context.ChunksDir);

            await DownloadOperationChunksAsync(context, mergeOperations,
                "GameServer_StageDownloadChunks".GetLocalized()).ConfigureAwait(false);
            await MergeOperationAssetsAsync(context, mergeOperations).ConfigureAwait(false);
        }

        foreach (var operation in deleteOperations)
        {
            context.Token.ThrowIfCancellationRequested();
            string path = context.GetGameFilePath(operation.OldAsset!.AssetName);
            if (File.Exists(path))
            {
                TryDeleteFile(path);
            }
        }
    }

    private async Task DownloadOperationChunksAsync(GameUpdateContext context,
        List<GameUpdateAssetOperation> operations, string stage, ChunkCacheScan? scan = null)
    {
        var chunkByName = new Dictionary<string, SophonChunk>(StringComparer.Ordinal);
        foreach (var chunk in operations.SelectMany(operation => operation.Chunks))
        {
            chunkByName.TryAdd(chunk.AssetChunk.ChunkName, chunk);
        }

        if (chunkByName.Count == 0)
        {
            return;
        }

        Dictionary<string, long> blocks = GetDownloadBlocks([], operations);
        ChunkCacheScan cacheScan = scan ?? await ScanChunkCacheAsync(context, blocks).ConfigureAwait(false);
        context.Progress.Report(new GameServerConversionProgress(stage, blocks.Count, cacheScan.CachedNames.Count,
            cacheScan.TotalBytes, cacheScan.CachedBytes, null, cacheScan.PendingNames));

        long doneChunks = cacheScan.CachedNames.Count;
        long doneBytes = cacheScan.CachedBytes;
        Action<long>? onBytesTransferred = context.DownloadMonitor is null ? null : context.DownloadMonitor.AddBytes;

        await Parallel.ForEachAsync(cacheScan.PendingNames, context.ParallelOptions, async (name, token) =>
        {
            await _chunkDownloader
                .DownloadChunkAsync(chunkByName[name], context.ChunksDir, context.ChunkLocks, token,
                    onBytesTransferred)
                .ConfigureAwait(false);
            context.ValidatedChunks.TryAdd(name, 0);

            long current = Interlocked.Increment(ref doneChunks);
            long bytes = Interlocked.Add(ref doneBytes, blocks[name]);
            context.Progress.Report(new GameServerConversionProgress(stage, blocks.Count, (int)current,
                cacheScan.TotalBytes, bytes, name));
        }).ConfigureAwait(false);
    }

    private async Task MergeOperationAssetsAsync(GameUpdateContext context, List<GameUpdateAssetOperation> operations)
    {
        string stage = "GameUpdate_StageMerge".GetLocalized();
        int total = operations.Count;
        context.Progress.Report(GameServerConversionProgress.Reset(stage, total, 0));

        for (int i = 0; i < operations.Count; i++)
        {
            context.Token.ThrowIfCancellationRequested();
            GameUpdateAssetOperation operation = operations[i];
            bool merged = operation.Kind switch
            {
                GameUpdateAssetOperationKind.AddOrRepair => await MergeNewAssetAsync(context, operation.NewAsset!)
                    .ConfigureAwait(false),
                GameUpdateAssetOperationKind.Modify => await MergeDiffAssetAsync(context, operation)
                    .ConfigureAwait(false),
                _ => true,
            };

            context.Progress.Report(new GameServerConversionProgress(
                string.Format("GameUpdate_MergingFile".GetLocalized(), i + 1, total, operation.NewAsset!.AssetName),
                total, i + 1, 0, 0, null));

            if (!merged)
            {
                context.Log(string.Format("GameUpdate_SkipAsset".GetLocalized(), operation.NewAsset!.AssetName));
            }
        }
    }

    private async Task<bool> MergeNewAssetAsync(GameUpdateContext context, AssetProperty asset)
    {
        string targetPath = context.GetGameFilePath(asset.AssetName);
        string? targetDirectory = Path.GetDirectoryName(targetPath);
        if (targetDirectory is null)
        {
            return false;
        }

        Directory.CreateDirectory(targetDirectory);

        string tempPath = GetTemporaryAssetPath(targetPath);
        try
        {
            int missingChunks = 0;
            using (SafeFileHandle fileHandle = File.OpenHandle(tempPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, preallocationSize: asset.AssetSize))
            {
                RandomAccess.SetLength(fileHandle, asset.AssetSize);
                await Parallel.ForEachAsync(asset.AssetChunks, context.ParallelOptions, async (chunk, token) =>
                {
                    string chunkPath = context.GetChunkFilePath(chunk.ChunkName);
                    if (!File.Exists(chunkPath))
                    {
                        Interlocked.Increment(ref missingChunks);
                        return;
                    }

                    using (await context.ChunkLocks.LockAsync(chunk.ChunkName, token).ConfigureAwait(false))
                    {
                        byte[] buffer = new byte[ChunkDownloader.BufferSize];
                        long offset = chunk.ChunkOnFileOffset;

                        using (FileStream chunkFile = new(chunkPath, FileMode.Open, FileAccess.Read,
                                   FileShare.ReadWrite))
                        {
                            using var decompressor = new DecompressionStream(chunkFile);
                            while (true)
                            {
                                int read = await decompressor.ReadAsync(buffer, token).ConfigureAwait(false);
                                if (read <= 0)
                                {
                                    break;
                                }

                                await RandomAccess.WriteAsync(fileHandle, buffer.AsMemory(0, read), offset, token)
                                    .ConfigureAwait(false);
                                offset += read;
                            }
                        }
                    }
                }).ConfigureAwait(false);
            }

            if (missingChunks > 0)
            {
                return false;
            }

            return await CommitAssetAsync(context, tempPath, targetPath, asset.AssetSize, asset.AssetHashMd5)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ZstdException)
        {
            context.Log($"{asset.AssetName}: {ex.Message}");
            return false;
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private async Task<bool> MergeDiffAssetAsync(GameUpdateContext context, GameUpdateAssetOperation operation)
    {
        AssetProperty oldAsset = operation.OldAsset!;
        AssetProperty newAsset = operation.NewAsset!;

        string oldAssetPath = context.GetGameFilePath(oldAsset.AssetName);
        if (!File.Exists(oldAssetPath))
        {
            return false;
        }

        string targetPath = context.GetGameFilePath(newAsset.AssetName);
        string? targetDirectory = Path.GetDirectoryName(targetPath);
        if (targetDirectory is null)
        {
            return false;
        }

        Directory.CreateDirectory(targetDirectory);

        string tempPath = GetTemporaryAssetPath(targetPath);
        try
        {
            byte[] buffer = new byte[ChunkDownloader.BufferSize];
            using (var targetFile = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                targetFile.SetLength(newAsset.AssetSize);

                foreach (var chunk in newAsset.AssetChunks)
                {
                    context.Token.ThrowIfCancellationRequested();
                    targetFile.Seek(chunk.ChunkOnFileOffset, SeekOrigin.Begin);
                    long remaining = chunk.ChunkSizeDecompressed;

                    var oldChunk = oldAsset.AssetChunks.FirstOrDefault(candidate =>
                        (candidate.ChunkDecompressedHashMd5 ?? string.Empty).Equals(
                            chunk.ChunkDecompressedHashMd5 ?? string.Empty, StringComparison.OrdinalIgnoreCase));

                    if (oldChunk is not null)
                    {
                        using (var localFile =
                               new FileStream(oldAssetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            localFile.Seek(oldChunk.ChunkOnFileOffset, SeekOrigin.Begin);
                            while (remaining > 0)
                            {
                                int read = await localFile
                                    .ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                                        context.Token)
                                    .ConfigureAwait(false);
                                if (read <= 0)
                                {
                                    return false;
                                }

                                await targetFile.WriteAsync(buffer.AsMemory(0, read), context.Token)
                                    .ConfigureAwait(false);
                                remaining -= read;
                            }
                        }
                    }
                    else
                    {
                        string chunkPath = context.GetChunkFilePath(chunk.ChunkName);
                        if (!File.Exists(chunkPath))
                        {
                            return false;
                        }

                        using (await context.ChunkLocks.LockAsync(chunk.ChunkName, context.Token).ConfigureAwait(false))
                        {
                            using (FileStream chunkFile = new(chunkPath, FileMode.Open, FileAccess.Read,
                                       FileShare.ReadWrite))
                            {
                                using var decompressor = new DecompressionStream(chunkFile);
                                while (remaining > 0)
                                {
                                    int read = await decompressor
                                        .ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)),
                                            context.Token).ConfigureAwait(false);
                                    if (read <= 0)
                                    {
                                        return false;
                                    }

                                    await targetFile.WriteAsync(buffer.AsMemory(0, read), context.Token)
                                        .ConfigureAwait(false);
                                    remaining -= read;
                                }
                            }
                        }
                    }
                }
            }

            return await CommitAssetAsync(context, tempPath, targetPath, newAsset.AssetSize, newAsset.AssetHashMd5)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ZstdException)
        {
            context.Log($"{newAsset.AssetName}: {ex.Message}");
            return false;
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }
}