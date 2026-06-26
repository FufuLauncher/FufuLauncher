/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;
using FufuLauncher.Models.Genshin;

namespace FufuLauncher.Services;

public class GenshinApiClient
{
    private readonly HttpClient _httpClient;
    private const string CnAppVersion = "2.90.1";
    private const string OsAppVersion = "3.13.0";
    private const string CnUserAgent = "Mozilla/5.0 (Linux; Android 13; Pixel 5 Build/TQ3A.230901.001; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/118.0.0.0 Mobile Safari/537.36 miHoYoBBS/2.90.1";
    private const string OsUserAgent = "Mozilla/5.0 (Linux; Android 13; Pixel 5) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/118.0.0.0 Mobile Safari/537.36 miHoYoBBSOversea/3.13.0";
    private const string CnSalt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string OsSalt = "h4c1d6ywfq5bsbnbhm1bzq7bxzzv6srt";
    private readonly string _deviceId = Guid.NewGuid().ToString("D");

    public GenshinApiClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<TravelersDiarySummary> GetTravelersDiarySummaryAsync(string uid, string cookie, string region, int month = 0, CancellationToken cancellationToken = default)
    {
        var isOs = region.StartsWith("os_");

        string url;
        if (isOs)
        {
            url = $"{ApiEndpoints.TravelersDiaryMonthInfoOsUrl}?month={month}&region={region}&uid={uid}&lang=zh-cn";
        }
        else
        {
            url = $"{ApiEndpoints.TravelersDiaryMonthInfoUrl}?month={month}&bind_uid={uid}&bind_region={region}&bbs_presentation_style=fullscreen&bbs_auth_required=true&utm_source=bbs&utm_medium=mys&utm_campaign=icon";
        }

        var request = CreateRequest(HttpMethod.Get, url, cookie, isOs);
        request.Headers.Add("X-Requested-With", isOs ? "com.mihoyo.hoyolab" : "com.mihoyo.hyperion");

        return await SendAsync<TravelersDiarySummary>(request, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string cookie, bool isOs)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("x-rpc-app_version", isOs ? OsAppVersion : CnAppVersion);
        request.Headers.Add("x-rpc-client_type", "5");
        request.Headers.Add("x-rpc-device_id", _deviceId);
        request.Headers.Add("DS", CreateSecret2(url, isOs));
        request.Headers.Add("Referer", isOs ? "https://act.hoyolab.com/" : "https://webstatic.mihoyo.com/");
        request.Headers.UserAgent.ParseAdd(isOs ? OsUserAgent : CnUserAgent);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        Debug.WriteLine($"[API] Request: {request.RequestUri}");
        Debug.WriteLine($"[API] Response: {content}");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        using var document = JsonDocument.Parse(content);
        if (document.RootElement.TryGetProperty("retcode", out var retcodeElement) &&
            TryReadRetcode(retcodeElement, out var retcode) &&
            retcode != 0)
        {
            var message = document.RootElement.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : "请求失败";
            throw new InvalidOperationException(message ?? "请求失败");
        }

        return JsonSerializer.Deserialize<T>(content, options) ?? Activator.CreateInstance<T>();
    }

    private static bool TryReadRetcode(JsonElement element, out int retcode)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return element.TryGetInt32(out retcode);

        if (element.ValueKind == JsonValueKind.String)
            return int.TryParse(element.GetString(), out retcode);

        retcode = 0;
        return false;
    }

    private string CreateSecret2(string url, bool isOs)
    {
        var salt = isOs ? OsSalt : CnSalt;
        var t = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
        var r = new Random().Next(100000, 200000).ToString();
        var m = $"salt={salt}&t={t}&r={r}&b=&q=";

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(m));
        var check = Convert.ToHexString(hash).ToLower();

        return $"{t},{r},{check}";
    }
}

