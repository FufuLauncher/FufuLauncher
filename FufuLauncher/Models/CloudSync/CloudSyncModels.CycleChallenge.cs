/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public sealed class CycleChallengeCloudInfo
{
    [JsonPropertyName("export_app")]
    public string ExportApp
    {
        get;
        set;
    } = "FufuLauncher";

    [JsonPropertyName("export_app_version")]
    public string? ExportAppVersion
    {
        get;
        set;
    }

    [JsonPropertyName("challenge_version")]
    public string ChallengeVersion
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

public sealed class CycleChallengeCloudRecord
{
    [JsonPropertyName("uid")]
    public string Uid
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("mode")]
    public int Mode
    {
        get;
        set;
    }

    [JsonPropertyName("schedule_id")]
    public string ScheduleId
    {
        get;
        set;
    } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("account_id")]
    public string? AccountId
    {
        get;
        set;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("start_utc")]
    public string? StartUtc
    {
        get;
        set;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("end_utc")]
    public string? EndUtc
    {
        get;
        set;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("summary")]
    public string? Summary
    {
        get;
        set;
    }

    [JsonPropertyName("payload")]
    public string Payload
    {
        get;
        set;
    } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fetched_at")]
    public string? FetchedAt
    {
        get;
        set;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("api_version")]
    public string? ApiVersion
    {
        get;
        set;
    }
}

public sealed class CycleChallengeCloudDocument
{
    [JsonPropertyName("info")]
    public CycleChallengeCloudInfo Info
    {
        get;
        set;
    } = new();

    [JsonPropertyName("records")]
    public List<CycleChallengeCloudRecord> Records
    {
        get;
        set;
    } = new();
}

public sealed class CycleChallengeMergeResult
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

    public int IncomingCount
    {
        get;
        init;
    }

    public int AddedCount
    {
        get;
        init;
    }

    public int UpgradedCount
    {
        get;
        init;
    }

    public int KeptLocalCount
    {
        get;
        init;
    }

    public int BeforeTotalCount
    {
        get;
        init;
    }

    public int AfterTotalCount
    {
        get;
        init;
    }

    public int AddedAbyssCount
    {
        get;
        init;
    }

    public int AddedTheaterCount
    {
        get;
        init;
    }

    public int AddedStygianCount
    {
        get;
        init;
    }

    public int UpgradedAbyssCount
    {
        get;
        init;
    }

    public int UpgradedTheaterCount
    {
        get;
        init;
    }

    public int UpgradedStygianCount
    {
        get;
        init;
    }
}

public sealed class CycleChallengeLocalUidSummary
{
    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public int TotalCount
    {
        get;
        init;
    }

    public int AbyssCount
    {
        get;
        init;
    }

    public int TheaterCount
    {
        get;
        init;
    }

    public int StygianCount
    {
        get;
        init;
    }
}

public sealed class CycleChallengeLocalSummary
{
    public int UidCount
    {
        get;
        init;
    }

    public int TotalCount
    {
        get;
        init;
    }

    public int AbyssCount
    {
        get;
        init;
    }

    public int TheaterCount
    {
        get;
        init;
    }

    public int StygianCount
    {
        get;
        init;
    }

    public IReadOnlyList<CycleChallengeLocalUidSummary> Uids
    {
        get;
        init;
    } = Array.Empty<CycleChallengeLocalUidSummary>();
}
