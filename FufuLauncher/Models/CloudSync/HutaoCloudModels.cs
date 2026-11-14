using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public sealed class HutaoApiResponse<T>
{
    [JsonPropertyName("retcode")]
    public int RetCode
    {
        get;
        set;
    }

    [JsonPropertyName("message")]
    public string? Message
    {
        get;
        set;
    }

    [JsonPropertyName("data")]
    public T? Data
    {
        get;
        set;
    }

    [JsonPropertyName("l10nKey")]
    public string? L10nKey
    {
        get;
        set;
    }

    [JsonIgnore]
    public bool IsSuccess => RetCode == 0;
}

public sealed class HutaoTokenSet
{
    [JsonPropertyName("AccessToken")]
    public string AccessToken
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("ExpiresIn")]
    public int ExpiresIn
    {
        get;
        set;
    }

    [JsonPropertyName("RefreshToken")]
    public string RefreshToken
    {
        get;
        set;
    } = string.Empty;
}

public sealed class HutaoUserInfo
{
    [JsonPropertyName("NormalizedUserName")]
    public string? NormalizedUserName
    {
        get;
        set;
    }

    [JsonPropertyName("UserName")]
    public string? UserName
    {
        get;
        set;
    }

    [JsonPropertyName("CdnExpireAt")]
    public DateTimeOffset? CdnExpireAt
    {
        get;
        set;
    }

    [JsonPropertyName("GachaLogExpireAt")]
    public DateTimeOffset? GachaLogExpireAt
    {
        get;
        set;
    }

    [JsonPropertyName("IsLicensedDeveloper")]
    public bool IsLicensedDeveloper
    {
        get;
        set;
    }

    [JsonPropertyName("IsMaintainer")]
    public bool IsMaintainer
    {
        get;
        set;
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields
    {
        get;
        set;
    }

    [JsonIgnore]
    public bool HasValidGachaPlan =>
        IsMaintainer || IsLicensedDeveloper || (GachaLogExpireAt.HasValue && GachaLogExpireAt.Value > DateTimeOffset.UtcNow);

    [JsonIgnore]
    public bool HasValidCdnPlan =>
        IsMaintainer || IsLicensedDeveloper || (CdnExpireAt.HasValue && CdnExpireAt.Value > DateTimeOffset.UtcNow);
}

public sealed class HutaoLoggedInDevice
{
    [JsonPropertyName("DeviceId")]
    public string? DeviceId
    {
        get;
        set;
    }

    [JsonPropertyName("Id")]
    public string? Id
    {
        get;
        set;
    }

    [JsonPropertyName("DeviceName")]
    public string? DeviceName
    {
        get;
        set;
    }

    [JsonPropertyName("Name")]
    public string? Name
    {
        get;
        set;
    }

    [JsonPropertyName("OperatingSystem")]
    public string? OperatingSystem
    {
        get;
        set;
    }

    [JsonPropertyName("OS")]
    public string? Os
    {
        get;
        set;
    }

    [JsonPropertyName("Platform")]
    public string? Platform
    {
        get;
        set;
    }

    [JsonPropertyName("LastLoginTime")]
    public string? LastLoginTime
    {
        get;
        set;
    }

    [JsonPropertyName("LoginTime")]
    public string? LoginTime
    {
        get;
        set;
    }

    [JsonPropertyName("CreatedAt")]
    public string? CreatedAt
    {
        get;
        set;
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields
    {
        get;
        set;
    }

    [JsonIgnore]
    public string EffectiveDeviceId =>
        FirstNonEmpty(DeviceId, Id, ReadExtraString("device_id"), ReadExtraString("id"));

    [JsonIgnore]
    public string EffectiveDeviceName =>
        FirstNonEmpty(DeviceName, Name, ReadExtraString("device_name"), ReadExtraString("name"));

    [JsonIgnore]
    public string EffectiveOs =>
        FirstNonEmpty(OperatingSystem, Os, Platform, ReadExtraString("os"), ReadExtraString("operating_system"), ReadExtraString("system"));

    [JsonIgnore]
    public string EffectiveLoginTime =>
        FirstNonEmpty(LastLoginTime, LoginTime, CreatedAt, ReadExtraString("last_login_time"), ReadExtraString("login_time"), ReadExtraString("created_at"));

    private string? ReadExtraString(string key)
    {
        if (ExtraFields == null) return null;
        foreach (var kv in ExtraFields)
        {
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value.ValueKind == JsonValueKind.String ? kv.Value.GetString() : kv.Value.ToString();
            }
        }
        return null;
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim();
        }
        return string.Empty;
    }
}

