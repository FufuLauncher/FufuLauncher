/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Models;

public sealed record MiyousheCheckinGame(
    string GameBiz, string Name, string ActId, string ApiBase, string Referer, string? SignGame = null)
{
    public string SettingKey => $"GameCheckinEnabled_{GameBiz}";

    // Activity IDs and API routes are maintained in the upstream implementation:
    // https://github.com/Womsxd/MihoyoBBSTools/blob/master/setting.py
    private const string LunaApi = "https://api-takumi.mihoyo.com/event/luna";

    public static IReadOnlyList<MiyousheCheckinGame> SupportedGames { get; } =
    [
        new("hk4e_cn", "原神", "e202311201442471", LunaApi, "https://act.mihoyo.com/", "hk4e"),
        new("hkrpg_cn", "崩坏：星穹铁道", "e202304121516551", LunaApi, "https://act.mihoyo.com/"),
        new("nap_cn", "绝区零", "e202406242138391", "https://act-nap-api.mihoyo.com/event/luna/zzz",
            "https://act.mihoyo.com/", "zzz"),
        new("bh3_cn", "崩坏3", "e202306201626331", LunaApi,
            "https://webstatic.mihoyo.com/bbs/event/signin/bh3/index.html?act_id=e202306201626331"),
        new("bh2_cn", "崩坏学园2", "e202203291431091", LunaApi,
            "https://webstatic.mihoyo.com/bbs/event/signin/bh2/index.html?act_id=e202203291431091"),
        new("nxx_cn", "未定事件簿", "e202202251749321", LunaApi,
            "https://webstatic.mihoyo.com/bbs/event/signin/nxx/index.html?act_id=e202202251749321")
    ];

    public static bool IsLoginExpired(int retCode) => retCode is -100 or 10001;
}

public sealed record MiyousheRoleCheckinResult(
    string GameBiz, string GameName, string Uid, bool? Success, string Message,
    int SignDays = 0, string RewardItem = "")
{
    public static string GetFailureSummary(IEnumerable<MiyousheRoleCheckinResult> results) =>
        string.Join("；", results.Where(r => r.Success == false)
            .Select(r => $"{r.GameName}：{r.Message.Trim()}").Distinct());
}
