/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace FufuLauncher.Models.DataCenter;

public sealed class WishHistoryResponse
{
    [JsonPropertyName("code")]
    public int? Code
    {
        get;
        set;
    }

    [JsonPropertyName("result")]
    public List<WishBannerEntry>? Characters
    {
        get;
        set;
    }

    [JsonPropertyName("weapon")]
    public List<WishBannerEntry>? Weapons
    {
        get;
        set;
    }

    [JsonPropertyName("avatar_list")]
    public Dictionary<string, string>? AvatarList
    {
        get;
        set;
    }
}

public sealed class WishBannerEntry
{
    [JsonPropertyName("avatar")]
    public string? Avatar
    {
        get;
        set;
    }

    [JsonPropertyName("version")]
    public string? Version
    {
        get;
        set;
    }

    [JsonPropertyName("time")]
    public string? Time
    {
        get;
        set;
    }

    [JsonPropertyName("star5_role")]
    public List<string>? Star5
    {
        get;
        set;
    }

    [JsonPropertyName("star4_role")]
    public List<string>? Star4
    {
        get;
        set;
    }
}

public sealed class RerunResponse
{
    [JsonPropertyName("code")]
    public int? Code
    {
        get;
        set;
    }

    [JsonPropertyName("version")]
    public string? Version
    {
        get;
        set;
    }

    [JsonPropertyName("result")]
    public List<List<RerunEntry>>? Result
    {
        get;
        set;
    }
}

public sealed class RerunEntry
{
    [JsonPropertyName("role")]
    public string? Name
    {
        get;
        set;
    }

    [JsonPropertyName("avatar")]
    public string? Avatar
    {
        get;
        set;
    }

    [JsonPropertyName("star")]
    public int? Star
    {
        get;
        set;
    }

    [JsonPropertyName("days")]
    public double? Days
    {
        get;
        set;
    }

    [JsonPropertyName("intro")]
    public string? Intro
    {
        get;
        set;
    }

    [JsonPropertyName("avg_days")]
    public double? AvgDays
    {
        get;
        set;
    }

    [JsonPropertyName("up_times")]
    public int? UpTimes
    {
        get;
        set;
    }

    [JsonPropertyName("history")]
    public List<string>? History
    {
        get;
        set;
    }

    [JsonPropertyName("tags")]
    public string? Tags
    {
        get;
        set;
    }

    [JsonPropertyName("max_gap_days")]
    public double? MaxGapDays
    {
        get;
        set;
    }

    [JsonPropertyName("max_gap_pool")]
    public string? MaxGapPool
    {
        get;
        set;
    }

    [JsonPropertyName("min_gap_days")]
    public double? MinGapDays
    {
        get;
        set;
    }

    [JsonPropertyName("min_gap_pool")]
    public string? MinGapPool
    {
        get;
        set;
    }

    [JsonPropertyName("width_rate")]
    public double? WidthRate
    {
        get;
        set;
    }
}