public sealed class HutaoGachaEntry
{
    [JsonPropertyName("Uid")]
    public string Uid
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("ItemCount")]
    public int ItemCount
    {
        get;
        set;
    }

    [JsonPropertyName("Excluded")]
    public bool Excluded
    {
        get;
        set;
    }
}

public sealed class HutaoGachaItem
{
    [JsonPropertyName("GachaType")]
    public int GachaType
    {
        get;
        set;
    }

    [JsonPropertyName("QueryType")]
    public int QueryType
    {
        get;
        set;
    }

    [JsonPropertyName("ItemId")]
    public int ItemId
    {
        get;
        set;
    }

    [JsonPropertyName("Time")]
    public string Time
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("Id")]
    public long Id
    {
        get;
        set;
    }
}

public sealed class HutaoAbyssItemRank
{
    [JsonPropertyName("AvatarId")]
    public int AvatarId
    {
        get;
        set;
    }

    [JsonPropertyName("Value")]
    public long Value
    {
        get;
        set;
    }
}

public sealed class HutaoAbyssBattle
{
    [JsonPropertyName("Index")]
    public int Index
    {
        get;
        set;
    }

    [JsonPropertyName("Avatars")]
    public List<int> Avatars
    {
        get;
        set;
    } = new();
}

public sealed class HutaoAbyssLevel
{
    [JsonPropertyName("Index")]
    public int Index
    {
        get;
        set;
    }

    [JsonPropertyName("Star")]
    public int Star
    {
        get;
        set;
    }

    [JsonPropertyName("Battles")]
    public List<HutaoAbyssBattle> Battles
    {
        get;
        set;
    } = new();
}

public sealed class HutaoAbyssFloor
{
    [JsonPropertyName("Index")]
    public int Index
    {
        get;
        set;
    }

    [JsonPropertyName("Star")]
    public int Star
    {
        get;
        set;
    }

    [JsonPropertyName("Levels")]
    public List<HutaoAbyssLevel> Levels
    {
        get;
        set;
    } = new();
}

public sealed class HutaoSpiralAbyssData
{
    [JsonPropertyName("ScheduleId")]
    public int ScheduleId
    {
        get;
        set;
    }

    [JsonPropertyName("TotalBattleTimes")]
    public int TotalBattleTimes
    {
        get;
        set;
    }

    [JsonPropertyName("TotalWinTimes")]
    public int TotalWinTimes
    {
        get;
        set;
    }

    [JsonPropertyName("Damage")]
    public HutaoAbyssItemRank Damage
    {
        get;
        set;
    } = new();

    [JsonPropertyName("Defeat")]
    public HutaoAbyssItemRank Defeat
    {
        get;
        set;
    } = new();

    [JsonPropertyName("EnergySkill")]
    public HutaoAbyssItemRank EnergySkill
    {
        get;
        set;
    } = new();

    [JsonPropertyName("NormalSkill")]
    public HutaoAbyssItemRank NormalSkill
    {
        get;
        set;
    } = new();

    [JsonPropertyName("TakeDamage")]
    public HutaoAbyssItemRank TakeDamage
    {
        get;
        set;
    } = new();

    [JsonPropertyName("Floors")]
    public List<HutaoAbyssFloor> Floors
    {
        get;
        set;
    } = new();
}

