/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Constants;

namespace FufuLauncher.Models;

public sealed record MiyousheCheckinGame(
    string GameBiz,
    string Name,
    string ActId,
    string ApiBase,
    string Referer,
    string? SignGame = null)
{
    public string SettingKey => $"GameCheckinEnabled_{GameBiz}";


    private const string LunaApi = ApiEndpoints.LunaEventApiBase;

    public static IReadOnlyList<MiyousheCheckinGame> SupportedGames
    {
        get;
    } =
    [
        new("hk4e_cn", "原神", "e202311201442471", LunaApi, ApiEndpoints.ActMihoyoBase + "/", "hk4e"),
        new("hkrpg_cn", "崩坏：星穹铁道", "e202304121516551", LunaApi, ApiEndpoints.ActMihoyoBase + "/"),
        new("nap_cn", "绝区零", "e202406242138391", ApiEndpoints.LunaZzzEventApiBase,
            ApiEndpoints.ActMihoyoBase + "/", "zzz"),
        new("bh3_cn", "崩坏3", "e202306201626331", LunaApi,
            ApiEndpoints.WebstaticSigninBh3Url),
        new("bh2_cn", "崩坏学园2", "e202203291431091", LunaApi,
            ApiEndpoints.WebstaticSigninBh2Url),
        new("nxx_cn", "未定事件簿", "e202202251749321", LunaApi,
            ApiEndpoints.WebstaticSigninNxxUrl)
    ];

    public static bool IsLoginExpired(int retCode) => retCode is -100 or 10001;
}

public sealed record MiyousheRoleCheckinResult(
    string GameBiz,
    string GameName,
    string Uid,
    bool? Success,
    string Message,
    int SignDays = 0,
    string RewardItem = "")
{
    public static string GetFailureSummary(IEnumerable<MiyousheRoleCheckinResult> results) =>
        string.Join("；", results.Where(r => r.Success == false)
            .Select(r => $"{r.GameName}：{r.Message.Trim()}").Distinct());
}