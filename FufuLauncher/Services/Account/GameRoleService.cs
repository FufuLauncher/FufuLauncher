/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.Services;

public sealed record SelectedGameRole(
    string AccountId,
    string ServerType,
    GameRoleInfo Role,
    Dictionary<string, string> Cookies);

public sealed class GameRoleService(AccountManager accounts, IUserInfoService userInfo)
{
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task RefreshAsync()
    {
        var account = accounts.GetActiveAccountEntry();
        if (account == null) return;
        var accountId = account.Id;
        await _refreshLock.WaitAsync();
        try
        {
            if (accounts.ActiveAccountId != accountId) return;
            var cookies = await accounts.LoadCookiesAsync(accountId);
            if (cookies == null || cookies.Count == 0)
                throw new InvalidOperationException("Account_CannotReadLoginInfo".GetLocalized());
            var response = await userInfo.GetUserGameRolesAsync(
                string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}")), account.ServerType);
            if (response.retcode != 0 || response.data?.list == null)
                throw new InvalidOperationException(string.Format("Account_FetchRolesFailed_Format".GetLocalized(), response.retcode));
            if (accounts.ActiveAccountId != accountId) return;
            await UpdateBindingsAsync(accountId, response.data.list);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task UpdateBindingsAsync(string accountId, IEnumerable<GameRoleInfo> roles)
    {
        var roleList = roles.ToList();
        var oldRole = accounts.GetAllAccounts().FirstOrDefault(a => a.Id == accountId) is { } oldAccount
            ? GameRoleSelection.Current(oldAccount)
            : null;
        await accounts.UpdateGameRolesAsync(accountId, roleList);
        accounts.CacheSessionGameRoles(accountId, roleList);
        WeakReferenceMessenger.Default.Send(new GameRolesUpdatedMessage(accountId));
        if (accounts.GetActiveAccountEntry() is { } current && current.Id == accountId && oldRole != null)
        {
            var newRole = GameRoleSelection.Current(current);
            if (newRole == null || newRole.game_uid != oldRole.game_uid || newRole.region != oldRole.region)
                WeakReferenceMessenger.Default.Send(new GameRoleChangedMessage(accountId,
                    current.GameUid, current.GameRegion));
        }
    }

    public async Task<SelectedGameRole?> GetCurrentAsync(GameRoleScope? scope = null)
    {
        var account = accounts.GetActiveAccountEntry();
        if (account == null) return null;
        var accountId = account.Id;
        if (account.GameRoles == null) await RefreshAsync();
        if (accounts.ActiveAccountId != accountId) return null;
        var role = scope == null ? GameRoleSelection.Current(account) : scope.Current(account);
        if (role == null)
            throw new InvalidOperationException("Account_RoleNotBoundPrompt".GetLocalized());
        var cookies = await accounts.LoadCookiesAsync(accountId);
        if (cookies == null || cookies.Count == 0)
            throw new InvalidOperationException("Account_InvalidLoginRelogin".GetLocalized());
        var result = new SelectedGameRole(accountId, account.ServerType, role, cookies);
        return IsCurrent(result, scope) ? result : null;
    }

    public bool IsCurrent(SelectedGameRole selected, GameRoleScope? scope = null) =>
        accounts.GetActiveAccountEntry() is { } account && account.Id == selected.AccountId &&
        (scope == null ? GameRoleSelection.Current(account) : scope.Current(account)) is { } role &&
        role.game_uid == selected.Role.game_uid && role.region == selected.Role.region;

    public async Task SelectAsync(string accountId, GameRoleInfo role, GameRoleScope? scope = null)
    {
        var account = accounts.GetActiveAccountEntry();
        if (scope != null)
        {
            if (account?.Id != accountId)
                throw new InvalidOperationException("Account_SwitchedReselectRole".GetLocalized());
            scope.Select(account, role);
            WeakReferenceMessenger.Default.Send(new FeatureGameRoleChangedMessage(scope));
            return;
        }

        if (account?.Id == accountId && GameRoleSelection.Current(account) is { } current &&
            current.game_uid == role.game_uid && current.region == role.region)
            return;
        await accounts.SelectGameRoleAsync(accountId, role.game_uid, role.region);
        WeakReferenceMessenger.Default.Send(new GameRoleChangedMessage(accountId, role.game_uid, role.region));
    }
}
