/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;

namespace FufuLauncher.Services.GameServer;

public sealed partial class GameUpdateService
{
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

    private static void EnsureFreeSpace(GameUpdateContext context, long downloadBytes, long temporaryBytes = 0)
    {
        long required = downloadBytes + temporaryBytes + TempFileMarginBytes;
        if (required <= 0)
        {
            return;
        }

        string? root = Path.GetPathRoot(context.GameDir);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        try
        {
            var drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < required)
            {
                throw new InvalidOperationException(string.Format("GameUpdate_NoFreeSpace".GetLocalized(),
                    ToSizeString(required), ToSizeString(drive.AvailableFreeSpace)));
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
        }
    }

    private static long GetPendingDownloadSize(GameUpdateContext context,
        IEnumerable<KeyValuePair<string, long>> blocks)
    {
        long size = 0;
        foreach (var block in blocks)
        {
            try
            {
                var info = new FileInfo(context.GetChunkFilePath(block.Key));
                if (info.Exists && info.Length == block.Value)
                {
                    continue;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }

            size += block.Value;
        }

        return size;
    }

    private static Dictionary<string, long> GetDownloadBlocks(List<SophonPatchAsset> patchAssets,
        List<GameUpdateAssetOperation> operations)
    {
        return patchAssets.Count > 0
            ? patchAssets.GroupBy(asset => asset.PatchInfo.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().PatchInfo.PatchFileSize,
                    StringComparer.Ordinal)
            : operations.SelectMany(operation => operation.Chunks)
                .GroupBy(chunk => chunk.AssetChunk.ChunkName, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().AssetChunk.ChunkSize, StringComparer.Ordinal);
    }


    private static string GetTemporaryAssetPath(string assetPath) => $"{assetPath}.{Guid.NewGuid():N}.tmp";

    private static async Task<bool> CommitAssetAsync(GameUpdateContext context, string tempPath, string targetPath,
        long expectedSize, string expectedHash)
    {
        context.Token.ThrowIfCancellationRequested();
        if (new FileInfo(tempPath).Length != expectedSize ||
            !expectedHash.Equals(await HashUtility.Md5FileAsync(tempPath, context.Token).ConfigureAwait(false),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        context.Token.ThrowIfCancellationRequested();
        File.Move(tempPath, targetPath, overwrite: true);
        return true;
    }

    private async Task<List<GameUpdateAssetOperation>> BuildUpdateOperationsAsync(GameUpdateContext context,
        string localVersion, SophonManifestProto targetManifest, SophonBranchInfo targetInfo)
    {
        SophonManifestProto localManifest;
        try
        {
            SophonBranchInfo localInfo = await _sophonBuildClient
                .GetBranchInfoByTagAsync(context.Scheme, localVersion, context.Token).ConfigureAwait(false);
            localManifest = await _sophonBuildClient.DownloadManifestAsync(localInfo, context.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Log(string.Format("GameUpdate_LocalManifestUnavailable".GetLocalized(), ex.Message));
            List<AssetProperty> brokenAssets = await VerifyAssetsAsync(context, targetManifest).ConfigureAwait(false);
            return brokenAssets.Select(asset =>
                GameUpdateAssetOperation.Add(targetInfo.ChunkPrefix, targetInfo.ChunkSuffix, asset)).ToList();
        }

        return BuildDiffOperations(localManifest, targetManifest, targetInfo.ChunkPrefix, targetInfo.ChunkSuffix);
    }

    private static void WriteGameVersion(string gameDir, string tag)
    {
        string configPath = Path.Combine(gameDir, GameConstants.CONFIG_FILE_NAME);
        if (!File.Exists(configPath))
        {
            return;
        }

        var ini = new IniFile(configPath);
        ini.UpdateMultiple(new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["General"] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["game_version"] = tag,
            },
        });
    }

    private static async Task WritePredownloadStatusAsync(GameUpdateContext context, PredownloadStatus status,
        CancellationToken token)
    {
        using FileStream stream = File.Create(context.PredownloadStatusFilePath);
        await JsonSerializer.SerializeAsync(stream, status, cancellationToken: token).ConfigureAwait(false);
    }

    private static void CleanupChunks(GameUpdateContext context)
    {
        try
        {
            if (Directory.Exists(context.ChunksDir))
            {
                Directory.Delete(context.ChunksDir, true);
            }
        }
        catch (Exception ex)
        {
            context.Log(string.Format("GameServer_ChunkCleanupFailed".GetLocalized(), ex.Message));
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                CutsceneProtectionService.UnlockFile(path, deleteFile: true);
            }
        }
        catch
        {
        }
    }

    private static string ToSizeString(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L)
        {
            return $"{bytes / 1073741824.0:F1} GB";
        }

        if (bytes >= 1024L * 1024L)
        {
            return $"{bytes / 1048576.0:F1} MB";
        }

        return $"{bytes / 1024.0:F1} KB";
    }
}