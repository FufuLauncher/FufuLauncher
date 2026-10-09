/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace MihoyoBBS;

public class AccountItem
{
    [JsonPropertyName("game_biz")]
    public string GameBiz { get; set; } = "";

    [JsonPropertyName("nickname")]
    public string Nickname
    {
        get;
        set;
    }

    [JsonPropertyName("game_uid")]
    public string GameUid
    {
        get;
        set;
    }

    [JsonPropertyName("region")]
    public string Region
    {
        get;
        set;
    }
}