public sealed class HutaoRecordUploadRequest
{
    [JsonPropertyName("Uid")]
    public string Uid
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("Identity")]
    public string Identity
    {
        get;
        set;
    } = "FufuLauncher";

    [JsonPropertyName("SpiralAbyss")]
    public HutaoSpiralAbyssData SpiralAbyss
    {
        get;
        set;
    } = new();

    [JsonPropertyName("Avatars")]
    public List<HutaoSimpleAvatar> Avatars
    {
        get;
        set;
    } = new();

    [JsonPropertyName("ReservedUserName")]
    public string ReservedUserName
    {
        get;
        set;
    } = string.Empty;
}

public sealed class HutaoRoleCombatUploadRequest
{
    [JsonPropertyName("Version")]
    public int Version
    {
        get;
        set;
    } = 1;

    [JsonPropertyName("Uid")]
    public string Uid
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("Identity")]
    public string Identity
    {
        get;
        set;
    } = "FufuLauncher";

    [JsonPropertyName("BackupAvatars")]
    public List<int> BackupAvatars
    {
        get;
        set;
    } = new();

    [JsonPropertyName("ScheduleId")]
    public int ScheduleId
    {
        get;
        set;
    }
}

public sealed class HutaoOperationResult<T>
{
    public bool Ok
    {
        get;
        init;
    }

    public string Message
    {
        get;
        init;
    } = string.Empty;

    public T? Data
    {
        get;
        init;
    }

    public bool AuthInvalidated
    {
        get;
        init;
    }
}

public sealed class HutaoDeviceListItem
{
    public string DeviceId
    {
        get;
        init;
    } = string.Empty;

    public string TitleText
    {
        get;
        init;
    } = string.Empty;

    public string SubtitleText
    {
        get;
        init;
    } = string.Empty;

    public string BadgeText
    {
        get;
        init;
    } = string.Empty;

    public bool IsCurrentDevice
    {
        get;
        init;
    }
}

public sealed class HutaoAccountExtraFieldItem
{
    public string Label
    {
        get;
        init;
    } = string.Empty;

    public string Value
    {
        get;
        init;
    } = string.Empty;
}

public sealed class AccountBoundRoleOption
{
    public bool IsAllRolesOption
    {
        get;
        init;
    }

    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public string Nickname
    {
        get;
        init;
    } = string.Empty;

    public string ServerName
    {
        get;
        init;
    } = string.Empty;

    public int Level
    {
        get;
        init;
    }

    public bool IsActiveRole
    {
        get;
        init;
    }

    public string DisplayText
    {
        get;
        init;
    } = string.Empty;

    public string ShortDisplayText
    {
        get;
        init;
    } = string.Empty;

    public override string ToString() => DisplayText;
}

public sealed partial class HutaoGachaEntrySelectionItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _roleTitle = string.Empty;

    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public int ItemCount
    {
        get;
        init;
    }

    public bool Excluded
    {
        get;
        init;
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(RoleTitle) ? RoleTitle : $"UID {Uid}";

    partial void OnRoleTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    public string CountSummary => string.Format(
        "HutaoSync_GachaEntryCountFormat".GetLocalized(),
        ItemCount);
}

public sealed partial class FufuCloudUigfUidItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected = true;
    [ObservableProperty] private string _roleTitle = string.Empty;

    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public int RecordCount
    {
        get;
        init;
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(RoleTitle) ? RoleTitle : $"UID {Uid}";

    partial void OnRoleTitleChanged(string value) => OnPropertyChanged(nameof(DisplayTitle));

    public string DetailText => string.Format(
        "HutaoSync_GachaEntryCountFormat".GetLocalized(),
        RecordCount);
}

public sealed partial class FufuCloudChallengeItem : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _roleDisplayName = string.Empty;

    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public int Mode
    {
        get;
        init;
    }

    public string ScheduleId
    {
        get;
        init;
    } = string.Empty;

    public bool IsLikelyCurrent
    {
        get;
        init;
    }

    public bool CanUpload
    {
        get;
        init;
    }

    public string ModeLabel
    {
        get;
        init;
    } = string.Empty;

    public string TitleText => !string.IsNullOrWhiteSpace(RoleDisplayName)
        ? $"{ModeLabel} · {RoleDisplayName}"
        : $"{ModeLabel} · UID {Uid}";

    partial void OnRoleDisplayNameChanged(string value) => OnPropertyChanged(nameof(TitleText));

    public string PeriodText
    {
        get;
        init;
    } = string.Empty;

    public string SummaryText
    {
        get;
        init;
    } = string.Empty;

    public string StatusBadgeText
    {
        get;
        init;
    } = string.Empty;

    public string Payload
    {
        get;
        init;
    } = string.Empty;
}

