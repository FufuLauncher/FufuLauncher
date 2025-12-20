using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MihoyoBBS
{

    public class Config
    {
        public AccountConfig Account
        {
            get;
            set;
        } = new AccountConfig();

        public DeviceConfig Device
        {
            get;
            set;
        } = new DeviceConfig();

        public GamesConfig Games
        {
            get;
            set;
        } = new GamesConfig();
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
        } = new CnConfig();
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

        public List<string> BlackList
        {
            get;
            set;
        } = new List<string>();
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

        public List<AccountItem> AccountList
        {
            get;
            protected set;
        } = new List<AccountItem>();

        protected List<RewardItem> CheckinRewards;

        protected static readonly string WebApi = "https://api-takumi.mihoyo.com";
        protected readonly string AccountInfoUrl = $"{WebApi}/binding/api/getUserGameRolesByCookie";
        protected readonly string CheckinRewardsUrl = $"{WebApi}/event/luna/home";
        protected readonly string IsSignUrl = $"{WebApi}/event/luna/info";
        protected readonly string SignUrl = $"{WebApi}/event/luna/sign";

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
                Debug.WriteLine(" [API] 警告: Cookie中缺少cookie_token字段");
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
            AccountList = await GetAccountListAsync(config).ConfigureAwait(false);
            if (AccountList?.Count > 0)
                CheckinRewards = await GetCheckinRewardsAsync().ConfigureAwait(false);
        }

        protected async Task<List<AccountItem>> GetAccountListAsync(Config config)
        {
            Console.WriteLine($"正在获取米哈游账号绑定的「{GameName}」账号列表...");
            try
            {
                var url = $"{AccountInfoUrl}?game_biz={GameId}";
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    AddHeadersToRequest(request);
                    var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                    var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    var result = JsonSerializer.Deserialize<ApiResponse<AccountInfoData>>(responseText);
                    if (result != null && result.RetCode == 0 && result.Data?.List != null)
                        return result.Data.List;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取账号列表异常: {ex.Message}");
            }

            return new List<AccountItem>();
        }

        protected async Task<List<RewardItem>> GetCheckinRewardsAsync()
        {
            Console.WriteLine("正在获取签到奖励列表...");
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
                catch (Exception ex)
                {
                    Console.WriteLine($"获取奖励列表失败: {ex.Message}");
                }

                await Task.Delay(5000);
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
                    if (result != null && result.RetCode == 0 && result.Data != null)
                    {
                        return result.Data;
                    }
                    else
                    {
                        var msg = result?.Message ?? "未知错误";
                        Console.WriteLine($"获取账号签到信息失败: {msg} (retcode: {result?.RetCode})");

                        if (!responseText.Trim().StartsWith("{"))
                        {
                            Console.WriteLine($"原始响应: {responseText}");
                        }

                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"获取签到信息异常: {ex.Message}");
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
                    Console.WriteLine($"触发验证码，即将进行第 {i - 1} 次重试，最多 {retries} 次");
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
                            Console.WriteLine("429 Too Many Requests，等待10秒后重试");
                            continue;
                        }

                        var responseText = await result.Content.ReadAsStringAsync();
                        Console.WriteLine($"签到API响应: {responseText}");

                        var data = JsonSerializer.Deserialize<ApiResponse<SignResponseData>>(responseText);
                        if (data != null && data.RetCode == 0 && data.Data != null && data.Data.Success == 1 &&
                            i <= retries)
                        {

                            await Task.Delay(new Random().Next(6000, 15000));
                            continue;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"签到异常: {ex.Message}");
                    return null;
                }
            }

            return result;
        }

        public async Task<string> SignAccountAsync(Config config)
        {
            var returnData = $"{GameName}: ";

            if (AccountList == null || AccountList.Count == 0)
            {
                Console.WriteLine($"账号没有绑定任何{GameName}账号！");
                returnData += $"\n并没有绑定任何{GameName}账号";
                return returnData;
            }

            foreach (var account in AccountList)
            {
                if (config.Games.Cn.Genshin.BlackList.Contains(account.GameUid))
                {
                    Console.WriteLine($"跳过黑名单账号: {account.Nickname} ({account.GameUid})");
                    continue;
                }

                Console.WriteLine($"正在为{PlayerName}「{account.Nickname} ({account.GameUid})」进行签到...");
                await Task.Delay(new Random().Next(2000, 8000));

                var isData = await IsSignAsync(account.Region, account.GameUid);
                if (isData == null)
                {
                    Console.WriteLine($"{account.Nickname}，获取签到信息失败");
                    returnData += $"\n{account.Nickname}，获取签到信息失败";
                    continue;
                }

                if (isData.FirstBind)
                {
                    Console.WriteLine($"{PlayerName}「{account.Nickname}」是第一次绑定米游社，请先手动签到一次");
                    returnData += $"\n{account.Nickname}是第一次绑定米游社，请先手动签到一次";
                    continue;
                }

                var signDays = isData.TotalSignDay - 1;

                if (isData.IsSign)
                {
                    if (CheckinRewards != null && CheckinRewards.Count > signDays)
                    {
                        Console.WriteLine($"{PlayerName}「{account.Nickname}」今天已经签到过了~");
                        Console.WriteLine($"今天获得的奖励是{Tools.GetItem(CheckinRewards[signDays])}");
                        returnData += $"\n{account.Nickname}今天已经签到过了";
                        returnData += $"\n今天获得的奖励是{Tools.GetItem(CheckinRewards[signDays])}";
                        signDays += 1;
                    }
                    else
                    {
                        Console.WriteLine($"{PlayerName}「{account.Nickname}」今天已经签到过了~");
                        returnData += $"\n{account.Nickname}今天已经签到过了";
                        signDays += 1;
                    }
                }
                else
                {
                    Console.WriteLine($"{PlayerName}「{account.Nickname}」今天还未签到，开始签到...");
                    await Task.Delay(new Random().Next(2000, 8000));

                    var req = await CheckIn(account);
                    if (req == null)
                    {
                        Console.WriteLine($"{account.Nickname}，签到失败！");
                        returnData += $"\n{account.Nickname}，本次签到失败";
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
                                Console.WriteLine($"{PlayerName}「{account.Nickname}」签到成功~");

                                var rewardIndex = (signDays == 0) ? 0 : signDays + 1;
                                if (CheckinRewards != null && CheckinRewards.Count > rewardIndex)
                                {
                                    Console.WriteLine($"今天获得的奖励是{Tools.GetItem(CheckinRewards[rewardIndex])}");
                                    returnData += $"\n{account.Nickname}签到成功";
                                    returnData += $"\n今天获得的奖励是{Tools.GetItem(CheckinRewards[rewardIndex])}";
                                    signDays += 2;
                                }
                                else
                                {
                                    Console.WriteLine("签到成功（无法获取奖励信息）");
                                    returnData += $"\n{account.Nickname}签到成功";
                                    signDays += 2;
                                }
                            }
                            else if (data.RetCode == -5003)
                            {
                                Console.WriteLine($"{PlayerName}「{account.Nickname}」今天已经签到过了~");
                                if (CheckinRewards != null && CheckinRewards.Count > signDays)
                                {
                                    Console.WriteLine($"今天获得的奖励是{Tools.GetItem(CheckinRewards[signDays])}");
                                    returnData += $"\n{account.Nickname}今天已经签到过了";
                                    returnData += $"\n今天获得的奖励是{Tools.GetItem(CheckinRewards[signDays])}";
                                }
                            }
                            else
                            {
                                var s = "账号签到失败！";
                                if (data.Data != null && data.Data.Success != -1)
                                {
                                    s += "原因：验证码\njson 信息：" + responseText;
                                }

                                Console.WriteLine(s);
                                returnData += $"\n{account.Nickname}，触发验证码，本次签到失败";
                                continue;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"{account.Nickname}，本次签到失败（429 Too Many Requests）");
                        returnData += $"\n{account.Nickname}，本次签到失败";
                        continue;
                    }
                }

                returnData += $"\n{account.Nickname}已连续签到{signDays}天";
                if (CheckinRewards != null && CheckinRewards.Count > signDays - 1)
                {
                    returnData += $"\n今天获得的奖励是{Tools.GetItem(CheckinRewards[signDays - 1])}";
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
        public Genshin() : base("hk4e_cn", "原神", "e202311201442471", "旅行者")
        {
        }


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

        class Program
        {
            static async Task Main(string[] args)
            {
                Console.WriteLine("米游社原神签到脚本");
                Console.WriteLine("==========");

                var config = LoadConfig();

                if (config == null)
                {
                    Console.WriteLine("配置文件加载失败，请确保config.json文件存在且格式正确");
                    return;
                }

                if (string.IsNullOrEmpty(config.Account.Cookie))
                {
                    Console.WriteLine("请先在config.json中配置米游社Cookie");
                    Console.WriteLine("Cookie需要包含以下字段：");
                    Console.WriteLine("1. account_id (或 ltuid, login_uid)");
                    Console.WriteLine("2. cookie_token");
                    Console.WriteLine("3. login_ticket (可选)");
                    return;
                }

                if (string.IsNullOrEmpty(config.Device.Id))
                {
                    config.Device.Id = Tools.GetDeviceId(config.Account.Cookie);
                    Console.WriteLine($"已自动生成设备ID: {config.Device.Id}");
                }

                if (config.Games.Cn.Enable && config.Games.Cn.Genshin.Checkin)
                {
                    Console.WriteLine("正在进行「原神」签到");

                    try
                    {
                        var genshin = new Genshin();

                        var result = await genshin.SignAccountAsync(config);
                        Console.WriteLine("\n签到结果：");
                        Console.WriteLine(result);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"签到过程中出现异常: {ex.Message}");
                        Console.WriteLine($"详细异常: {ex.StackTrace}");
                    }
                }
                else
                {
                    Console.WriteLine("原神签到功能已禁用");
                }

                Console.WriteLine("==========");
                Console.WriteLine("签到完成");

                Console.WriteLine("\n按任意键退出...");
                Console.ReadKey();
            }

            static Config LoadConfig()
            {
                try
                {
                    var configPath = "config.json";
                    if (!File.Exists(configPath))
                    {

                        var defaultConfig = new Config();
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        var json = JsonSerializer.Serialize(defaultConfig, options);
                        File.WriteAllText(configPath, json);
                        Console.WriteLine("已创建默认配置文件 config.json，请在其中填写您的Cookie");

                        var fullPath = Path.GetFullPath(configPath);
                        Console.WriteLine($"配置文件位置: {fullPath}");

                        Console.WriteLine("\nCookie示例格式：");
                        Console.WriteLine(@"account_id=12345678; cookie_token=abcdefgh12345678; login_ticket=xyz123");
                        return null;
                    }

                    var jsonText = File.ReadAllText(configPath);
                    return JsonSerializer.Deserialize<Config>(jsonText);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"加载配置文件失败: {ex.Message}");
                    return null;
                }
            }
        }
    }
}