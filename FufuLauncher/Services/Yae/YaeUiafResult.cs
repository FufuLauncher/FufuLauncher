/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace FufuLauncher.Services.Yae;

public sealed class YaeUiafResult
{
    [JsonPropertyName("info")]
    public YaeUiafInfo Info
    {
        get;
        set;
    } = new();

    [JsonPropertyName("list")]
    public List<YaeUiafItem> List
    {
        get;
        set;
    } = [];
}