using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public class GachaLogResponse
{
    [JsonPropertyName("retcode")]
    public int Retcode { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; }

    [JsonPropertyName("data")]
    public GachaLogData Data { get; set; }
}

public class GachaLogData
{
    [JsonPropertyName("list")]
    public List<GachaLogItem> List { get; set; } = new();
}

public class GachaLogItem
{
    [JsonPropertyName("uid")]
    public string Uid { get; set; }

    [JsonPropertyName("gacha_type")]
    public string GachaType { get; set; }

    [JsonPropertyName("item_id")]
    public string ItemId { get; set; }

    [JsonPropertyName("count")]
    public string Count { get; set; }

    [JsonPropertyName("time")]
    public string Time { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("lang")]
    public string Lang { get; set; }

    [JsonPropertyName("item_type")]
    public string ItemType { get; set; }

    [JsonPropertyName("rank_type")]
    public string RankType { get; set; }

    [JsonPropertyName("id")]
    public string Id { get; set; }
}

public class GachaStatistic
{
    public string PoolName { get; set; }
    public int TotalCount { get; set; }
    public int FiveStarCount { get; set; }
    public int FourStarCount { get; set; }
    public int CurrentPity { get; set; }
    public int CurrentPity4 { get; set; }
    
    public List<FiveStarRecord> FiveStarRecords { get; set; } = new();
    public List<FiveStarRecord> FourStarRecords { get; set; } = new(); 
}

public class FiveStarRecord
{
    public string Name { get; set; }
    public int PityUsed { get; set; }
    public string Time { get; set; }
    public int Rank { get; set; }
}