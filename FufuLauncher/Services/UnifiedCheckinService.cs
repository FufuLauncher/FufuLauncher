/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
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
                Nickname = entry.Nickname ?? string.Format("Checkin_DefaultUser".GetLocalized(), entry.Stuid),
                ConfigPath = entry.Id,          
                CloudComboToken = cloudComboToken
            });
        }

        if (credentialsList.Count == 0)
        {
            result.SummaryMessage = "Checkin_NoBoundAccount".GetLocalized();
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
            result.SummaryMessage = isBatchCheckinEnabled ? "Checkin_AllDisabled".GetLocalized() : "Checkin_AccountNotFound".GetLocalized();
            result.GameResult.Message = result.SummaryMessage;
            result.GameResult.Executed = true;
            return result;
        }

        void Report(string msg) => progress?.Report(msg);

        
        if (gameEnabled)
        {
            var gameSw = Stopwatch.StartNew();
            result.GameResult.Executed = true;
            try
            {
                foreach (var account in activeAccounts)
                {
                    Report($"[{account.Nickname}] {"Checkin_GameCheckinProgress".GetLocalized()}");
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
                            success = string.IsNullOrEmpty(GameCheckin.LastApiError);
                        }

                        if (success) result.GameResult.SuccessCount++;
                        else result.GameResult.FailCount++;

                        result.AccountResults.Add(new AccountCheckinDetail
                        {
                            Nickname = account.Nickname,
                            Items = { ("Checkin_GameCheckin".GetLocalized(), success, success ? "Status_Completed".GetLocalized() : signResult) }
                        });
                    }
                    catch (Exception ex)
                    {
                        result.GameResult.FailCount++;
                        result.AccountResults.Add(new AccountCheckinDetail
                        {
                            Nickname = account.Nickname,
                            Items = { ("Checkin_GameCheckin".GetLocalized(), false, ex.Message) }
                        });
                    }
                    if (activeAccounts.Count > 1)
                        await Task.Delay(new Random().Next(2000, 5000));
                }

                result.GameResult.Success = result.GameResult.FailCount == 0;
                bool anyOs = activeAccounts.Any(a => a.ConfigPath.StartsWith("os_"));
                int signDays = anyOs ? HoyolabCheckinService.LastSignDays : GameCheckin.LastSignDays;
                string rewardItem = anyOs ? HoyolabCheckinService.LastRewardItem : GameCheckin.LastRewardItem;
                result.GameSignDays = signDays.ToString();
                result.GameRewardItem = rewardItem;

                result.GameResult.Message = result.GameResult.Success
                    ? string.Format("Checkin_ConsecutiveDays".GetLocalized(), signDays, rewardItem)
                    : string.Format("Checkin_SuccessFailCount".GetLocalized(), result.GameResult.SuccessCount, result.GameResult.FailCount);
            }
            catch (Exception ex)
            {
                result.GameResult.Success = false;
                result.GameResult.Message = string.Format("CheckinGame_Exception".GetLocalized(), ex.Message);
                Debug.WriteLine($"[统一签到] 游戏签到异常: {ex.Message}");
            }
            gameSw.Stop();
            Debug.WriteLine($"[统一签到] 游戏签到耗时 {gameSw.ElapsedMilliseconds}ms");
        }

        // ===================== 社区签到 =====================
        if (communityEnabled)
        {
            var communitySw = Stopwatch.StartNew();
            result.CommunityResult.Executed = true;
            try
            {
                foreach (var account in activeAccounts)
                {
                    // 国际服跳过社区签到
                    if (account.ConfigPath.StartsWith("os_"))
                    {
                        Report($"[{account.Nickname}] {"Checkin_OSAccountSkippedCommunity".GetLocalized()}");
                        var acct = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                        if (acct != null)
                            acct.Items.Add(("Checkin_CommunityCheckin".GetLocalized(), null, "Checkin_OSAccountSkipped".GetLocalized()));
                        else
                            result.AccountResults.Add(new AccountCheckinDetail
                            {
                                Nickname = account.Nickname,
                                Items = { ("Checkin_CommunityCheckin".GetLocalized(), null, "Checkin_OSAccountSkipped".GetLocalized()) }
                            });
                        continue;
                    }

                    Report($"[{account.Nickname}] {"Checkin_CommunityCheckinProgress".GetLocalized()}");
                    var communityResult = await _communityCheckinService.ExecuteCheckinAsync(
                        account, true, communityRead, communityLike, communityShare);

                    result.CommunityResult.SuccessCount += communityResult.SuccessCount;
                    result.CommunityResult.FailCount += communityResult.FailCount;
                    result.CommunityResult.SkippedCount += communityResult.SkippedCount;
                    result.CommunityResult.Details.AddRange(communityResult.Details);

                    bool success = communityResult.FailCount == 0;
                    var detail = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                    if (detail != null)
                        detail.Items.Add(("Checkin_CommunityCheckin".GetLocalized(), success, success ? "Status_Completed".GetLocalized() : "Status_Failure".GetLocalized()));
                    else
                        result.AccountResults.Add(new AccountCheckinDetail
                        {
                            Nickname = account.Nickname,
                            Items = { ("Checkin_CommunityCheckin".GetLocalized(), success, success ? "Status_Completed".GetLocalized() : "Status_Failure".GetLocalized()) }
                        });

                    if (activeAccounts.Count > 1)
                        await Task.Delay(new Random().Next(2000, 5000));
                }

                result.CommunityResult.Success = result.CommunityResult.FailCount == 0;
                result.CommunityResult.Message = result.CommunityResult.Success
                    ? "Checkin_AllDone".GetLocalized()
                    : string.Format("Checkin_CountFailed".GetLocalized(), result.CommunityResult.FailCount);
            }
            catch (Exception ex)
            {
                result.CommunityResult.Success = false;
                result.CommunityResult.Message = string.Format("CheckinCommunity_Exception".GetLocalized(), ex.Message);
                Debug.WriteLine($"[统一签到] 社区签到异常: {ex.Message}");
            }
            communitySw.Stop();
            Debug.WriteLine($"[统一签到] 社区签到耗时 {communitySw.ElapsedMilliseconds}ms");
        }

        // ===================== 云游戏签到 =====================
        if (cloudGameEnabled)
        {
            var cloudSw = Stopwatch.StartNew();
            result.CloudGameResult.Executed = true;
            try
            {
                bool hasAnyCredential = activeAccounts.Any(a => !string.IsNullOrEmpty(a.CloudComboToken));
                if (!hasAnyCredential)
                {
                    result.CloudGameResult.Success = false;
                    result.CloudGameResult.Message = "Checkin_NoCloudCredential".GetLocalized();
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
                            if (cd != null) cd.Items.Add(("Checkin_CloudGameCheckin".GetLocalized(), null, "Checkin_NotConfiguredCredential".GetLocalized()));
                            continue;
                        }

                        Report($"[{account.Nickname}] {"Checkin_CloudGameCheckinProgress".GetLocalized()}");
                        var cloudResult = await _cloudGameCheckinService.ExecuteCheckinAsync(account.Uid, account.CloudComboToken);
                        result.CloudGameResult.SuccessCount += cloudResult.SuccessCount;
                        result.CloudGameResult.FailCount += cloudResult.FailCount;
                        result.CloudGameResult.SkippedCount += cloudResult.SkippedCount;
                        result.CloudGameResult.Details.AddRange(cloudResult.Details);

                        bool success = cloudResult.FailCount == 0;
                        var cdd = result.AccountResults.FirstOrDefault(a => a.Nickname == account.Nickname);
                        if (cdd != null)
                            cdd.Items.Add(("Checkin_CloudGameCheckin".GetLocalized(), success, success ? "Status_Completed".GetLocalized() : "Status_Failure".GetLocalized()));
                        else
                            result.AccountResults.Add(new AccountCheckinDetail
                            {
                                Nickname = account.Nickname,
                                Items = { ("Checkin_CloudGameCheckin".GetLocalized(), success, success ? "Status_Completed".GetLocalized() : "Status_Failure".GetLocalized()) }
                            });

                        if (activeAccounts.Count > 1)
                            await Task.Delay(new Random().Next(2000, 5000));
                    }

                    result.CloudGameResult.Success = result.CloudGameResult.FailCount == 0;
                    result.CloudGameResult.Message = result.CloudGameResult.Success
                        ? "Checkin_AllDone".GetLocalized()
                        : string.Format("Checkin_CountFailed".GetLocalized(), result.CloudGameResult.FailCount);
                }
            }
            catch (Exception ex)
            {
                result.CloudGameResult.Success = false;
                result.CloudGameResult.Message = string.Format("CheckinCloud_Exception".GetLocalized(), ex.Message);
                Debug.WriteLine($"[统一签到] 云原神签到异常: {ex.Message}");
            }
            cloudSw.Stop();
            Debug.WriteLine($"[统一签到] 云游戏签到耗时 {cloudSw.ElapsedMilliseconds}ms");
        }

        int successAccounts = result.AccountResults.Count(a => a.Items.Any(i => i.Success == true));
        int failAccounts = result.AccountResults.Count(a => a.Items.Any(i => i.Success == false));

        result.SummaryMessage = failAccounts == 0
            ? string.Format("Checkin_AllAccountsSuccess".GetLocalized(), successAccounts)
            : successAccounts > 0
                ? string.Format("Checkin_PartialSuccess".GetLocalized(), successAccounts, failAccounts)
                : string.Format("Checkin_AllFailed".GetLocalized(), failAccounts);

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

