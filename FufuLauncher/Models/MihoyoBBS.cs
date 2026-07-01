/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Constants;

namespace MihoyoBBS
{
    public class CalendarRewardItem
    {
        [JsonPropertyName("icon")]
        public string Icon { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("cnt")]
        public int Count { get; set; }

        public string CountText => $"x{Count}";
    }

    public class CheckinCalendarData
    {
        [JsonPropertyName("month")]
        public int Month { get; set; }

        [JsonPropertyName("awards")]
        public List<CalendarRewardItem> Awards { get; set; }
    }

    public class Config
    {
        public AccountConfig Account
        {
            get;
            set;
        } = new();

        public DeviceConfig Device
        {
            get;
            set;
        } = new();

        public GamesConfig Games
        {
            get;
            set;
        } = new();

        public DisplayConfig Display
        {
            get;
            set;
        } = new();
    }

    public class AccountConfig
    {
        public string Cookie
        {
            get;
            set;
        } = "";

        public string Stuid
        {
            get;
            set;
        } = "";

        public string Stoken
        {
            get;
            set;
        } = "";

        public string Mid
        {
            get;
            set;
        } = "";

        public string CloudComboToken
        {
            get;
            set;
        } = "";
    }

    public class DeviceConfig
    {
        public string Name
        {
            get;
            set;
        } = "Xiaomi MI 6";

        public string Model
        {
            get;
            set;
        } = "Mi 6";

        public string Id
        {
            get;
            set;
        } = "";

        public string Fp
        {
            get;
            set;
        } = "";
    }

    public class GamesConfig
    {
        public CnConfig Cn
        {
            get;
            set;
        } = new();
    }

    public class CnConfig
    {
        public bool Enable
        {
            get;
            set;
        } = true;

        public string UserAgent
        {
            get;
            set;
        } =
            "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36";

        public int Retries
        {
            get;
            set;
        } = 3;

        public GameConfig Genshin
        {
            get;
            set;
        } = new GameConfig();
    }

    public class GameConfig
    {
        public bool Checkin
        {
            get;
            set;
        } = true;
    }

    public class ApiResponse<T>
    {
        [JsonPropertyName("retcode")]
        public int RetCode
        {
            get;
            set;
        }

        [JsonPropertyName("message")]
        public string Message
        {
            get;
            set;
        }

        [JsonPropertyName("data")]
        public T Data
        {
            get;
            set;
        }
    }

    public class CheckinRewardsData
    {
        [JsonPropertyName("awards")]
        public List<RewardItem> Awards
        {
            get;
            set;
        }
    }

    public class IsSignData
    {
        [JsonPropertyName("total_sign_day")]
        public int TotalSignDay
        {
            get;
            set;
        }

        [JsonPropertyName("today")]
        public string Today
        {
            get;
            set;
        }

        [JsonPropertyName("is_sign")]
        public bool IsSign
        {
            get;
            set;
        }

        [JsonPropertyName("first_bind")]
        public bool FirstBind
        {
            get;
            set;
        }
    }

    public class AccountInfoData
    {
        [JsonPropertyName("list")]
        public List<AccountItem> List
        {
            get;
            set;
        }
    }

    public class RewardItem
    {
        [JsonPropertyName("name")]
        public string Name
        {
            get;
            set;
        }

        [JsonPropertyName("cnt")]
        public int Count
        {
            get;
            set;
        }
    }

    public class AccountItem
    {
        [JsonPropertyName("nickname")]
        public string Nickname
        {
            get;
            set;
        }

        [JsonPropertyName("game_uid")]
        public string GameUid
        {
            get;
            set;
        }

        [JsonPropertyName("region")]
        public string Region
        {
            get;
            set;
        }
    }

    public class SignResponseData
    {
        [JsonPropertyName("success")]
        public int Success
        {
            get;
            set;
        }

        [JsonPropertyName("gt")]
        public string Gt
        {
            get;
            set;
        }

        [JsonPropertyName("challenge")]
        public string Challenge
        {
            get;
            set;
        }
    }

    public class DisplayConfig
    {
        [JsonPropertyName("Nickname")]
        public string Nickname
        {
            get;
            set;
        } = "";

