/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json.Serialization;

namespace FufuLauncher.Services.Yae;

public sealed class YaeUiafInfo
{
    [JsonPropertyName("export_app")]
    public string ExportApp
    {
        get;
        set;
    } = "FufuLauncher";

    [JsonPropertyName("export_app_version")]
    public string ExportAppVersion
    {
        get;
        set;
    } = "1.0.0";

    [JsonPropertyName("uiaf_version")]
    public string UiafVersion
    {
        get;
        set;
    } = "v1.1";

    [JsonPropertyName("export_timestamp")]
    public long ExportTimestamp
    {
        get;
        set;
    }
}