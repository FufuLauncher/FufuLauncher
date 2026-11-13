using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models.Miyoushe;

public sealed record MiyoushePendant(
    string Id,
    string Name,
    string Description,
    string Author,
    string Url,
    long ExpiredTime,
    long RemainTime,
    bool Owned,
    bool IsUsed)
{
    public bool Available => Owned && (ExpiredTime <= 0 || ExpiredTime > DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public string RemainText => RemainTime > 0
        ? string.Format("MiyoushePendant_Remain".GetLocalized(), TimeSpan.FromSeconds(RemainTime).Days,
            TimeSpan.FromSeconds(RemainTime).Hours)
        : ExpiredTime > 0 && !Available
            ? "MiyoushePendant_Expired".GetLocalized()
            : "";

    public string AuthorText =>
        Author.Length == 0 ? "" : string.Format("MiyoushePendant_Author".GetLocalized(), Author);

    public string StateText => IsUsed ? "MiyoushePendant_Equipped".GetLocalized()
        : Owned ? "" : "MiyoushePendant_NotOwned".GetLocalized();

    internal static MiyoushePendant Parse(JsonElement item) => new(
        item.Text("id"), item.Text("name"), item.Text("description"), item.Text("author"), item.Text("url"),
        item.Number("expired_time"), item.Number("remain_time"), item.Flag("owned"), item.Flag("is_used"));
}