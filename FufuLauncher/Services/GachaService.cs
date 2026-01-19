using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class GachaService
{
    private readonly HttpClient _httpClient;
    
    public static readonly Dictionary<string, string> GachaTypes = new()
    {
        { "301", "角色活动祈愿" },
        { "302", "武器活动祈愿" },
        { "200", "常驻祈愿" },
        { "100", "新手祈愿" },
        { "500", "集录祈愿" }
    };

    public GachaService()
    {
        _httpClient = new HttpClient();
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
    
    public async Task<List<GachaLogItem>> FetchGachaLogAsync(string baseUrl, string gachaType)
    {
        var allItems = new List<GachaLogItem>();
        string endId = "0";
        int page = 1;
        
        var uri = new Uri(baseUrl);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        
        var authParams = new[] { "authkey", "authkey_ver", "sign_type" };
        var cleanQuery = System.Web.HttpUtility.ParseQueryString(string.Empty);
        foreach(var key in query.AllKeys)
        {
            if(authParams.Contains(key) || key == "region" || key == "lang")
                cleanQuery[key] = query[key];
        }

        while (true)
        {
            cleanQuery["gacha_type"] = gachaType;
            cleanQuery["page"] = page.ToString();
            cleanQuery["size"] = "20";
            cleanQuery["end_id"] = endId;

            var requestUrl = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}?{cleanQuery}";

            try 
            {
                var json = await _httpClient.GetStringAsync(requestUrl);
                var response = JsonSerializer.Deserialize<GachaLogResponse>(json);

                if (response?.Data?.List == null || response.Data.List.Count == 0)
                    break;

                allItems.AddRange(response.Data.List);
                endId = response.Data.List.Last().Id;
                page++;
                await Task.Delay(200); 
            }
            catch (Exception)
            {
                break;
            }
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