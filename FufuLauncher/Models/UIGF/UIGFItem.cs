/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text.Json.Serialization;

namespace FufuLauncher.Models.UIGF;

public class UIGFItem
{
    [JsonPropertyName("uigf_gacha_type")]
    public string UigfGachaType { get; set; }

    [JsonPropertyName("gacha_type")]
    [JsonConverter(typeof(JsonStringOrNumberConverter))]
    public string GachaType { get; set; }

    [JsonPropertyName("item_id")]
    [JsonConverter(typeof(JsonStringOrNumberConverter))]
    public string ItemId { get; set; }

    [JsonPropertyName("count")]
    [JsonConverter(typeof(JsonStringOrNumberConverter))]
    public string Count { get; set; }

    [JsonPropertyName("time")]
    public string Time { get; set; }

    [JsonPropertyName("id")]
    [JsonConverter(typeof(JsonStringOrNumberConverter))]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("item_type")]
    public string ItemType { get; set; }

    [JsonPropertyName("rank_type")]
    [JsonConverter(typeof(JsonStringOrNumberConverter))]
    public string RankType { get; set; }
}

