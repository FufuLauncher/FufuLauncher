/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Constants;

public static class GenshinApiEndpoints
{
    private const string BbsApi = ApiEndpoints.MiyousheBbsApiBase;
    private const string CloudGameApi = ApiEndpoints.CloudGameApiBase;


    public const string BbsSignUrl = $"{BbsApi}/apihub/app/api/signIn";
    public static readonly string BbsSignStatusUrl = $"{BbsApi}/apihub/api/querySignInStatus?gids={GenshinCommunityId}";
    public const string GenshinTavernUrl = "https://www.miyoushe.com/ys/home/26";
    public const string BbsCreateVerificationUrl = $"{BbsApi}/misc/api/createVerification?is_high=true";
    public const string BbsVerifyVerificationUrl = $"{BbsApi}/misc/api/verifyVerification";


    public const string CloudGameWalletUrl = $"{CloudGameApi}/hk4e_cg_cn/wallet/wallet/get";
    public const string CloudGameHost = "api-cloudgame.mihoyo.com";
    public const string CloudGameReferer = ApiEndpoints.AppMihoyoBase;


    public const int GenshinCommunityId = 2;


    public const string BbsX6Salt = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    public const string PassportAppId = "bll8iq97cem8";
}
