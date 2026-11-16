/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using AsyncKeyedLock;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;

namespace FufuLauncher.Services.GameServer;

public enum GameServerAssetOperationKind
{
    Add,
    ModifyOrReplace,
    Backup,
}

public sealed class GameServerAssetOperation
{
    private GameServerAssetOperation(GameServerAssetOperationKind kind, string urlPrefix, string urlSuffix,
        AssetProperty? oldAsset, AssetProperty? newAsset, List<SophonChunk>? diffChunks)
    {
        Kind = kind;
        UrlPrefix = urlPrefix;
        UrlSuffix = urlSuffix;
        OldAsset = oldAsset;
        NewAsset = newAsset;
        DiffChunks = diffChunks ?? [];
    }

    public GameServerAssetOperationKind Kind
    {
        get;
    }

    public string UrlPrefix
    {
        get;
    }

    public string UrlSuffix
    {
        get;
    }

    public AssetProperty? OldAsset
    {
        get;
    }

    public AssetProperty? NewAsset
    {
        get;
    }

    public List<SophonChunk> DiffChunks
    {
        get;
    }

    public IEnumerable<SophonChunk> Chunks => Kind switch
    {
        GameServerAssetOperationKind.Add => NewAsset!.AssetChunks.Select(chunk =>
            new SophonChunk(UrlPrefix, UrlSuffix, chunk)),
        GameServerAssetOperationKind.ModifyOrReplace => DiffChunks,
        _ => [],
    };

    public static GameServerAssetOperation Add(string urlPrefix, string urlSuffix, AssetProperty newAsset)
    {
        return new GameServerAssetOperation(GameServerAssetOperationKind.Add, urlPrefix, urlSuffix, null, newAsset,
            null);
    }

    public static GameServerAssetOperation ModifyOrReplace(string urlPrefix, string urlSuffix, AssetProperty oldAsset,
        AssetProperty newAsset, List<SophonChunk> diffChunks)
    {
        return new GameServerAssetOperation(GameServerAssetOperationKind.ModifyOrReplace, urlPrefix, urlSuffix,
            oldAsset, newAsset, diffChunks);
    }

    public static GameServerAssetOperation Backup(AssetProperty oldAsset)
    {
        return new GameServerAssetOperation(GameServerAssetOperationKind.Backup, string.Empty, string.Empty, oldAsset,
            null, null);
    }
}

public sealed class GameServerConversionContext
{
    public GameServerConversionContext(string gameDir, GameServerScheme currentScheme, GameServerScheme targetScheme,
        string serverCacheDir, IProgress<GameServerConversionProgress> progress, Action<string> log,
        CancellationToken token,
        GameServerDownloadMonitor? downloadMonitor = null)
    {
        GameDir = gameDir;
        CurrentScheme = currentScheme;
        TargetScheme = targetScheme;
        Progress = progress;
        Log = log;
        Token = token;
        DownloadMonitor = downloadMonitor;

        ParallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
            CancellationToken = token,
        };

        ChunksFolder = Path.Combine(serverCacheDir, "Chunks");
        string overseaFolder = Path.Combine(serverCacheDir, "Oversea");
        string chineseFolder = Path.Combine(serverCacheDir, "Chinese");

        (BackupFolder, TargetFolder) = targetScheme.IsOversea
            ? (chineseFolder, overseaFolder)
            : (overseaFolder, chineseFolder);

        (FromDataFolderName, ToDataFolderName) = targetScheme.IsOversea
            ? (GameConstants.CN_DATA_DIR, GameConstants.OS_DATA_DIR)
            : (GameConstants.OS_DATA_DIR, GameConstants.CN_DATA_DIR);
    }

    public string GameDir
    {
        get;
    }

    public GameServerScheme CurrentScheme
    {
        get;
    }

    public GameServerScheme TargetScheme
    {
        get;
    }

    public IProgress<GameServerConversionProgress> Progress
    {
        get;
    }

    public Action<string> Log
    {
        get;
    }

    public CancellationToken Token
    {
        get;
    }

    public GameServerDownloadMonitor? DownloadMonitor
    {
        get;
    }

    public ParallelOptions ParallelOptions
    {
        get;
    }

    public string ChunksFolder
    {
        get;
    }

    public string BackupFolder
    {
        get;
    }

    public string TargetFolder
    {
        get;
    }

    public string FromDataFolderName
    {
        get;
    }

    public string ToDataFolderName
    {
        get;
    }

    public ConcurrentDictionary<string, byte> DuplicatedChunkNames
    {
        get;
    } = new(StringComparer.Ordinal);

    public AsyncKeyedLocker<string> ChunkLocks
    {
        get;
    } = new();

    public string GetGameFilePath(string assetName) => Path.Combine(GameDir, assetName);
    public string GetBackupFilePath(string assetName) => Path.Combine(BackupFolder, assetName);
    public string GetTargetFilePath(string assetName) => Path.Combine(TargetFolder, assetName);
}