/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public sealed class PlayTimeSegment
{
    [JsonPropertyName("start")]
    public long Start
    {
        get;
        set;
    }

    [JsonPropertyName("end")]
    public long End
    {
        get;
        set;
    }
}

public sealed class PlayTimeCloudInfo
{
    [JsonPropertyName("export_app")]
    public string ExportApp
    {
        get;
        set;
    } = "FufuLauncher";

    [JsonPropertyName("export_app_version")]
    public string ExportAppVersion
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("playtime_version")]
    public string PlaytimeVersion
    {
        get;
        set;
    } = "v1.0";

    [JsonPropertyName("export_timestamp")]
    public long ExportTimestamp
    {
        get;
        set;
    }
}

public sealed class PlayTimeCloudDocument
{
    [JsonPropertyName("info")]
    public PlayTimeCloudInfo Info
    {
        get;
        set;
    } = new();

    [JsonPropertyName("segments")]
    public List<PlayTimeSegment> Segments
    {
        get;
        set;
    } = new();

    [JsonPropertyName("daily_seconds")]
    public Dictionary<string, long> DailySeconds
    {
        get;
        set;
    } = new(StringComparer.Ordinal);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("legacy_daily_seconds")]
    public Dictionary<string, long>? LegacyDailySeconds
    {
        get;
        set;
    }
}

public sealed class PlayTimeMergeResult
{
    public bool Ok
    {
        get;
        init;
    }

    public string? ErrorCode
    {
        get;
        init;
    }

    public long BeforeTotalSeconds
    {
        get;
        init;
    }

    public long AfterTotalSeconds
    {
        get;
        init;
    }

    public long AddedSeconds
    {
        get;
        init;
    }

    public long IncomingSegmentSeconds
    {
        get;
        init;
    }

    public long OverlapDeduplicatedSeconds
    {
        get;
        init;
    }

    public int ResultingSegmentCount
    {
        get;
        init;
    }

    public int UpdatedDaysCount
    {
        get;
        init;
    }
}

public sealed class PlayTimeLocalSummary
{
    public int TotalDays
    {
        get;
        init;
    }

    public long TotalSeconds
    {
        get;
        init;
    }

    public int SegmentCount
    {
        get;
        init;
    }

    public long SegmentedSeconds
    {
        get;
        init;
    }

    public long LegacySeconds
    {
        get;
        init;
    }
}