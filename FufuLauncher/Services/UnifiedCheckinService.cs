using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using MihoyoBBS;

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

    public async Task<UnifiedCheckinResult> ExecuteAllCheckinsAsync(IProgress<string>? progress = null)
    {
        var result = new UnifiedCheckinResult();

        var gameEnabled = await GetBoolSettingAsync("IsGameCheckinEnabled", true);
        var communityEnabled = await GetBoolSettingAsync("IsCommunityCheckinEnabled", true);
        var cloudGameEnabled = await GetBoolSettingAsync("IsCloudGameCheckinEnabled", false);
        var communityLike = await GetBoolSettingAsync("IsCommunityLikeEnabled", false);
        var communityRead = await GetBoolSettingAsync("IsCommunityReadEnabled", false);
        var communityShare = await GetBoolSettingAsync("IsCommunityShareEnabled", false);
        var isBatchCheckinEnabled = await GetBoolSettingAsync("IsBatchCheckinEnabled", false);

        var accounts = await LoadAllAccountsAsync();

        if (accounts.Count == 0)
        {
            result.SummaryMessage = "未检测到绑定账号";
            result.GameResult.Message = "未检测到账号";
            result.GameResult.Executed = true;
            return result;
        }

        var disabledUids = await LoadDisabledUidsAsync();
        List<AccountCredentials> activeAccounts;
        if (isBatchCheckinEnabled)
        {
            activeAccounts = accounts.Where(a => !disabledUids.Contains(a.Uid)).ToList();
        }
        else
        {
            var activeFileObj = await _localSettingsService.ReadSettingAsync("ActiveConfigFile");
            string activeFile = activeFileObj?.ToString() ?? "config.json";
            var activeAccount = accounts.FirstOrDefault(a => a.ConfigPath.EndsWith(activeFile));
            activeAccounts = activeAccount != null ? new List<AccountCredentials> { activeAccount } : new List<AccountCredentials>();
        }

        if (activeAccounts.Count == 0)
        {
            result.SummaryMessage = isBatchCheckinEnabled ? "所有账号已被禁用" : "未找到当前账号";
            result.GameResult.Message = result.SummaryMessage;
            result.GameResult.Executed = true;
            return result;
        }

        void Report(string msg) => progress?.Report(msg);

        if (gameEnabled)
        {
            result.GameResult.Executed = true;
            try
            {
                foreach (var account in activeAccounts)
                {
                    Report($"[{account.Nickname}] 正在游戏签到...");
                    try
                    {
                        var config = await LoadAccountConfigAsync(account.ConfigPath);
                        if (config == null)
                        {
                            result.GameResult.FailCount++;
                            result.AccountResults.Add(new AccountCheckinDetail
                            {
                                Nickname = account.Nickname,
                                Items = { ("游戏签到", false, "配置加载失败") }
                            });
                            continue;
                        }

                        bool isOs = account.ConfigPath.Contains("lab");
                        string signResult;

                        if (isOs)
                        {
                            var os = new HoyolabCheckinService();
                            await os.InitializeAsync(config.Account.Cookie);
                            signResult = await os.SignAccountAsync(config.Account.Cookie, disabledUids);
                        }
                        else
                        {
                            var genshin = new Genshin();
                            await genshin.InitializeAsync(config);
                            signResult = await genshin.SignAccountAsync(config, null, disabledUids);
                        }

                        bool success = !signResult.Contains("失败") && !signResult.Contains("异常");
                        if (success)
                            result.GameResult.SuccessCount++;
                        else
                            result.GameResult.FailCount++;

                        result.AccountResults.Add(new AccountCheckinDetail
                        {
                            Nickname = account.Nickname,
                            Items = { ("游戏签到", success, success ? "完成" : signResult) }
                        });
                    }
                    catch (Exception ex)
                    {
                        result.GameResult.FailCount++;
                        result.AccountResults.Add(new AccountCheckinDetail
                        {
                            Nickname = account.Nickname,
                            Items = { ("游戏签到", false, ex.Message) }
                        });
                    }
                    await Task.Delay(new Random().Next(2000, 5000));
                }

                result.GameResult.Success = result.GameResult.FailCount == 0;
                bool anyOs = activeAccounts.Any(a => a.ConfigPath.Contains("lab"));
                int signDays = anyOs ? HoyolabCheckinService.LastSignDays : GameCheckin.LastSignDays;
                string rewardItem = anyOs ? HoyolabCheckinService.LastRewardItem : GameCheckin.LastRewardItem;
                result.GameSignDays = signDays.ToString();
                result.GameRewardItem = rewardItem;

                if (result.GameResult.Success)
                    result.GameResult.Message = $"连续{signDays}天 | 获得{rewardItem}";
                else
                    result.GameResult.Message = $"{result.GameResult.SuccessCount}成功，{result.GameResult.FailCount}失败";
            }
            catch (Exception ex)
            {
                result.GameResult.Success = false;
                result.GameResult.Message = $"异常: {ex.Message}";
                Debug.WriteLine($"[统一签到] 游戏签到异常: {ex.Message}");
            }
        }

        if (communityEnabled)
        {
            result.CommunityResult.Executed = true;
            try
            {
                foreach (var account in activeAccounts)
                {
                    if (account.ConfigPath.Contains("lab"))
                    {
                        Report($"[{account.Nickname}] OS 账号跳过社区签到");
                        var acct = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                        if (acct != null)
                            acct.Items.Add(("社区签到", null, "OS 账号跳过"));
                        else
                            result.AccountResults.Add(new AccountCheckinDetail
                            {
                                Nickname = account.Nickname,
                                Items = { ("社区签到", null, "OS 账号跳过") }
                            });
                        continue;
                    }

                    Report($"[{account.Nickname}] 正在社区签到...");
                    var communityResult = await _communityCheckinService.ExecuteCheckinAsync(
                        account, true, communityRead, communityLike, communityShare);

                    result.CommunityResult.SuccessCount += communityResult.SuccessCount;
                    result.CommunityResult.FailCount += communityResult.FailCount;
                    result.CommunityResult.SkippedCount += communityResult.SkippedCount;
                    result.CommunityResult.Details.AddRange(communityResult.Details);

                    bool success = communityResult.FailCount == 0;
                    var acctDetail = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                    if (acctDetail != null)
                        acctDetail.Items.Add(("社区签到", success, success ? "完成" : "失败"));
                    else
                        result.AccountResults.Add(new AccountCheckinDetail
                        {
                            Nickname = account.Nickname,
                            Items = { ("社区签到", success, success ? "完成" : "失败") }
                        });

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
                        if (account.ConfigPath.Contains("lab"))
                        {
                            result.CloudGameResult.SkippedCount++;
                            continue;
                        }

                        if (string.IsNullOrEmpty(account.CloudComboToken))
                        {
                            result.CloudGameResult.SkippedCount++;
                            var cloudDetail = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                            if (cloudDetail != null)
                                cloudDetail.Items.Add(("云游戏签到", null, "未配置凭证"));
                            continue;
                        }

                        Report($"[{account.Nickname}] 正在云游戏签到...");
                        var cloudResult = await _cloudGameCheckinService.ExecuteCheckinAsync(account.Uid, account.CloudComboToken);
                        result.CloudGameResult.SuccessCount += cloudResult.SuccessCount;
                        result.CloudGameResult.FailCount += cloudResult.FailCount;
                        result.CloudGameResult.SkippedCount += cloudResult.SkippedCount;
                        result.CloudGameResult.Details.AddRange(cloudResult.Details);

                        bool success = cloudResult.FailCount == 0;
                        var cloudDoneDetail = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                        if (cloudDoneDetail != null)
                            cloudDoneDetail.Items.Add(("云游戏签到", success, success ? "完成" : "失败"));
                        else
                            result.AccountResults.Add(new AccountCheckinDetail
                            {
                                Nickname = account.Nickname,
                                Items = { ("云游戏签到", success, success ? "完成" : "失败") }
                            });

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

        int successAccounts = result.AccountResults.Count(a => a.Items.Any(i => i.Success == true));
        int failAccounts = result.AccountResults.Count(a => a.Items.Any(i => i.Success == false));

        if (failAccounts == 0)
            result.SummaryMessage = $"签到完成，{successAccounts}个账号全部成功";
        else if (successAccounts > 0)
            result.SummaryMessage = $"签到完成，{successAccounts}个成功，{failAccounts}个失败";
        else
            result.SummaryMessage = $"签到失败，共{failAccounts}个账号出错";

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

                var stoken = ExtractCookieValue(cookie, "stoken") ?? config.Account.Stoken ?? "";
                var mid = ExtractCookieValue(cookie, "mid") ?? config.Account.Mid ?? "";
                var stuid = ExtractCookieValue(cookie, "stuid") ?? ExtractCookieValue(cookie, "account_id_v2") ?? config.Account.Stuid ?? uid;

                string nickname = config.Display?.Nickname;
                if (string.IsNullOrEmpty(nickname)) nickname = $"用户{uid}";

                accounts.Add(new AccountCredentials
                {
                    Uid = uid,
                    Cookie = cookie,
                    Stuid = stuid,
                    Stoken = stoken,
                    Mid = mid,
                    Nickname = nickname,
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
