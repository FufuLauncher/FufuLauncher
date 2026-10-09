/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class SettingsViewModel
{
    #region 自动签到与云游戏

    partial void OnIsCaptchaPopupDisabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsCaptchaPopupDisabled", value);
    }

    partial void OnIsCaptchaNoticeEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsCaptchaNoticeEnabled", value);
    }

    partial void OnIsRedeemCodeNotificationEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsRedeemCodeNotificationEnabled", value);
    }

    partial void OnIsAutoCheckinEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        Debug.WriteLine($"SettingsViewModel: 自动签到设置变更为 {value}");
        _ = _localSettingsService.SaveSettingAsync("IsAutoCheckinEnabled", value);
    }

    partial void OnIsGameCheckinEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsGameCheckinEnabled", value);
    }

    partial void OnIsCommunityCheckinEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsCommunityCheckinEnabled", value);
    }

    partial void OnIsCommunityLikeEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsCommunityLikeEnabled", value);
    }

    partial void OnIsCommunityReadEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsCommunityReadEnabled", value);
    }

    partial void OnIsCommunityShareEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsCommunityShareEnabled", value);
    }

    partial void OnIsCloudGameCheckinEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsCloudGameCheckinEnabled", value);
    }

    partial void OnIsBatchCheckinEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync("IsBatchCheckinEnabled", value);
    }

    private async Task LoadCheckinGamesAsync()
    {
        var games = new ObservableCollection<CheckinGameItem>();
        string[] gameOrder = ["nap_cn","hkrpg_cn", "nxx_cn",  "bh3_cn", "bh2_cn"];
        foreach (var gameBiz in gameOrder)
        {
            var game = MiyousheCheckinGame.SupportedGames.First(g => g.GameBiz == gameBiz);
            var value = await _localSettingsService.ReadSettingAsync(game.SettingKey);
            var item = new CheckinGameItem(game)
            {
                IsSelected = value == null || !bool.TryParse(value.ToString(), out bool enabled) || enabled
            };
            item.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(CheckinGameItem.IsSelected))
                    await _localSettingsService.SaveSettingAsync(item.SettingKey, item.IsSelected);
            };
            games.Add(item);
        }
        CheckinGames = games;
    }

    private async Task LoadCheckinAccountsAsync()
    {
        try
        {
            var disabledUidsJson = await _localSettingsService.ReadSettingAsync("CheckinDisabledUids");
            var disabledUids = new HashSet<string>();
            if (disabledUidsJson != null)
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<string>>(disabledUidsJson.ToString() ?? "[]");
                    if (list != null) disabledUids = new HashSet<string>(list);
                }
                catch
                {
                }
            }

            var accounts = new ObservableCollection<CheckinAccountItem>();
            var entries = _accountManager.GetAllAccounts();
            foreach (var entry in entries)
            {
                var cookies = await _accountManager.LoadCookiesAsync(entry.Id);
                if (cookies == null || cookies.Count == 0) continue;

                string uid = entry.Stuid;
                string nickname = entry.Nickname ?? $"用户 {uid}";

                string cloudTokenKey = $"CloudComboToken_{uid}";
                var cloudTokenObj = await _localSettingsService.ReadSettingAsync(cloudTokenKey);
                bool hasCloudCredential = !string.IsNullOrEmpty(cloudTokenObj?.ToString());

                accounts.Add(new CheckinAccountItem
                {
                    Uid = uid,
                    Nickname = nickname,
                    IsSelected = !disabledUids.Contains(uid),
                    HasCloudCredential = hasCloudCredential
                });
            }

            CheckinAccounts = accounts;

            foreach (var account in CheckinAccounts)
            {
                account.PropertyChanged += async (s, e) =>
                {
                    if (e.PropertyName == nameof(CheckinAccountItem.IsSelected))
                    {
                        var disabled = CheckinAccounts.Where(a => !a.IsSelected).Select(a => a.Uid).ToList();
                        await _localSettingsService.SaveSettingAsync("CheckinDisabledUids",
                            JsonSerializer.Serialize(disabled));
                    }
                };
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"LoadCheckinAccountsAsync 异常: {ex.Message}");
        }
    }

    public static async Task SaveCloudCredentialAsync(string uid, string credential)
    {
        try
        {
            var localSettings = App.GetService<ILocalSettingsService>();
            string key = $"CloudComboToken_{uid}";
            await localSettings.SaveSettingAsync(key, credential);

            WeakReferenceMessenger.Default.Send(new CloudCredentialUpdatedMessage(uid));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存云游戏凭证失败: {ex.Message}");
        }
    }

    public async Task RemoveCloudCredentialAsync(string uid)
    {
        try
        {
            string key = $"CloudComboToken_{uid}";
            await _localSettingsService.RemoveSettingAsync(key);

            var account = CheckinAccounts?.FirstOrDefault(a => a.Uid == uid);
            if (account != null)
            {
                account.HasCloudCredential = false;
            }

            WeakReferenceMessenger.Default.Send(new CloudCredentialUpdatedMessage(uid));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"移除云游戏凭证失败: {ex.Message}");
        }
    }

    #endregion
}
