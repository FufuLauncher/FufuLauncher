/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public sealed class FlexibleTimestampConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var intVal)
                ? intVal.ToString(CultureInfo.InvariantCulture)
                : ((long)reader.GetDouble()).ToString(CultureInfo.InvariantCulture),
            _ => throw new JsonException($"Unexpected token {reader.TokenType} when parsing timestamp.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value == null) writer.WriteNullValue();
        else writer.WriteStringValue(value);
    }
}

public sealed class CloudApiEnvelope
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("details")]
    public JsonElement? Details { get; set; }
}

public sealed class CloudApiResult<T>
{
    public bool Ok { get; init; }
    public int HttpStatusCode { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }
    public CloudChallengeInfo? Challenge { get; init; }
    public bool RequiresCaptcha => Challenge != null && string.Equals(Code, "CAPTCHA_REQUIRED", StringComparison.Ordinal);
    public bool IsPending => string.Equals(Code, "CHALLENGE_PENDING", StringComparison.Ordinal);
    public bool IsAccountLocked => HttpStatusCode == 423 || string.Equals(Code, "ACCOUNT_LOCKED", StringComparison.Ordinal);
}

public sealed class CloudChallengeInfo
{
    [JsonPropertyName("challenge_id")]
    public string ChallengeId { get; set; } = string.Empty;

    [JsonPropertyName("challenge_secret")]
    public string ChallengeSecret { get; set; } = string.Empty;

    [JsonPropertyName("captcha_url")]
    public string CaptchaUrl { get; set; } = string.Empty;

    [JsonPropertyName("poll_url")]
    public string PollUrl { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public sealed class CloudLoginSessionData
{
    [JsonPropertyName("stoken")]
    public string Stoken { get; set; } = string.Empty;

    [JsonPropertyName("stoken_expires_in")]
    public long StokenExpiresIn { get; set; }

    [JsonPropertyName("ltoken")]
    public string Ltoken { get; set; } = string.Empty;

    [JsonPropertyName("ltoken_expires_in")]
    public long LtokenExpiresIn { get; set; }

    [JsonPropertyName("account_status")]
    public string AccountStatus { get; set; } = "active";

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("locked_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? LockedAt { get; set; }
}

public sealed class CloudLtokenIssueData
{
    [JsonPropertyName("ltoken")]
    public string Ltoken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }

    [JsonPropertyName("account_status")]
    public string AccountStatus { get; set; } = "active";

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("locked_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? LockedAt { get; set; }
}

public sealed class CloudAccountStatusData
{
    [JsonPropertyName("account_status")]
    public string AccountStatus { get; set; } = "active";

    [JsonPropertyName("locked")]
    public bool Locked { get; set; }

    [JsonPropertyName("locked_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? LockedAt { get; set; }
}

public sealed class CloudOtpSentData
{
    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}

public sealed class CloudArchiveItem
{
    [JsonPropertyName("data_type")]
    public string DataType { get; set; } = string.Empty;

    [JsonPropertyName("slot")]
    public string Slot { get; set; } = string.Empty;

    [JsonPropertyName("compressed_size")]
    public long CompressedSize { get; set; }

    [JsonPropertyName("raw_size")]
    public long RawSize { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string CreatedAt { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string UpdatedAt { get; set; } = string.Empty;

    public string TypeBadge => DataType.ToLowerInvariant() switch
    {
        "uiaf" => "CloudSync_TypeBadgeUiaf".GetLocalized(),
        "uigf" => "CloudSync_TypeBadgeUigf".GetLocalized(),
        "playtime" => "CloudSync_TypeBadgePlayTime".GetLocalized(),
        _ => DataType.ToUpperInvariant()
    };

    public bool IsPlayTime => string.Equals(DataType, "playtime", StringComparison.OrdinalIgnoreCase);

    public Microsoft.UI.Xaml.Visibility PlayTimeMergeVisibility =>
        IsPlayTime ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

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
    public long Ops { get; set; }

    [JsonPropertyName("uploads")]
    public long Uploads { get; set; }

    [JsonPropertyName("deletes")]
    public long Deletes { get; set; }
}

public sealed class CloudArchiveListData
{
    [JsonPropertyName("r2_bytes_used")]
    public long R2BytesUsed { get; set; }

    [JsonPropertyName("r2_bytes_limit")]
    public long R2BytesLimit { get; set; }

    [JsonPropertyName("file_count")]
    public int FileCount { get; set; }

    [JsonPropertyName("file_count_limit")]
    public int FileCountLimit { get; set; }

    [JsonPropertyName("archives")]
    public List<CloudArchiveItem> Archives { get; set; } = new();

    [JsonPropertyName("remaining_quota")]
    public CloudRemainingQuotaSnapshot? RemainingQuota { get; set; }
}

public sealed class CloudUploadResultData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("slot")]
    public string Slot { get; set; } = string.Empty;

    [JsonPropertyName("overwritten")]
    public bool Overwritten { get; set; }

    [JsonPropertyName("compressed_size")]
    public long CompressedSize { get; set; }

    [JsonPropertyName("raw_size")]
    public long RawSize { get; set; }

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string UpdatedAt { get; set; } = string.Empty;
}

public sealed class CloudPullResult
{
    public bool Ok { get; init; }
    public int HttpStatusCode { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public string Slot { get; init; } = string.Empty;
    public string Sha256 { get; init; } = string.Empty;
    public long CompressedBytes { get; init; }
    public long RawBytes { get; init; }
    public string JsonContent { get; init; } = string.Empty;
}

public sealed class PlayTimeSegment
{
    [JsonPropertyName("start")]
    public long Start { get; set; }

    [JsonPropertyName("end")]
    public long End { get; set; }
}

public sealed class PlayTimeCloudInfo
{
    [JsonPropertyName("export_app")]
    public string ExportApp { get; set; } = "FufuLauncher";

    [JsonPropertyName("export_app_version")]
    public string ExportAppVersion { get; set; } = string.Empty;

    [JsonPropertyName("playtime_version")]
    public string PlaytimeVersion { get; set; } = "v1.0";

    [JsonPropertyName("export_timestamp")]
    public long ExportTimestamp { get; set; }
}

public sealed class PlayTimeCloudDocument
{
    [JsonPropertyName("info")]
    public PlayTimeCloudInfo Info { get; set; } = new();

    [JsonPropertyName("segments")]
    public List<PlayTimeSegment> Segments { get; set; } = new();

    [JsonPropertyName("daily_seconds")]
    public Dictionary<string, long> DailySeconds { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("legacy_daily_seconds")]
    public Dictionary<string, long>? LegacyDailySeconds { get; set; }
}

public sealed class PlayTimeMergeResult
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public long BeforeTotalSeconds { get; init; }
    public long AfterTotalSeconds { get; init; }
    public long AddedSeconds { get; init; }
    public long IncomingSegmentSeconds { get; init; }
    public long OverlapDeduplicatedSeconds { get; init; }
    public int ResultingSegmentCount { get; init; }
    public int UpdatedDaysCount { get; init; }
}

public sealed class PlayTimeLocalSummary
{
    public int TotalDays { get; init; }
    public long TotalSeconds { get; init; }
    public int SegmentCount { get; init; }
    public long SegmentedSeconds { get; init; }
    public long LegacySeconds { get; init; }
}

public sealed class CloudRenameResultData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("old_slot")]
    public string OldSlot { get; set; } = string.Empty;

    [JsonPropertyName("new_slot")]
    public string NewSlot { get; set; } = string.Empty;

    [JsonPropertyName("overwritten")]
    public bool Overwritten { get; set; }
}

public sealed class CloudDeleteResultData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("slot")]
    public string Slot { get; set; } = string.Empty;

    [JsonPropertyName("freed_bytes")]
    public long FreedBytes { get; set; }
}

public sealed class CloudDeleteAllResultData
{
    [JsonPropertyName("deleted_files")]
    public int DeletedFiles { get; set; }

