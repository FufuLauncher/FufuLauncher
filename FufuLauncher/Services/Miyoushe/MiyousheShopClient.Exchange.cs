using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Models.Miyoushe;

namespace FufuLauncher.Services.Miyoushe;

internal sealed record MiyousheShopResult(MiyousheShopPlanStatus Status, string Message = "");

internal sealed class MiyousheShopException(string message) : Exception(message);

internal sealed partial class MiyousheShopClient
{
    private const string Origin = "https://webstatic.miyoushe.com";

    internal async Task<MiyousheShopGoods> GetAccountGoodsAsync(string id, AccountContext account, string uid,
        CancellationToken ct)
    {
        var goods = await GetGoodsAsync(id, new(account, uid), ct);
        if (!goods.SupportsExchange) throw ShopError("MiyousheShop_Unsupported");
        if (goods.AccountLimitReached) throw ShopError("MiyousheShop_LimitReached");
        return goods;
    }

    internal async Task<IReadOnlyList<MiyousheShopTarget>> GetTargetsAsync(MiyousheShopGoods goods,
        AccountContext account, string uid, CancellationToken ct)
    {
        if (!goods.SupportsExchange) throw ShopError("MiyousheShop_Unsupported");
        string communityUid = goods.Game == "bbs" ? uid : "";
        string communityBiz = goods.Game == "bbs" ? "bbs" : "";
        if (!goods.RequiresAddress && !goods.RequiresRole)
            return [new("UID " + uid, communityUid, "", communityBiz, 0)];

        string path = goods.RequiresAddress ? "/account/address/list?point_sn=myb" :
            "/binding/api/getUserGameRolesByCookie?game_biz=" + Uri.EscapeDataString(goods.GameBiz);
        var data = await ReadAccountDataAsync(ApiEndpoints.MihoyoBbsWebApi + path, account, uid, ct);
        if (goods.RequiresAddress)
            return data.Get("list").Items().Where(item => item.Number("id") > 0).Select(item =>
            {
                var name = item.Text("connect_name");
                var phone = item.Text("connect_mobile");
                var label = string.Join(" ", item.Text("city_name"), name.Length > 0 ? name[..1] + "**" : "",
                    phone.Length >= 4 ? "****" + phone[^4..] : "");
                return new MiyousheShopTarget(label.Trim(), communityUid, "", communityBiz, item.Number("id"));
            }).ToArray();

        return data.Get("list").Items()
            .Where(item => item.Text("game_biz") == goods.GameBiz && IsDigits(item.Text("game_uid")) &&
                           item.Text("region").Length > 0)
            .Select(item => new MiyousheShopTarget(
                $"{item.Text("nickname")} · {item.Text("region_name")} · UID {item.Text("game_uid")}",
                item.Text("game_uid"), item.Text("region"), goods.GameBiz, 0)).ToArray();
    }

    internal HttpRequestMessage PrepareExchange(MiyousheShopPlan plan, AccountContext account, string uid)
    {
        var target = plan.Target ?? throw ShopError("MiyousheShop_NoTarget");
        var body = new Dictionary<string, object>
        {
            ["app_id"] = 1, ["point_sn"] = "myb", ["goods_id"] = plan.GoodsId, ["exchange_num"] = 1,
            ["address_id"] = target.AddressId
        };
        if (target.Uid.Length > 0) body["uid"] = target.Uid;
        if (target.Region.Length > 0) body["region"] = target.Region;
        if (target.GameBiz.Length > 0) body["game_biz"] = target.GameBiz;
        var request = CreateAccountRequest(HttpMethod.Post, Api + "exchange", account, uid);
        request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body));
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return request;
    }

    internal async Task<MiyousheShopResult> SubmitAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            using var response = await _http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return new(MiyousheShopPlanStatus.Failed, "MiyousheShop_RateLimited".GetLocalized());
            if (response.Headers.Contains("x-rpc-aigis"))
                return new(MiyousheShopPlanStatus.Verification, "MiyousheShop_ManualVerification".GetLocalized());
            if (!response.IsSuccessStatusCode) return new(MiyousheShopPlanStatus.Unknown);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var root = json.RootElement;
            if (!int.TryParse(root.Text("retcode"), out int code)) return new(MiyousheShopPlanStatus.Unknown);
            return code switch
            {
                0 => new(MiyousheShopPlanStatus.Succeeded, root.Get("data").Text("order_sn")),
                1028 or 1034 => new(MiyousheShopPlanStatus.Verification, "MiyousheShop_ManualVerification".GetLocalized()),
                _ => new(MiyousheShopPlanStatus.Failed, FormatRejection(root))
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException or JsonException)
        {
            return new(MiyousheShopPlanStatus.Unknown);
        }
    }

    private async Task<JsonElement> ReadAccountDataAsync(string url, AccountContext account, string uid,
        CancellationToken ct)
    {
        using var request = CreateAccountRequest(HttpMethod.Get, url, account, uid);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        if (json.RootElement.Text("retcode") != "0")
            throw new MiyousheShopException(FormatRejection(json.RootElement));
        var data = json.RootElement.Get("data");
        if (data.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid shop response.");
        return data.Clone();
    }

    private static HttpRequestMessage CreateAccountRequest(HttpMethod method, string url, AccountContext account,
        string uid)
    {
        var cookie = MiyousheClient.BuildShopCookies(account.Cookies, uid);
        if (account.ServerType != ServerType.Cn || !IsDigits(uid) || cookie.Length == 0 ||
            cookie.Any(char.IsControl) || string.IsNullOrEmpty(account.Device.DeviceFp))
            throw ShopError("MiyousheShop_LoginRequired");

        bool shopHost = new Uri(url).Host == new Uri(ApiEndpoints.MiyousheBbsApiBase).Host;
        var request = new HttpRequestMessage(method, url);
        request.Headers.Referrer = new((shopHost ? Origin : "https://user.mihoyo.com") + "/");
        request.Headers.TryAddWithoutValidation("Origin", request.Headers.Referrer.GetLeftPart(UriPartial.Authority));
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("User-Agent", account.UserAgent.Mobile);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", shopHost ? "1" : "5");
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", HeaderVersions.MobileCnLogin);
        request.Headers.TryAddWithoutValidation("x-rpc-channel", "appstore");
        request.Headers.TryAddWithoutValidation("x-rpc-verify_key", GenshinApiEndpoints.PassportAppId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", account.Device.BbsDeviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_fp", Uri.EscapeDataString(account.Device.DeviceFp));
        request.Headers.TryAddWithoutValidation("x-rpc-device_model", account.Device.Model);
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", Uri.EscapeDataString(account.Device.DeviceName));
        request.Headers.TryAddWithoutValidation("x-rpc-sys_version", account.Device.SysVersion);
        return request;
    }

    internal static MiyousheShopException ShopError(string key) => new(key.GetLocalized());

    private static bool IsDigits(string text) => text.Length is > 0 and <= 20 && text.All(char.IsAsciiDigit);

    private static string FormatRejection(JsonElement root)
    {
        string code = root.Text("retcode");
        if (code is "1028" or "1034") return "MiyousheShop_ManualVerification".GetLocalized();
        if (code is "-100" or "-2007" or "10001") return "MiyousheShop_LoginRequired".GetLocalized();
        string message = root.Text("message");
        return $"{message[..Math.Min(message.Length, 160)]} ({code})";
    }
}
