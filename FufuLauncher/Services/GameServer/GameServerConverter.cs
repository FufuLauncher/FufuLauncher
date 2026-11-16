/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using Microsoft.Win32.SafeHandles;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;
using ZstdSharp;

namespace FufuLauncher.Services.GameServer;

public sealed class GameServerConverter
{
    private readonly SophonBuildClient _sophonBuildClient;
    private readonly ChunkDownloader _chunkDownloader;
    private readonly GameServerConfigurationService _configurationService;
    private readonly GameChannelSdkService _gameChannelSdkService;

    public GameServerConverter(SophonBuildClient sophonBuildClient, ChunkDownloader chunkDownloader,
        GameServerConfigurationService configurationService, GameChannelSdkService gameChannelSdkService)
    {
        _sophonBuildClient = sophonBuildClient;
        _chunkDownloader = chunkDownloader;
        _configurationService = configurationService;
        _gameChannelSdkService = gameChannelSdkService;
    }

    #region 公开入口

    public async Task ConvertAsync(string gameDir, GameServerScheme currentScheme, GameServerScheme targetScheme,
        IProgress<GameServerConversionProgress> progress, Action<string> log, CancellationToken token = default,
        GameServerDownloadMonitor? downloadMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(currentScheme);
        ArgumentNullException.ThrowIfNull(targetScheme);


        if (currentScheme.IsOversea == targetScheme.IsOversea)
        {
            await _gameChannelSdkService.SwitchSchemeAsync(gameDir, targetScheme, log, token,
                downloadMonitor is null ? null : downloadMonitor.AddBytes).ConfigureAwait(false);
            return;
        }

        using var operation = await GameServerOperation.EnterAsync(gameDir, token).ConfigureAwait(false);
        EnsureDirectoryPermissions(gameDir);

        var context = new GameServerConversionContext(gameDir, currentScheme, targetScheme, AppPaths.ServerCacheDir,
            progress, log, token, downloadMonitor);

        _configurationService.BackupConfig(gameDir, currentScheme.IsOversea);

        log("GameServer_StageFetchBranches".GetLocalized());
        SophonBranchInfo currentInfo =
            await _sophonBuildClient.GetBranchInfoAsync(currentScheme, false, token).ConfigureAwait(false);
        SophonBranchInfo targetInfo =
            await _sophonBuildClient.GetBranchInfoAsync(targetScheme, false, token).ConfigureAwait(false);

        log("GameServer_StageDecodeManifests".GetLocalized());
        SophonManifestProto currentManifest =
            await _sophonBuildClient.DownloadManifestAsync(currentInfo, token).ConfigureAwait(false);
        SophonManifestProto targetManifest =
            await _sophonBuildClient.DownloadManifestAsync(targetInfo, token).ConfigureAwait(false);

        log("GameServer_StageDiff".GetLocalized());
        List<GameServerAssetOperation> operations = BuildDiffOperations(currentManifest, targetManifest,
            targetInfo.ChunkPrefix, targetInfo.ChunkSuffix, gameDir);

        log("GameServer_StageDownloadChunks".GetLocalized());
        await PrepareCacheFilesAsync(context, operations).ConfigureAwait(false);

        log("GameServer_StageReplace".GetLocalized());
        await ReplaceGameResourceAsync(context, operations, targetScheme).ConfigureAwait(false);

        log("GameServer_StageCleanup".GetLocalized());
        CleanupChunksFolder(context);
    }