        [JsonPropertyName("GameUid")]
        public string GameUid
        {
            get;
            set;
        } = "";

        [JsonPropertyName("Server")]
        public string Server
        {
            get;
            set;
        } = "";

        [JsonPropertyName("AvatarUrl")]
        public string AvatarUrl
        {
            get;
            set;
        } = "ms-appx:///Assets/DefaultAvatar.png";

        [JsonPropertyName("Level")]
        public string Level
        {
            get;
            set;
        } = "";

        [JsonPropertyName("Sign")]
        public string Sign
        {
            get;
            set;
        } = "这个人很懒，什么都没有写...";

        [JsonPropertyName("IpRegion")]
        public string IpRegion
        {
            get;
            set;
        } = "未知";

        [JsonPropertyName("Gender")]
        public int Gender
        {
            get;
            set;
        } = 0;

        [JsonPropertyName("HasBoundRole")]
        public bool HasBoundRole
        {
            get;
            set;
        } = true;
    }

    public static class Tools
    {
        public static string Md5(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(input);
                var hashBytes = md5.ComputeHash(bytes);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }
        }

        public static string RandomString(int length)
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var result = new char[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = chars[random.Next(chars.Length)];
            }

            return new string(result);
        }

        public static long Timestamp()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public static string GetDs(bool web = true)
        {

            var salt = web ? "G1ktdwFL4IyGkHuuWSmz0wUe9Db9scyK" : "idMMaGYmVgPzh3wxmWudUXKUPGidO7GM";
            var t = Timestamp().ToString();
            var r = RandomString(6);
            var c = Md5($"salt={salt}&t={t}&r={r}");
            return $"{t},{r},{c}";
        }

        public static string GetItem(RewardItem item)
        {
            return $"「{item.Name}」x{item.Count}";
        }

        public static string GetDeviceId(string cookie)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(cookie);
                var hash = md5.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        public static string GetUserAgent(string useragent)
        {
            if (string.IsNullOrEmpty(useragent))
            {
                return "Mozilla/5.0 (Linux; Android 12; Unspecified Device) AppleWebKit/537.36 (KHTML, like Gecko) Version/4.0 Chrome/103.0.5060.129 Mobile Safari/537.36 miHoYoBBS/2.93.1";
            }

            useragent = useragent.Replace("; ", " ").Replace(";", " ");

            if (useragent.Contains("miHoYoBBS"))
            {
                int i = useragent.IndexOf("miHoYoBBS");
                if (i > 0 && useragent[i - 1] == ' ')
                    i = i - 1;
                return $"{useragent.Substring(0, i)} miHoYoBBS/2.93.1";
            }

            return $"{useragent} miHoYoBBS/2.93.1";
        }

        public static string TidyCookie(string cookies)
        {
            var cookieDict = new Dictionary<string, string>();
            var splitCookie = cookies.Split(';');

            if (splitCookie.Length < 2)
                return cookies;

            foreach (var cookie in splitCookie)
            {
                var trimmedCookie = cookie.Trim();
                if (string.IsNullOrEmpty(trimmedCookie))
                    continue;

                var parts = trimmedCookie.Split('=', 2);
                if (parts.Length == 2)
                {
                    cookieDict[parts[0]] = parts[1];
                }
            }

            return string.Join("; ", cookieDict.Select(kv => $"{kv.Key}={kv.Value}"));
        }
    }

    public abstract class GameCheckin
    {
        protected readonly string GameId;
        protected readonly string GameName;
        protected readonly string ActId;
        protected readonly string PlayerName;
        protected HttpClient HttpClient;
        protected Dictionary<string, string> Headers;
        public static string LastApiError { get; set; } = string.Empty;
        public static int LastSignDays { get; set; } = 0;
        public static string LastRewardItem { get; set; } = "无/未知";

        public List<AccountItem> AccountList
        {
            get;
            protected set;
        } = new List<AccountItem>();

        protected List<RewardItem> CheckinRewards;

        protected static readonly string WebApi = ApiEndpoints.MihoyoBbsWebApi;
        protected readonly string AccountInfoUrl = ApiEndpoints.MihoyoBbsAccountInfoUrl;
        protected readonly string CheckinRewardsUrl = ApiEndpoints.MihoyoBbsCheckinRewardsUrl;
        protected readonly string IsSignUrl = ApiEndpoints.MihoyoBbsIsSignUrl;
        protected readonly string SignUrl = ApiEndpoints.MihoyoBbsSignUrl;

        protected GameCheckin(string gameId, string gameName, string actId, string playerName = "玩家")
        {
            GameId = gameId;
            GameName = gameName;
            ActId = actId;
            PlayerName = playerName;

            HttpClient = new HttpClient(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            HttpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<CheckinCalendarData> GetCheckinCalendarAsync()
        {
            try
            {
                var url = $"https://api-takumi.mihoyo.com/event/luna/home?act_id={ActId}&lang=zh-cn";
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    AddHeadersToRequest(request);
                    var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                    var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var result = JsonSerializer.Deserialize<ApiResponse<CheckinCalendarData>>(responseText);
                    if (result != null && result.RetCode == 0)
                    {
                        return result.Data;
                    }
                }
            }
            catch (Exception ex)
            {
                LastApiError = $"获取奖励日历异常: {ex.Message}";
            }
            return null;
        }

        protected virtual void SetHeaders(Config config)
        {
            var deviceId = string.IsNullOrEmpty(config.Device.Id)
                ? Tools.GetDeviceId(config.Account.Cookie)
                : config.Device.Id;

            var cookie = Tools.TidyCookie(config.Account.Cookie);

            var cookieParts = cookie.Split(';')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList();

            var hasCookieToken = cookieParts.Any(p => p.StartsWith("cookie_token="));
            if (!hasCookieToken)
            {

            }

            var userAgent = Tools.GetUserAgent(config.Games.Cn.UserAgent);

            Headers = new Dictionary<string, string>
            {
                ["Accept"] = "application/json, text/plain, */*",
                ["DS"] = Tools.GetDs(true),
                ["x-rpc-channel"] = "miyousheluodi",
                ["Origin"] = "https://act.mihoyo.com",
                ["x-rpc-app_version"] = "2.93.1",
                ["x-rpc-client_type"] = "5",
                ["Referer"] = "https://act.mihoyo.com/",
                ["Accept-Encoding"] = "gzip, deflate",
                ["Accept-Language"] = "zh-CN,en-US;q=0.8",
                ["X-Requested-With"] = "com.mihoyo.hyperion",
                ["Cookie"] = cookie,
                ["x-rpc-device_id"] = deviceId,
                ["User-Agent"] = userAgent,
                ["x-rpc-signgame"] = "hk4e"
            };
        }

        public virtual async Task InitializeAsync(Config config)
        {
            SetHeaders(config);
            // 并行获取账号列表和签到奖励，省一个 HTTP 往返
            var accountTask = GetAccountListAsync(config);
            var rewardsTask = GetCheckinRewardsAsync();
            AccountList = await accountTask.ConfigureAwait(false);
            if (AccountList?.Count > 0)
                CheckinRewards = await rewardsTask.ConfigureAwait(false);
        }

        protected async Task<List<AccountItem>> GetAccountListAsync(Config config)
        {
            try
            {
                var url = $"{AccountInfoUrl}?game_biz={GameId}";
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    AddHeadersToRequest(request);
                    var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                    var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    var result = JsonSerializer.Deserialize<ApiResponse<AccountInfoData>>(responseText);
                    if (result != null)
                    {
                        if (result.RetCode == 0 && result.Data?.List != null)
                        {
                            return result.Data.List;
                        }
                        else
                        {
                            LastApiError = $"{result.Message}";
                        }
                    }
                    else
                    {
                        LastApiError = "解析账号列表响应数据失败";
                    }
                }
            }
            catch (Exception ex)
            {
                LastApiError = $"网络请求异常: {ex.Message}";
            }

            return new List<AccountItem>();
        }

        protected async Task<List<RewardItem>> GetCheckinRewardsAsync()
        {
            var maxRetry = 3;
            for (int i = 0; i < maxRetry; i++)
            {
                try
                {
                    var url = $"{CheckinRewardsUrl}?lang=zh-cn&act_id={ActId}";
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                    {
                        AddHeadersToRequest(request);
                        var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                        var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        var result = JsonSerializer.Deserialize<ApiResponse<CheckinRewardsData>>(responseText);
                        if (result != null && result.RetCode == 0 && result.Data?.Awards != null)
                            return result.Data.Awards;
                    }
                }
                catch (Exception)
                {
                    // ignored
                }

                await Task.Delay(1500);
            }

            return new List<RewardItem>();
        }

        public async Task<IsSignData> IsSignAsync(string region, string uid, bool update = false)
        {
            try
            {
                var url = $"{IsSignUrl}?lang=zh-cn&act_id={ActId}&region={region}&uid={uid}";

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    AddHeadersToRequest(request);

                    var response = await HttpClient.SendAsync(request);
                    var responseText = await response.Content.ReadAsStringAsync();

                    var result = JsonSerializer.Deserialize<ApiResponse<IsSignData>>(responseText);
                    if (result != null)
                    {
                        if (result.RetCode == 0 && result.Data != null)
                        {
                            return result.Data;
                        }
                        else
                        {
                            LastApiError = $"{result.Message}";
                        }
                    }
                    else
                    {
                        LastApiError = "解析签到状态响应数据失败";
                    }

                    return null;
                }
            }
            catch (Exception ex)
            {
                LastApiError = $"请求签到状态异常: {ex.Message}";
                return null;
            }
        }

        protected async Task<HttpResponseMessage> CheckIn(AccountItem account)
        {
            var header = new Dictionary<string, string>(Headers);
            var retries = 3;
            HttpResponseMessage result = null;

            for (int i = 1; i <= retries + 1; i++)
            {
                if (i > 1)
                {
                }

                try
                {
                    var content = new
                    {
                        act_id = ActId,
                        region = account.Region,
                        uid = account.GameUid
                    };

                    var jsonContent = JsonSerializer.Serialize(content);

                    using (var request = new HttpRequestMessage(HttpMethod.Post, SignUrl))
                    {
                        foreach (var h in header)
                        {
                            AddHeaderToRequest(request, h.Key, h.Value);
                        }

                        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                        result = await HttpClient.SendAsync(request);

                        if ((int)result.StatusCode == 429)
                        {
                            await Task.Delay(10000);
                            continue;
                        }

                        var responseText = await result.Content.ReadAsStringAsync();

                        var data = JsonSerializer.Deserialize<ApiResponse<SignResponseData>>(responseText);
                        if (data != null && data.RetCode == 0 && data.Data != null && data.Data.Success == 1 &&
                            i <= retries)
                        {

                            await Task.Delay(new Random().Next(6000, 15000));
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch (Exception)
                {
                    return null;
                }
            }

            return result;
        }

public async Task<string> SignAccountAsync(Config config, string targetUid = null, HashSet<string> disabledUids = null)
    {
        LastApiError = string.Empty;
        var returnData = $"{GameName}: ";

        if (AccountList == null || AccountList.Count == 0)
        {
            returnData += "未检测到绑定账号";
            if (!string.IsNullOrEmpty(LastApiError))
            {
                returnData += $"，原因: {LastApiError}";
            }
            return returnData;
        }

        // 统计需要签到的角色数，只有多角色时才加延时
        var activeAccounts = AccountList
            .Where(a => disabledUids == null || !disabledUids.Contains(a.GameUid))
            .Where(a => string.IsNullOrEmpty(targetUid) || a.GameUid == targetUid)
            .ToList();
        var isFirstActive = true;

        foreach (var account in AccountList)
        {
            if (disabledUids != null && disabledUids.Contains(account.GameUid))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(targetUid) && account.GameUid != targetUid)
            {
                continue;
            }

            if (!isFirstActive)
                await Task.Delay(new Random().Next(2000, 8000));
            else
                isFirstActive = false;

            var isData = await IsSignAsync(account.Region, account.GameUid);
            if (isData == null)
            {
                returnData += $"\n{account.Nickname} 获取签到信息失败";
                if (!string.IsNullOrEmpty(LastApiError))
                {
                    returnData += $"，详情: {LastApiError}";
                }
                continue;
            }

            if (isData.FirstBind)
            {
                returnData += $"\n{account.Nickname}是第一次绑定米游社，请先手动签到一次";
                continue;
            }

            var signDays = isData.TotalSignDay - 1;

            if (isData.IsSign)
            {
                if (CheckinRewards != null && CheckinRewards.Count > signDays)
                {
                    returnData += $"\n{account.Nickname}今天已经签到过了";
                    returnData += $"\n今天获得的奖励是{Tools.GetItem(CheckinRewards[signDays])}";
                    signDays += 1;
                }
                else
                {
                    returnData += $"\n{account.Nickname}今天已经签到过了";
                    signDays += 1;
                }
            }
            else
            {
                if (activeAccounts.Count > 1)
                    await Task.Delay(new Random().Next(2000, 8000));

                var req = await CheckIn(account);
                if (req == null)
                {
                    returnData += $"\n{account.Nickname} 本次签到请求失败";
                    if (!string.IsNullOrEmpty(LastApiError))
                    {
                        returnData += $"，详情: {LastApiError}";
                    }
                    continue;
                }

                if ((int)req.StatusCode != 429)
                {
                    var responseText = await req.Content.ReadAsStringAsync();
                    var data = JsonSerializer.Deserialize<ApiResponse<SignResponseData>>(responseText);

                    if (data != null)
                    {
                        if (data.RetCode == 0 && data.Data != null && data.Data.Success == 0)
                        {
                            var rewardIndex = (signDays == 0) ? 0 : signDays + 1;
                            if (CheckinRewards != null && CheckinRewards.Count > rewardIndex)
                            {
                                returnData += $"\n{account.Nickname}签到成功";
                                returnData += $"\n奖励是{Tools.GetItem(CheckinRewards[rewardIndex])}";
                                signDays += 2;
                            }
                            else
                            {
                                returnData += $"\n{account.Nickname}签到成功";
                                signDays += 2;
                            }
                        }
                        else if (data.RetCode == -5003)
                        {
                            if (CheckinRewards != null && CheckinRewards.Count > signDays)
                            {
                                returnData += $"\n{account.Nickname}今天已经签到过了";
                                returnData += $"\n奖励是{Tools.GetItem(CheckinRewards[signDays])}";
                            }
                        }
                        else
                        {
                            returnData += $"\n{account.Nickname} 签到失败，API提示: {data.Message}";
                            continue;
                        }
                    }
                    else
                    {
                        returnData += $"\n{account.Nickname} 解析签到结果失败";
                        continue;
                    }
                }
                else
                {
                    returnData += $"\n{account.Nickname} 签到失败，触发 HTTP 429 限流";
                    continue;
                }
            }

            returnData += $"\n{account.Nickname}已签到{signDays}天";
            LastSignDays = signDays;
            
            if (CheckinRewards != null && CheckinRewards.Count > signDays - 1)
            {
                LastRewardItem = Tools.GetItem(CheckinRewards[signDays - 1]);
                returnData += $"\n奖励是{LastRewardItem}";
            }
        }

        return returnData;
    }

        private void AddHeadersToRequest(HttpRequestMessage request)
        {
            foreach (var header in Headers)
            {
                AddHeaderToRequest(request, header.Key, header.Value);
            }
        }

        private void AddHeaderToRequest(HttpRequestMessage request, string key, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

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
    }

    public class Genshin : GameCheckin
    {
        public Genshin() : base("hk4e_cn", "原神", "e202311201442471", "旅行者") {}
        public override async Task InitializeAsync(Config config)
        {
            SetHeaders(config);
            Headers["Origin"] = "https://act.mihoyo.com";
            Headers["x-rpc-signgame"] = "hk4e";
            Headers["Referer"] = "https://act.mihoyo.com/";

            AccountList = await GetAccountListAsync(config).ConfigureAwait(false);
            if (AccountList?.Count > 0)
            {
                CheckinRewards = await GetCheckinRewardsAsync().ConfigureAwait(false);
            }
        }

    }
}
