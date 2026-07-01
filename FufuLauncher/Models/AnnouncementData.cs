/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public class AnnouncementData
{
    [JsonPropertyName("Info")]
    public string Info
    {
        get; set;
    }
}
