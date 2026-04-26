using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using FufuLauncher.Constants;
using MihoyoBBS;

namespace FufuLauncher.Services;

public class TokenRefreshService
{
    private const string Salt = "dDIQHbKOdaPaLuvQKVzUzqdeCaxjtaPV";
    private const string WebSalt = "G1ktdwFL4IyGkHuuWSmz0wUe9Db9scyK";
    private readonly string _deviceId;
    private readonly string _deviceFp;
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    public TokenRefreshService()
    {
        _deviceId = Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();
        _deviceFp = GenerateDeviceFingerprint();

        var handler = new HttpClientHandler { UseCookies = false };
        _httpClient = new HttpClient(handler);
    }
    
    public async Task RefreshCookieAsync()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(path)) return;

            var json = await File.ReadAllTextAsync(path);
            var config = JsonSerializer.Deserialize<Config>(json);
            
            if (config == null || string.IsNullOrEmpty(config.Account.Cookie)) return;
            
            bool isValid = await CheckCookieValidAsync(config.Account.Cookie);
            if (isValid)
            {
                Debug.WriteLine("当前 Cookie 仍然有效且获取到了角色列表，无需刷新");
                return;
            }

            Debug.WriteLine("当前 Cookie 已失效或角色列表为空，开始执行 Token 刷新...");

            var cookieDict = ParseCookieString(config.Account.Cookie);
            
            cookieDict.TryGetValue("stoken", out string stoken);
            cookieDict.TryGetValue("mid", out string mid);

            if (string.IsNullOrEmpty(stoken) || string.IsNullOrEmpty(mid))
            {
                Debug.WriteLine("本地 Cookie 中缺少 stoken 或 mid，无法刷新");
                return;
            }

            string authCookie = $"stoken={stoken}; mid={mid}";
            
            string webTicket = await CreateWebQrCodeAsync();
            if (string.IsNullOrEmpty(webTicket)) return;

            bool scanResult = await SimulateAppActionAsync(ApiEndpoints.PassportScanQrLoginUrl, webTicket, authCookie);
            if (!scanResult) return;

            await Task.Delay(500);
            
            bool confirmResult = await SimulateAppActionAsync(ApiEndpoints.PassportConfirmQrLoginUrl, webTicket, authCookie);
            if (!confirmResult) return;

            var v2Cookies = await GetWebQrStatusAndExtractCookiesAsync(webTicket);
            if (v2Cookies != null && v2Cookies.Count > 0)
            {
                foreach (var kvp in v2Cookies)
                {
                    cookieDict[kvp.Key] = kvp.Value;
                }

                config.Account.Cookie = BuildCookieString(cookieDict);

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var newJson = JsonSerializer.Serialize(config, options);
                await File.WriteAllTextAsync(path, newJson);

                var localSettingsService = new LocalSettingsService();
                await localSettingsService.SaveSettingAsync("AccountConfig", config);

                Debug.WriteLine("Token刷新成功");
                return;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Token 刷新异常: {ex.Message}");
        }
    }

    private async Task<bool> CheckCookieValidAsync(string cookie)
    {
        try
        {
            string url = ApiEndpoints.MihoyoBbsUserGameRolesUrl;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("DS", GenerateWebDS());
            request.Headers.TryAddWithoutValidation("x-rpc-channel", "miyousheluodi");
            request.Headers.TryAddWithoutValidation("Origin", "https://act.mihoyo.com");
            request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.93.1");
            request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
            request.Headers.TryAddWithoutValidation("Referer", "https://act.mihoyo.com/");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Headers.TryAddWithoutValidation("x-rpc-device_id", _deviceId);
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36 miHoYoBBS/2.93.1");

            var response = await _httpClient.SendAsync(request);
            var responseText = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<ApiResponse<AccountInfoData>>(responseText, _jsonOptions);
            
            if (result != null && result.RetCode == 0 && result.Data?.List != null && result.Data.List.Count > 0)
            {
                return true; 
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"验证 Cookie 状态时发生异常: {ex.Message}");
        }
        
        return false;
    }

    private Dictionary<string, string> ParseCookieString(string cookieString)
    {
        var dict = new Dictionary<string, string>();
        var pairs = cookieString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2)
            {
                dict[kv[0].Trim()] = kv[1].Trim();
            }
        }
        return dict;
    }

    private string BuildCookieString(Dictionary<string, string> cookieDict)
    {
        var list = new List<string>();
        foreach (var kvp in cookieDict)
        {
            list.Add($"{kvp.Key}={kvp.Value}");
        }
        return string.Join("; ", list);
    }

    private async Task<string> CreateWebQrCodeAsync()
    {
        string url = ApiEndpoints.PassportCreateQrLoginUrl;
        var body = new JsonObject();
        string bodyStr = body.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");

        AddCommonHeaders(request, bodyStr, "", "2", "bll8iq97cem8", "2.90.1");

        try
        {
            var response = await _httpClient.SendAsync(request);
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            if (result["retcode"]?.GetValue<int>() == 0) 
                return result["data"]["ticket"]?.GetValue<string>();
        }
        catch { }

        return null;
    }

    private async Task<bool> SimulateAppActionAsync(string url, string ticket, string authCookie)
    {
        var tokenTypes = new JsonArray { "4" }; 
        var body = new JsonObject { ["ticket"] = ticket, ["token_types"] = tokenTypes };
        string bodyStr = body.ToJsonString(_jsonOptions);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
        AddCommonHeaders(request, bodyStr, "", "2", "bll8iq97cem8", "2.90.1", authCookie);

        try
        {
            var response = await _httpClient.SendAsync(request);
            var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());
            return result["retcode"]?.GetValue<int>() == 0;
        }
        catch { }
        return false;
    }

    private async Task<Dictionary<string, string>> GetWebQrStatusAndExtractCookiesAsync(string ticket)
    {
        string url = ApiEndpoints.PassportQueryQrLoginStatusUrl;
        var body = new JsonObject { ["ticket"] = ticket };
        string bodyStr = body.ToJsonString(_jsonOptions);

        for (int i = 0; i < 3; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(bodyStr, Encoding.UTF8, "application/json");
            AddCommonHeaders(request, bodyStr, "", "2", "bll8iq97cem8", "2.90.1");

            try
            {
                var response = await _httpClient.SendAsync(request);
                var result = JsonNode.Parse(await response.Content.ReadAsStringAsync());

                if (result["retcode"]?.GetValue<int>() == 0)
                {
                    string status = result["data"]["status"]?.GetValue<string>();
                    if (status == "Confirmed" || status == "confirmed")
                    {
                        var cookieDict = new Dictionary<string, string>();
                        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
                        {
                            foreach (var cookieStr in setCookies)
                            {
                                var mainPart = cookieStr.Split(';')[0];
                                var kv = mainPart.Split('=', 2);
                                if (kv.Length == 2) cookieDict[kv[0].Trim()] = kv[1].Trim();
                            }
                        }
                        return cookieDict;
                    }
                }
            }
            catch { }
            await Task.Delay(1000);
        }
        return null;
    }

    private void AddCommonHeaders(HttpRequestMessage request, string body, string query, string clientType, string appId, string sdkVersion, string cookie = "")
    {
        request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 miHoYoBBS/2.90.1 Capture/2.2.0");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-cn");

        if (!string.IsNullOrEmpty(cookie)) request.Headers.TryAddWithoutValidation("Cookie", cookie);

        request.Headers.TryAddWithoutValidation("x-rpc-client_type", clientType);
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.90.1");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", _deviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_fp", _deviceFp);
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", appId);
        request.Headers.TryAddWithoutValidation("x-rpc-sdk_version", sdkVersion);

        request.Headers.TryAddWithoutValidation("DS", GenerateDS(body, query));
    }

    private string GenerateDeviceFingerprint()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string seedId = GenerateRandomString(16, "0123456789abcdef");

        var deviceInfo = new
        {
            device_id = _deviceId,
            seed_id = seedId,
            seed_time = timestamp,
            platform = "2",
            device_fp = "",
            app_name = "bbs_cn"
        };

        string fpStr = JsonSerializer.Serialize(deviceInfo, _jsonOptions);
        return CreateMD5(fpStr);
    }

    private string GenerateDS(string body, string query)
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GenerateRandomString(6, "abcdefghijklmnopqrstuvwxyz0123456789");
        
        string b = string.IsNullOrEmpty(body) ? "" : body;
        string q = string.IsNullOrEmpty(query) ? "" : query; 

        string signStr = $"salt={Salt}&t={t}&r={r}&b={b}&q={q}";
        string sign = CreateMD5(signStr);

        return $"{t},{r},{sign}";
    }

    private string GenerateWebDS()
    {
        long t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string r = GenerateRandomString(6, "abcdefghijklmnopqrstuvwxyz0123456789");
        string c = CreateMD5($"salt={WebSalt}&t={t}&r={r}");
        
        return $"{t},{r},{c}";
    }

    private string GenerateRandomString(int length, string chars)
    {
        var random = new Random();
        var result = new char[length];
        for (int i = 0; i < length; i++)
        {
            result[i] = chars[random.Next(chars.Length)];
        }
        return new string(result);
    }

    private string CreateMD5(string input)
    {
        using (MD5 md5 = MD5.Create())
        {
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);
            
            StringBuilder sb = new();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }
}