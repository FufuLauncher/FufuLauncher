using System.Text.Json.Serialization;

namespace FufuLauncher.Models.UIGF;

public class UIGFInfo
{
    [JsonPropertyName("export_timestamp")]
    public long ExportTimestamp { get; set; }

    [JsonPropertyName("export_app")]
    public string ExportApp { get; set; } = "FufuLauncher";

    [JsonPropertyName("export_app_version")]
    public string ExportAppVersion { get; set; }

    [JsonPropertyName("version")]
    public string Version { get; set; }

    [JsonPropertyName("uigf_version")]
    public string UigfVersion { get; set; }

    [JsonPropertyName("uid")]
    public string Uid { get; set; }

    [JsonPropertyName("lang")]
    public string Lang { get; set; }
}