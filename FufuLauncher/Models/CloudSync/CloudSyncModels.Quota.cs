/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public sealed class CloudQuotaCounter
{
    [JsonPropertyName("used")]
    public long Used
    {
        get;
        set;
    }

    [JsonPropertyName("limit")]
    public long Limit
    {
        get;
        set;
    }

    [JsonPropertyName("remaining")]
    public long Remaining
    {
        get;
        set;
    }
}

public sealed class CloudQuotaDaily
{
    [JsonPropertyName("ops")]
    public CloudQuotaCounter Ops
    {
        get;
        set;
    } = new();

    [JsonPropertyName("uploads")]
    public CloudQuotaCounter Uploads
    {
        get;
        set;
    } = new();

    [JsonPropertyName("deletes")]
    public CloudQuotaCounter Deletes
    {
        get;
        set;
    } = new();
}

public sealed class CloudQuotaStorage
{
    [JsonPropertyName("r2_bytes")]
    public CloudQuotaCounter R2Bytes
    {
        get;
        set;
    } = new();

    [JsonPropertyName("files")]
    public CloudQuotaCounter Files
    {
        get;
        set;
    } = new();
}

public sealed class CloudQuotaLimits
{
    [JsonPropertyName("user_daily_max_ops")]
    public long UserDailyMaxOps
    {
        get;
        set;
    }

    [JsonPropertyName("user_daily_max_uploads")]
    public long UserDailyMaxUploads
    {
        get;
        set;
    }

    [JsonPropertyName("user_daily_max_deletes")]
    public long UserDailyMaxDeletes
    {
        get;
        set;
    }

    [JsonPropertyName("max_user_r2_bytes")]
    public long MaxUserR2Bytes
    {
        get;
        set;
    }

    [JsonPropertyName("max_user_files")]
    public long MaxUserFiles
    {
        get;
        set;
    }
}

public sealed class CloudQuotaUpgradeInfo
{
    [JsonPropertyName("active")]
    public bool Active
    {
        get;
        set;
    }

    [JsonPropertyName("activated_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? ActivatedAt
    {
        get;
        set;
    }

    [JsonPropertyName("expires_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? ExpiresAt
    {
        get;
        set;
    }

    [JsonPropertyName("expires_in_seconds")]
    public long ExpiresInSeconds
    {
        get;
        set;
    }

    [JsonPropertyName("expanded_by")]
    public CloudQuotaLimits? ExpandedBy
    {
        get;
        set;
    }
}

public sealed class CloudQuotaData
{
    [JsonPropertyName("utc_date")]
    public string UtcDate
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("resets_at")]
    public long ResetsAt
    {
        get;
        set;
    }

    [JsonPropertyName("daily")]
    public CloudQuotaDaily Daily
    {
        get;
        set;
    } = new();

    [JsonPropertyName("storage")]
    public CloudQuotaStorage Storage
    {
        get;
        set;
    } = new();

    [JsonPropertyName("base_limits")]
    public CloudQuotaLimits BaseLimits
    {
        get;
        set;
    } = new();

    [JsonPropertyName("effective_limits")]
    public CloudQuotaLimits EffectiveLimits
    {
        get;
        set;
    } = new();

    [JsonPropertyName("upgrade")]
    public CloudQuotaUpgradeInfo Upgrade
    {
        get;
        set;
    } = new();
}

public sealed class CloudActivationResultData
{
    [JsonPropertyName("effective_limits")]
    public CloudQuotaLimits? EffectiveLimits
    {
        get;
        set;
    }

    [JsonPropertyName("upgrade")]
    public CloudQuotaUpgradeInfo? Upgrade
    {
        get;
        set;
    }
}
