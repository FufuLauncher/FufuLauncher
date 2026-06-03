namespace FufuLauncher.Constants;

public static class GenshinApiEndpoints
{
    private const string BbsApi = "https://bbs-api.miyoushe.com";
    private const string CloudGameApi = "https://api-cloudgame.mihoyo.com";

    // Community / BBS
    public const string BbsTasksUrl = $"{BbsApi}/apihub/wapi/getUserMissionsState";
    public const string BbsSignUrl = $"{BbsApi}/apihub/app/api/signIn";
    public const string BbsPostListUrl = $"{BbsApi}/post/api/getForumPostList";
    public const string BbsPostDetailUrl = $"{BbsApi}/post/api/getPostFull";
    public const string BbsLikeUrl = $"{BbsApi}/apihub/sapi/upvotePost";
    public const string BbsShareUrl = $"{BbsApi}/apihub/api/getShareConf";
    public const string BbsCreateVerificationUrl = $"{BbsApi}/misc/api/createVerification?is_high=true";
    public const string BbsVerifyVerificationUrl = $"{BbsApi}/misc/api/verifyVerification";

    // Cloud Game (Genshin only)
    public const string CloudGameWalletUrl = $"{CloudGameApi}/hk4e_cg_cn/wallet/wallet/get";
    public const string CloudGameHost = "api-cloudgame.mihoyo.com";
    public const string CloudGameReferer = "https://app.mihoyo.com";

    // Forum IDs
    public const int GenshinForumId = 2;

    // Task IDs
    public const int TaskSign = 58;
    public const int TaskRead = 59;
    public const int TaskLike = 60;
    public const int TaskShare = 61;

    // DS Salts
    public const string BbsX6Salt = "t0qEgfub6cvueAPgR5m9aQWWVciEer7v";
    public const string PassportAppId = "bll8iq97cem8";
    public const string BbsVersion = "2.99.1";
}
