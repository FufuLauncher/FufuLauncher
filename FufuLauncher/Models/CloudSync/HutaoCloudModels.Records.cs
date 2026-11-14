using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

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
