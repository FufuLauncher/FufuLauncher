using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Models.Miyoushe;

namespace FufuLauncher.Services.Miyoushe;

internal sealed record MiyousheShopSession(AccountContext Context, string Uid);

internal sealed partial class MiyousheShopClient : IDisposable
{
    internal const int PageSize = 20;
    private const string Api = ApiEndpoints.MiyousheShopApiBase;
    private const string OfficialPage = ApiEndpoints.MiyousheShopOfficialPageBase;
    private readonly HttpClient _http;

    public MiyousheShopClient() : this(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
    {
    }

    internal MiyousheShopClient(HttpMessageHandler handler) =>
        _http = new(handler) { Timeout = TimeSpan.FromSeconds(10) };

    internal static Uri OfficialUri(string? goodsId = null) => new(OfficialPage +
                                                                   (string.IsNullOrEmpty(goodsId)
                                                                       ? "redeem"
                                                                       : "goodsDetail/" +
                                                                         Uri.EscapeDataString(goodsId)));

    internal static Uri OfficialHistoryUri
    {
        get;
    } = new(OfficialPage + "miyobHistory/exchange");

    internal async Task<MiyousheShopPage> GetPageAsync(string game, int page, MiyousheShopSession? session,
        CancellationToken ct)
    {
        var data = await GetAsync(
            $"{ApiEndpoints.MiyousheShopListEndpoint}?page={page}&page_size={PageSize}&game={Uri.EscapeDataString(game)}",
            session, ct).ConfigureAwait(false);
        if (data.Get("list").ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Invalid shop list.");

        return new(
            data.Get("list").Items().Select(item => MiyousheShopGoods.Parse(item, session != null))
                .Where(item => item.Id.Length > 0 && item.Name.Length > 0).ToArray(),
            data.Get("games").Items().Select(game => new MiyousheShopGame(game.Text("key"), game.Text("name")))
                .Where(game => game.Key.Length > 0 && game.Name.Length > 0).ToArray(),
            data.Number("total"));
    }

    internal async Task<MiyousheShopGoods> GetGoodsAsync(string id, MiyousheShopSession? session, CancellationToken ct)
    {
        var data = await GetAsync(ApiEndpoints.MiyousheShopDetailEndpoint + "?goods_id=" + Uri.EscapeDataString(id),
            session, ct).ConfigureAwait(false);
        var goods = MiyousheShopGoods.Parse(data, session != null);
        return goods.Id == id && goods.Name.Length > 0 ? goods : throw new InvalidDataException("Invalid shop goods.");
    }

    private async Task<JsonElement> GetAsync(string path, MiyousheShopSession? session, CancellationToken ct)
    {
        string url = Api + path + "&app_id=1&point_sn=myb";
        if (session != null)
            return await ReadAccountDataAsync(url, session.Context, session.Uid, ct).ConfigureAwait(false);

        using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = data.RootElement;
        if (root.Text("retcode") != "0" || root.Get("data").ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Shop API retcode: " + root.Text("retcode"));
        return root.Get("data").Clone();
    }

    public void Dispose() => _http.Dispose();
}