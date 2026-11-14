using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Models;

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

public sealed class HutaoUidRankItem
{
    public string Uid
    {
        get;
        init;
    } = string.Empty;

    public HutaoRecordRankData? Rank
    {
        get;
        init;
    }

    public string DamageAvatarName
    {
        get;
        init;
    } = string.Empty;

    public string TakeDamageAvatarName
    {
        get;
        init;
    } = string.Empty;
}

public sealed class HutaoUidRankDisplayItem
{
    public string RoleTitle
    {
        get;
        init;
    } = string.Empty;

    public string DamageLabel
    {
        get;
        init;
    } = string.Empty;

    public string DamageText
    {
        get;
        init;
    } = "-";

    public string TakeDamageLabel
    {
        get;
        init;
    } = string.Empty;

    public string TakeDamageText
    {
        get;
        init;
    } = "-";

    public bool HasRankData
    {
        get;
        init;
    }

    public string NoRecordHintText
    {
        get;
        init;
    } = string.Empty;

    public Visibility RankGridVisibility => HasRankData ? Visibility.Visible : Visibility.Collapsed;

    public Visibility NoRecordVisibility => HasRankData ? Visibility.Collapsed : Visibility.Visible;
}

public sealed class HutaoStatisticsBundle
{
    public bool IsLastPeriod
    {
        get;
        init;
    }

    public IReadOnlyList<string> QueriedUids
    {
        get;
        init;
    } = Array.Empty<string>();

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

    public IReadOnlyList<HutaoUidRankItem> UidRanks
    {
        get;
        init;
    } = Array.Empty<HutaoUidRankItem>();

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