    public async Task VerifyAndRepairAsync(string gameDir, GameServerScheme currentScheme,
        IProgress<GameServerConversionProgress> progress, Action<string> log, CancellationToken token = default,
        GameServerDownloadMonitor? downloadMonitor = null)
    {
        ArgumentNullException.ThrowIfNull(currentScheme);

        using var operation = await GameServerOperation.EnterAsync(gameDir, token).ConfigureAwait(false);
        EnsureDirectoryPermissions(gameDir);

        var context = new GameServerConversionContext(gameDir, currentScheme, currentScheme, AppPaths.ServerCacheDir,
            progress, log, token, downloadMonitor);

        log("GameServer_StageFetchBranches".GetLocalized());
        SophonBranchInfo branchInfo =
            await _sophonBuildClient.GetBranchInfoAsync(currentScheme, false, token).ConfigureAwait(false);

        log("GameServer_StageDecodeManifests".GetLocalized());
        SophonManifestProto manifest =
            await _sophonBuildClient.DownloadManifestAsync(branchInfo, token).ConfigureAwait(false);

        await _gameChannelSdkService.VerifyAndRepairChannelSdkAsync(
            context.GameDir, currentScheme, context.Log, context.Token,
            context.DownloadMonitor is null ? null : context.DownloadMonitor.AddBytes).ConfigureAwait(false);

        log("GameServer_StageVerify".GetLocalized());
        List<AssetProperty> brokenAssets = await VerifyAssetsAsync(context, manifest).ConfigureAwait(false);

        if (brokenAssets.Count == 0)
        {
            log("GameServer_VerifyOk".GetLocalized());
            return;
        }

        log(string.Format("GameServer_RepairFound".GetLocalized(), brokenAssets.Count));

        var operations = brokenAssets
            .Select(asset => GameServerAssetOperation.Add(branchInfo.ChunkPrefix, branchInfo.ChunkSuffix, asset))
            .ToList();

        await PrepareCacheFilesAsync(context, operations).ConfigureAwait(false);

        log("GameServer_StageReplace".GetLocalized());
        MovePreparedFilesToGame(context, operations);
        log("GameServer_RepairDone".GetLocalized());

        log("GameServer_StageCleanup".GetLocalized());
        CleanupChunksFolder(context);
    }

    #endregion

    #region 差异计算

