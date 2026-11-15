/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public partial class AccountManager
{
    public async Task UpdateGameRolesAsync(string accountId, IEnumerable<GameRoleInfo> roles)
    {
        await _lock.WaitAsync();
        try
        {
            var entry = _accountList.Accounts.FirstOrDefault(a => a.Id == accountId)
                        ?? throw new InvalidOperationException("Account_Removed".GetLocalized());
            var oldRoles = entry.GameRoles;
            var oldUid = entry.GameUid;
            var oldRegion = entry.GameRegion;
            GameRoleSelection.UpdateBindings(entry, roles);
            try
            {
                await SaveAccountListAsync();
            }
            catch
            {
                entry.GameRoles = oldRoles;
                entry.GameUid = oldUid;
                entry.GameRegion = oldRegion;
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SelectGameRoleAsync(string accountId, string uid, string region)
    {
        await _lock.WaitAsync();
        try
        {
            if (ActiveAccountId != accountId)
                throw new InvalidOperationException("Account_SwitchedReselectRole".GetLocalized());
            var entry = GetActiveAccountEntry()!;
            var oldUid = entry.GameUid;
            var oldRegion = entry.GameRegion;
            GameRoleSelection.Select(entry, uid, region);
            try
            {
                await SaveAccountListAsync();
            }
            catch
            {
                entry.GameUid = oldUid;
                entry.GameRegion = oldRegion;
                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }
}