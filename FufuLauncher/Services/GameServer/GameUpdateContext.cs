/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;

namespace FufuLauncher.Services.GameServer;

public sealed class GameUpdateContext
{
    public GameUpdateContext(string gameDir, GameServerScheme scheme, IProgress<GameServerConversionProgress> progress,
        Action<string> log, CancellationToken token, GameServerDownloadMonitor? downloadMonitor = null)
    {
        GameDir = gameDir;
        Scheme = scheme;
        Progress = progress;
        Log = log;
        Token = token;
        DownloadMonitor = downloadMonitor;

        ParallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
            CancellationToken = token,
        };

        ChunksDir = Path.Combine(gameDir, GameUpdateService.ChunksDirectoryName);
        PredownloadStatusFilePath = Path.Combine(ChunksDir, GameUpdateService.PredownloadStatusFileName);
    }

    public string GameDir
    {
        get;
    }

    public GameServerScheme Scheme
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

    public string ChunksDir
    {
        get;
    }

    public string PredownloadStatusFilePath
    {
        get;
    }

    public ConcurrentDictionary<string, byte> DuplicatedChunkNames
    {
        get;
    } = new(StringComparer.Ordinal);

    public KeyedSemaphoreSlim ChunkLocks
    {
        get;
    } = new();

    public ConcurrentDictionary<string, byte> ValidatedChunks
    {
        get;
    } = new(StringComparer.Ordinal);

    public string GetGameFilePath(string assetName) => Path.Combine(GameDir, assetName);
    public string GetChunkFilePath(string name) => Path.Combine(ChunksDir, name);
}