/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace MihoyoBBS;

public abstract class GameCheckin
{
    protected readonly string GameId;
    protected readonly string GameName;
    protected readonly string ActId;
    protected readonly string PlayerName;
    protected HttpClient HttpClient;
    protected Dictionary<string, string> Headers;

    public static string LastApiError
    {
        get;
        set;
    } = string.Empty;

    public static int LastSignDays
    {
        get;
        set;
    } = 0;

    public static string LastRewardItem
    {
        get;
        set;
    } = "Status_None".GetLocalized();

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
    protected readonly string ResignInfoUrl = ApiEndpoints.LunaResignInfoUrl;
    protected readonly string ResignUrl = ApiEndpoints.LunaResignUrl;

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
            var url = $"{CheckinRewardsUrl}?act_id={ActId}&lang=zh-cn";
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
            LastApiError = string.Format("Checkin_GetCalendarException".GetLocalized(), ex.Message);
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
            ["Origin"] = ApiEndpoints.ActMihoyoBase,
            ["x-rpc-app_version"] = "2.93.1",
            ["x-rpc-client_type"] = "5",
            ["Referer"] = ApiEndpoints.ActMihoyoBase + "/",
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
                    LastApiError = "Checkin_ParseResponseDataFailed".GetLocalized();
                }
            }
        }
        catch (Exception ex)
        {
            LastApiError = string.Format("Checkin_NetworkRequestException".GetLocalized(), ex.Message);
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
                    LastApiError = "Checkin_ParseSignStatusFailed".GetLocalized();
                }

                return null;
            }
        }
        catch (Exception ex)
        {
            LastApiError = string.Format("Checkin_RequestSignStatusException".GetLocalized(), ex.Message);
            return null;
        }
    }

    public async Task<CheckinResignInfo?> GetResignInfoAsync(string region, string uid)
    {
        try
        {
            var url = $"{ResignInfoUrl}?lang=zh-cn&act_id={ActId}&region={region}&uid={uid}";
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                AddHeadersToRequest(request);
                var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var result = JsonSerializer.Deserialize<ApiResponse<CheckinResignInfo>>(responseText);
                if (result != null && result.RetCode == 0 && result.Data != null)
                {
                    return result.Data;
                }

                LastApiError = result?.Message ?? "Checkin_ResignQueryFailed".GetLocalized();
            }
        }
        catch (Exception ex)
        {
            LastApiError = string.Format("Checkin_ResignQueryFailed".GetLocalized(), ex.Message);
        }

        return null;
    }

    public async Task<(bool success, string message, int retcode)> ResignAsync(string region, string uid)
    {
        try
        {
            var content = new
            {
                act_id = ActId,
                region,
                uid
            };
            var jsonContent = JsonSerializer.Serialize(content);

            using (var request = new HttpRequestMessage(HttpMethod.Post, ResignUrl))
            {
                foreach (var h in Headers)
                {
                    AddHeaderToRequest(request, h.Key, h.Value);
                }

                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await HttpClient.SendAsync(request).ConfigureAwait(false);
                var responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var data = JsonSerializer.Deserialize<ApiResponse<SignResponseData>>(responseText);
                if (data == null)
                {
                    return (false, "Checkin_ParseResultFailed".GetLocalized(), -1);
                }

                if (data.RetCode == 0)
                {
                    return (true, "Checkin_ResignSuccess".GetLocalized(), 0);
                }

                return (false, data.Message, data.RetCode);
            }
        }
        catch (Exception ex)
        {
            return (false, ex.Message, -1);
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

    public async Task<string> SignAccountAsync(Config config, string targetUid = null,
        HashSet<string> disabledUids = null)
    {
        LastApiError = string.Empty;
        var returnData = $"{GameName}: ";

        if (AccountList == null || AccountList.Count == 0)
        {
            returnData += "Checkin_NoBoundAccount".GetLocalized();
            if (!string.IsNullOrEmpty(LastApiError))
            {
                returnData += string.Format("Checkin_Reason".GetLocalized(), LastApiError);
            }

            return returnData;
        }


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
                returnData += "\n" + account.Nickname + "Checkin_GetInfoFailed".GetLocalized();
                if (!string.IsNullOrEmpty(LastApiError))
                {
                    returnData += string.Format("Checkin_Detail".GetLocalized(), LastApiError);
                }

                continue;
            }

            if (isData.FirstBind)
            {
                returnData += "\n" + account.Nickname + "Checkin_FirstBindWarning".GetLocalized();
                continue;
            }

            var signDays = isData.TotalSignDay - 1;

            if (isData.IsSign)
            {
                if (CheckinRewards != null && CheckinRewards.Count > signDays)
                {
                    returnData += "\n" + account.Nickname + "Checkin_AlreadySignedToday".GetLocalized();
                    returnData += "\n" + string.Format("Checkin_TodayReward".GetLocalized(),
                        Tools.GetItem(CheckinRewards[signDays]));
                    signDays += 1;
                }
                else
                {
                    returnData += "\n" + account.Nickname + "Checkin_AlreadySignedToday".GetLocalized();
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
                    returnData += "\n" + account.Nickname + "Checkin_SignRequestFailed".GetLocalized();
                    if (!string.IsNullOrEmpty(LastApiError))
                    {
                        returnData += string.Format("Checkin_Detail".GetLocalized(), LastApiError);
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
                                returnData += "\n" + account.Nickname + "Checkin_SignSuccess".GetLocalized();
                                returnData += "\n" + string.Format("Checkin_RewardIs".GetLocalized(),
                                    Tools.GetItem(CheckinRewards[rewardIndex]));
                                signDays += 2;
                            }
                            else
                            {
                                returnData += "\n" + account.Nickname + "Checkin_SignSuccess".GetLocalized();
                                signDays += 2;
                            }
                        }
                        else if (data.RetCode == -5003)
                        {
                            if (CheckinRewards != null && CheckinRewards.Count > signDays)
                            {
                                returnData += "\n" + account.Nickname + "Checkin_AlreadySignedToday".GetLocalized();
                                returnData += "\n" + string.Format("Checkin_RewardIs".GetLocalized(),
                                    Tools.GetItem(CheckinRewards[signDays]));
                            }
                        }
                        else
                        {
                            returnData += "\n" + account.Nickname +
                                          string.Format("Checkin_SignFailedApi".GetLocalized(), data.Message);
                            continue;
                        }
                    }
                    else
                    {
                        returnData += "\n" + account.Nickname + "Checkin_ParseResultFailed".GetLocalized();
                        continue;
                    }
                }
                else
                {
                    returnData += "\n" + account.Nickname + "Checkin_SignRateLimited".GetLocalized();
                    continue;
                }
            }

            returnData += "\n" + account.Nickname + string.Format("Checkin_SignedDays".GetLocalized(), signDays);
            LastSignDays = signDays;

            if (CheckinRewards != null && CheckinRewards.Count > signDays - 1)
            {
                LastRewardItem = Tools.GetItem(CheckinRewards[signDays - 1]);
                returnData += "\n" + string.Format("Checkin_RewardIs".GetLocalized(), LastRewardItem);
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