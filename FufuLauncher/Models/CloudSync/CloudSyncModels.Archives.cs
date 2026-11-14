/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Json.Serialization;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public sealed class CloudArchiveItem
{
    [JsonPropertyName("data_type")]
    public string DataType
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("slot")]
    public string Slot
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("compressed_size")]
    public long CompressedSize
    {
        get;
        set;
    }

    [JsonPropertyName("raw_size")]
    public long RawSize
    {
        get;
        set;
    }

    [JsonPropertyName("sha256")]
    public string Sha256
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string CreatedAt
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("updated_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string UpdatedAt
    {
        get;
        set;
    } = string.Empty;

    public string TypeBadge => DataType.ToLowerInvariant() switch
    {
        "uiaf" => "CloudSync_TypeBadgeUiaf".GetLocalized(),
        "uigf" => "CloudSync_TypeBadgeUigf".GetLocalized(),
        "playtime" => "CloudSync_TypeBadgePlayTime".GetLocalized(),
        "challenge" => "CloudSync_TypeBadgeChallenge".GetLocalized(),
        _ => DataType.ToUpperInvariant()
    };

    public bool IsUiaf => string.Equals(DataType, "uiaf", StringComparison.OrdinalIgnoreCase);

    public bool IsUigf => string.Equals(DataType, "uigf", StringComparison.OrdinalIgnoreCase);

    public bool IsPlayTime => string.Equals(DataType, "playtime", StringComparison.OrdinalIgnoreCase);

    public bool IsChallenge => string.Equals(DataType, "challenge", StringComparison.OrdinalIgnoreCase);

    public bool CanMergeToLocal => IsPlayTime || IsChallenge || IsUiaf || IsUigf;

    public Microsoft.UI.Xaml.Visibility PlayTimeMergeVisibility =>
        IsPlayTime ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility MergeToLocalVisibility =>
        CanMergeToLocal ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string SizeSummary => string.Format(
        "CloudSync_SizeSummaryFormat".GetLocalized(),
        FormatBytes(CompressedSize),
        FormatBytes(RawSize));

    public string UpdatedAtLocalText => FormatIsoTimestamp(UpdatedAt);

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }

    public static string FormatIsoTimestamp(string? iso)
    {
        if (string.IsNullOrWhiteSpace(iso)) return "-";
        var trimmed = iso.Trim();
        if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix))
        {
            if (unix <= 0) return "-";
            var dto = unix < 10_000_000_000L
                ? DateTimeOffset.FromUnixTimeSeconds(unix)
                : DateTimeOffset.FromUnixTimeMilliseconds(unix);
            return dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        return DateTimeOffset.TryParse(trimmed, out var parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
            : trimmed;
    }
}

public sealed class CloudRemainingQuotaSnapshot
{
    [JsonPropertyName("ops")]
    public long Ops
    {
        get;
        set;
    }

    [JsonPropertyName("uploads")]
    public long Uploads
    {
        get;
        set;
    }

    [JsonPropertyName("deletes")]
    public long Deletes
    {
        get;
        set;
    }
}

public sealed class CloudArchiveListData
{
    [JsonPropertyName("r2_bytes_used")]
    public long R2BytesUsed
    {
        get;
        set;
    }

    [JsonPropertyName("r2_bytes_limit")]
    public long R2BytesLimit
    {
        get;
        set;
    }

    [JsonPropertyName("file_count")]
    public int FileCount
    {
        get;
        set;
    }

    [JsonPropertyName("file_count_limit")]
    public int FileCountLimit
    {
        get;
        set;
    }

    [JsonPropertyName("archives")]
    public List<CloudArchiveItem> Archives
    {
        get;
        set;
    } = new();

    [JsonPropertyName("remaining_quota")]
    public CloudRemainingQuotaSnapshot? RemainingQuota
    {
        get;
        set;
    }
}

public sealed class CloudUploadResultData
{
    [JsonPropertyName("type")]
    public string Type
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("slot")]
    public string Slot
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("overwritten")]
    public bool Overwritten
    {
        get;
        set;
    }

    [JsonPropertyName("compressed_size")]
    public long CompressedSize
    {
        get;
        set;
    }

    [JsonPropertyName("raw_size")]
    public long RawSize
    {
        get;
        set;
    }

    [JsonPropertyName("sha256")]
    public string Sha256
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("updated_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string UpdatedAt
    {
        get;
        set;
    } = string.Empty;
}

public sealed class CloudPullResult
{
    public bool Ok
    {
        get;
        init;
    }

    public int HttpStatusCode
    {
        get;
        init;
    }

    public string Code
    {
        get;
        init;
    } = string.Empty;

    public string Message
    {
        get;
        init;
    } = string.Empty;

    public string DataType
    {
        get;
        init;
    } = string.Empty;

    public string Slot
    {
        get;
        init;
    } = string.Empty;

    public string Sha256
    {
        get;
        init;
    } = string.Empty;

    public long CompressedBytes
    {
        get;
        init;
    }

    public long RawBytes
    {
        get;
        init;
    }

    public string JsonContent
    {
        get;
        init;
    } = string.Empty;
}

public sealed class CloudRenameResultData
{
    [JsonPropertyName("type")]
    public string Type
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("old_slot")]
    public string OldSlot
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("new_slot")]
    public string NewSlot
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("overwritten")]
    public bool Overwritten
    {
        get;
        set;
    }
}

public sealed class CloudDeleteResultData
{
    [JsonPropertyName("type")]
    public string Type
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("slot")]
    public string Slot
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("freed_bytes")]
    public long FreedBytes
    {
        get;
        set;
    }
}

public sealed class CloudDeleteAllResultData
{
    [JsonPropertyName("deleted_files")]
    public int DeletedFiles
    {
        get;
        set;
    }

    [JsonPropertyName("freed_bytes")]
    public long FreedBytes
    {
        get;
        set;
    }
}
