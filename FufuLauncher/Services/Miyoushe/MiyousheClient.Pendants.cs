using System.Text.Json;
using FufuLauncher.Models.Miyoushe;

namespace FufuLauncher.Services.Miyoushe;

public sealed partial class MiyousheClient
{
    public async Task<IReadOnlyList<MiyoushePendant>> GetPendantsAsync(CancellationToken ct)
    {
        RequireLogin("cookie_token", "ltoken");
        var data = await SendAsync("pendant/wapi/getUserPendantList", new() { ["gids"] = "2" }, Profile.WebSign, ct);
        return data.Get("list").Items().Select(MiyoushePendant.Parse)
            .Where(pendant => pendant.Id.Length > 0 && pendant.Url.Length > 0).ToArray();
    }

    public async Task<string> GetMyAvatarAsync(CancellationToken ct)
    {
        RequireLogin("cookie_token", "ltoken");
        var data = await SendAsync("user/wapi/getUserFullInfo", new() { ["gids"] = "2" }, Profile.WebSign, ct);
        return data.Get("user_info").Text("avatar_url");
    }

    public async Task SetPendantAsync(string pendantId, CancellationToken ct)
    {
        ValidateId(pendantId);
        RequireLogin("cookie_token", "ltoken");
        await SendAsync("pendant/wapi/setUserPendant", [], Profile.WebSign, ct,
            JsonSerializer.Serialize(new Dictionary<string, string> { ["pendant_id"] = pendantId }));
    }
}