    private static List<GameServerAssetOperation> BuildDiffOperations(SophonManifestProto currentManifest,
        SophonManifestProto targetManifest,
        string urlPrefix, string urlSuffix, string gameDir)
    {
        var operations = new List<GameServerAssetOperation>();
        var currentMap = currentManifest.Assets.ToDictionary(asset => NormalizeAssetName(asset.AssetName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var targetAsset in targetManifest.Assets)
        {
            string normalized = NormalizeAssetName(targetAsset.AssetName);

            if (!currentMap.TryGetValue(normalized, out var currentAsset))
            {
                operations.Add(GameServerAssetOperation.Add(urlPrefix, urlSuffix, targetAsset));
                continue;
            }

            bool hasLocalFile = File.Exists(Path.Combine(gameDir, currentAsset.AssetName));

            if ((currentAsset.AssetHashMd5 ?? string.Empty).Equals(targetAsset.AssetHashMd5 ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase)
                && hasLocalFile)
            {
                continue;
            }

            if (hasLocalFile)
            {
                List<SophonChunk> diffChunks = BuildDiffChunks(currentAsset, targetAsset, urlPrefix, urlSuffix);
                operations.Add(GameServerAssetOperation.ModifyOrReplace(urlPrefix, urlSuffix, currentAsset, targetAsset,
                    diffChunks));
            }
            else
            {
                operations.Add(GameServerAssetOperation.Add(urlPrefix, urlSuffix, targetAsset));
            }
        }

        var targetNames = targetManifest.Assets.Select(asset => NormalizeAssetName(asset.AssetName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var currentAsset in currentManifest.Assets)
        {
            if (!targetNames.Contains(NormalizeAssetName(currentAsset.AssetName)))
            {
                operations.Add(GameServerAssetOperation.Backup(currentAsset));
            }
        }

        return operations;
    }

    private static List<SophonChunk> BuildDiffChunks(AssetProperty currentAsset, AssetProperty targetAsset,
        string urlPrefix, string urlSuffix)
    {
        return targetAsset.AssetChunks
            .Where(chunk => currentAsset.AssetChunks.FirstOrDefault(candidate =>
                (candidate.ChunkDecompressedHashMd5 ?? string.Empty).Equals(
                    chunk.ChunkDecompressedHashMd5 ?? string.Empty, StringComparison.OrdinalIgnoreCase)) is null)
            .Select(chunk => new SophonChunk(urlPrefix, urlSuffix, chunk))
            .ToList();
    }

    private static string NormalizeAssetName(string? assetName)
    {
        string name = assetName ?? string.Empty;
        int separatorIndex = name.IndexOf('/');
        return separatorIndex >= 0 ? name[(separatorIndex + 1)..] : name;
    }

    private static void InitializeDuplicatedChunkNames(GameServerConversionContext context,
        IEnumerable<GameServerAssetOperation> operations)
    {
        IEnumerable<string> names = operations
            .SelectMany(operation => operation.Chunks)
            .Select(chunk => chunk.AssetChunk.ChunkName)
            .GroupBy(name => name)
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key);

        foreach (string name in names)
        {
            context.DuplicatedChunkNames.TryAdd(name, 0);
        }
    }

    #endregion

    #region 下载组装

    private async Task PrepareCacheFilesAsync(GameServerConversionContext context,
        List<GameServerAssetOperation> operations)
    {
        var toProcess = operations
            .Where(operation =>
                operation.Kind is GameServerAssetOperationKind.Add or GameServerAssetOperationKind.ModifyOrReplace)
            .ToList();

        if (toProcess.Count == 0)
        {
            return;
        }

        InitializeDuplicatedChunkNames(context, toProcess);
        Directory.CreateDirectory(context.ChunksFolder);

        var pending = new List<(GameServerAssetOperation Operation, List<SophonChunk> Chunks)>();
        foreach (var operation in toProcess)
        {
            context.Token.ThrowIfCancellationRequested();

            AssetProperty newAsset = operation.NewAsset!;
            string cacheFile = context.GetTargetFilePath(newAsset.AssetName);

            if (File.Exists(cacheFile) && new FileInfo(cacheFile).Length == newAsset.AssetSize)
            {
                string cacheMd5 = await HashUtility.Md5FileAsync(cacheFile, context.Token).ConfigureAwait(false);
                if (cacheMd5.Equals(newAsset.AssetHashMd5 ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Delete(cacheFile);
            }

            string? cacheDirectory = Path.GetDirectoryName(cacheFile);
            ArgumentNullException.ThrowIfNull(cacheDirectory);
            Directory.CreateDirectory(cacheDirectory);

            pending.Add((operation, operation.Chunks.ToList()));
        }

        if (pending.Count == 0)
        {
            return;
        }

        string stage = "GameServer_StageDownloadChunks".GetLocalized();
        int totalChunks = pending.Sum(item => item.Chunks.Count);
        long totalBytes = pending.Sum(item => item.Chunks.Sum(chunk => chunk.AssetChunk.ChunkSize));

        var pendingChunkNames = pending
            .SelectMany(item => item.Chunks)
            .Select(chunk => chunk.AssetChunk.ChunkName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        context.Progress.Report(GameServerConversionProgress.Reset(stage, totalChunks, totalBytes, pendingChunkNames));

        var counters = new DownloadCounters();

        foreach (var (operation, chunks) in pending)
        {
            context.Token.ThrowIfCancellationRequested();

            await DownloadChunksAsync(context, chunks, counters, totalChunks, totalBytes, stage).ConfigureAwait(false);

            AssetProperty newAsset = operation.NewAsset!;
            string cacheFile = context.GetTargetFilePath(newAsset.AssetName);

            bool merged = await MergeAssetAsync(context, operation).ConfigureAwait(false);
            if (merged && File.Exists(cacheFile) && new FileInfo(cacheFile).Length == newAsset.AssetSize)
            {
                if (operation.Kind == GameServerAssetOperationKind.ModifyOrReplace
                    && operation.DiffChunks.Count < newAsset.AssetChunks.Count)
                {
                    string mergedMd5 = await HashUtility.Md5FileAsync(cacheFile, context.Token).ConfigureAwait(false);
                    if (!mergedMd5.Equals(newAsset.AssetHashMd5 ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        context.Log(string.Format("GameServer_AssembleVerifyFailed".GetLocalized(),
                            newAsset.AssetName));
                        File.Delete(cacheFile);
                        continue;
                    }
                }

                continue;
            }

            context.Log(string.Format("GameServer_SkipAssetNoCache".GetLocalized(), newAsset.AssetName));
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
            }
        }
    }

    private sealed class DownloadCounters
    {
        public long DoneChunks;
        public long DoneBytes;
    }

    private async Task DownloadChunksAsync(GameServerConversionContext context, IReadOnlyList<SophonChunk> chunks,
        DownloadCounters counters, int totalChunks, long totalBytes, string stage)
    {
        if (chunks.Count == 0)
        {
            return;
        }

        Action<long>? onBytesTransferred = context.DownloadMonitor is null ? null : context.DownloadMonitor.AddBytes;

        await Parallel.ForEachAsync(chunks, context.ParallelOptions, async (chunk, token) =>
        {
            await _chunkDownloader
                .DownloadChunkAsync(chunk, context.ChunksFolder, context.ChunkLocks, token, onBytesTransferred)
                .ConfigureAwait(false);

            long current = Interlocked.Increment(ref counters.DoneChunks);
            long bytes = Interlocked.Add(ref counters.DoneBytes, chunk.AssetChunk.ChunkSize);
            context.Progress.Report(new GameServerConversionProgress(stage, totalChunks, (int)current, totalBytes,
                bytes, chunk.AssetChunk.ChunkName));
        }).ConfigureAwait(false);
    }

    private Task<bool> MergeAssetAsync(GameServerConversionContext context, GameServerAssetOperation operation)
    {
        return operation.Kind switch
        {
            GameServerAssetOperationKind.Add => MergeNewAssetAsync(context, operation.NewAsset!),
            GameServerAssetOperationKind.ModifyOrReplace => MergeDiffAssetAsync(context, operation),
            _ => Task.FromResult(true),
        };
    }

    private async Task<bool> MergeNewAssetAsync(GameServerConversionContext context, AssetProperty asset)
    {
        string targetPath = context.GetTargetFilePath(asset.AssetName);
        string? targetDirectory = Path.GetDirectoryName(targetPath);
        ArgumentNullException.ThrowIfNull(targetDirectory);
        Directory.CreateDirectory(targetDirectory);

        int missingChunks = 0;

        using (SafeFileHandle fileHandle = File.OpenHandle(targetPath, FileMode.Create, FileAccess.Write,
                   FileShare.None, preallocationSize: asset.AssetSize))
        {
            await Parallel.ForEachAsync(asset.AssetChunks, context.ParallelOptions, async (chunk, token) =>
            {
                string chunkPath = Path.Combine(context.ChunksFolder, chunk.ChunkName);
                if (!File.Exists(chunkPath))
                {
                    Interlocked.Increment(ref missingChunks);
                    return;
                }

                using (await context.ChunkLocks.LockAsync(chunk.ChunkName, token).ConfigureAwait(false))
                {
                    byte[] buffer = new byte[ChunkDownloader.BufferSize];
                    long offset = chunk.ChunkOnFileOffset;

                    using (FileStream chunkFile = new(chunkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
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

                if (!context.DuplicatedChunkNames.ContainsKey(chunk.ChunkName))
                {
                    try
                    {
                        File.Delete(chunkPath);
                    }
                    catch
                    {
                    }
                }
            }).ConfigureAwait(false);
        }

        if (missingChunks > 0)
        {
            try
            {
                File.Delete(targetPath);
            }
            catch
            {
            }

            return false;
        }

        return true;
    }

    private async Task<bool> MergeDiffAssetAsync(GameServerConversionContext context,
        GameServerAssetOperation operation)
    {
        AssetProperty oldAsset = operation.OldAsset!;
        AssetProperty newAsset = operation.NewAsset!;

        string oldAssetPath = context.GetGameFilePath(oldAsset.AssetName);
        if (!File.Exists(oldAssetPath))
        {
            return false;
        }

        string targetPath = context.GetTargetFilePath(newAsset.AssetName);
        string? targetDirectory = Path.GetDirectoryName(targetPath);
        ArgumentNullException.ThrowIfNull(targetDirectory);
        Directory.CreateDirectory(targetDirectory);

        byte[] buffer = new byte[ChunkDownloader.BufferSize];

        using (var targetFile = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
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
                                .ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), context.Token)
                                .ConfigureAwait(false);
                            if (read <= 0)
                            {
                                break;
                            }

                            await targetFile.WriteAsync(buffer.AsMemory(0, read), context.Token).ConfigureAwait(false);
                            remaining -= read;
                        }
                    }
                }
                else
                {
                    string chunkPath = Path.Combine(context.ChunksFolder, chunk.ChunkName);
                    if (!File.Exists(chunkPath))
                    {
                        try
                        {
                            File.Delete(targetPath);
                        }
                        catch
                        {
                        }

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
                                    break;
                                }

                                await targetFile.WriteAsync(buffer.AsMemory(0, read), context.Token)
                                    .ConfigureAwait(false);
                                remaining -= read;
                            }
                        }
                    }

                    if (!context.DuplicatedChunkNames.ContainsKey(chunk.ChunkName))
                    {
                        try
                        {
                            File.Delete(chunkPath);
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        return true;
    }

    #endregion

    #region 落盘替换

    private async Task ReplaceGameResourceAsync(GameServerConversionContext context,
        List<GameServerAssetOperation> operations, GameServerScheme targetScheme)
    {
        var orderedOperations = operations.OrderBy(operation => operation.Kind switch
        {
            GameServerAssetOperationKind.Backup => 0,
            GameServerAssetOperationKind.ModifyOrReplace => 1,
            _ => 2,
        }).ToList();

        string stage = "GameServer_StageReplace".GetLocalized();
        int totalFiles = orderedOperations.Count;
        int doneFiles = 0;
        context.Progress.Report(GameServerConversionProgress.Reset(stage, totalFiles, 0));

        foreach (var operation in orderedOperations)
        {
            context.Token.ThrowIfCancellationRequested();

            doneFiles++;
            string? currentFileName = operation.Kind == GameServerAssetOperationKind.Backup
                ? operation.OldAsset?.AssetName
                : operation.NewAsset?.AssetName;
            context.Progress.Report(new GameServerConversionProgress(
                string.Format("GameServer_ReplacingFile".GetLocalized(), doneFiles, totalFiles,
                    currentFileName ?? string.Empty),
                totalFiles, doneFiles, 0, 0, null));

            (bool moveToBackup, bool moveToTarget) = operation.Kind switch
            {
                GameServerAssetOperationKind.Backup => (true, false),
                GameServerAssetOperationKind.ModifyOrReplace => (true, true),
                _ => (false, true),
            };

            if (moveToBackup && operation.OldAsset is { } oldAsset)
            {
                string localPath = context.GetGameFilePath(oldAsset.AssetName);
                if (File.Exists(localPath))
                {
                    string backupPath = context.GetBackupFilePath(oldAsset.AssetName);
                    string? backupDirectory = Path.GetDirectoryName(backupPath);
                    ArgumentNullException.ThrowIfNull(backupDirectory);
                    Directory.CreateDirectory(backupDirectory);

                    File.SetAttributes(localPath, FileAttributes.Normal);
                    MoveFile(localPath, backupPath);
                }
            }

            if (moveToTarget && operation.NewAsset is { } newAsset)
            {
                string cachePath = context.GetTargetFilePath(newAsset.AssetName);
                if (!File.Exists(cachePath))
                {
                    context.Log(string.Format("GameServer_SkipAssetNoCache".GetLocalized(), newAsset.AssetName));
                    continue;
                }

                string targetPath = context.GetGameFilePath(newAsset.AssetName);
                string? targetDirectory = Path.GetDirectoryName(targetPath);
                ArgumentNullException.ThrowIfNull(targetDirectory);
                Directory.CreateDirectory(targetDirectory);

                if (File.Exists(targetPath))
                {
                    File.SetAttributes(targetPath, FileAttributes.Normal);
                    File.Delete(targetPath);
                }

                MoveFile(cachePath, targetPath);
            }
        }

        if (!string.Equals(context.FromDataFolderName, context.ToDataFolderName, StringComparison.OrdinalIgnoreCase))
        {
            string fromDataDir = Path.Combine(context.GameDir, context.FromDataFolderName);
            string toDataDir = Path.Combine(context.GameDir, context.ToDataFolderName);
            if (Directory.Exists(fromDataDir))
            {
                if (Directory.Exists(toDataDir))
                {
                    MergeDirectoryContents(fromDataDir, toDataDir);
                }
                else
                {
                    Directory.Move(fromDataDir, toDataDir);
                }
            }
        }

        string currentExe = Path.Combine(context.GameDir,
            context.CurrentScheme.IsOversea ? GameConstants.OS_EXE : GameConstants.CN_EXE);
        string targetExe = Path.Combine(context.GameDir,
            targetScheme.IsOversea ? GameConstants.OS_EXE : GameConstants.CN_EXE);
        if (!string.Equals(Path.GetFileName(currentExe), Path.GetFileName(targetExe),
                StringComparison.OrdinalIgnoreCase)
            && File.Exists(currentExe)
            && !File.Exists(targetExe))
        {
            File.Move(currentExe, targetExe);
        }

        await _gameChannelSdkService.EnsureSdkAndDeprecatedFilesAsync(
            context.GameDir, targetScheme, context.Log, context.Token,
            context.DownloadMonitor is null ? null : context.DownloadMonitor.AddBytes).ConfigureAwait(false);

        _configurationService.ApplyScheme(context.GameDir, targetScheme);
    }

    private static void MovePreparedFilesToGame(GameServerConversionContext context,
        IEnumerable<GameServerAssetOperation> operations)
    {
        foreach (var operation in operations.Where(operation => operation.NewAsset is not null))
        {
            AssetProperty asset = operation.NewAsset!;
            string cachePath = context.GetTargetFilePath(asset.AssetName);
            if (!File.Exists(cachePath))
            {
                context.Log(string.Format("GameServer_SkipAssetNoCache".GetLocalized(), asset.AssetName));
                continue;
            }

            string destination = context.GetGameFilePath(asset.AssetName);
            string? destinationDirectory = Path.GetDirectoryName(destination);
            ArgumentNullException.ThrowIfNull(destinationDirectory);
            Directory.CreateDirectory(destinationDirectory);

            if (File.Exists(destination))
            {
                File.SetAttributes(destination, FileAttributes.Normal);
                File.Delete(destination);
            }

            MoveFile(cachePath, destination);
        }
    }

    #endregion

    #region 校验

    private async Task<List<AssetProperty>> VerifyAssetsAsync(GameServerConversionContext context,
        SophonManifestProto manifest)
    {
        var brokenAssets = new ConcurrentBag<AssetProperty>();
        int totalAssets = manifest.Assets.Count;
        int doneAssets = 0;
        string stage = "GameServer_StageVerify".GetLocalized();

        await Parallel.ForEachAsync(manifest.Assets, context.ParallelOptions, async (asset, token) =>
        {
            token.ThrowIfCancellationRequested();

            string assetPath = context.GetGameFilePath(asset.AssetName);
            bool isBroken = !File.Exists(assetPath);

            if (!isBroken)
            {
                try
                {
                    using SafeFileHandle fileHandle = File.OpenHandle(assetPath, FileMode.Open, FileAccess.Read,
                        FileShare.ReadWrite, FileOptions.RandomAccess);

                    foreach (var chunk in asset.AssetChunks)
                    {
                        token.ThrowIfCancellationRequested();

                        byte[] buffer = new byte[checked((int)chunk.ChunkSizeDecompressed)];
                        await ReadExactlyAsync(fileHandle, buffer, chunk.ChunkOnFileOffset, token)
                            .ConfigureAwait(false);

                        string actualMd5 = HashUtility.Md5Bytes(buffer);
                        if (!actualMd5.Equals(chunk.ChunkDecompressedHashMd5 ?? string.Empty,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            isBroken = true;
                            break;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or EndOfStreamException or ArgumentException
                                               or OverflowException or UnauthorizedAccessException)
                {
                    isBroken = true;
                }
            }

            if (isBroken)
            {
                brokenAssets.Add(asset);
            }

            int current = Interlocked.Increment(ref doneAssets);
            if (current % 100 == 0 || current == totalAssets)
            {
                context.Progress.Report(new GameServerConversionProgress(
                    string.Format("GameServer_VerifyProgress".GetLocalized(), current, totalAssets),
                    totalAssets, current, 0, 0, null));
            }
        }).ConfigureAwait(false);

        return brokenAssets.ToList();
    }

    private static async Task ReadExactlyAsync(SafeFileHandle handle, Memory<byte> buffer, long offset,
        CancellationToken token)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = await RandomAccess.ReadAsync(handle, buffer[total..], offset + total, token)
                .ConfigureAwait(false);
            if (read <= 0)
            {
                throw new EndOfStreamException("GameServer_UnexpectedEof".GetLocalized());
            }

            total += read;
        }
    }

    #endregion

    #region 辅助

    private static void EnsureDirectoryPermissions(string gameDir)
    {
        try
        {
            Directory.CreateDirectory(gameDir);
            string tempFilePath = Path.Combine(gameDir, $"{Guid.NewGuid():N}.tmp");
            string movedFilePath = Path.Combine(gameDir, $"{Guid.NewGuid():N}.tmp");

            using (SafeFileHandle handle = File.OpenHandle(tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite,
                       FileShare.None, preallocationSize: 32 * 1024))
            {
                RandomAccess.Write(handle, "FUFU LAUNCHER DIRECTORY PERMISSION CHECK"u8, 0);
                RandomAccess.FlushToDisk(handle);
            }

            File.Move(tempFilePath, movedFilePath);
            File.Delete(movedFilePath);
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException(
                string.Format("GameServer_InsufficientPermission".GetLocalized(), ex.Message), ex);
        }
    }

    private static void MoveFile(string source, string destination, bool overwrite = true)
    {
        if (!string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
        {
            if (overwrite && File.Exists(destination))
            {
                File.SetAttributes(destination, FileAttributes.Normal);
                File.Delete(destination);
            }

            File.Copy(source, destination, false);
            File.Delete(source);
            return;
        }

        File.Move(source, destination, overwrite);
    }

    private static void MergeDirectoryContents(string sourceDir, string targetDir)
    {
        foreach (string file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceDir, file);
            string destination = Path.Combine(targetDir, relativePath);
            string? destinationDirectory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(destinationDirectory))
            {
                Directory.CreateDirectory(destinationDirectory);
            }

            if (File.Exists(destination))
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                }

                continue;
            }

            File.Move(file, destination);
        }

        try
        {
            Directory.Delete(sourceDir, true);
        }
        catch
        {
        }
    }

    private void CleanupChunksFolder(GameServerConversionContext context)
    {
        try
        {
            if (Directory.Exists(context.ChunksFolder))
            {
                Directory.Delete(context.ChunksFolder, true);
            }
        }
        catch (Exception ex)
        {
            context.Log(string.Format("GameServer_ChunkCleanupFailed".GetLocalized(), ex.Message));
        }
    }

    #endregion
}