public sealed class HutaoBridgeTransferSummary
{
    public string TargetSlot
    {
        get;
        init;
    } = string.Empty;

    public int UidCount
    {
        get;
        init;
    }

    public int TransferredRecords
    {
        get;
        init;
    }

    public int TotalMergedRecords
    {
        get;
        init;
    }

    public int SkippedRecords
    {
        get;
        init;
    }
}

public sealed class HutaoChallengeUploadSummary
{
    public int UploadedCount
    {
        get;
        init;
    }

    public int GachaExtendedCount
    {
        get;
        init;
    }

    public int SkippedPastCount
    {
        get;
        init;
    }

    public int SkippedIncompleteCount
    {
        get;
        init;
    }

    public int FailedCount
    {
        get;
        init;
    }

    public string DetailMessage
    {
        get;
        init;
    } = string.Empty;
}

public sealed class HutaoPersistedSession
{
    public string Email
    {
        get;
        set;
    } = string.Empty;

    public string AccessToken
    {
        get;
        set;
    } = string.Empty;

    public long AccessTokenExpiresAtUnix
    {
        get;
        set;
    }

    public string RefreshToken
    {
        get;
        set;
    } = string.Empty;
}

public sealed class HutaoJsonPayloadHelper
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed class HutaoSimpleAvatar
{
    [JsonPropertyName("AvatarId")]
    public int AvatarId
    {
        get;
        set;
    }

    [JsonPropertyName("WeaponId")]
    public int WeaponId
    {
        get;
        set;
    }

    [JsonPropertyName("ReliquarySetIds")]
    public List<int> ReliquarySetIds
    {
        get;
        set;
    } = new();

    [JsonPropertyName("ActivedConstellationNumber")]
    public int ActivedConstellationNumber
    {
        get;
        set;
    }
}

public sealed class HutaoAbyssOverview
{
    [JsonPropertyName("ScheduleId")]
    public int ScheduleId
    {
        get;
        set;
    }

    [JsonPropertyName("RecordTotal")]
    public int RecordTotal
    {
        get;
        set;
    }

    [JsonPropertyName("SpiralAbyssTotal")]
    public int SpiralAbyssTotal
    {
        get;
        set;
    }

    [JsonPropertyName("SpiralAbyssFullStar")]
    public int SpiralAbyssFullStar
    {
        get;
        set;
    }

    [JsonPropertyName("SpiralAbyssPassed")]
    public int SpiralAbyssPassed
    {
        get;
        set;
    }

    [JsonPropertyName("SpiralAbyssStarTotal")]
    public int SpiralAbyssStarTotal
    {
        get;
        set;
    }

    [JsonPropertyName("SpiralAbyssBattleTotal")]
    public int SpiralAbyssBattleTotal
    {
        get;
        set;
    }

    [JsonPropertyName("Timestamp")]
    public long Timestamp
    {
        get;
        set;
    }

    [JsonPropertyName("TimeTotal")]
    public double TimeTotal
    {
        get;
        set;
    }

    [JsonPropertyName("TimeAverage")]
    public double TimeAverage
    {
        get;
        set;
    }
}

public sealed class HutaoItemRateIntDouble
{
    [JsonPropertyName("Item")]
    public int Item
    {
        get;
        set;
    }

    [JsonPropertyName("Rate")]
    public double Rate
    {
        get;
        set;
    }
}

public sealed class HutaoAbyssAvatarRateFloor
{
    [JsonPropertyName("Floor")]
    public int Floor
    {
        get;
        set;
    }

    [JsonPropertyName("Ranks")]
    public List<HutaoItemRateIntDouble> Ranks
    {
        get;
        set;
    } = new();
}

