using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    private readonly AccountManager _accountManager;
    private readonly IHoyolabRoleResolverService _hoyolabRoleResolverService;

    public UnifiedCheckinService(
        ILocalSettingsService localSettingsService,
        IHoyoverseCheckinService gameCheckinService,
        ICommunityCheckinService communityCheckinService,
        ICloudGameCheckinService cloudGameCheckinService,
        AccountManager accountManager,
        IHoyolabRoleResolverService hoyolabRoleResolverService)
    {
        _localSettingsService = localSettingsService;
        _gameCheckinService = gameCheckinService;
        _communityCheckinService = communityCheckinService;
        _cloudGameCheckinService = cloudGameCheckinService;
        _accountManager = accountManager;
        _hoyolabRoleResolverService = hoyolabRoleResolverService;
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

       
        var allEntries = _accountManager.GetAllAccounts();
        var credentialsList = new List<AccountCredentials>();
        foreach (var entry in allEntries)
        {
            var cookies = await _accountManager.LoadCookiesAsync(entry.Id);
            if (cookies == null || cookies.Count == 0)
                continue;

            string cookieStr = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));
            cookies.TryGetValue("stoken", out var stoken);
            cookies.TryGetValue("mid", out var mid);

            
            string cloudTokenKey = $"CloudComboToken_{entry.Stuid}";
            var cloudTokenObj = await _localSettingsService.ReadSettingAsync(cloudTokenKey);
            string cloudComboToken = cloudTokenObj?.ToString() ?? "";

            credentialsList.Add(new AccountCredentials
            {
                Uid = entry.Stuid,
                Cookie = cookieStr,
                Stuid = entry.Stuid,
                Stoken = stoken ?? "",
                Mid = mid ?? "",
                Nickname = entry.Nickname ?? $"用户{entry.Stuid}",
                ConfigPath = entry.Id,          
                CloudComboToken = cloudComboToken
            });
        }

        if (credentialsList.Count == 0)
        {
            result.SummaryMessage = "未检测到绑定账号";
            result.GameResult.Executed = true;
            return result;
        }

        var disabledUids = await LoadDisabledUidsAsync();

        
        List<AccountCredentials> activeAccounts;
        if (isBatchCheckinEnabled)
        {
            activeAccounts = credentialsList.Where(a => !disabledUids.Contains(a.Uid)).ToList();
        }
        else
        {
            var activeId = _accountManager.ActiveAccountId;
            activeAccounts = credentialsList.Where(a => a.ConfigPath == activeId).ToList();
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
                        
                        var config = new Config
                        {
                            Account = new AccountConfig
                            {
                                Cookie = account.Cookie,
                                Stuid = account.Stuid,
                                Stoken = account.Stoken,
                                Mid = account.Mid
                            }
                        };

                        
                        bool isOs = account.ConfigPath.StartsWith("os_");
                        string signResult;
                        bool success;

                        if (isOs)
                        {
                            var rolesResult = await _hoyolabRoleResolverService.ResolveRolesAsync(account.Cookie);
                            if (!rolesResult.HasRoles)
                            {
                                signResult = rolesResult.Message;
                                success = false;
                            }
                            else
                            {
                                var os = new HoyolabCheckinService();
                                await os.InitializeAsync(account.Cookie, rolesResult.Roles.Select(ToOsAccountItem).ToList());
                                var osSignResult = await os.SignAccountWithResultAsync(account.Cookie, disabledUids);
                                signResult = osSignResult.Message;
                                success = osSignResult.Success;
                            }
                        }
                        else
                        {
                            var genshin = new Genshin();
                            await genshin.InitializeAsync(config);
                            signResult = await genshin.SignAccountAsync(config, null, disabledUids);
                            success = !signResult.Contains("失败") && !signResult.Contains("异常");
                        }

                        if (success) result.GameResult.SuccessCount++;
                        else result.GameResult.FailCount++;

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
                bool anyOs = activeAccounts.Any(a => a.ConfigPath.StartsWith("os_"));
                int signDays = anyOs ? HoyolabCheckinService.LastSignDays : GameCheckin.LastSignDays;
                string rewardItem = anyOs ? HoyolabCheckinService.LastRewardItem : GameCheckin.LastRewardItem;
                result.GameSignDays = signDays.ToString();
                result.GameRewardItem = rewardItem;

                result.GameResult.Message = result.GameResult.Success
                    ? $"连续{signDays}天 | 获得{rewardItem}"
                    : $"{result.GameResult.SuccessCount}成功，{result.GameResult.FailCount}失败";
            }
            catch (Exception ex)
            {
                result.GameResult.Success = false;
                result.GameResult.Message = $"异常: {ex.Message}";
                Debug.WriteLine($"[统一签到] 游戏签到异常: {ex.Message}");
            }
        }

        // ===================== 社区签到 =====================
        if (communityEnabled)
        {
            result.CommunityResult.Executed = true;
            try
            {
                foreach (var account in activeAccounts)
                {
                    // 国际服跳过社区签到
                    if (account.ConfigPath.StartsWith("os_"))
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
                    var detail = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                    if (detail != null)
                        detail.Items.Add(("社区签到", success, success ? "完成" : "失败"));
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
                result.CommunityResult.Message = gainedMsgs.Count > 0
                    ? string.Join("; ", gainedMsgs)
                    : result.CommunityResult.Success ? "全部完成" : $"{result.CommunityResult.FailCount}个失败";
            }
            catch (Exception ex)
            {
                result.CommunityResult.Success = false;
                result.CommunityResult.Message = $"异常: {ex.Message}";
                Debug.WriteLine($"[统一签到] 社区签到异常: {ex.Message}");
            }
        }

        // ===================== 云游戏签到 =====================
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
                        if (account.ConfigPath.StartsWith("os_"))
                        {
                            result.CloudGameResult.SkippedCount++;
                            continue;
                        }
                        if (string.IsNullOrEmpty(account.CloudComboToken))
                        {
                            result.CloudGameResult.SkippedCount++;
                            var cd = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                            if (cd != null) cd.Items.Add(("云游戏签到", null, "未配置凭证"));
                            continue;
                        }

                        Report($"[{account.Nickname}] 正在云游戏签到...");
                        var cloudResult = await _cloudGameCheckinService.ExecuteCheckinAsync(account.Uid, account.CloudComboToken);
                        result.CloudGameResult.SuccessCount += cloudResult.SuccessCount;
                        result.CloudGameResult.FailCount += cloudResult.FailCount;
                        result.CloudGameResult.SkippedCount += cloudResult.SkippedCount;
                        result.CloudGameResult.Details.AddRange(cloudResult.Details);

                        bool success = cloudResult.FailCount == 0;
                        var cdd = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                        if (cdd != null)
                            cdd.Items.Add(("云游戏签到", success, success ? "完成" : "失败"));
                        else
                            result.AccountResults.Add(new AccountCheckinDetail
                            {
                                Nickname = account.Nickname,
                                Items = { ("云游戏签到", success, success ? "完成" : "失败") }
                            });

                        await Task.Delay(new Random().Next(2000, 5000));
                    }

                    result.CloudGameResult.Success = result.CloudGameResult.FailCount == 0;
                    var gainedMsgs = result.CloudGameResult.Details.Where(d => d.Contains("获得")).ToList();
                    result.CloudGameResult.Message = gainedMsgs.Count > 0
                        ? string.Join("; ", gainedMsgs)
                        : result.CloudGameResult.Success ? "全部完成" : $"{result.CloudGameResult.FailCount}个失败";
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

        result.SummaryMessage = failAccounts == 0
            ? $"签到完成，{successAccounts}个账号全部成功"
            : successAccounts > 0
                ? $"签到完成，{successAccounts}个成功，{failAccounts}个失败"
                : $"签到失败，共{failAccounts}个账号出错";

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

    private static OsAccountItem ToOsAccountItem(GameRoleInfo role)
    {
        return new OsAccountItem
        {
            GameUid = role.game_uid,
            Region = role.region,
            Nickname = role.nickname
        };
    }

}
