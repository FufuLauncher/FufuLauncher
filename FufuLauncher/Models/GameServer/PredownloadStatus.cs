/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace FufuLauncher.Models.GameServer;

public sealed class PredownloadStatus
{
    [JsonPropertyName("tag")]
    public string Tag
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("finished")]
    public bool Finished
    {
        get;
        set;
    }

    [JsonPropertyName("total_blocks")]
    public int TotalBlocks
    {
        get;
        set;
    }

    [JsonPropertyName("blocks")]
    public Dictionary<string, long> Blocks
    {
        get;
        set;
    } = new(StringComparer.Ordinal);

    [JsonPropertyName("completed_blocks")]
    public Dictionary<string, long> CompletedBlocks
    {
        get;
        set;
    } = new(StringComparer.Ordinal);
}