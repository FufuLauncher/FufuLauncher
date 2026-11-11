/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Models;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class AccountManager
{
    #region 账号管理

    public async Task<AccountEntry> AddAccountAsync(
        Dictionary<string, string> cookies, string serverType, string nickname = "")
    {
        await _lock.WaitAsync();
        try
        {
            string stuid = ExtractStuid(cookies, serverType);
            string id = $"{serverType}_{stuid}";

            var existingEntry = _accountList.Accounts.FirstOrDefault(a => a.Id == id);
            if (existingEntry != null)
            {
                string existingCookiePath = ResolveCookiePath(existingEntry.CookieFilePath);
                await WriteCookieFileAsync(existingCookiePath, cookies);
                existingEntry.LastLoginTime = DateTime.Now;
                existingEntry.CookieVersion = CookieFileVersion;
                existingEntry.UpdatedAt = DateTime.Now;
                if (!string.IsNullOrWhiteSpace(nickname))
                    existingEntry.Nickname = nickname;
                await SaveAccountListAsync();
                return existingEntry;
            }

            string cookieFileName = $"{id}.json";
            string cookiePath = ResolveCookiePath(cookieFileName);
            await WriteCookieFileAsync(cookiePath, cookies);

            var entry = new AccountEntry
            {
                Id = id,
                Stuid = stuid,
                Nickname = nickname,
                ServerType = serverType,
                CookieFilePath = cookieFileName,
                LastLoginTime = DateTime.Now,
                CookieVersion = CookieFileVersion,
                UpdatedAt = DateTime.Now
            };

            _accountList.Accounts.Add(entry);
            await SaveAccountListAsync();
            return entry;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAccountAsync(string accountId)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (entry == null) return;

            string path;
            try
            {
                path = ResolveCookiePath(entry.CookieFilePath);
            }
            catch (Exception ex)
            {
                path = string.Empty;
                DebugLog.Info("AccountManager.Accounts",
                    $"[AccountManager] Invalid cookie file path {accountId}: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);

            _accountList.Accounts.Remove(entry);
            _sessionGameRoles.TryRemove(accountId, out _);
            await SaveAccountListAsync();

            if (_activeAccountId == accountId)
            {
                var next = _accountList.Accounts.FirstOrDefault();
                await SetActiveAccountIdAsync(next?.Id);
            }

            TryDeleteCookieKeyIfUnused();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SwitchAccountAsync(string accountId)
    {
        if (_accountList.Accounts.All(a => a.Id != accountId)) return;
        await SetActiveAccountIdAsync(accountId);

        var entry = GetActiveAccountEntry();
        if (entry != null)
        {
            entry.LastLoginTime = DateTime.Now;
            await SaveAccountListAsync();
        }
    }

    public async Task UpdateAccountMetaAsync(string accountId, string nickname, string avatarUrl, string gameUid = "",
        string? avatarPendantUrl = null)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (entry != null)
            {
                entry.Nickname = nickname;
                entry.AvatarUrl = avatarUrl;
                entry.AvatarPendantUrl = avatarPendantUrl;
                if (!string.IsNullOrEmpty(gameUid))
                    entry.GameUid = gameUid;
                await SaveAccountListAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAvatarPendantAsync(string accountId, string? avatarPendantUrl)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId);
            if (entry != null)
            {
                entry.AvatarPendantUrl = avatarPendantUrl;
                await SaveAccountListAsync();
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private string ExtractStuid(Dictionary<string, string> cookies, string serverType)
    {
        if (serverType == "cn")
        {
            if (cookies.TryGetValue("ltuid", out var ltuid)) return ltuid;
            if (cookies.TryGetValue("stuid", out var stuid)) return stuid;
        }
        else
        {
            if (cookies.TryGetValue("ltuid_v2", out var ltuidV2)) return ltuidV2;
        }

        throw new ArgumentException("无法提取账户 ID");
    }

    #endregion
}
