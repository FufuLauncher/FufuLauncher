/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Constants
{
    public static class ApiEndpoints
    {
        public const string RedeemCodesUrl = "https://cnb.cool/bettergi/genshin-redeem-code/-/git/raw/main/codes.json";
        public const string RedeemCodesOsUrl = "https://hoyo-codes.seria.moe/codes?game=genshin";

        public const string GameBranchesUrl =
            "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameBranches?launcher_id=jGHBHlcOq1&language=zh-cn&game_ids[]=1Z8W5NHUQb";

        public const string AccountLockUrl =
            "https://user.mihoyo.com/login-platform/index.html?app_id=dw9y09jqjpxc&theme=passport&token_type=4&game_biz=plat_cn&steps_bar=1&uc_type=3&redirect_url=https%253A%252F%252Fuser.mihoyo.com%252Fpassport%252Findex.html%253Flegacy_env%253Dproduction%2523%252Fhome%252Fsecurity&st=https%253A%252F%252Fuser.mihoyo.com%252Fpassport%252Findex.html%253Flegacy_env%253Dproduction%2523%252Fhome%252Fsecurity&succ_back_type=redirect&fail_back_type=reLogin&ux_mode=redirect#/account/lock";

        public const string AccountSecurityUrl =
            "https://user.mihoyo.com/passport/index.html?legacy_env=production#/home/security";

        public const string AgreementUrl = "https://fufu.skin/";
        public const string AgreementFallbackUrl = "https://fu1.fun/";
        public const string IconTroubleshootUrl = "https://wwaoi.lanzouu.com/ig75f3hedlaj";
        public const string RoleAvgUrl = "https://api.lelaer.com/ys/getRoleAvg.php?star=all&lang=zh-Hans";

        public const string AbyssRank2Url =
            "https://api.lelaer.com/ys/getAbyssRank2.php?star=all&role=all&lang=zh-Hans";

        public const string WishHistoryUrl = "https://api.yshelper.com/ys/getWishHistory.php?lang=zh-Hans";

        public const string SpiralAbyssRankUrl =
            "https://api.yshelper.com/ys/getAbyssRank.php?star=all&role=all&lang=zh-Hans";

        public const string RerunListUrl = "https://api.yshelper.com/ys/getRerunList.php?star=5&role=all&lang=zh-Hans";
        public const string TravelersDiaryMonthInfoUrl = "https://hk4e-api.mihoyo.com/event/ys_ledger/monthInfo";

        public const string TravelersDiaryMonthInfoOsUrl =
            "https://sg-hk4e-api.hoyolab.com/event/ysledgeros/month_info";

        public const string BackgroundCnApi =
            "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getAllGameBasicInfo?launcher_id=jGHBHlcOq1&language=zh-cn&game_id=1Z8W5NHUQb";

        public const string BackgroundOsApi =
            "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getAllGameBasicInfo?launcher_id=VYTpXlbWo8&game_id=gopR6Cufr3&language=zh-cn";

        public const string ContentCnApi =
            "https://hyp-api.mihoyo.com/hyp/hyp-connect/api/getGameContent?launcher_id=jGHBHlcOq1&game_id=1Z8W5NHUQb&language=zh-cn";

        public const string ContentOsApi =
            "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api/getGameContent?launcher_id=VYTpXlbWo8&game_id=gopR6Cufr3&language=zh-cn";

        public const string MihoyoBbsWebApi = "https://api-takumi.mihoyo.com";
        public const string MihoyoBbsAccountInfoUrl = MihoyoBbsWebApi + "/binding/api/getUserGameRolesByCookie";
        public const string MihoyoBbsCheckinRewardsUrl = MihoyoBbsWebApi + "/event/luna/home";
        public const string MihoyoBbsIsSignUrl = MihoyoBbsWebApi + "/event/luna/info";
        public const string MihoyoBbsSignUrl = MihoyoBbsWebApi + "/event/luna/sign";

        public const string PluginProxyUrl =
            "https://gh-proxy.org/https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/FuFuPlugin.zip";

        public const string PluginRawUrl =
            "https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/FuFuPlugin.zip?raw=true";

        public const string AnnouncementUrl = "https://fufu.skin/announcement.json";
        public const string AnnouncementFallbackUrl = "https://fu1.fun/announcement.json";
        public const string UpdateJsonUrl = "https://fufu.skin/Update.json";
        public const string UpdateJsonFallbackUrl = "https://fu1.fun/Update.json";
        public const string UpdateHtmlUrl = "https://fufu.skin/Update.html";
        public const string UpdateHtmlFallbackUrl = "https://fu1.fun/Update.html";
        public const string UpdatePreHtmlUrl = "https://fufu.skin/UpdatePre.html";
        public const string UpdatePreHtmlFallbackUrl = "https://fu1.fun/UpdatePre.html";

        public const string EasterEggVideoUrl = "https://r2.fu1.fun/BgVideo.mp4";
        public const string EasterEggMusicUrl = "https://r2.fu1.fun/BgMusic.wav";

        public const string MihoyoBbsUserGameRolesUrl =
            "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn";

        public const string MiyousheUserFullInfoUrl = "https://bbs-api.miyoushe.com/user/wapi/getUserFullInfo";

        public const string PassportCreateQrLoginUrl =
            "https://passport-api.mihoyo.com/account/ma-cn-passport/web/createQRLogin";

        public const string PassportScanQrLoginUrl =
            "https://passport-api.mihoyo.com/account/ma-cn-passport/app/scanQRLogin";

        public const string PassportConfirmQrLoginUrl =
            "https://passport-api.mihoyo.com/account/ma-cn-passport/app/confirmQRLogin";

        public const string PassportQueryQrLoginStatusUrl =
            "https://passport-api.mihoyo.com/account/ma-cn-passport/web/queryQRLoginStatus";

        public const string BbsDefaultUrl =
            "https://webstatic.mihoyo.com/app/community-game-records/index.html?bbs_presentation_style=fullscreen&game_id=2";

        public const string MiyousheArticleUrl = "https://m.miyoushe.com/ys/#/article/";

        public const string PassportAppCreateQrLoginUrl =
            "https://passport-api.mihoyo.com/account/ma-cn-passport/app/createQRLogin";

        public const string PassportAppQueryQrLoginStatusUrl =
            "https://passport-api.mihoyo.com/account/ma-cn-passport/app/queryQRLoginStatus";

        public const string Hk4eQrCodeFetchUrl = "https://hk4e-sdk.mihoyo.com/hk4e_cn/combo/panda/qrcode/fetch";
        public const string Hk4eQrCodeQueryUrl = "https://hk4e-sdk.mihoyo.com/hk4e_cn/combo/panda/qrcode/query";

        public const string GetTokenByGameTokenUrl =
            "https://api-takumi.mihoyo.com/account/ma-cn-session/app/getTokenByGameToken";

        public const string GetCookieAccountInfoBySTokenUrl =
            "https://passport-api.mihoyo.com/account/auth/api/getCookieAccountInfoBySToken";

        public const string GetLTokenBySTokenUrl = "https://passport-api.mihoyo.com/account/auth/api/getLTokenBySToken";
        public const string UserMihoyoLoginPlatformUrl = "https://user.mihoyo.com/login-platform/index.html";

        public const string AccountCreateLoginCaptchaUrl =
            "https://passport-api.mihoyo.com/account/ma-cn-verifier/verifier/createLoginCaptcha";

        public const string AccountLoginByMobileCaptchaUrl =
            "https://passport-api.mihoyo.com/account/ma-cn-passport/app/loginByMobileCaptcha";

        public const string OverseaLoginByPasswordUrl =
            "https://sg-public-api.hoyoverse.com/account/ma-passport/api/appLoginByPassword";

        public const string OverseaLoginByThirdPartyUrl =
            "https://sg-public-api.hoyoverse.com/account/ma-passport/api/appLoginByThirdParty";

        public const string OverseaGetActionTicketInfoUrl =
            "https://sg-public-api.hoyoverse.com/account/ma-verifier/api/getActionTicketInfo";

        public const string OverseaVerifyActionTicketPartlyUrl =
            "https://sg-public-api.hoyoverse.com/account/ma-verifier/api/verifyActionTicketPartly";

        public const string OverseaCreateEmailCaptchaByActionTicketUrl =
            "https://sg-public-api.hoyoverse.com/account/ma-verifier/api/createEmailCaptchaByActionTicket";

        public const string OverseaGetLTokenBySTokenUrl =
            "https://api-account-os.hoyoverse.com/account/auth/api/getLTokenBySToken";

        public const string OverseaGetCookieAccountInfoBySTokenUrl =
            "https://api-account-os.hoyoverse.com/account/auth/api/getCookieAccountInfoBySToken";

        public const string OverseaThirdPartyOAuthUrl =
            "https://account.hoyoverse.com/single-page/third-party-oauth.html";

        public const string OverseaGameRecordApiBase = "https://bbs-api-os.hoyolab.com/game_record/app/genshin/api";
        public const string OverseaSignBaseApi = "https://sg-hk4e-api.hoyoverse.com";
        public const string OverseaSignActId = "e202102251931481";

        public const string OverseaUserGameRolesUrl =
            "https://api-os-takumi.hoyoverse.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_global";

        public const string OverseaUserFullInfoUrl = "https://bbs-api-os.hoyolab.com/community/painter/wapi/user/full";
        public const string SeelieAchievementsUrl = "https://seelie.me/achievements";
        public const string HypCnApi = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api";
        public const string SophonCnApi = "https://downloader-api.mihoyo.com/downloader/sophon_chunk/api";
        public const string HypOsApi = "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api";
        public const string SophonOsApi = "https://sg-downloader-api.hoyoverse.com/downloader/sophon_chunk/api";

        public const string Hk4eAnnouncementPageUrl =
            "https://sdk.mihoyo.com/hk4e/announcement/index.html?auth_appid=announcement&authkey_ver=1&bundle_id=hk4e_cn&channel_id=1&game=hk4e&game_biz=hk4e_cn&lang=zh-cn&level=60&platform=pc&region=cn_gf01&sdk_presentation_style=fullscreen&sdk_screen_transparent=true&sign_type=2&uid=100000000";

        public const string GameAnnouncementListCnUrl =
            "https://hk4e-ann-api.mihoyo.com/common/hk4e_cn/announcement/api/getAnnList?game=hk4e&game_biz=hk4e_cn&lang={0}&bundle_id=hk4e_cn&platform=pc&region={1}&level=55&uid=100000000";

        public const string GameAnnouncementContentCnUrl =
            "https://hk4e-ann-api.mihoyo.com/common/hk4e_cn/announcement/api/getAnnContent?game=hk4e&game_biz=hk4e_cn&lang={0}&bundle_id=hk4e_cn&platform=pc&region={1}&level=55&uid=100000000";

        public const string GameAnnouncementListOsUrl =
            "https://sg-hk4e-api.hoyoverse.com/common/hk4e_global/announcement/api/getAnnList?game=hk4e&game_biz=hk4e_global&lang={0}&bundle_id=hk4e_global&platform=pc&region={1}&level=55&uid=100000000";

        public const string GameAnnouncementContentOsUrl =
            "https://sg-hk4e-api.hoyoverse.com/common/hk4e_global/announcement/api/getAnnContent?game=hk4e&game_biz=hk4e_global&lang={0}&bundle_id=hk4e_global&platform=pc&region={1}&level=55&uid=100000000";

        public const string BaikeDailyMaterialsUrl =
            "https://baike.mihoyo.com/ys/obc/channel/map/193?bbs_presentation_style=no_header&visit_device=pc";

        public const string BaikeActivitiesUrl =
            "https://baike.mihoyo.com/ys/obc/channel/position/48/53?bbs_presentation_style=no_header&visit_device=pc&no_page_view=1";

        public const string GithubFeatureRequestUrl =
            "https://github.com/FufuLauncher/FufuLauncher/issues/new?template=feature_request.yml";

        public const string GithubBugReportUrl =
            "https://github.com/FufuLauncher/FufuLauncher/issues/new?template=bug_report.yml";

        public const string GithubBanAppealUrl =
            "https://github.com/FufuLauncher/FufuLauncher/issues/new?template=ban_appeal.yml";

        public const string GachaCalculatorCharacterUrl =
            "https://act.mihoyo.com/ys/event/calculator/index.html#/character";

        public const string GachaCalculatorWeaponUrl = "https://act.mihoyo.com/ys/event/calculator/index.html#/weapon";
        public const string WebstaticRefererUrl = "https://webstatic.mihoyo.com";

        public const string CalculateAvatarListUrl =
            "https://api-takumi.mihoyo.com/event/e20200928calculate/v1/avatar/list";

        public const string CalculateWeaponListUrl =
            "https://api-takumi.mihoyo.com/event/e20200928calculate/v1/weapon/list";

        public const string CalculateBatchComputeUrl =
            "https://api-takumi.mihoyo.com/event/e20200928calculate/v3/batch_compute";

        public const string GenAuthKeyUrl = "https://api-takumi.mihoyo.com/binding/api/genAuthKey";
        public const string InteractiveMapUrl = "https://act.mihoyo.com/ys/app/interactive-map/index.html";
        public const string LelaerPlayerRecordApiUrl = "https://api.lelaer.com/ys/getPlayerRecord.php?uid={0}";
        public const string TeamWikiUrl = "https://webstatic.mihoyo.com/ys/event/lineup-fe/index.html#/m/index";
        public const string MiyousheSearchUrl = "https://www.miyoushe.com/ys/search?keyword=";
        public const string UpdateShareUrl = "https://wwaoi.lanzouu.com/b00wnb99ef";

        public const string CharVideoUrl =
            "https://baike.mihoyo.com/ys/obc/channel/map/80/212?bbs_presentation_style=no_header&visit_device=pc";

        public const string CutsceneVideoUrl =
            "https://baike.mihoyo.com/ys/obc/channel/map/80/81?bbs_presentation_style=no_header&visit_device=pc";

        public const string CreateAuthTicketByGameBizUrl =
            "https://passport-api.mihoyo.com/account/ma-cn-verifier/app/createAuthTicketByGameBiz";

        public const string CreateAuthTicketBySTokenUrl =
            "https://sg-public-api.hoyoverse.com/account/ma-verifier/api/createAuthTicketBySToken";

        public const string MicrosoftNetworkCheckUrl = "https://www.microsoft.com";
        public const string VcRedistDownloadUrl = "https://aka.ms/vc14/vc_redist.x64.exe";
        public const string TelegramContactUrl = "https://t.me/Adimisra6717";
        public const string PaimonTimelineUrl = "https://paimon.moe/timeline";
        public const string PluginStoreBaseUrl = "https://fu1.fun";
        public const string PluginStoreApiPrefix = "/api/v1/plugins";
        public const string PluginStoreListUrl = PluginStoreBaseUrl + PluginStoreApiPrefix + "/list";
        public const string PluginStoreDetailUrl = PluginStoreBaseUrl + PluginStoreApiPrefix + "/detail";
        public const string PluginStoreCategoriesUrl = PluginStoreBaseUrl + PluginStoreApiPrefix + "/categories";
        public const string PluginStoreHashUrl = PluginStoreBaseUrl + PluginStoreApiPrefix + "/hash";
        public const string PluginStoreStatsUrl = PluginStoreBaseUrl + PluginStoreApiPrefix + "/stats";
        public const string PluginStoreLeaderboardUrl = PluginStoreBaseUrl + PluginStoreApiPrefix + "/leaderboard";
        public const string PluginStoreDownloadTokenUrl = PluginStoreBaseUrl + PluginStoreApiPrefix + "/download-token";
        public const string PluginStorePrivateAccessUrl = PluginStoreBaseUrl + PluginStoreApiPrefix + "/private-access";
        public const string ConstraintStatusUrl = "https://fu1.fun/constraint.json";
        public const string ConstraintDialogUrl = "https://fu1.fun/dialog.json";

        public const string BanCheckUrl = "https://fu1.fun/Unlock.json";

        public const string SentryDsn =
            "https://9c8e89f029c240e3dba227979a26759a@o4511497397272576.ingest.de.sentry.io/4511497409265745";

        public const string ActMihoyoBase = "https://act.mihoyo.com";
        public const string ActNapApiBase = "https://act-nap-api.mihoyo.com";
        public const string AppMihoyoBase = "https://app.mihoyo.com";
        public const string BbsMihoyoBase = "https://bbs.mihoyo.com";
        public const string MihoyoWebBase = "https://mihoyo.com";
        public const string ActSdkApiBase = "https://api-sdk.mihoyo.com";
        public const string CloudGameApiBase = "https://api-cloudgame.mihoyo.com";
        public const string GtUiIconsBase = "https://act.mihoyo.com/act/gt-ui/assets/icons/";
        public const string BaikeBase = "https://baike.mihoyo.com";
        public const string SeelieBase = "https://seelie.me";

        public const string AchievementIconSingleUrl =
            "https://act-upload.mihoyo.com/wiki-user-upload/2024/09/16/76362111/27ec1c00c5bb06745e16039a10ff3aaa_145617018918069405.png";

        public const string AchievementIconMultiUrl =
            "https://act-upload.mihoyo.com/wiki-user-upload/2024/09/16/76362111/42fd361ea1336daf7cd33bf187e62fa2_8745920187606549502.png";

        public const string AchievementIconPrimoUrl =
            "https://upload-bbs.mihoyo.com/upload/2022/12/26/300350281/fb334cf8fd21e669ed6e7aa95890e2b5_2523388253479178875.png";

        public const string ActHoyolabBase = "https://act.hoyolab.com";
        public const string HoyolabWebBase = "https://www.hoyolab.com";
        public const string HoyolabBbsApiBase = "https://bbs-api-os.hoyolab.com";
        public const string HoyolabAccountApiBase = "https://api-account-os.hoyolab.com";
        public const string HoyoverseAccountBase = "https://account.hoyoverse.com";
        public const string Hk4eGachaOsBase = "https://hk4e-api-os.hoyoverse.com";

        public const string MiyousheWebBase = "https://www.miyoushe.com";
        public const string MiyousheBbsApiBase = "https://bbs-api.miyoushe.com";

        public const string GithubApiBase = "https://api.github.com";
        public const string GithubWebBase = "https://github.com";
        public const string GithubAvatarsBase = "https://avatars.githubusercontent.com";
        public const string GhProxyBase = "https://gh-proxy.org";
        public const string DevServiceBase = "https://dev.s1ky3.xyz";
        public const string CertificateSyncUrl = "https://certificate.fu1.fun";
        public const string BingBase = "https://www.bing.com";
        public const string StatsUptimeRobotBase = "https://stats.uptimerobot.com";
        public const string MyMemoryTranslateUrl = "https://api.mymemory.translated.net/get";
        public const string CloudGamePageUrl = "https://ys.mihoyo.com/cloud/#/";
        public const string UigfHomeUrl = "https://uigf.org/";
        public const string CalculatorWebUrl = "https://act.mihoyo.com/ys/event/calculator/index.html";

        public const string HoyolabSigninSeaUrl =
            "https://act.hoyolab.com/ys/event/signin-sea-v3/index.html?act_id=e202102251931481&lang=zh-cn";

        public const string MiyousheAccountCenterUrl = "https://www.miyoushe.com/ys/accountCenter/postList?id=";
        public const string LunaEventApiBase = "https://api-takumi.mihoyo.com/event/luna";
        public const string GachaLogCnUrl = "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog";
        public const string GachaLogOsUrl = Hk4eGachaOsBase + "/gacha_info/api/getGachaLog";
        public const string DeviceFpUrl = "https://public-data-api.mihoyo.com/device-fp/api/getFp";
        public const string DailyNoteRecordApiBase = "https://api-takumi-record.mihoyo.com/game_record/app/genshin";

        public const string GeetestCreateVerificationUrl =
            "https://api-takumi-record.mihoyo.com/game_record/app/card/wapi/createVerification?is_high=true";

        public const string GeetestVerifyVerificationUrl =
            "https://api-takumi-record.mihoyo.com/game_record/app/card/wapi/verifyVerification";

        public const string GeetestScriptV4Url = "https://static.geetest.com/v4/gt4.js";
        public const string GeetestScriptV349Url = "https://static.geetest.com/static/js/gt.0.4.9.js";
        public const string GeetestScriptV052Url = "https://static.geetest.com/static/js/gt.0.5.2.js";

        public const string HoyolabGameRolesByCookieTokenUrl =
            HoyolabAccountApiBase + "/binding/api/getUserGameRolesByCookieToken";

        public const string HoyolabGameRecordCardUrl =
            HoyolabBbsApiBase + "/game_record/card/wapi/getGameRecordCard";

        public const string FufuLauncherContributorsUrl =
            "https://api.github.com/repos/FufuLauncher/FufuLauncher/contributors";

        public const string UnlockerIslandContributorsUrl =
            "https://api.github.com/repos/FufuLauncher/FufuLauncher.UnlockerIsland/contributors";

        public const string GenshinDataAchievementsUrl =
            "https://api.github.com/repos/dvaJi/genshin-data/contents/src/data/chinese-simplified/achievements";

        public const string GithubPullsUrl = "https://github.com/FufuLauncher/FufuLauncher/pulls";
        public const string MiyoQianUrl = "https://github.com/Marchen-orz/MiyoQian";
        public const string DeveloperPortalUrl = PluginStoreBaseUrl + "/dev-add";
        public const string DevVerifyHwidUrl = DevServiceBase + "/api/verify-hwid";
        public const string DevApplyUrl = DevServiceBase + "/api/dev-apply";
        public const string DocsConfigUrl = PluginStoreBaseUrl + "/api/docs-config";
        public const string DocsContentBaseUrl = PluginStoreBaseUrl + "/api/docs/zh-CN";

        public const string UnlockerLiteReleaseApiUrl =
            "https://api.github.com/repos/FufuLauncher/YuanShen-UnlockerLite/releases/latest";

        public const string UnlockerLiteReleaseZipUrl =
            "https://github.com/FufuLauncher/YuanShen-UnlockerLite/releases/latest/download/YuanShen-UnlockerLite.zip";

        public const string YaeMetadataPrimaryUrl = "https://rin.holohat.work/schicksal/metadata";
        public const string YaeMetadataMirrorUrl = "https://ena-rin.holohat.work//schicksal/metadata";
        public const string YaeMetadataFallbackUrl = "https://cn-cd-1259389942.file.myqcloud.com/schicksal/metadata";

        public const string WebstaticSigninBh3Url =
            "https://webstatic.mihoyo.com/bbs/event/signin/bh3/index.html?act_id=e202306201626331";

        public const string WebstaticSigninBh2Url =
            "https://webstatic.mihoyo.com/bbs/event/signin/bh2/index.html?act_id=e202203291431091";

        public const string WebstaticSigninNxxUrl =
            "https://webstatic.mihoyo.com/bbs/event/signin/nxx/index.html?act_id=e202202251749321";

        public static string GetPluginFileDownloadUrl(string pluginId) =>
            $"{PluginStoreBaseUrl}/plugins/files/{Uri.EscapeDataString(pluginId)}.zip";
    }
}