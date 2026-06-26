/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FufuLauncher.Models.UIGF;

public class UIGFEntry
{
    [JsonPropertyName("uid")]
    [JsonConverter(typeof(JsonStringOrNumberConverter))]
    public string Uid { get; set; }

    [JsonPropertyName("timezone")]
    public int Timezone { get; set; } = 8;

    [JsonPropertyName("lang")]
    public string Lang { get; set; } = "zh-cn";

    [JsonPropertyName("list")]
    public List<UIGFItem> List { get; set; } = new();
}

public class JsonStringOrNumberConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
            return reader.GetInt64().ToString();
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString() ?? "";
        return "";
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

