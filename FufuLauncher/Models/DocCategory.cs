/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public class DocCategory
{
    [JsonPropertyName("category")]
    public string CategoryName { get; set; } = string.Empty;

    [JsonPropertyName("items")]
    public List<DocItem> Items { get; set; } = new();
}