    [JsonPropertyName("freed_bytes")]
    public long FreedBytes { get; set; }
}

public sealed class CloudQuotaCounter
{
    [JsonPropertyName("used")]
    public long Used { get; set; }

    [JsonPropertyName("limit")]
    public long Limit { get; set; }

    [JsonPropertyName("remaining")]
    public long Remaining { get; set; }
}

public sealed class CloudQuotaDaily
{
    [JsonPropertyName("ops")]
    public CloudQuotaCounter Ops { get; set; } = new();

    [JsonPropertyName("uploads")]
    public CloudQuotaCounter Uploads { get; set; } = new();

    [JsonPropertyName("deletes")]
    public CloudQuotaCounter Deletes { get; set; } = new();
}

public sealed class CloudQuotaStorage
{
    [JsonPropertyName("r2_bytes")]
    public CloudQuotaCounter R2Bytes { get; set; } = new();

    [JsonPropertyName("files")]
    public CloudQuotaCounter Files { get; set; } = new();
}

public sealed class CloudQuotaLimits
{
    [JsonPropertyName("user_daily_max_ops")]
    public long UserDailyMaxOps { get; set; }

    [JsonPropertyName("user_daily_max_uploads")]
    public long UserDailyMaxUploads { get; set; }

    [JsonPropertyName("user_daily_max_deletes")]
    public long UserDailyMaxDeletes { get; set; }

    [JsonPropertyName("max_user_r2_bytes")]
    public long MaxUserR2Bytes { get; set; }

    [JsonPropertyName("max_user_files")]
    public long MaxUserFiles { get; set; }
}

public sealed class CloudQuotaUpgradeInfo
{
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("activated_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? ActivatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    [JsonConverter(typeof(FlexibleTimestampConverter))]
    public string? ExpiresAt { get; set; }

    [JsonPropertyName("expires_in_seconds")]
    public long ExpiresInSeconds { get; set; }

    [JsonPropertyName("expanded_by")]
    public CloudQuotaLimits? ExpandedBy { get; set; }
}

public sealed class CloudQuotaData
{
    [JsonPropertyName("utc_date")]
    public string UtcDate { get; set; } = string.Empty;

    [JsonPropertyName("resets_at")]
    public long ResetsAt { get; set; }

    [JsonPropertyName("daily")]
    public CloudQuotaDaily Daily { get; set; } = new();

    [JsonPropertyName("storage")]
    public CloudQuotaStorage Storage { get; set; } = new();

    [JsonPropertyName("base_limits")]
    public CloudQuotaLimits BaseLimits { get; set; } = new();

    [JsonPropertyName("effective_limits")]
    public CloudQuotaLimits EffectiveLimits { get; set; } = new();

    [JsonPropertyName("upgrade")]
    public CloudQuotaUpgradeInfo Upgrade { get; set; } = new();
}

public sealed class CloudActivationResultData
{
    [JsonPropertyName("effective_limits")]
    public CloudQuotaLimits? EffectiveLimits { get; set; }

    [JsonPropertyName("upgrade")]
    public CloudQuotaUpgradeInfo? Upgrade { get; set; }
}

public sealed class CloudPersistedSession
{
    public string Email { get; set; } = string.Empty;
    public string Stoken { get; set; } = string.Empty;
    public long StokenExpiresAtUnix { get; set; }
}
