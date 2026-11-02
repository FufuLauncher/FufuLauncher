/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;

namespace FufuLauncher.Services.GameServer;

public sealed partial class GameUpdateService
{
    public const string ChunksDirectoryName = "chunks";

    public const string PredownloadStatusFileName = "predownload_status.json";

    private const long TempFileMarginBytes = 1024L * 1024L * 1024L;

    private readonly SophonBuildClient _sophonBuildClient;
    private readonly ChunkDownloader _chunkDownloader;
    private readonly GameServerConfigurationService _configurationService;
    private readonly GameChannelSdkService _gameChannelSdkService;

    public GameUpdateService(SophonBuildClient sophonBuildClient, ChunkDownloader chunkDownloader,
        GameServerConfigurationService configurationService, GameChannelSdkService gameChannelSdkService)
    {
        _sophonBuildClient = sophonBuildClient;
        _chunkDownloader = chunkDownloader;
        _configurationService = configurationService;
        _gameChannelSdkService = gameChannelSdkService;
    }

    #region 公开入口

    public async Task<GameUpdateResult> PredownloadAsync(string gameDir, GameServerScheme currentScheme,
        IProgress<GameServerConversionProgress> progress, Action<string> log, CancellationToken token = default,
        GameServerDownloadMonitor? downloadMonitor = null, Func<GameUpdatePlan, Task<bool>>? confirmAsync = null)
    {
        ArgumentNullException.ThrowIfNull(currentScheme);

        EnsureDirectoryPermissions(gameDir);

        var context = new GameUpdateContext(gameDir, currentScheme, progress, log, token, downloadMonitor);
        string? localVersion = _configurationService.TryGetGameVersion(gameDir);
        if (string.IsNullOrEmpty(localVersion))
        {
            log("GameUpdate_UnknownLocalVersion".GetLocalized());
            return GameUpdateResult.Failed;
        }

        log("GameServer_StageFetchBranches".GetLocalized());
        SophonBranchPayload preBranch = await _sophonBuildClient.GetBranchPayloadAsync(currentScheme, true, token)
            .ConfigureAwait(false);
        if (string.Equals(preBranch.Tag, localVersion, StringComparison.Ordinal))
        {
            log("GameUpdate_NoPredownloadNeeded".GetLocalized());
            return GameUpdateResult.NothingToDo;
        }

        SophonBranchInfo preInfo =
            await _sophonBuildClient.GetBranchInfoAsync(currentScheme, true, token).ConfigureAwait(false);

        log("GameServer_StageDecodeManifests".GetLocalized());
        SophonManifestProto preManifest =
            await _sophonBuildClient.DownloadManifestAsync(preInfo, token).ConfigureAwait(false);

        log("GameServer_StageDiff".GetLocalized());
        SophonDecodedPatchBuild? patchBuild =
            await TryDecodePatchBuildAsync(currentScheme, preBranch, localVersion, token).ConfigureAwait(false);
        List<SophonPatchAsset> patchAssets = patchBuild is null ? [] : EnumeratePatchAssets(patchBuild);
        List<GameUpdateAssetOperation> downloadOperations = patchAssets.Count > 0
            ? []
            : await BuildUpdateOperationsAsync(context, localVersion, preManifest, preInfo).ConfigureAwait(false);

        Dictionary<string, long> blocks = GetDownloadBlocks(patchAssets, downloadOperations);
        long downloadTotalBytes = blocks.Values.Sum();
        int totalBlocks = blocks.Count;

        if (totalBlocks == 0)
        {
            log("GameUpdate_NoPredownloadNeeded".GetLocalized());
            return GameUpdateResult.NothingToDo;
        }

        Directory.CreateDirectory(context.ChunksDir);
        ChunkCacheScan scan = await ScanChunkCacheAsync(context, blocks).ConfigureAwait(false);
        bool needsDownload = scan.PendingBytes > 0;

        if (needsDownload)
        {
            EnsureFreeSpace(context, scan.PendingBytes);
        }

        var plan = new GameUpdatePlan(GameUpdateOperationKind.Predownload, preBranch.Tag, scan.PendingBytes,
            downloadTotalBytes, patchAssets.Count > 0);
        if (needsDownload && confirmAsync is not null && !await confirmAsync(plan).ConfigureAwait(false))
        {
            log("AdvancedServerSwitch_Cancelled".GetLocalized());
            return GameUpdateResult.Cancelled;
        }

        log(string.Format("GameUpdate_PlanSummary".GetLocalized(), ToSizeString(scan.PendingBytes), preBranch.Tag));

        PredownloadStatus status = new()
        {
            Tag = preBranch.Tag,
            Finished = false,
            TotalBlocks = totalBlocks,
            Blocks = blocks,
            CompletedBlocks = ToBlockMap(scan.CachedNames, blocks),
        };
        await WritePredownloadStatusAsync(context, status, token).ConfigureAwait(false);

        if (needsDownload)
        {
            if (patchAssets.Count > 0)
            {
                await PredownloadPatchesAsync(context, patchAssets, scan).ConfigureAwait(false);
            }
            else
            {
                await DownloadOperationChunksAsync(context, downloadOperations,
                    "GameServer_StageDownloadChunks".GetLocalized(), scan).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            status.CompletedBlocks = new Dictionary<string, long>(blocks, StringComparer.Ordinal);
        }

        status.Finished = true;
        await WritePredownloadStatusAsync(context, status, token).ConfigureAwait(false);
        log("GameUpdate_PredownloadDone".GetLocalized());

        return GameUpdateResult.Completed;
    }

    public async Task<GameUpdateResult> UpdateAsync(string gameDir, GameServerScheme currentScheme,
        bool usePredownloadAsTarget,
        IProgress<GameServerConversionProgress> progress, Action<string> log, CancellationToken token = default,
        GameServerDownloadMonitor? downloadMonitor = null, Func<GameUpdatePlan, Task<bool>>? confirmAsync = null)
    {
        ArgumentNullException.ThrowIfNull(currentScheme);

        EnsureDirectoryPermissions(gameDir);

        var context = new GameUpdateContext(gameDir, currentScheme, progress, log, token, downloadMonitor);
        string? localVersion = _configurationService.TryGetGameVersion(gameDir);
        if (string.IsNullOrEmpty(localVersion))
        {
            log("GameUpdate_UnknownLocalVersion".GetLocalized());
            return GameUpdateResult.Failed;
        }

        log("GameServer_StageFetchBranches".GetLocalized());
        SophonBranchPayload targetBranch = await _sophonBuildClient
            .GetBranchPayloadAsync(currentScheme, usePredownloadAsTarget, token).ConfigureAwait(false);
        if (string.Equals(targetBranch.Tag, localVersion, StringComparison.Ordinal))
        {
            log("GameUpdate_AlreadyLatest".GetLocalized());
            return GameUpdateResult.NothingToDo;
        }

        SophonBranchInfo targetInfo = await _sophonBuildClient
            .GetBranchInfoAsync(currentScheme, usePredownloadAsTarget, token).ConfigureAwait(false);

        log("GameServer_StageDecodeManifests".GetLocalized());
        SophonManifestProto targetManifest =
            await _sophonBuildClient.DownloadManifestAsync(targetInfo, token).ConfigureAwait(false);

        log("GameServer_StageDiff".GetLocalized());
        SophonDecodedPatchBuild? patchBuild =
            await TryDecodePatchBuildAsync(currentScheme, targetBranch, localVersion, token).ConfigureAwait(false);
        List<SophonPatchAsset> patchAssets = patchBuild is null ? [] : EnumeratePatchAssets(patchBuild);
        List<GameUpdateAssetOperation> operations = patchAssets.Count > 0
            ? []
            : await BuildUpdateOperationsAsync(context, localVersion, targetManifest, targetInfo).ConfigureAwait(false);

        Dictionary<string, long> blocks = GetDownloadBlocks(patchAssets, operations);
        long downloadTotalBytes = blocks.Values.Sum();
        long installTotalBytes = patchAssets.Count > 0
            ? patchAssets.Sum(asset => asset.FileData.FileSize)
            : operations.Where(operation => operation.Kind != GameUpdateAssetOperationKind.Delete)
                .Sum(operation => operation.NewAsset!.AssetSize);
        EnsureFreeSpace(context, GetPendingDownloadSize(context, blocks), installTotalBytes);

        var plan = new GameUpdatePlan(
            usePredownloadAsTarget ? GameUpdateOperationKind.ApplyPredownload : GameUpdateOperationKind.Update,
            targetBranch.Tag, downloadTotalBytes, installTotalBytes, patchAssets.Count > 0);
        if (confirmAsync is not null && !await confirmAsync(plan).ConfigureAwait(false))
        {
            log("AdvancedServerSwitch_Cancelled".GetLocalized());
            return GameUpdateResult.Cancelled;
        }

        log(string.Format("GameUpdate_PlanSummary".GetLocalized(), ToSizeString(downloadTotalBytes), targetBranch.Tag));

        if (patchAssets.Count > 0)
        {
            log("GameUpdate_StageApplyPatches".GetLocalized());
            await InstallOrPatchAssetsAsync(context, patchAssets).ConfigureAwait(false);
            DeletePatchDeprecatedFiles(context, patchBuild!);
        }
        else
        {
            await UpdateDiffAssetsAsync(context, operations).ConfigureAwait(false);
        }

        await _gameChannelSdkService.EnsureSdkAndDeprecatedFilesAsync(gameDir, currentScheme, log, token,
            downloadMonitor is null ? null : downloadMonitor.AddBytes).ConfigureAwait(false);

        log("GameServer_StageVerify".GetLocalized());
        List<AssetProperty> brokenAssets = await VerifyAssetsAsync(context, targetManifest).ConfigureAwait(false);
        if (brokenAssets.Count > 0)
        {
            log(string.Format("GameServer_RepairFound".GetLocalized(), brokenAssets.Count));
            var repairOperations = brokenAssets
                .Select(asset => GameUpdateAssetOperation.Add(targetInfo.ChunkPrefix, targetInfo.ChunkSuffix, asset))
                .ToList();
            await UpdateDiffAssetsAsync(context, repairOperations).ConfigureAwait(false);
            brokenAssets = await VerifyAssetsAsync(context, targetManifest).ConfigureAwait(false);
            if (brokenAssets.Count > 0)
            {
                log(string.Format("GameUpdate_RepairFailed".GetLocalized(), brokenAssets.Count));
                return GameUpdateResult.Failed;
            }

            log("GameServer_RepairDone".GetLocalized());
        }
        else
        {
            log("GameServer_VerifyOk".GetLocalized());
        }

        token.ThrowIfCancellationRequested();
        WriteGameVersion(gameDir, targetBranch.Tag);

        log("GameServer_StageCleanup".GetLocalized());
        CleanupChunks(context);

        return GameUpdateResult.Completed;
    }

    public static PredownloadStatus? TryReadPredownloadStatus(string gameDir)
    {
        try
        {
            string path = Path.Combine(gameDir, ChunksDirectoryName, PredownloadStatusFileName);
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<PredownloadStatus>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static bool IsPredownloadFinished(string gameDir, out PredownloadStatus? status)
    {
        status = TryReadPredownloadStatus(gameDir);
        if (status is null || !status.Finished)
        {
            return false;
        }

        try
        {
            string chunksDir = Path.Combine(gameDir, ChunksDirectoryName);
            if (status.Blocks is not { Count: > 0 })
            {
                int fileCount = Directory.Exists(chunksDir)
                    ? Directory.GetFiles(chunksDir).Count(IsChunkCacheFile)
                    : 0;
                return fileCount == status.TotalBlocks;
            }

            return status.Blocks.Count == status.TotalBlocks && status.Blocks.All(block =>
            {
                var file = new FileInfo(Path.Combine(chunksDir, block.Key));
                return file.Exists && file.Length == block.Value;
            });
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    #endregion
}