/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FufuLauncher.Models
{
  
    public class OsRewardItem
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("cnt")] public int Count { get; set; }
    }

    public class OsCheckinRewardsData
    {
        [JsonPropertyName("awards")] public List<OsRewardItem> Awards { get; set; }
    }

    public class OsAccountItem
    {
        [JsonPropertyName("nickname")] public string Nickname { get; set; }
        [JsonPropertyName("game_uid")] public string GameUid { get; set; }
        [JsonPropertyName("region")] public string Region { get; set; }
    }

    public class OsAccountInfoData
    {
        [JsonPropertyName("list")] public List<OsAccountItem> List { get; set; }
    }

   
    public class OsIsSignData
    {
        [JsonPropertyName("total_sign_day")] public int TotalSignDay { get; set; }
        [JsonPropertyName("today")] public string Today { get; set; }
        [JsonPropertyName("is_sign")] public bool IsSign { get; set; }
        [JsonPropertyName("first_bind")] public bool FirstBind { get; set; }
    }


    public class OsApiResponse<T>
    {
        [JsonPropertyName("retcode")] public int RetCode { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; }
        [JsonPropertyName("data")] public T Data { get; set; }
    }

    
    public class OsSignResponseData
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("first_bind")] public bool FirstBind { get; set; }
    }

    public class HoyolabSignResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int SuccessCount { get; set; }
        public int FailCount { get; set; }
        public int SkippedCount { get; set; }
    }

    public class HoyolabCheckinService
    {
      
        public string BaseApi { get; set; } = "https://sg-hk4e-api.hoyolab.com";
        public string ActId { get; set; } = "e202102251931481";
        public string GameBiz { get; set; } = "hk4e_global";
        public string DsSalt { get; set; } = "okr4obncj8bw5a65hbnn5oo6ixjc3l9w";
        public string DsSalt2 { get; set; } = "h4c1d6ywfq5bsbnbhm1bzq7bxzzv6srt";

        private HttpClient _httpClient;
        private Dictionary<string, string> _headers;

        public static string LastApiError { get; set; } = string.Empty;
        public static int LastSignDays { get; set; } = 0;
        public static string LastRewardItem { get; set; } = "无/未知";

        public List<OsAccountItem> AccountList { get; private set; } = new();
        private List<OsRewardItem> _checkinRewards;

        public HoyolabCheckinService()
        {
            _httpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

       
        private void SetHeaders(string cookie)
        {
            var deviceId = Guid.NewGuid().ToString("N");

            _headers = new Dictionary<string, string>
            {
                ["Accept"] = "application/json, text/plain, */*",
                ["Origin"] = "https://act.hoyolab.com",
                ["x-rpc-app_version"] = "3.13.0",
                ["x-rpc-client_type"] = "5",
                ["x-rpc-language"] = "zh-cn",
                ["Referer"] = "https://act.hoyolab.com/",
                ["Accept-Encoding"] = "gzip, deflate",
                ["Accept-Language"] = "zh-CN,en-US;q=0.8",
                ["Cookie"] = cookie,
                ["x-rpc-device_id"] = deviceId,
                ["x-rpc-game_biz"] = GameBiz,
                ["X-Requested-With"] = "com.mihoyo.hoyolab",
                ["User-Agent"] = "Mozilla/5.0 (Linux; Android 13; Pixel 5) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/118.0.0.0 Mobile Safari/537.36 miHoYoBBSOversea/3.13.0"
            };
        }

        private string GenerateDs()
        {
            var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var r = RandomString(6);
            var c = Md5($"salt={DsSalt}&t={t}&r={r}");
            return $"{t},{r},{c}";
        }

        private string GenerateDs2(string body = "", string query = "")
        {
            var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var r = new Random().Next(100001, 200000).ToString();
            var b = string.IsNullOrEmpty(body) ? "" : body;
            var q = string.IsNullOrEmpty(query) ? "" : query;

            if (!string.IsNullOrEmpty(q))
            {
                var pairs = q.Split('&').OrderBy(x => x).ToArray();
                q = string.Join("&", pairs);
            }

            var c = Md5($"salt={DsSalt2}&t={t}&r={r}&b={b}&q={q}");
            return $"{t},{r},{c}";
        }

        private string Md5(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var r = new Random();
            return new string(Enumerable.Range(0, length).Select(_ => chars[r.Next(chars.Length)]).ToArray());
        }

        private void AddHeaders(HttpRequestMessage request)
        {
            foreach (var h in _headers)
                AddHeader(request, h.Key, h.Value);
        }

        private void AddHeader(HttpRequestMessage request, string key, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            switch (key.ToLower())
            {
                case "cookie":
                    request.Headers.Add("Cookie", value);
                    break;
                case "user-agent":
                    request.Headers.UserAgent.ParseAdd(value);
                    break;
                case "referer":
                    request.Headers.Referrer = new Uri(value);
                    break;
                case "accept-encoding":
                case "accept-language":
                    request.Headers.TryAddWithoutValidation(key, value);
                    break;
                default:
                    request.Headers.Add(key, value);
                    break;
            }
        }

   
        public async Task InitializeAsync(string cookie, List<OsAccountItem>? fallbackAccounts = null)
        {
            LastApiError = string.Empty;
            SetHeaders(cookie);
            AccountList = await GetAccountListAsync();
            if (AccountList.Count == 0 && fallbackAccounts != null && fallbackAccounts.Count > 0)
                AccountList = fallbackAccounts;
            if (AccountList.Count > 0)
                _checkinRewards = await GetCheckinRewardsAsync();
        }

        private async Task<List<OsAccountItem>> GetAccountListAsync()
        {
            try
            {
                var query = $"game_biz={GameBiz}";
                var url = $"https://api-account-os.hoyolab.com/binding/api/getUserGameRolesByCookieToken?{query}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                AddHeaders(req);
                req.Headers.Remove("DS");
                req.Headers.Add("DS", GenerateDs2(query: query));
                var resp = await _httpClient.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OsApiResponse<OsAccountInfoData>>(text);
                if (result != null && result.RetCode == 0 && result.Data?.List != null)
                    return result.Data.List;
                LastApiError = result?.Message ?? "解析账号列表失败";
            }
            catch (Exception ex)
            {
                LastApiError = $"获取账号列表异常: {ex.Message}";
            }
            return new List<OsAccountItem>();
        }

        private async Task<List<OsRewardItem>> GetCheckinRewardsAsync()
        {
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var query = $"act_id={ActId}&lang=zh-cn";
                    var url = $"{BaseApi}/event/sol/home?{query}";
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    AddHeaders(req);
                    req.Headers.Remove("DS");
                    req.Headers.Add("DS", GenerateDs2(query: query));
                    var resp = await _httpClient.SendAsync(req);
                    var text = await resp.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<OsApiResponse<OsCheckinRewardsData>>(text);
                    if (result != null && result.RetCode == 0 && result.Data?.Awards != null)
                        return result.Data.Awards;
                }
                catch { }
                await Task.Delay(5000);
            }
            return new List<OsRewardItem>();
        }

        public async Task<OsIsSignData> IsSignAsync(string region, string uid)
        {
            try
            {
                var query = $"act_id={ActId}&lang=zh-cn&region={region}&uid={uid}";
                var url = $"{BaseApi}/event/sol/info?{query}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                AddHeaders(req);
                req.Headers.Remove("DS");
                req.Headers.Add("DS", GenerateDs2(query: query));
                var resp = await _httpClient.SendAsync(req);
                var text = await resp.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OsApiResponse<OsIsSignData>>(text);
                if (result != null && result.RetCode == 0 && result.Data != null)
                    return result.Data;
                LastApiError = result?.Message ?? "解析签到状态失败";
            }
            catch (Exception ex)
            {
                LastApiError = $"请求签到状态异常: {ex.Message}";
            }
            return null;
        }

      
        private async Task<HttpResponseMessage> DoSignAsync(OsAccountItem account)
        {
            var headers = new Dictionary<string, string>(_headers);

            for (int i = 1; i <= 4; i++)
            {
                try
                {
                    var body = new { act_id = ActId, region = account.Region, uid = account.GameUid };
                    var jsonBody = JsonSerializer.Serialize(body);

                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{BaseApi}/event/sol/sign");
                    foreach (var h in headers)
                        AddHeader(req, h.Key, h.Value);

                    req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                   
                    req.Headers.Remove("DS");
                    req.Headers.Add("DS", GenerateDs2(body: jsonBody));

                    var resp = await _httpClient.SendAsync(req);
                    if ((int)resp.StatusCode == 429)
                    {
                        await Task.Delay(10000);
                        continue;
                    }
                    return resp;
                }
                catch { return null; }
            }
            return null;
        }

   
        public async Task<string> SignAccountAsync(string cookie, HashSet<string> disabledUids = null)
        {
            var signResult = await SignAccountWithResultAsync(cookie, disabledUids);
            return signResult.Message;
        }

        public async Task<HoyolabSignResult> SignAccountWithResultAsync(string cookie, HashSet<string> disabledUids = null, string targetUid = null)
        {
            LastApiError = string.Empty;
            var message = "HoYoLAB: ";
            var signResult = new HoyolabSignResult();

            if (AccountList.Count == 0)
            {
                message += "未检测到绑定账号";
                if (!string.IsNullOrEmpty(LastApiError))
                    message += $"，原因: {LastApiError}";
                signResult.FailCount++;
                signResult.Message = message;
                return signResult;
            }

            foreach (var account in AccountList)
            {
                if (disabledUids != null && disabledUids.Contains(account.GameUid))
                {
                    signResult.SkippedCount++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(targetUid) && account.GameUid != targetUid)
                {
                    signResult.SkippedCount++;
                    continue;
                }

                await Task.Delay(new Random().Next(2000, 8000));

                var isData = await IsSignAsync(account.Region, account.GameUid);
                if (isData == null)
                {
                    message += $"\n{account.Nickname} 获取签到信息失败";
                    if (!string.IsNullOrEmpty(LastApiError))
                        message += $"，详情: {LastApiError}";
                    signResult.FailCount++;
                    continue;
                }

                if (isData.FirstBind)
                {
                    message += $"\n{account.Nickname}是第一次绑定HoYoLAB，请先手动签到一次";
                    signResult.FailCount++;
                    continue;
                }

                var signDays = isData.TotalSignDay;

                if (isData.IsSign)
                {
                    message += $"\n{account.Nickname}今天已经签到过了";
                    var idx = signDays - 1;
                    if (_checkinRewards != null && idx >= 0 && idx < _checkinRewards.Count)
                        message += $"\n今天获得的奖励是{FormatItem(_checkinRewards[idx])}";
                }
                else
                {
                    await Task.Delay(new Random().Next(2000, 8000));

                    var req = await DoSignAsync(account);
                    if (req == null)
                    {
                        message += $"\n{account.Nickname} 本次签到请求失败";
                        if (!string.IsNullOrEmpty(LastApiError))
                            message += $"，详情: {LastApiError}";
                        signResult.FailCount++;
                        continue;
                    }

                    if ((int)req.StatusCode == 429)
                    {
                        message += $"\n{account.Nickname} 签到失败，触发 HTTP 429 限流";
                        signResult.FailCount++;
                        continue;
                    }

                    var text = await req.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<OsApiResponse<OsSignResponseData>>(text);

                    if (data == null)
                    {
                        message += $"\n{account.Nickname} 解析签到结果失败";
                        signResult.FailCount++;
                        continue;
                    }

                    if (data.RetCode == 0 && data.Data?.Code == "ok")
                    {
                        signDays++;
                        message += $"\n{account.Nickname}签到成功";
                    }
                    else if (data.RetCode == -5003)
                    {
                        message += $"\n{account.Nickname}今天已经签到过了";
                    }
                    else
                    {
                        message += $"\n{account.Nickname} 签到失败，API提示: {data.Message}";
                        signResult.FailCount++;
                        continue;
                    }
                }

                signResult.SuccessCount++;
                message += $"\n{account.Nickname}已签到{signDays}天";
                LastSignDays = signDays;

                var rewardIdx = signDays - 1;
                if (_checkinRewards != null && rewardIdx >= 0 && rewardIdx < _checkinRewards.Count)
                {
                    LastRewardItem = FormatItem(_checkinRewards[rewardIdx]);
                    message += $"\n奖励是{LastRewardItem}";
                }
            }

            if (signResult.SuccessCount == 0 && signResult.FailCount == 0)
                message += "\n没有可签到的账号";

            signResult.Success = signResult.SuccessCount > 0 && signResult.FailCount == 0;
            signResult.Message = message;
            return signResult;
        }

        private string FormatItem(OsRewardItem item)
        {
            return $"「{item.Name}」x{item.Count}";
        }
    }
}

