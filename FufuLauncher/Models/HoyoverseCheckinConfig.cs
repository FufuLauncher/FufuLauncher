using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public class HoyoverseCheckinConfig
{
    [JsonPropertyName("Account")]
    public AccountConfig Account { get; set; } = new();

    [JsonPropertyName("Device")]
    public DeviceConfig Device { get; set; } = new();

    [JsonPropertyName("Games")]
    public GamesConfig Games { get; set; } = new();
}

public class AccountConfig
{
    [JsonPropertyName("Cookie")]
    public string Cookie { get; set; } = "";

    [JsonPropertyName("Stuid")]
    public string Stuid { get; set; } = "";

    [JsonPropertyName("Stoken")]
    public string Stoken { get; set; } = "";

    [JsonPropertyName("Mid")]
    public string Mid { get; set; } = "";
}

public class DeviceConfig
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "Xiaomi MI 6";

    [JsonPropertyName("Model")]
    public string Model { get; set; } = "Mi 6";

    [JsonPropertyName("Id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Fp")]
    public string Fp { get; set; } = "";
}

public class GamesConfig
{
    [JsonPropertyName("Cn")]
    public CnConfig Cn { get; set; } = new();
}

public class CnConfig
{
    [JsonPropertyName("Enable")]
    public bool Enable { get; set; } = true;

    [JsonPropertyName("UserAgent")]
    public string UserAgent { get; set; } = "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36";

    [JsonPropertyName("Retries")]
    public int Retries { get; set; } = 3;

    [JsonPropertyName("Genshin")]
    public GameConfig Genshin { get; set; } = new();
}

public class GameConfig
{
    [JsonPropertyName("Checkin")]
    public bool Checkin { get; set; } = true;

    [JsonPropertyName("BlackList")]
    public List<string> BlackList { get; set; } = new();
}

public class ApiResponse<T>
{
    [JsonPropertyName("retcode")]
    public int RetCode
    {
        get; set;
    }

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("data")]
    public T Data
    {
        get; set;
    }
}

public class CheckinRewardsData
{
    [JsonPropertyName("awards")]
    public List<RewardItem> Awards { get; set; } = new();
}

public class IsSignData
{
    [JsonPropertyName("total_sign_day")]
    public int TotalSignDay
    {
        get; set;
    }

    [JsonPropertyName("today")]
    public string Today { get; set; } = "";

    [JsonPropertyName("is_sign")]
    public bool IsSign
    {
        get; set;
    }

    [JsonPropertyName("first_bind")]
    public bool FirstBind
    {
        get; set;
    }
}

public class AccountInfoData
{
    [JsonPropertyName("list")]
    public List<AccountItem> List { get; set; } = new();
}

public class RewardItem
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("cnt")]
    public int Count
    {
        get; set;
    }
}

public class AccountItem
{
    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = "";

    [JsonPropertyName("game_uid")]
    public string GameUid { get; set; } = "";

    [JsonPropertyName("region")]
    public string Region { get; set; } = "";
}

public class SignResponseData
{
    [JsonPropertyName("success")]
    public int Success
    {
        get; set;
    }

    [JsonPropertyName("gt")]
    public string Gt { get; set; } = "";

    [JsonPropertyName("challenge")]
    public string Challenge { get; set; } = "";
}