public sealed class HutaoAbyssAvatarHolding
{
    [JsonPropertyName("AvatarId")]
    public int AvatarId
    {
        get;
        set;
    }

    [JsonPropertyName("HoldingRate")]
    public double HoldingRate
    {
        get;
        set;
    }

    [JsonPropertyName("Constellations")]
    public List<HutaoItemRateIntDouble> Constellations
    {
        get;
        set;
    } = new();
}

public sealed class HutaoTeamRateEntry
{
    [JsonPropertyName("Item")]
    public string Item
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("Rate")]
    public int Rate
    {
        get;
        set;
    }
}

public sealed class HutaoAbyssTeamFloor
{
    [JsonPropertyName("Floor")]
    public int Floor
    {
        get;
        set;
    }

    [JsonPropertyName("Up")]
    public List<HutaoTeamRateEntry> Up
    {
        get;
        set;
    } = new();

    [JsonPropertyName("Down")]
    public List<HutaoTeamRateEntry> Down
    {
        get;
        set;
    } = new();
}

public sealed class HutaoRoleCombatStatistics
{
    [JsonPropertyName("ScheduleId")]
    public int ScheduleId
    {
        get;
        set;
    }

    [JsonPropertyName("RecordTotal")]
    public int RecordTotal
    {
        get;
        set;
    }

    [JsonPropertyName("Timestamp")]
    public long Timestamp
    {
        get;
        set;
    }

    [JsonPropertyName("BackupAvatarRates")]
    public List<HutaoItemRateIntDouble> BackupAvatarRates
    {
        get;
        set;
    } = new();
}

public sealed class HutaoRankValue
{
    [JsonPropertyName("Item")]
    public int Item
    {
        get;
        set;
    }

    [JsonPropertyName("Value")]
    public int Value
    {
        get;
        set;
    }

    [JsonPropertyName("Rate")]
    public double Rate
    {
        get;
        set;
    }

    [JsonPropertyName("RateOnAvatar")]
    public double RateOnAvatar
    {
        get;
        set;
    }
}

public sealed class HutaoRecordRankData
{
    [JsonPropertyName("Damage")]
    public HutaoRankValue? Damage
    {
        get;
        set;
    }

    [JsonPropertyName("TakeDamage")]
    public HutaoRankValue? TakeDamage
    {
        get;
        set;
    }
}

public sealed class HutaoStatisticsBundle
{
    public bool IsLastPeriod
    {
        get;
        init;
    }

    public string QueriedUid
    {
        get;
        init;
    } = string.Empty;

    public HutaoAbyssOverview? Overview
    {
        get;
        init;
    }

    public HutaoRoleCombatStatistics? RoleCombat
    {
        get;
        init;
    }

    public HutaoRecordRankData? UidRank
    {
        get;
        init;
    }

    public Dictionary<int, List<HutaoRankDisplayItem>> UtilizationByFloor
    {
        get;
        init;
    } = new();

    public Dictionary<int, List<HutaoRankDisplayItem>> AttendanceByFloor
    {
        get;
        init;
    } = new();

    public List<HutaoRankDisplayItem> HoldingRates
    {
        get;
        init;
    } = new();

    public Dictionary<int, List<HutaoRankDisplayItem>> TeamsByFloor
    {
        get;
        init;
    } = new();

    public List<HutaoRankDisplayItem> RoleCombatRates
    {
        get;
        init;
    } = new();

    public string UidDamageAvatarName
    {
        get;
        init;
    } = string.Empty;

    public string UidTakeDamageAvatarName
    {
        get;
        init;
    } = string.Empty;
}

public sealed class HutaoRankDisplayItem
{
    public string RankNumberText
    {
        get;
        init;
    } = string.Empty;

    public string PrimaryText
    {
        get;
        init;
    } = string.Empty;

    public string SecondaryText
    {
        get;
        init;
    } = string.Empty;

    public string MetricText
    {
        get;
        init;
    } = string.Empty;

    public string DetailText
    {
        get;
        init;
    } = string.Empty;
}
