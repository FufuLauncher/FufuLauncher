using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public class UnifiedCheckinService : IUnifiedCheckinService
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IHoyoverseCheckinService _gameCheckinService;
    private readonly ICommunityCheckinService _communityCheckinService;
    private readonly ICloudGameCheckinService _cloudGameCheckinService;

    public UnifiedCheckinService(
        ILocalSettingsService localSettingsService,
        IHoyoverseCheckinService gameCheckinService,
        ICommunityCheckinService communityCheckinService,
        ICloudGameCheckinService cloudGameCheckinService)
    {
        _localSettingsService = localSettingsService;
        _gameCheckinService = gameCheckinService;
        _communityCheckinService = communityCheckinService;
        _cloudGameCheckinService = cloudGameCheckinService;
    }

    public async Task<UnifiedCheckinResult> ExecuteAllCheckinsAsync()
    {
        var result = new UnifiedCheckinResult();

        // 1. Load settings
        var gameEnabled = await GetBoolSettingAsync("IsGameCheckinEnabled", true);
        var communityEnabled = await GetBoolSettingAsync("IsCommunityCheckinEnabled", false);
        var cloudGameEnabled = await GetBoolSettingAsync("IsCloudGameCheckinEnabled", false);
        var communityLike = await GetBoolSettingAsync("IsCommunityLikeEnabled", false);
        var communityRead = await GetBoolSettingAsync("IsCommunityReadEnabled", false);
        var communityShare = await GetBoolSettingAsync("IsCommunityShareEnabled", false);

        // 2. Load accounts and credentials
        var accounts = await LoadAllAccountsAsync();
        var disabledUids = await LoadDisabledUidsAsync();

        if (accounts.Count == 0)
        {
            result.SummaryMessage = "未检测到绑定账号";
            result.GameResult.Message = "未检测到账号";
            result.GameResult.Executed = true;
            return result;
        }

        // Filter disabled accounts
        var activeAccounts = accounts.Where(a => !disabledUids.Contains(a.Uid)).ToList();
        if (activeAccounts.Count == 0)
        {
            result.SummaryMessage = "所有账号已被禁用";
            result.GameResult.Message = "所有账号已被禁用";
            result.GameResult.Executed = true;
            return result;
        }

        // 3. Game Check-in
        if (gameEnabled)
        {
            result.GameResult.Executed = true;
            try
            {
                foreach (var account in activeAccounts)
                {
                    try
                    {
                        var config = await LoadAccountConfigAsync(account.ConfigPath);
                        if (config == null) continue;

                        var genshin = new MihoyoBBS.Genshin();
                        await genshin.InitializeAsync(config);
                        var signResult = await genshin.SignAccountAsync(config, null, disabledUids);

                        if (!signResult.Contains("失败") && !signResult.Contains("异常"))
                            result.GameResult.SuccessCount++;
                        else
                            result.GameResult.FailCount++;

                        result.GameResult.Details.Add(signResult);
                    }
                    catch (Exception ex)
                    {
                        result.GameResult.FailCount++;
                        result.GameResult.Details.Add($"{account.Nickname}({account.Uid}): {ex.Message}");
                    }
                    await Task.Delay(new Random().Next(2000, 5000));
                }

                result.GameResult.Success = result.GameResult.FailCount == 0;
                int signDays = MihoyoBBS.GameCheckin.LastSignDays;
                string rewardItem = MihoyoBBS.GameCheckin.LastRewardItem;
                result.GameSignDays = signDays.ToString();
                result.GameRewardItem = rewardItem;

                if (result.GameResult.Success)
                {
                    result.GameResult.Message = $"连续{signDays}天 | 获得{rewardItem}";
                }
                else
                {
                    result.GameResult.Message = $"{result.GameResult.SuccessCount}个成功，{result.GameResult.FailCount}个失败";
                }

                result.GameResult.Details.Add($"游戏签到: {result.GameResult.SuccessCount}个账号 | 连续{signDays}天 | 获得{rewardItem}");
            }
            catch (Exception ex)
            {
                result.GameResult.Success = false;
                result.GameResult.Message = $"异常: {ex.Message}";
                Debug.WriteLine($"[统一签到] 游戏签到异常: {ex.Message}");
            }
        }

        // 4. Community Check-in
        if (communityEnabled)
        {
            result.CommunityResult.Executed = true;
            try
            {
                foreach (var account in activeAccounts)
                {
                    var communityResult = await _communityCheckinService.ExecuteCheckinAsync(
                        account, true, communityRead, communityLike, communityShare);

                    result.CommunityResult.SuccessCount += communityResult.SuccessCount;
                    result.CommunityResult.FailCount += communityResult.FailCount;
                    result.CommunityResult.SkippedCount += communityResult.SkippedCount;
                    result.CommunityResult.Details.AddRange(communityResult.Details);

                    await Task.Delay(new Random().Next(2000, 5000));
                }

                result.CommunityResult.Success = result.CommunityResult.FailCount == 0;

                var gainedMsgs = result.CommunityResult.Details
                    .Where(d => d.Contains("获得") && d.Contains("米游币"))
                    .ToList();
                if (gainedMsgs.Count > 0)
                    result.CommunityResult.Message = string.Join("; ", gainedMsgs);
                else
                    result.CommunityResult.Message = result.CommunityResult.Success
                        ? "全部完成" : $"{result.CommunityResult.FailCount}个失败";
            }
            catch (Exception ex)
            {
                result.CommunityResult.Success = false;
                result.CommunityResult.Message = $"异常: {ex.Message}";
                Debug.WriteLine($"[统一签到] 社区签到异常: {ex.Message}");
            }
        }

        // 5. Cloud Game Check-in
        if (cloudGameEnabled)
        {
            result.CloudGameResult.Executed = true;
            try
            {
                bool hasAnyCredential = activeAccounts.Any(a => !string.IsNullOrEmpty(a.CloudComboToken));
                if (!hasAnyCredential)
                {
                    result.CloudGameResult.Success = false;
                    result.CloudGameResult.Message = "未配置云游戏凭证";
                }
                else
                {
                    foreach (var account in activeAccounts)
                    {
                        if (string.IsNullOrEmpty(account.CloudComboToken))
                        {
                            result.CloudGameResult.SkippedCount++;
                            continue;
                        }

                        var cloudResult = await _cloudGameCheckinService.ExecuteCheckinAsync(account.Uid, account.CloudComboToken);
                        result.CloudGameResult.SuccessCount += cloudResult.SuccessCount;
                        result.CloudGameResult.FailCount += cloudResult.FailCount;
                        result.CloudGameResult.SkippedCount += cloudResult.SkippedCount;
                        result.CloudGameResult.Details.AddRange(cloudResult.Details);

                        await Task.Delay(new Random().Next(2000, 5000));
                    }

                    result.CloudGameResult.Success = result.CloudGameResult.FailCount == 0;

                    var gainedMsgs = result.CloudGameResult.Details
                        .Where(d => d.Contains("获得"))
                        .ToList();
                    if (gainedMsgs.Count > 0)
                        result.CloudGameResult.Message = string.Join("; ", gainedMsgs);
                    else
                        result.CloudGameResult.Message = result.CloudGameResult.Success
                            ? "全部完成" : $"{result.CloudGameResult.FailCount}个失败";
                }
            }
            catch (Exception ex)
            {
                result.CloudGameResult.Success = false;
                result.CloudGameResult.Message = $"异常: {ex.Message}";
                Debug.WriteLine($"[统一签到] 云原神签到异常: {ex.Message}");
            }
        }

        // 6. Build summary
        int totalSuccess = result.GameResult.SuccessCount + result.CommunityResult.SuccessCount + result.CloudGameResult.SuccessCount;
        int totalFail = result.GameResult.FailCount + result.CommunityResult.FailCount + result.CloudGameResult.FailCount;
        int executedCount = (result.GameResult.Executed ? 1 : 0) + (result.CommunityResult.Executed ? 1 : 0) + (result.CloudGameResult.Executed ? 1 : 0);

        if (totalFail == 0)
        {
            result.SummaryMessage = $"签到完成 - {activeAccounts.Count}个账号全部成功";
        }
        else if (totalSuccess > 0)
        {
            result.SummaryMessage = $"签到完成 - 部分成功 ({totalSuccess}成功, {totalFail}失败)";
        }
        else
        {
            result.SummaryMessage = $"签到失败 - 共{totalFail}个错误";
        }

        Debug.WriteLine($"[统一签到] {result.SummaryMessage}");
        return result;
    }

    private async Task<bool> GetBoolSettingAsync(string key, bool defaultValue)
    {
        var value = await _localSettingsService.ReadSettingAsync(key);
        if (value == null) return defaultValue;
        return bool.TryParse(value.ToString(), out var result) ? result : defaultValue;
    }

    private async Task<HashSet<string>> LoadDisabledUidsAsync()
    {
        var disabledUidsJson = await _localSettingsService.ReadSettingAsync("CheckinDisabledUids");
        if (disabledUidsJson != null)
        {
            try
            {
                var list = JsonSerializer.Deserialize<List<string>>(disabledUidsJson.ToString() ?? "[]");
                if (list != null) return new HashSet<string>(list);
            }
            catch { }
        }
        return new HashSet<string>();
    }

    private async Task<List<AccountCredentials>> LoadAllAccountsAsync()
    {
        var accounts = new List<AccountCredentials>();
        var baseDir = Helpers.AppPaths.DataDir;
        var seenUids = new HashSet<string>();

        if (!Directory.Exists(baseDir)) return accounts;

        foreach (var file in Directory.GetFiles(baseDir, "config*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var config = JsonSerializer.Deserialize<MihoyoBBS.Config>(json);
                if (config?.Account?.Cookie == null) continue;

                var cookie = config.Account.Cookie;
                var match = Regex.Match(cookie, @"(?:account_id_v2|ltuid_v2|ltuid|account_id|stuid)=(\d+)");
                if (!match.Success) continue;
                var uid = match.Groups[1].Value;
                if (!seenUids.Add(uid)) continue;

                // Extract stoken and mid from cookie or account config
                var stoken = ExtractCookieValue(cookie, "stoken") ?? config.Account.Stoken ?? "";
                var mid = ExtractCookieValue(cookie, "mid") ?? config.Account.Mid ?? "";
                var stuid = ExtractCookieValue(cookie, "stuid") ?? ExtractCookieValue(cookie, "account_id_v2") ?? config.Account.Stuid ?? uid;

                accounts.Add(new AccountCredentials
                {
                    Uid = uid,
                    Cookie = cookie,
                    Stuid = stuid,
                    Stoken = stoken,
                    Mid = mid,
                    Nickname = $"用户{uid}",
                    ConfigPath = file,
                    CloudComboToken = config.Account.CloudComboToken ?? ""
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[统一签到] 读取账号配置失败 {file}: {ex.Message}");
            }
        }

        return accounts;
    }

    private static string? ExtractCookieValue(string cookie, string key)
    {
        var pattern = $@"(?:^|;)\s*{Regex.Escape(key)}=([^;]+)";
        var match = Regex.Match(cookie, pattern);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private async Task<MihoyoBBS.Config?> LoadAccountConfigAsync(string configPath)
    {
        try
        {
            if (!File.Exists(configPath)) return null;
            var json = await File.ReadAllTextAsync(configPath);
            return JsonSerializer.Deserialize<MihoyoBBS.Config>(json);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[统一签到] 加载配置失败 {configPath}: {ex.Message}");
            return null;
        }
    }

}
