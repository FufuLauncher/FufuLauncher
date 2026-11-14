/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FufuLauncher.Models.DataCenter;

internal sealed class LenientDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetDouble(out var num) ? num : null;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text)) return null;
                text = text.Trim().TrimEnd('%', ' ');
                return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            case JsonTokenType.True:
                return 1d;
            case JsonTokenType.False:
                return 0d;
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

internal sealed class LenientIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i)) return i;
                return reader.TryGetDouble(out var d) ? (int)Math.Round(d) : null;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text)) return null;
                return double.TryParse(text.Trim().TrimEnd('%', ' '), NumberStyles.Any, CultureInfo.InvariantCulture,
                    out var parsed)
                    ? (int)Math.Round(parsed)
                    : null;
            case JsonTokenType.True:
                return 1;
            case JsonTokenType.False:
                return 0;
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value.HasValue) writer.WriteNumberValue(value.Value);
        else writer.WriteNullValue();
    }
}

internal sealed class LenientStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var l)
                    ? l.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

public static class DataCenterJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters =
        {
            new LenientDoubleConverter(),
            new LenientIntConverter()
        }
    };
}

public sealed class RoleAvgResponse
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

    [JsonPropertyName("data_from")]
    public string? DataFrom
    {
        get;
        set;
    }

    [JsonPropertyName("last_update")]
    public string? LastUpdate
    {
        get;
        set;
    }

    [JsonPropertyName("title")]
    public string? Title
    {
        get;
        set;
    }

    [JsonPropertyName("result")]
    public List<RoleAvgEntry>? Result
    {
        get;
        set;
    }
}

public sealed class RoleAvgEntry
{
    [JsonPropertyName("role")]
    public string? Role
    {
        get;
        set;
    }

    [JsonPropertyName("ename")]
    public string? Ename
    {
        get;
        set;
    }

    [JsonPropertyName("role_sum")]
    public double? RoleSum
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

    [JsonPropertyName("avatar")]
    public string? Avatar
    {
        get;
        set;
    }

    [JsonPropertyName("avg_level")]
    public double? AvgLevel
    {
        get;
        set;
    }

    [JsonPropertyName("avg_class")]
    public double? AvgConstellation
    {
        get;
        set;
    }

    [JsonPropertyName("ability1")]
    public double? Ability1
    {
        get;
        set;
    }

    [JsonPropertyName("ability2")]
    public double? Ability2
    {
        get;
        set;
    }

    [JsonPropertyName("ability3")]
    public double? Ability3
    {
        get;
        set;
    }

    [JsonPropertyName("c0")]
    public double? C0
    {
        get;
        set;
    }

    [JsonPropertyName("c1")]
    public double? C1
    {
        get;
        set;
    }

    [JsonPropertyName("c2")]
    public double? C2
    {
        get;
        set;
    }

    [JsonPropertyName("c3")]
    public double? C3
    {
        get;
        set;
    }

    [JsonPropertyName("c4")]
    public double? C4
    {
        get;
        set;
    }

    [JsonPropertyName("c5")]
    public double? C5
    {
        get;
        set;
    }

    [JsonPropertyName("c6")]
    public double? C6
    {
        get;
        set;
    }

    [JsonPropertyName("artifacts")]
    public double? ArtifactScore
    {
        get;
        set;
    }

    [JsonPropertyName("damage")]
    public double? Damage
    {
        get;
        set;
    }

    [JsonPropertyName("damage_name")]
    public string? DamageName
    {
        get;
        set;
    }

    [JsonPropertyName("weapon")]
    public List<WeaponUsageEntry>? Weapons
    {
        get;
        set;
    }

    [JsonPropertyName("artifacts_set")]
    public List<ArtifactUsageEntry>? ArtifactSets
    {
        get;
        set;
    }
}

public sealed class WeaponUsageEntry
{
    [JsonPropertyName("name")]
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

    [JsonPropertyName("rate")]
    public double? Rate
    {
        get;
        set;
    }
}

public sealed class ArtifactUsageEntry
{
    [JsonPropertyName("name")]
    public string? Name
    {
        get;
        set;
    }

    [JsonPropertyName("avatars")]
    public List<string>? Avatars
    {
        get;
        set;
    }

    [JsonPropertyName("rate")]
    public double? Rate
    {
        get;
        set;
    }
}
