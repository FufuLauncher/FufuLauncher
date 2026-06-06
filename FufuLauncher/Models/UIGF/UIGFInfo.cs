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
    public string Version { get; set; } = "v4.0";
}
