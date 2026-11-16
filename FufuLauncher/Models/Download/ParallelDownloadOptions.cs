/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;

namespace FufuLauncher.Models.Download;

public enum DownloadHashAlgorithm
{
    None = 0,

    Md5 = 1,

    XxHash64 = 2,
}

public sealed record ParallelDownloadOptions
{
    public const int MinSegments = 1;

    public const int MaxSegments = 32;


    public int MaxParallelSegments
    {
        get;
        init;
    } = 8;


    public int ChunkMultiplier
    {
        get;
        init;
    } = 4;

    public long MinChunkSize
    {
        get;
        init;
    } = 128 * 1024;


    public long MinParallelFileSize
    {
        get;
        init;
    } = 1024 * 1024;


    public int MaxConnectionsPerServer
    {
        get;
        init;
    }


    public long MaxBytesPerSecond
    {
        get;
        init;
    }

    public long MaxBytesPerSecondPerSegment
    {
        get;
        init;
    }


    public int MaxRetries
    {
        get;
        init;
    } = 3;

    public TimeSpan RetryBaseDelay
    {
        get;
        init;
    } = TimeSpan.FromSeconds(1);

    public TimeSpan RetryMaxDelay
    {
        get;
        init;
    } = TimeSpan.FromSeconds(30);


    public TimeSpan ConnectTimeout
    {
        get;
        init;
    } = TimeSpan.FromSeconds(15);

    public TimeSpan RequestTimeout
    {
        get;
        init;
    } = TimeSpan.FromSeconds(30);


    public TimeSpan StallTimeout
    {
        get;
        init;
    } = TimeSpan.FromSeconds(30);


    public TimeSpan? OverallTimeout
    {
        get;
        init;
    }


    public string? ExpectedHash
    {
        get;
        init;
    }

    public DownloadHashAlgorithm HashAlgorithm
    {
        get;
        init;
    } = DownloadHashAlgorithm.None;


    public long? ExpectedSize
    {
        get;
        init;
    }


    public bool AllowRangeFallback
    {
        get;
        init;
    } = true;


    public bool UseTemporaryFile
    {
        get;
        init;
    } = true;

    public bool OverwriteExisting
    {
        get;
        init;
    } = true;


    public bool EnableResume
    {
        get;
        init;
    }

    public bool DeleteOnFailure
    {
        get;
        init;
    } = true;

    public string? UserAgent
    {
        get;
        init;
    }

    public int BufferSize
    {
        get;
        init;
    } = 81920;


    public TimeSpan ProgressReportInterval
    {
        get;
        init;
    } = TimeSpan.FromMilliseconds(500);


    public static ParallelDownloadOptions Default
    {
        get;
    } = new();

    public static ParallelDownloadOptions Conservative
    {
        get;
    } = new()
    {
        MaxParallelSegments = 4,
        MaxBytesPerSecond = 2L * 1024 * 1024,
    };

    public static ParallelDownloadOptions Balanced
    {
        get;
    } = new()
    {
        MaxParallelSegments = 8,
        MaxBytesPerSecond = 5L * 1024 * 1024,
    };

    public static ParallelDownloadOptions Aggressive
    {
        get;
    } = new()
    {
        MaxParallelSegments = 16,
        ChunkMultiplier = 6,
        MinChunkSize = 256 * 1024,
    };

    public static ParallelDownloadOptions LowBandwidth
    {
        get;
    } = new()
    {
        MaxParallelSegments = 2,
        MaxBytesPerSecond = 256 * 1024,
        MaxBytesPerSecondPerSegment = 128 * 1024,
    };

    public ParallelDownloadOptions Validate()
    {
        var o = this with
        {
            MaxParallelSegments = Math.Clamp(MaxParallelSegments, MinSegments, MaxSegments),
            ChunkMultiplier = Math.Clamp(ChunkMultiplier, 1, 16),
            MinChunkSize = Math.Max(MinChunkSize, 16 * 1024),
            BufferSize = Math.Clamp(BufferSize, 4 * 1024, 1024 * 1024),
            MaxRetries = Math.Max(MaxRetries, 0),
            RetryBaseDelay = RetryBaseDelay < TimeSpan.Zero ? TimeSpan.Zero : RetryBaseDelay,
            MinParallelFileSize = Math.Max(MinParallelFileSize, 0),
            MaxBytesPerSecond = Math.Max(MaxBytesPerSecond, 0),
            MaxBytesPerSecondPerSegment = Math.Max(MaxBytesPerSecondPerSegment, 0),
            StallTimeout = StallTimeout < TimeSpan.Zero ? TimeSpan.Zero : StallTimeout,
            ProgressReportInterval = ProgressReportInterval < TimeSpan.FromMilliseconds(50)
                ? TimeSpan.FromMilliseconds(50)
                : ProgressReportInterval,
        };

        if (o.RetryMaxDelay < o.RetryBaseDelay)
        {
            o = o with { RetryMaxDelay = o.RetryBaseDelay };
        }

        if (o.MaxConnectionsPerServer <= 0)
        {
            o = o with { MaxConnectionsPerServer = Math.Max(o.MaxParallelSegments, 8) };
        }

        if (o.HashAlgorithm != DownloadHashAlgorithm.None && string.IsNullOrWhiteSpace(o.ExpectedHash))
        {
            throw new ArgumentException(
                "Download_HashAlgorithmWithoutExpectedHash".GetLocalized(), nameof(ExpectedHash));
        }

        if (o.ExpectedSize is <= 0)
        {
            o = o with { ExpectedSize = null };
        }

        return o;
    }
}