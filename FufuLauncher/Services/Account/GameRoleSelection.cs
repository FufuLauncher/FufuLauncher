/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.MiHoYo;

namespace FufuLauncher.Services;

public static class GameRoleSelection
{
    public static List<GameRoleInfo> Filter(IEnumerable<GameRoleInfo> roles, string serverType) => roles
        .Where(r => r.game_biz == (serverType == "os" ? "hk4e_global" : "hk4e_cn") &&
                    r.game_uid is { Length: 9 or 10 } && r.game_uid.All(char.IsAsciiDigit) &&
                    !string.IsNullOrEmpty(r.region) && (serverType == "os"
                        ? ServerRegion.IsOversea(r.region)
                        : r.region is ServerRegion.CnGf01 or ServerRegion.CnQd01) &&
                    ServerRegion.Resolve(r.game_uid) == r.region)
        .DistinctBy(r => (r.game_uid, r.region)).ToList();

    public static GameRoleInfo? Current(AccountEntry account) => Filter(account.GameRoles ?? [], account.ServerType)
        .FirstOrDefault(r => r.game_uid == account.GameUid &&
                             (string.IsNullOrEmpty(account.GameRegion) || r.region == account.GameRegion));

    public static void UpdateBindings(AccountEntry account, IEnumerable<GameRoleInfo> roles)
    {
        account.GameRoles = Filter(roles, account.ServerType);


        if (string.IsNullOrEmpty(account.GameUid) && account.GameRoles.Count > 0)
            account.GameUid = account.GameRoles[0].game_uid;
        if (Current(account) is { } selected)
            account.GameRegion = selected.region;
    }

    public static void Select(AccountEntry account, string uid, string region)
    {
        if (!Filter(account.GameRoles ?? [], account.ServerType).Any(r => r.game_uid == uid && r.region == region))
            throw new InvalidOperationException("Account_RoleNotBoundRefresh".GetLocalized());
        account.GameUid = uid;
        account.GameRegion = region;
    }
}