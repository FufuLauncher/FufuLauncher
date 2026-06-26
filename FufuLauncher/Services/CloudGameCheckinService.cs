/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Constants;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class CloudGameCheckinService : ICloudGameCheckinService
{
    private readonly HttpClient _httpClient;

    public CloudGameCheckinService()
    {
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    public async Task<CheckinTypeResult> ExecuteCheckinAsync(string uid, string comboToken)
    {
        var result = new CheckinTypeResult { TypeName = "云原神签到", Executed = true };

        if (string.IsNullOrEmpty(comboToken))
        {
            result.Success = false;
            result.Message = "缺少云游戏凭证 (x-rpc-combo_token)";
            return result;
        }

        try
        {
            Debug.WriteLine($"[云原神签到] 账号 {uid}: 正在签到");

            JsonElement data;
            try
            {
                data = await RequestWalletAsync(comboToken);
            }
            catch (Exception)
            {
                result.Success = false;
                result.Message = "请求失败";
                return result;
            }

            int retcode = TryGetInt(data, "retcode");
            if (retcode == -100)
            {
                result.Success = false;
                result.Message = "token 失效或账号状态受限";
                return result;
            }
            if (retcode != 0)
            {
                var msg = TryGetString(data, "message") ?? "未知错误";
                result.Success = false;
                result.Message = $"{msg}({retcode})";
                return result;
            }

            var wallet = TryGetProperty(data, "data");
            int freeTime = GetFreeTime(wallet);
            int sendFreeTime = GetSendFreeTime(wallet);
            int gained = sendFreeTime;

            if (gained <= 0 && freeTime < 600)
            {
                gained = await RetryDetectGainedTime(comboToken, freeTime);
            }

            if (gained > 0)
            {
                result.Success = true;
                result.SuccessCount++;
                result.Message = $"获得 {gained} 分钟免费时长";
                result.Details.Add($"当前免费时长: {FormatMinutes(freeTime + gained)}");
            }
            else
            {
                result.Success = true;
                result.SkippedCount++;
                result.Message = "今日已签到或已达上限";
                result.Details.Add($"当前免费时长: {FormatMinutes(freeTime)}");
            }

            if (wallet.HasValue)
            {
                var playCardMsg = TryGetString(wallet.Value, "play_card", "short_msg") ?? "未知";
                int coinNum = 0;
                var coinProp = TryGetProperty(wallet.Value, "coin");
                if (coinProp != null)
                    coinNum = TryGetInt(coinProp.Value, "coin_num");
                result.Details.Add($"畅玩卡状态: {playCardMsg}，米游币: {coinNum}");
            }

            Debug.WriteLine($"[云原神签到] 账号 {uid}: {result.Message}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"异常: {ex.Message}";
            Debug.WriteLine($"[云原神签到] 账号 {uid} 异常: {ex.Message}");
        }

        return result;
    }

    private async Task<JsonElement> RequestWalletAsync(string comboToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GenshinApiEndpoints.CloudGameWalletUrl);
        request.Headers.Add("Host", GenshinApiEndpoints.CloudGameHost);
        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("x-rpc-combo_token", comboToken);
        request.Headers.Add("Accept-Encoding", "gzip, deflate");
        request.Headers.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/99.0.4844.84 Safari/537.36");
        request.Headers.Add("Referer", GenshinApiEndpoints.CloudGameReferer);

        var response = await _httpClient.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(text).RootElement;
    }

    private async Task<int> RetryDetectGainedTime(string comboToken, int initialFreeTime)
    {
        var random = new Random();
        await Task.Delay(random.Next(3000, 6000));

        JsonElement data;
        try
        {
            data = await RequestWalletAsync(comboToken);
        }
        catch { return 0; }

        if (TryGetInt(data, "retcode") != 0) return 0;

        var wallet = TryGetProperty(data, "data");
        int nextFreeTime = GetFreeTime(wallet);
        return Math.Max(nextFreeTime - initialFreeTime, 0);
    }

    private static int GetFreeTime(JsonElement? wallet)
    {
        if (wallet == null) return 0;
        var freeTimeProp = TryGetProperty(wallet.Value, "free_time");
        return freeTimeProp != null ? TryGetInt(freeTimeProp.Value, "free_time") : 0;
    }

    private static int GetSendFreeTime(JsonElement? wallet)
    {
        if (wallet == null) return 0;
        var freeTimeProp = TryGetProperty(wallet.Value, "free_time");
        return freeTimeProp != null ? TryGetInt(freeTimeProp.Value, "send_freetime") : 0;
    }

    private static string FormatMinutes(int minutes)
    {
        minutes = Math.Max(minutes, 0);
        int hours = minutes / 60;
        int mins = minutes % 60;
        if (hours > 0 && mins > 0) return $"{hours}小时{mins}分钟";
        if (hours > 0) return $"{hours}小时";
        return $"{mins}分钟";
    }

    private static int TryGetInt(JsonElement element, string propertyName)
    {
        try { return element.GetProperty(propertyName).GetInt32(); }
        catch { return 0; }
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        try { return element.GetProperty(propertyName).GetString(); }
        catch { return null; }
    }

    private static string? TryGetString(JsonElement element, string parentProperty, string childProperty)
    {
        try
        {
            var parent = element.GetProperty(parentProperty);
            return parent.GetProperty(childProperty).GetString();
        }
        catch { return null; }
    }

    private static JsonElement? TryGetProperty(JsonElement element, string propertyName)
    {
        try { return element.GetProperty(propertyName); }
        catch { return null; }
    }
}

