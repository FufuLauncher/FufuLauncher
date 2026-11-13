/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;
using FufuLauncher.Constants.MiHoYo;
using FufuLauncher.Services.MiHoYo.Networking;
using FufuLauncher.Services.MiHoYo.Transport;

namespace FufuLauncher.Services.MiHoYo.DailyNote;

public sealed class OverseaGameRecordClient
{
    private static readonly HttpClient _httpClient = new(new HttpClientHandler { UseCookies = false })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private readonly string _deviceId = Guid.NewGuid().ToString();

    public async Task<DailyNoteCardData> GetDailyNoteAsync(
        string uid, string region, IReadOnlyDictionary<string, string> cookies, CancellationToken token = default)
    {
        string roleQuery = $"role_id={Uri.EscapeDataString(uid)}&server={Uri.EscapeDataString(region)}";

        await GetAsync($"{ApiEndpoints.OverseaDailyNoteIndexUrl}?{roleQuery}", roleQuery, cookies, token)
            .ConfigureAwait(false);

        string json =
            await GetAsync($"{ApiEndpoints.OverseaDailyNoteUrl}?{roleQuery}", roleQuery, cookies, token)
                .ConfigureAwait(false);
        return DailyNoteParser.Parse(json);
    }

    private async Task<string> GetAsync(
        string url, string sortedQuery, IReadOnlyDictionary<string, string> cookies, CancellationToken token)
    {
        string cookieStr = BbsRequestBuilder.BuildCookieString(cookies, BbsRequestBuilder.CookieMode.Cookie);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(HeaderNames.UserAgent, UserAgents.WindowsBbsOversea254);
        request.Headers.TryAddWithoutValidation(HeaderNames.Accept, "application/json");
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcAppVersion, HeaderVersions.BbsOs254);
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcClientType, ClientTypes.Other);
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcLanguage, "zh-cn");
        request.Headers.TryAddWithoutValidation(HeaderNames.RpcDeviceId, _deviceId);
        if (!string.IsNullOrEmpty(cookieStr))
            request.Headers.TryAddWithoutValidation(HeaderNames.Cookie, cookieStr);
        request.Headers.TryAddWithoutValidation(HeaderNames.DS,
            MiHoYoHeaderFactory.CalculateDs2(HeaderSalts.OsGameRecord, sortedQuery));

        using HttpResponseMessage response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);
        return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
    }
}