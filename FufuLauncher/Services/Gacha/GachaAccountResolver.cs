/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.MiHoYo;

namespace FufuLauncher.Services;

public sealed record GachaAccountRole(AccountEntry Account, GameRoleInfo Role, Dictionary<string, string> Cookies);

public sealed record GachaAccountRolesResult(List<GachaAccountRole> Roles, List<string> Errors);

public sealed class GachaAccountResolver(
    Func<string, Task<Dictionary<string, string>?>> loadCookies,
    Func<string, Task<GameRolesResponse>> loadRoles)
{
    public static bool IsSupportedRole(GameRoleInfo role) =>
        role.game_biz == "hk4e_cn" &&
        role.region is ServerRegion.CnGf01 or ServerRegion.CnQd01 &&
        !string.IsNullOrEmpty(role.game_uid) && role.game_uid.Length is 9 or 10 &&
        role.game_uid.All(char.IsAsciiDigit) && ServerRegion.Resolve(role.game_uid) == role.region;

    public async Task<GachaAccountRolesResult> ResolveAsync(IEnumerable<AccountEntry> accounts,
        string? activeAccountId, string? targetUid = null)
    {
        var roles = new List<GachaAccountRole>();
        var errors = new List<string>();
        foreach (var account in accounts.Where(a => a.ServerType == "cn")
                     .OrderByDescending(a => a.Id == activeAccountId))
        {
            var label = string.IsNullOrWhiteSpace(account.Nickname) ? account.Stuid : account.Nickname;
            try
            {
                var cookies = await loadCookies(account.Id);
                if (cookies == null || cookies.Count == 0)
                {
                    errors.Add(string.Format("Gacha_Resolver_CookieUnreadable_Format".GetLocalized(), label));
                    continue;
                }

                var response = await loadRoles(string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}")));
                if (response?.retcode != 0 || response.data?.list == null)
                {
                    errors.Add(string.Format("Gacha_Resolver_RoleQueryFailed_Format".GetLocalized(), label,
                        response?.retcode.ToString() ?? "Common_Unknown".GetLocalized()));
                    continue;
                }

                var supportedRoles = response.data.list.Where(IsSupportedRole)
                    .DistinctBy(r => (r.game_uid, r.region));
                if (!string.IsNullOrEmpty(targetUid))
                    supportedRoles = supportedRoles.Where(r => r.game_uid == targetUid);
                roles.AddRange(supportedRoles.Select(r => new GachaAccountRole(account, r, cookies)));


                if (!string.IsNullOrEmpty(targetUid) && roles.Count > 0) break;
            }
            catch (Exception ex)
            {
                errors.Add(string.Format("Gacha_Resolver_RoleQueryIncomplete_Format".GetLocalized(), label, ex.GetType().Name));
            }
        }

        return new GachaAccountRolesResult(roles, errors);
    }
}