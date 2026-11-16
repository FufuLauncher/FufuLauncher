/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed class GameRoleScope
{
    private string? _accountId;
    private string? _uid;
    private string? _region;
    public bool HasOverride => _uid != null;

    public GameRoleInfo? Current(AccountEntry account)
    {
        if (_accountId != account.Id)
        {
            _accountId = account.Id;
            _uid = _region = null;
        }

        return _uid == null
            ? GameRoleSelection.Current(account)
            : GameRoleSelection.Filter(account.GameRoles ?? [], account.ServerType)
                .FirstOrDefault(r => r.game_uid == _uid && r.region == _region);
    }

    public void Select(AccountEntry account, GameRoleInfo role)
    {
        if (!GameRoleSelection.Filter(account.GameRoles ?? [], account.ServerType)
                .Any(r => r.game_uid == role.game_uid && r.region == role.region))
            throw new InvalidOperationException("Account_RoleNotBoundRefresh".GetLocalized());
        _accountId = account.Id;
        _uid = role.game_uid;
        _region = role.region;
    }

    public void Reset()
    {
        _accountId = _uid = _region = null;
    }
}