/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace FufuLauncher.Services.Yae;

public sealed class YaeUiafItem
{
    [JsonPropertyName("id")]
    public int Id
    {
        get;
        set;
    }

    [JsonPropertyName("current")]
    public int Current
    {
        get;
        set;
    }

    [JsonPropertyName("status")]
    public int Status
    {
        get;
        set;
    }

    [JsonPropertyName("timestamp")]
    public long Timestamp
    {
        get;
        set;
    }
}