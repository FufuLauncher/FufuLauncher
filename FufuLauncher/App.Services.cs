/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.Services.GameAnnouncement;
using FufuLauncher.ViewModels;
using FufuLauncher.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace FufuLauncher;

public partial class App
{
    #region Dependency Injection

    private static IHost CreateHost()
    {
        return Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureHostConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["hostBuilder:reloadConfigOnChange"] = "false"
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddTransient<ActivationHandler<LaunchActivatedEventArgs>, DefaultActivationHandler>();

                services.AddSingleton<LocalSettingsRepository>();
                services.AddSingleton<MetadataRepository>();
                services.AddSingleton<AchievementRepository>();

                services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();
                services.AddSingleton<IAutoThemeColorService, AutoThemeColorService>();

                services.AddSingleton<IHoyoverseBackgroundService, HoyoverseBackgroundService>();
                services.AddSingleton<IHoyoverseContentService, HoyoverseContentService>();
                services.AddSingleton<IBackgroundRenderer, BackgroundRenderer>();
                services.AddSingleton<BingWallpaperService>();

                services.AddSingleton<IActivationService, ActivationService>();
                services.AddSingleton<INavigationService, NavigationService>();
                services.AddSingleton<IFileService, FileService>();

                services.AddSingleton<MainViewModel>();
                services.AddTransient<MainPage>();

                services.AddSingleton<GameStatsService>();
                services.AddSingleton<IDataCenterPdfReportService, DataCenterPdfReportService>();
                services.AddTransient<DataViewModel>();
                services.AddTransient<DataPage>();


                services.AddSingleton<CycleChallengeRepository>();
                services.AddSingleton<Services.CycleChallenge.GameRecordApiClient>();
                services.AddSingleton<Services.CycleChallenge.CycleChallengeService>();
                services.AddTransient<CycleChallengeViewModel>();
                services.AddTransient<Views.CycleChallengePage>();

                services.AddSingleton<LaunchConfigurationViewModel>();
                services.AddTransient<SettingsViewModel>();
                services.AddTransient<SettingsPage>();
                services.AddTransient<ImageSourceViewModel>();

                services.AddTransient<NullToVisibilityConverter>();
                services.AddTransient<BoolToVisibilityConverter>();
                services.AddTransient<BoolToGlyphConverter>();
                services.AddTransient<IntToVisibilityConverter>();

                services.AddTransient<AccountViewModel>();
                services.AddTransient<AccountPage>();

                services.AddSingleton<IGameLauncherService, GameLauncherService>();
                services.AddSingleton<IGameConfigService, GameConfigService>();
                services.AddSingleton<Services.AuthTicket.IAuthTicketService, Services.AuthTicket.AuthTicketService>();

                services.AddSingleton<IHoyoverseCheckinService, HoyoverseCheckinService>();
                services.AddSingleton<MiyousheGameCheckinService>();
                services.AddSingleton<ICommunityCheckinService, CommunityCheckinService>();
                services.AddSingleton<ICloudGameCheckinService, CloudGameCheckinService>();
                services.AddSingleton<IHoyolabRoleResolverService, HoyolabRoleResolverService>();
                services.AddSingleton<IUnifiedCheckinService, UnifiedCheckinService>();
                services.AddSingleton<Services.MiHoYo.DailyNote.DailyNoteCardService>();
                services.AddSingleton<GameSettingsViewModel>();
                services.AddTransient<GameSettingsPage>();
                services.AddSingleton<ILauncherService, LauncherService>();
                services.AddTransient<OtherViewModel>();
                services.AddTransient<OtherPage>();
                services.AddSingleton<IAutoClickerService, AutoClickerService>();
                services.AddSingleton<IScreenshotService, ScreenshotService>();
                services.AddTransient<LanguageSelectionViewModel>();
                services.AddTransient<LanguageSelectionPage>();
                services.AddTransient<AgreementViewModel>();
                services.AddTransient<AgreementPage>();
                services.AddSingleton<IDevBuildDetectionService, DevBuildDetectionService>();
                services.AddSingleton<IUpdateService, UpdateService>();
                services.AddSingleton<GamePlayTimeStore>();
                services.AddSingleton<ControlPanelViewModel>();
                services.AddTransient<ControlPanelPage>();
                services.AddSingleton<IUserInfoService, UserInfoService>();
                services.AddSingleton<IUidLookupService, Services.UID.UidLookupService>();
                services.AddSingleton<Services.UID.BanCheckService>();

                services.AddSingleton<AccountManager>();
                services.AddSingleton<GameRoleService>();
                services.AddSingleton<Services.Device.MobileDeviceService>();
                services.AddSingleton<Services.MiHoYo.MiHoYoDeviceStore>();
                services.AddSingleton<Services.MiHoYo.Fingerprint.DeviceFpService>();
                services.AddSingleton<Services.MiHoYo.AccountIdentityService>();
                services.AddSingleton<IBbsRequestBuilder, Services.MiHoYo.Transport.BbsRequestBuilder>();

                services.AddSingleton<GeetestService>();
                services.AddSingleton<Services.MiHoYo.Passport.PassportClient>();
                services.AddSingleton<Services.MiHoYo.Passport.OverseaPassportClient>();
                services.AddSingleton<Services.MiHoYo.Passport.OverseaRiskVerificationService>();

                services.AddLogging(builder =>
                {
                    builder.ClearProviders();

                    builder.AddDebug();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                services.AddSingleton<GenshinApiClient>();
                services.AddSingleton<IGenshinService, GenshinService>();
                services.AddTransient<GenshinViewModel>();
                services.AddTransient<GenshinDataWindow>();
                services.AddSingleton<IFilePickerService, FilePickerService>();
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddTransient<PluginViewModel>();
                services.AddTransient<PluginPage>();
                services.AddTransient<GachaViewModel>();
                services.AddSingleton<GachaService>();
                services.AddSingleton<IAnnouncementService, AnnouncementService>();
                services.AddSingleton<IGameAnnouncementService, GameAnnouncementService>();
                services.AddSingleton<IGameAnnouncementImageService, GameAnnouncementImageService>();
                services.AddTransient<GameAnnouncementViewModel>();
                services.AddTransient<IPluginUpdateService, PluginUpdateService>();
                services.AddTransient<GachaAnalysisViewModel>();
                services.AddSingleton<PluginStoreService>();
                services.AddSingleton<LightweightPluginService>();
                services.AddSingleton<PluginGameVersionGuard>();
                services.AddSingleton<ConstraintService>();
                services.AddSingleton<LuaPluginInstaller>();
                services.AddSingleton<GameSessionService>();
                services.AddSingleton<Services.Mcp.McpInstallJobService>();
                services.AddSingleton<Services.Mcp.McpToolInvoker>();
                services.AddSingleton<Services.Mcp.McpHostService>();
                services.AddSingleton<Services.PluginMirror.MirrorSiteProvider>();
                services.AddSingleton<Services.PluginMirror.PluginMirrorDownloadService>();
                services.AddSingleton<PluginStoreViewModel>();
                services.AddTransient<PluginStorePage>();

                services.AddSingleton<Services.GameServer.GameServerHttpClientProvider>();
                services.AddSingleton<Services.GameServer.ChunkDownloader>();
                services.AddSingleton<Services.GameServer.SophonBuildClient>();
                services.AddSingleton<Services.GameServer.GameServerConfigurationService>();
                services.AddSingleton<Services.GameServer.GameChannelSdkService>();
                services.AddTransient<Services.GameServer.GameServerConverter>();

                services.AddSingleton<Services.GameServer.GameUpdateService>();

                services.AddSingleton<DeveloperAuthorizationService>();
                services.AddSingleton<Services.CodeSigning.CodeSigningTrustService>();
                services.AddSingleton<Services.CodeSigning.ModTrustGate>();

                services.Configure<LocalSettingsOptions>(
                    context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            })
            .Build();
    }

    #endregion
}