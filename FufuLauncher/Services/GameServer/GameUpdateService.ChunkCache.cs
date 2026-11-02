/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;

namespace FufuLauncher.Services.GameServer;

public sealed partial class GameUpdateService
{
    private sealed record ChunkCacheScan(
        IReadOnlyList<string> CachedNames,
        IReadOnlyList<string> PendingNames,
        long CachedBytes,
        long TotalBytes)
    {
        public long PendingBytes => TotalBytes - CachedBytes;
    }

    private async Task<ChunkCacheScan> ScanChunkCacheAsync(GameUpdateContext context,
        IReadOnlyDictionary<string, long> blocks)
    {
        if (blocks.Count == 0)
        {
            return new ChunkCacheScan([], [], 0, 0);
        }

        context.Progress.Report(GameServerConversionProgress.Status("GameUpdate_ScanChunks".GetLocalized()));

        var cachedNames = new ConcurrentBag<string>();
        var pendingNames = new ConcurrentBag<string>();
        long cachedBytes = 0;

        await Parallel.ForEachAsync(blocks, context.ParallelOptions, async (block, token) =>
        {
            bool cached = context.ValidatedChunks.ContainsKey(block.Key) ||
                          await _chunkDownloader
                              .IsCachedBlobValidAsync(context.ChunksDir, block.Key, block.Value, token)
                              .ConfigureAwait(false);
            if (cached)
            {
                context.ValidatedChunks.TryAdd(block.Key, 0);
                Interlocked.Add(ref cachedBytes, block.Value);
                cachedNames.Add(block.Key);
                return;
            }

            pendingNames.Add(block.Key);
        }).ConfigureAwait(false);

        ChunkCacheScan scan = new(cachedNames.ToList(), pendingNames.ToList(), cachedBytes, blocks.Values.Sum());
        LogReusedChunks(context, scan);
        return scan;
    }

    private static void LogReusedChunks(GameUpdateContext context, ChunkCacheScan scan)
    {
        if (scan.CachedNames.Count == 0)
        {
            return;
        }

        context.Log(string.Format("GameUpdate_ReuseCachedChunks".GetLocalized(), scan.CachedNames.Count,
            ToSizeString(scan.CachedBytes), scan.PendingNames.Count, ToSizeString(scan.PendingBytes)));
    }

    private static Dictionary<string, long> ToBlockMap(IEnumerable<string> names,
        Dictionary<string, long> blocks)
    {
        var map = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            if (blocks.TryGetValue(name, out long size))
            {
                map[name] = size;
            }
        }

        return map;
    }

    private static bool IsChunkCacheFile(string path)
    {
        string name = Path.GetFileName(path);
        return !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(name, PredownloadStatusFileName, StringComparison.OrdinalIgnoreCase);
    }
}
