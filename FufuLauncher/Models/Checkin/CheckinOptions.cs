/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Models;

public sealed class CheckinOptions
{
    public bool IsGameCheckinEnabled
    {
        get;
        set;
    } = true;

    public bool IsCommunityCheckinEnabled
    {
        get;
        set;
    } = true;

    public bool IsCloudGameCheckinEnabled
    {
        get;
        set;
    }

    public Dictionary<string, bool> Games
    {
        get;
        set;
    } = new();

    public bool IsGameEnabled(string gameBiz) => !Games.TryGetValue(gameBiz, out var enabled) || enabled;

    public HashSet<string> GetEnabledGames() => MiyousheCheckinGame.SupportedGames
        .Where(g => g.GameBiz == "hk4e_cn" || IsGameEnabled(g.GameBiz))
        .Select(g => g.GameBiz).ToHashSet();
}