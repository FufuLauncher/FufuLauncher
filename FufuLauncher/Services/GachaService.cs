/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Constants;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class GachaService
{
    private const string Lk2Salt = "sidQFEglajEz7FA0Aj7HQPV88zpf17SO";
    private const string AppVersion = "2.95.1";
    private readonly HttpClient _httpClient;

    public static readonly Dictionary<string, string> GachaTypes = new()
    {
        { "301", "角色活动祈愿" },
        { "302", "武器活动祈愿" },
        { "200", "常驻祈愿" },
        { "100", "新手祈愿" },
        { "400", "角色活动祈愿" },
        { "500", "集录祈愿" }
    };

    public GachaService()
    {
        _httpClient = new HttpClient(new HttpClientHandler { UseCookies = false });
    }

    public async Task<string> GenerateAuthKeyAsync(string stoken, string mid, string stuid, string gameUid)
    {
        try
        {
            var body = $"{{\"auth_appid\":\"webview_gacha\",\"game_biz\":\"hk4e_cn\",\"game_uid\":{gameUid},\"region\":\"cn_gf01\"}}";
            var ds = CalculateLk2Ds();
            var cookie = $"stuid={stuid};stoken={stoken};mid={mid};";

            var request = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.GenAuthKeyUrl);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Headers.TryAddWithoutValidation("DS", ds);
            request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
            request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
            request.Headers.TryAddWithoutValidation("x-rpc-device_id", Guid.NewGuid().ToString("N"));
            request.Headers.TryAddWithoutValidation("Referer", "https://app.mihoyo.com");
            request.Headers.TryAddWithoutValidation("User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{AppVersion}");

            var response = await _httpClient.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("retcode", out var rc) && rc.GetInt32() == 0)
            {
                return root.GetProperty("data").GetProperty("authkey").GetString();
            }
            Debug.WriteLine($"[Gacha] genAuthKey 失败: {json}");
            return null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Gacha] genAuthKey 异常: {ex.Message}");
            return null;
        }
    }

    private static string CalculateLk2Ds()
    {
        var t = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var chars = "abcdefghijklmnopqrstuvwxyz0123456789";
        var r = new string(Enumerable.Range(0, 6).Select(_ => chars[new Random().Next(chars.Length)]).ToArray());
        var check = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"salt={Lk2Salt}&t={t}&r={r}"))).ToLowerInvariant();
        return $"{t},{r},{check}";
    }

    public string ExtractBaseUrl(string fullUrl)
    {
        if (string.IsNullOrEmpty(fullUrl)) return null;
        var match = Regex.Match(fullUrl, @"(https://.+?/api/getGachaLog\?.+)");
        if (match.Success)
        {
            var url = match.Groups[1].Value;
            int hashIndex = url.IndexOf("#");
            if (hashIndex > 0) url = url.Substring(0, hashIndex);
            return url;
        }
        return null;
    }

    public async Task<List<GachaLogItem>> FetchGachaLogAsync(string baseUrl, string gachaType, Action<int> onPageFetched = null, long knownEndId = 0)
    {
        var allItems = new List<GachaLogItem>();
        string endId = "0";
        int page = 1;
        bool reachedKnown = false;
        const int maxRetry = 3;
        int[] retryDelays = { 2000, 4000, 6000 };

        var uri = new Uri(baseUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

        var authParams = new[] { "authkey", "authkey_ver", "sign_type" };
        var cleanQuery = System.Web.HttpUtility.ParseQueryString(string.Empty);
        foreach (var key in query.AllKeys)
        {
            if (authParams.Contains(key) || key == "region" || key == "lang")
                cleanQuery[key] = query[key];
        }

        while (true)
        {
            cleanQuery["gacha_type"] = gachaType;
            cleanQuery["page"] = page.ToString();
            cleanQuery["size"] = "20";
            cleanQuery["end_id"] = endId;

            var requestUrl = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}?{cleanQuery}";

            bool gotData = false;
            bool reachedEnd = false;

            for (int retry = 0; retry < maxRetry; retry++)
            {
                try
                {
                    var json = await _httpClient.GetStringAsync(requestUrl);
                    var response = JsonSerializer.Deserialize<GachaLogResponse>(json);

                    if (response?.Retcode != 0)
                    {
                        Debug.WriteLine($"[Gacha] type={gachaType} page={page} 重试 {retry + 1}/{maxRetry}, retcode={response?.Retcode}, message={response?.Message}");
                        if (retry < maxRetry - 1)
                        {
                            await Task.Delay(retryDelays[retry]);
                            continue;
                        }
                        Debug.WriteLine($"[Gacha] type={gachaType} page={page} 重试耗尽，跳过");
                        break;
                    }

                    if (response?.Data?.List == null || response.Data.List.Count == 0)
                    {
                        Debug.WriteLine($"[Gacha] type={gachaType} page={page} 返回空列表，正常结束");
                        reachedEnd = true;
                        break;
                    }

                    Debug.WriteLine($"[Gacha] type={gachaType} page={page} 获取 {response.Data.List.Count} 条, end_id={response.Data.List.Last().Id}{(knownEndId > 0 ? $", 增量基线={knownEndId}" : "")}");

                    if (knownEndId > 0)
                    {
                        bool reachedBoundary = false;
                        foreach (var item in response.Data.List)
                        {
                            if (long.TryParse(item.Id, out var itemId) && itemId <= knownEndId)
                            {
                                reachedBoundary = true;
                                break;
                            }
                            allItems.Add(item);
                        }

                        endId = response.Data.List.Last().Id;
                        onPageFetched?.Invoke(allItems.Count);
                        await Task.Delay(500);

                        if (reachedBoundary)
                        {
                            reachedKnown = true;
                            Debug.WriteLine($"[Gacha] type={gachaType} 增量更新到达已知记录边界，提前结束");
                        }
                        else
                        {
                            page++;
                        }
                        gotData = true;
                    }
                    else
                    {
                        allItems.AddRange(response.Data.List);
                        endId = response.Data.List.Last().Id;
                        page++;
                        onPageFetched?.Invoke(allItems.Count);
                        await Task.Delay(500);
                        gotData = true;
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Gacha] type={gachaType} page={page} 重试 {retry + 1}/{maxRetry}, 异常: {ex.GetType().Name}: {ex.Message}");
                    if (retry < maxRetry - 1)
                    {
                        await Task.Delay(retryDelays[retry]);
                        continue;
                    }
                    Debug.WriteLine($"[Gacha] type={gachaType} page={page} 重试耗尽，跳过");
                }
            }

            if (reachedEnd || !gotData || reachedKnown) break;
        }

        allItems.Reverse();
        return allItems;
    }

    public GachaStatistic AnalyzePool(string gachaTypeId, List<GachaLogItem> items)
    {
        var stat = new GachaStatistic
        {
            PoolName = GachaTypes.ContainsKey(gachaTypeId) ? GachaTypes[gachaTypeId] : gachaTypeId,
            TotalCount = items.Count,
            CurrentPity = 0,
            CurrentPity4 = 0
        };

        int pityCounter5 = 0;
        int pityCounter4 = 0;

        foreach (var item in items)
        {
            pityCounter5++;
            pityCounter4++;

            if (item.RankType == "5")
            {
                stat.FiveStarRecords.Add(new FiveStarRecord
                {
                    Name = item.Name,
                    ItemId = item.ItemId,
                    PityUsed = pityCounter5,
                    Time = item.Time,
                    Rank = 5
                });
                stat.FiveStarCount++;
                pityCounter5 = 0;
            }
            else if (item.RankType == "4")
            {
                stat.FourStarRecords.Add(new FiveStarRecord
                {
                    Name = item.Name,
                    ItemId = item.ItemId,
                    PityUsed = pityCounter4,
                    Time = item.Time,
                    Rank = 4
                });
                stat.FourStarCount++;
                pityCounter4 = 0;
            }
        }

        stat.CurrentPity = pityCounter5;
        stat.CurrentPity4 = pityCounter4;

        stat.FiveStarRecords.Reverse();
        stat.FourStarRecords.Reverse();

        return stat;
    }
}
