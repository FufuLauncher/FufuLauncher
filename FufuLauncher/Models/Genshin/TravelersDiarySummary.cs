using System.Text.Json.Serialization;

namespace FufuLauncher.Models.Genshin;

public class TravelersDiarySummary
{
    [JsonPropertyName("retcode")]
    public int Retcode
    {
        get; set;
    }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public TravelersDiaryData Data { get; set; } = new();
}

public class TravelersDiaryData
{
    [JsonPropertyName("uid")]
    public int Uid
    {
        get; set;
    }

    [JsonPropertyName("region")]
    public string Region { get; set; } = "";

    [JsonPropertyName("account_id")]
    public int AccountId
    {
        get; set;
    }

    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("month")]
    public int Month
    {
        get; set;
    }

    [JsonPropertyName("optional_month")]
    public List<int> OptionalMonth { get; set; } = new();

    [JsonPropertyName("day_data")]
    public DayData DayData { get; set; } = new();

    [JsonPropertyName("month_data")]
    public MonthData MonthData { get; set; } = new();
}

public class DayData
{
    [JsonPropertyName("current_primogems")]
    public int CurrentPrimogems
    {
        get; set;
    }

    [JsonPropertyName("current_mora")]
    public int CurrentMora
    {
        get; set;
    }

    [JsonPropertyName("last_primogems")]
    public int LastPrimogems
    {
        get; set;
    }

    [JsonPropertyName("last_mora")]
    public int LastMora
    {
        get; set;
    }
}

public class MonthData
{
    [JsonPropertyName("current_primogems")]
    public int CurrentPrimogems
    {
        get; set;
    }

    [JsonPropertyName("current_mora")]
    public int CurrentMora
    {
        get; set;
    }

    [JsonPropertyName("last_primogems")]
    public int LastPrimogems
    {
        get; set;
    }

    [JsonPropertyName("last_mora")]
    public int LastMora
    {
        get; set;
    }

    [JsonPropertyName("current_primogems_level")]
    public int CurrentPrimogemsLevel
    {
        get; set;
    }

    [JsonPropertyName("primogems_rate")]
    public int PrimogemsRate
    {
        get; set;
    }

    [JsonPropertyName("mora_rate")]
    public int MoraRate
    {
        get; set;
    }

    [JsonPropertyName("group_by")]
    public List<IncomeSource> GroupBy { get; set; } = new();
}

public class IncomeSource
{
    [JsonPropertyName("action_id")]
    public int ActionId
    {
        get; set;
    }

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("num")]
    public int Num
    {
        get; set;
    }

    [JsonPropertyName("percent")]
    public int Percent
    {
        get; set;
    }
}