using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Models.Genshin;

namespace FufuLauncher.Services;

public class GenshinApiClient
{
    private readonly HttpClient _httpClient;
    private const string AppVersion = "2.90.1";
    private const string UserAgent = "Mozilla/5.0 (Linux; Android 13; Pixel 5 Build/TQ3A.230901.001; wv) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/118.0.0.0 Mobile Safari/537.36 miHoYoBBS/2.90.1";
    private const string ApiSalt2 = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private readonly string _deviceId = Guid.NewGuid().ToString("D");

    public GenshinApiClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public async Task<TravelersDiarySummary> GetTravelersDiarySummaryAsync(string uid, string cookie, int month = 0, CancellationToken cancellationToken = default)
    {
        var region = "cn_gf01";
        var url = $"https://hk4e-api.mihoyo.com/event/ys_ledger/monthInfo?month={month}&bind_uid={uid}&bind_region={region}&bbs_presentation_style=fullscreen&bbs_auth_required=true&utm_source=bbs&utm_medium=mys&utm_campaign=icon";

        var request = CreateRequest(HttpMethod.Get, url, cookie);
        request.Headers.Add("X-Requested-With", "com.mihoyo.hyperion");

        return await SendAsync<TravelersDiarySummary>(request, cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, string cookie)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("x-rpc-app_version", AppVersion);
        request.Headers.Add("x-rpc-client_type", "5");
        request.Headers.Add("x-rpc-device_id", _deviceId);
        request.Headers.Add("DS", CreateSecret2(url));
        request.Headers.Add("Referer", "https://webstatic.mihoyo.com/");
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
        return JsonSerializer.Deserialize<T>(content, options) ?? Activator.CreateInstance<T>();
    }

    private string CreateSecret2(string url)
    {
        var t = DateTimeOffset.Now.ToUnixTimeSeconds().ToString();
        var r = new Random().Next(100000, 200000).ToString();
        var m = $"salt={ApiSalt2}&t={t}&r={r}&b=&q=";

        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(m));
        var check = Convert.ToHexString(hash).ToLower();

        return $"{t},{r},{check}";
    }
}