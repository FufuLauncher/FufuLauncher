using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class HoyolabRoleResolverService : IHoyolabRoleResolverService
{
    private const string GameBiz = "hk4e_global";
    private const string DsSalt = "h4c1d6ywfq5bsbnbhm1bzq7bxzzv6srt";
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public HoyolabRoleResolverService()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
    }

    public async Task<HoyolabRoleResolveResult> ResolveRolesAsync(string cookie, CancellationToken cancellationToken = default)
    {
        var bindingResult = await TryResolveFromBindingAsync(cookie, cancellationToken);
        if (bindingResult.HasRoles)
            return bindingResult;

        var cardResult = await TryResolveFromGameRecordCardAsync(cookie, cancellationToken);
        if (cardResult.HasRoles)
            return cardResult;

        var message = string.Join("；", new[]
        {
            BuildSourceMessage(bindingResult),
            BuildSourceMessage(cardResult)
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        return new HoyolabRoleResolveResult(
            cardResult.RetCode != 0 ? cardResult.RetCode : bindingResult.RetCode,
            string.IsNullOrWhiteSpace(message) ? "未检测到绑定账号" : message,
            "none",
            new List<GameRoleInfo>());
    }

    private async Task<HoyolabRoleResolveResult> TryResolveFromBindingAsync(string cookie, CancellationToken cancellationToken)
    {
        try
        {
            var query = $"game_biz={GameBiz}";
            var url = $"https://api-account-os.hoyolab.com/binding/api/getUserGameRolesByCookieToken?{query}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddCommonHeaders(request, cookie);
            request.Headers.TryAddWithoutValidation("DS", GenerateDs(query: query));

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<OsApiResponse<OsAccountInfoData>>(json, _jsonOptions);

            if (result?.RetCode == 0 && result.Data?.List != null)
            {
                var roles = result.Data.List
                    .Where(r => !string.IsNullOrWhiteSpace(r.GameUid) && !string.IsNullOrWhiteSpace(r.Region))
                    .Select(r => new GameRoleInfo(
                        GameBiz,
                        r.Region,
                        r.GameUid,
                        r.Nickname ?? "",
                        0,
                        FriendlyRegionName(r.Region)))
                    .ToList();

                return new HoyolabRoleResolveResult(0, result.Message ?? "OK", "binding", roles);
            }

            return new HoyolabRoleResolveResult(
                result?.RetCode ?? (int)response.StatusCode,
                result?.Message ?? $"HTTP {(int)response.StatusCode}",
                "binding",
                new List<GameRoleInfo>());
        }
        catch (Exception ex)
        {
            return new HoyolabRoleResolveResult(-1, ex.Message, "binding", new List<GameRoleInfo>());
        }
    }

    private async Task<HoyolabRoleResolveResult> TryResolveFromGameRecordCardAsync(string cookie, CancellationToken cancellationToken)
    {
        try
        {
            var uid = ExtractCookieValue(cookie, "account_id_v2")
                      ?? ExtractCookieValue(cookie, "ltuid_v2")
                      ?? ExtractCookieValue(cookie, "account_id");
            if (string.IsNullOrWhiteSpace(uid))
                return new HoyolabRoleResolveResult(-1, "Cookie 中缺少 HoYoLAB UID", "game_record_card", new List<GameRoleInfo>());

            var url = $"https://bbs-api-os.hoyolab.com/game_record/card/wapi/getGameRecordCard?uid={uid}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddCommonHeaders(request, cookie, "https://www.hoyolab.com/", "https://www.hoyolab.com");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<GameRecordCardResponse>(json, _jsonOptions);

            if (result?.retcode == 0 && result.data?.list != null)
            {
                var roles = result.data.list
                    .Where(r => !string.IsNullOrWhiteSpace(r.game_role_id))
                    .Select(r => new GameRoleInfo(
                        GameBiz,
                        ResolveRegion(r),
                        r.game_role_id,
                        r.nickname ?? "",
                        r.level,
                        r.region_name ?? ""))
                    .Where(r => !string.IsNullOrWhiteSpace(r.region))
                    .ToList();

                return new HoyolabRoleResolveResult(0, result.message ?? "OK", "game_record_card", roles);
            }

            return new HoyolabRoleResolveResult(
                result?.retcode ?? (int)response.StatusCode,
                result?.message ?? $"HTTP {(int)response.StatusCode}",
                "game_record_card",
                new List<GameRoleInfo>());
        }
        catch (Exception ex)
        {
            return new HoyolabRoleResolveResult(-1, ex.Message, "game_record_card", new List<GameRoleInfo>());
        }
    }

    private static string FriendlyRegionName(string regionCode) => regionCode switch
    {
        "os_asia" => "亚服",
        "os_usa" => "美服",
        "os_euro" => "欧服",
        "os_cht" => "台港澳服",
        _ => regionCode
    };

    private static string ResolveRegion(GameRecordCardInfo role)
    {
        var regionName = role.region_name ?? "";
        if (regionName.Contains("America", StringComparison.OrdinalIgnoreCase) ||
            regionName.Contains("美", StringComparison.OrdinalIgnoreCase))
            return "os_usa";
        if (regionName.Contains("Europe", StringComparison.OrdinalIgnoreCase) ||
            regionName.Contains("欧", StringComparison.OrdinalIgnoreCase))
            return "os_euro";
        if (regionName.Contains("Asia", StringComparison.OrdinalIgnoreCase) ||
            regionName.Contains("亚", StringComparison.OrdinalIgnoreCase))
            return "os_asia";
        if (regionName.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
            regionName.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
            regionName.Contains("MO", StringComparison.OrdinalIgnoreCase) ||
            regionName.Contains("港", StringComparison.OrdinalIgnoreCase) ||
            regionName.Contains("台", StringComparison.OrdinalIgnoreCase))
            return "os_cht";

        return "";
    }

    private static void AddCommonHeaders(
        HttpRequestMessage request,
        string cookie,
        string referer = "https://act.hoyolab.com/",
        string origin = "https://act.hoyolab.com")
    {
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,en-US;q=0.8");
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "3.13.0");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("x-rpc-language", "zh-cn");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", Guid.NewGuid().ToString("N"));
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", GameBiz);
        request.Headers.TryAddWithoutValidation("X-Requested-With", "com.mihoyo.hoyolab");
        request.Headers.TryAddWithoutValidation("Referer", referer);
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 13; Pixel 5) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/118.0.0.0 Mobile Safari/537.36 miHoYoBBSOversea/3.13.0");
    }

    private static string GenerateDs(string body = "", string query = "")
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var r = new Random().Next(100001, 200000).ToString();
        var q = string.IsNullOrEmpty(query)
            ? ""
            : string.Join("&", query.Split('&').OrderBy(x => x));
        var c = Md5($"salt={DsSalt}&t={t}&r={r}&b={body}&q={q}");
        return $"{t},{r},{c}";
    }

    private static string Md5(string input)
    {
        using var md5 = MD5.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = md5.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    private static string? ExtractCookieValue(string cookie, string key)
    {
        var pattern = $@"(?:^|;)\s*{Regex.Escape(key)}=([^;]+)";
        var match = Regex.Match(cookie, pattern);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string BuildSourceMessage(HoyolabRoleResolveResult result)
    {
        if (string.IsNullOrWhiteSpace(result.Message))
            return "";
        return $"{result.Source}: {result.Message}";
    }
}
