// FufuLauncher/Models/AnnouncementData.cs
using System.Text.Json.Serialization;

namespace FufuLauncher.Models;

public class AnnouncementData
{
    [JsonPropertyName("Info")]
    public string Info { get; set; }
}