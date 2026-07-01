/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public class GachaLogResponse
{
    [JsonPropertyName("retcode")]
    public int Retcode
    {
        get; set;
    }

    [JsonPropertyName("message")]
    public string Message
    {
        get; set;
    }

    [JsonPropertyName("data")]
    public GachaLogData Data
    {
        get; set;
    }
}

public class GachaLogData
{
    [JsonPropertyName("list")]
    public List<GachaLogItem> List { get; set; } = new();
}

public class GachaLogItem
{
    [JsonPropertyName("uid")]
    public string Uid
    {
        get; set;
    }

    [JsonPropertyName("gacha_type")]
    public string GachaType
    {
        get; set;
    }

    [JsonPropertyName("item_id")]
    public string ItemId
    {
        get; set;
    }

    [JsonPropertyName("count")]
    public string Count
    {
        get; set;
    }

    [JsonPropertyName("time")]
    public string Time
    {
        get; set;
    }

    [JsonPropertyName("name")]
    public string Name
    {
        get; set;
    }

    [JsonPropertyName("lang")]
    public string Lang
    {
        get; set;
    }

    [JsonPropertyName("item_type")]
    public string ItemType
    {
        get; set;
    }

    [JsonPropertyName("rank_type")]
    public string RankType
    {
        get; set;
    }

    [JsonPropertyName("id")]
    public string Id
    {
        get; set;
    }
}

public class GachaStatistic
{
    public string PoolName
    {
        get; set;
    }
    public int TotalCount
    {
        get; set;
    }
    public int FiveStarCount
    {
        get; set;
    }
    public int FourStarCount
    {
        get; set;
    }
    public int CurrentPity
    {
        get; set;
    }
    public int CurrentPity4
    {
        get; set;
    }

    public List<FiveStarRecord> FiveStarRecords { get; set; } = new();
    public List<FiveStarRecord> FourStarRecords { get; set; } = new();
}

public class FiveStarRecord
{
    public string Name
    {
        get; set;
    }
    public string ItemId
    {
        get; set;
    }
    public int PityUsed
    {
        get; set;
    }
    public string Time
    {
        get; set;
    }
    public int Rank
    {
        get; set;
    }
    public bool WasPreviousLost
    {
        get; set;
    }
}

public class GachaPoolItem
{
    [JsonPropertyName("itemId")]
    public int ItemId { get; set; }

    [JsonPropertyName("imageUrl")]
    public string ImageUrl { get; set; }

    [JsonPropertyName("itemType")]
    public string ItemType { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("rankType")]
    public int RankType { get; set; }
}

public class GachaPoolMetadata
{
    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("start")]
    public string Start { get; set; }

    [JsonPropertyName("end")]
    public string End { get; set; }

    [JsonPropertyName("items")]
    public List<GachaPoolItem> Items { get; set; } = new();
}

public enum PityStatus
{
    None,
    SmallPity,
    LostPity,
    Guaranteed,
    Up
}

public class WishHistoryResponse
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("result")]
    public List<WishBannerItem> Result { get; set; }

    [JsonPropertyName("weapon")]
    public List<WishBannerItem> Weapon { get; set; }

    [JsonPropertyName("avatar_list")]
    public Dictionary<string, string> AvatarList { get; set; }
}

public class WishBannerItem
{
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("star5_role")]
    public List<string> Star5Role { get; set; }

    [JsonPropertyName("star4_role")]
    public List<string> Star4Role { get; set; }

    [JsonPropertyName("time")]
    public string Time { get; set; }
}
