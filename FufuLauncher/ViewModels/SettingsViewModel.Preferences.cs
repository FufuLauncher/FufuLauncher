/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.GameAnnouncement;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.Services.PluginMirror;

namespace FufuLauncher.ViewModels;

public partial class SettingsViewModel
{
    #region 设置分区加载

    private async Task LoadAppearanceSectionAsync()
    {
        ElementTheme = _themeSelectorService.Theme;

        var appThemeColorJson = await _localSettingsService.ReadSettingAsync("AppThemeColor");
        AppThemeColor = appThemeColorJson?.ToString() ?? string.Empty;

        var launchOverlayColorJson = await _localSettingsService.ReadSettingAsync("LaunchButtonOverlayColor");
        LaunchButtonOverlayColor = launchOverlayColorJson?.ToString() ?? "#0078D7";
    }

    private async Task LoadHomeCardsSectionAsync()
    {
        var hideNewsCardJson = await _localSettingsService.ReadSettingAsync("IsHideGameNewsCardEnabled");
        IsHideGameNewsCardEnabled = hideNewsCardJson != null && Convert.ToBoolean(hideNewsCardJson);

        var hideCheckinCardJson = await _localSettingsService.ReadSettingAsync("IsHideCheckinCardEnabled");
        IsHideCheckinCardEnabled = hideCheckinCardJson != null && Convert.ToBoolean(hideCheckinCardJson);

        var showPresetCardJson = await _localSettingsService.ReadSettingAsync("IsShowPresetCardEnabled");
        IsShowPresetCardEnabled = showPresetCardJson == null || Convert.ToBoolean(showPresetCardJson);

        var showWidgetCardJson = await _localSettingsService.ReadSettingAsync("IsShowWidgetCardEnabled");
        IsShowWidgetCardEnabled = showWidgetCardJson == null || Convert.ToBoolean(showWidgetCardJson);
    }

    private async Task LoadGameAnnouncementSectionAsync()
    {
        var announcementViewModeJson =
            await _localSettingsService.ReadSettingAsync(LocalSettingsService.AnnouncementViewModeKey);
        AnnouncementViewMode = announcementViewModeJson is string modeStr &&
                               Enum.TryParse<AnnouncementViewMode>(modeStr, out var parsedMode)
            ? parsedMode
            : AnnouncementViewMode.New;
    }

    private async Task LoadWidgetsSectionAsync()
    {
        var showWidgetGachaJson = await _localSettingsService.ReadSettingAsync("ShowWidgetGacha");
        ShowWidgetGacha = showWidgetGachaJson == null || Convert.ToBoolean(showWidgetGachaJson);

        var showWidgetAchievementJson = await _localSettingsService.ReadSettingAsync("ShowWidgetAchievement");
        ShowWidgetAchievement = showWidgetAchievementJson == null || Convert.ToBoolean(showWidgetAchievementJson);

        var showWidgetInventoryJson = await _localSettingsService.ReadSettingAsync("ShowWidgetInventory");
        ShowWidgetInventory = showWidgetInventoryJson == null || Convert.ToBoolean(showWidgetInventoryJson);

        var showWidgetPlayerRoleJson = await _localSettingsService.ReadSettingAsync("ShowWidgetPlayerRole");
        ShowWidgetPlayerRole = showWidgetPlayerRoleJson == null || Convert.ToBoolean(showWidgetPlayerRoleJson);

        var showWidgetDailyNoteWindowJson = await _localSettingsService.ReadSettingAsync("ShowWidgetDailyNoteWindow");
        ShowWidgetDailyNoteWindow =
            showWidgetDailyNoteWindowJson == null || Convert.ToBoolean(showWidgetDailyNoteWindowJson);

        var showWidgetVideoJson = await _localSettingsService.ReadSettingAsync("ShowWidgetVideo");
        ShowWidgetVideo = showWidgetVideoJson == null || Convert.ToBoolean(showWidgetVideoJson);

        var showWidgetBBSJson = await _localSettingsService.ReadSettingAsync("ShowWidgetBBS");
        ShowWidgetBBS = showWidgetBBSJson == null || Convert.ToBoolean(showWidgetBBSJson);
    }

    private async Task LoadNotesSectionAsync()
    {
        var hideDailyNoteCardJson = await _localSettingsService.ReadSettingAsync("IsHideDailyNoteCardEnabled");
        IsHideDailyNoteCardEnabled = hideDailyNoteCardJson != null && Convert.ToBoolean(hideDailyNoteCardJson);

        var wasUpdatingDailyNote = _isUpdatingDailyNote;
        _isUpdatingDailyNote = true;
        try
        {
            int activeCount = 0;

            var showResinJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteResin");
            ShowDailyNoteResin = showResinJson == null || Convert.ToBoolean(showResinJson);
            if (ShowDailyNoteResin) activeCount++;

            var showDailyTasksJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteDailyTasks");
            ShowDailyNoteDailyTasks =
                (showDailyTasksJson == null || Convert.ToBoolean(showDailyTasksJson)) && activeCount < 3;
            if (ShowDailyNoteDailyTasks) activeCount++;

            var showHomeCoinJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteHomeCoin");
            ShowDailyNoteHomeCoin =
                (showHomeCoinJson == null || Convert.ToBoolean(showHomeCoinJson)) && activeCount < 3;
            if (ShowDailyNoteHomeCoin) activeCount++;

            var showExpeditionsJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteExpeditions");
            ShowDailyNoteExpeditions = (showExpeditionsJson == null || Convert.ToBoolean(showExpeditionsJson)) &&
                                       activeCount < 3;
            if (ShowDailyNoteExpeditions) activeCount++;

            var showTransformerJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteTransformer");
            ShowDailyNoteTransformer = (showTransformerJson == null || Convert.ToBoolean(showTransformerJson)) &&
                                       activeCount < 3;
        }
        finally
        {
            _isUpdatingDailyNote = wasUpdatingDailyNote;
        }
    }

    private async Task LoadHomeTextSectionAsync()
    {
        var gameNewsCardColorJson = await _localSettingsService.ReadSettingAsync("GameNewsCardTextColor");
        GameNewsCardTextColor = gameNewsCardColorJson?.ToString() ?? "#FFFFFF";

        var gameNewsCardOpacityJson = await _localSettingsService.ReadSettingAsync("GameNewsCardTextOpacity");
        GameNewsCardTextOpacity = gameNewsCardOpacityJson != null ? Convert.ToDouble(gameNewsCardOpacityJson) : 1.0;

        var launchBtnColorJson = await _localSettingsService.ReadSettingAsync("LaunchButtonTextColor");
        LaunchButtonTextColor = launchBtnColorJson?.ToString() ?? "#FFFFFF";

        var launchBtnOpacityJson = await _localSettingsService.ReadSettingAsync("LaunchButtonTextOpacity");
        LaunchButtonTextOpacity = launchBtnOpacityJson != null ? Convert.ToDouble(launchBtnOpacityJson) : 1.0;

        var checkinColorJson = await _localSettingsService.ReadSettingAsync("GameCheckinTextColor");
        GameCheckinTextColor = checkinColorJson?.ToString() ?? "#FFFFFF";

        var checkinOpacityJson = await _localSettingsService.ReadSettingAsync("GameCheckinTextOpacity");
        GameCheckinTextOpacity = checkinOpacityJson != null ? Convert.ToDouble(checkinOpacityJson) : 1.0;
    }

    private async Task LoadBackgroundSectionAsync()
    {
        var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
        SelectedServer = (ServerType)(serverJson != null ? Convert.ToInt32(serverJson) : 0);

        var customBackgroundApiJson = await _localSettingsService.ReadSettingAsync("CustomBackgroundApiUrl");
        CustomBackgroundApiUrl = customBackgroundApiJson?.ToString() ?? string.Empty;
        CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(CustomBackgroundApiUrl)
            ? GetDefaultBackgroundApiUrl(SelectedServer)
            : CustomBackgroundApiUrl;

        var enabledJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.IsBackgroundEnabledKey);
        IsBackgroundEnabled = enabledJson == null || Convert.ToBoolean(enabledJson);

        var acrylicOverlayJson = await _localSettingsService.ReadSettingAsync("IsAcrylicOverlayEnabled");
        IsAcrylicOverlayEnabled = acrylicOverlayJson == null || Convert.ToBoolean(acrylicOverlayJson);

        var pageOverlaySemiTransparentJson =
            await _localSettingsService.ReadSettingAsync("IsPageOverlaySemiTransparentEnabled");
        IsPageOverlaySemiTransparentEnabled = pageOverlaySemiTransparentJson != null &&
                                              Convert.ToBoolean(pageOverlaySemiTransparentJson);

        var pageOverlayTargetOpacityJson = await _localSettingsService.ReadSettingAsync("PageOverlayTargetOpacity");
        PageOverlayTargetOpacity = pageOverlayTargetOpacityJson != null
                                   && double.TryParse(pageOverlayTargetOpacityJson.ToString(),
                                       out var pageOverlayOpacity)
            ? Math.Clamp(pageOverlayOpacity, 0.1, 1.0)
            : 0.7;

        GlobalBackgroundOverlayOpacity = await ReadDoubleSettingAsync("GlobalBackgroundOverlayOpacity", 0);
        ContentFrameBackgroundOpacity = await ReadDoubleSettingAsync("ContentFrameBackgroundOpacity", 0.5);
        PanelBackgroundOpacity = await ReadDoubleSettingAsync("PanelBackgroundOpacity", 0.5);
        GlobalBackgroundImageOpacity = await ReadDoubleSettingAsync("GlobalBackgroundImageOpacity", 1.0);

        await LoadCustomBackgroundSettingsAsync();
    }

    private async Task<double> ReadDoubleSettingAsync(string key, double fallback)
    {
        var value = await _localSettingsService.ReadSettingAsync(key);
        if (value == null)
            return fallback;

        try
        {
            return Convert.ToDouble(value);
        }
        catch
        {
            return fallback;
        }
    }

    private async Task LoadWindowEffectsSectionAsync()
    {
        var backdropJson = await _localSettingsService.ReadSettingAsync("WindowBackdrop");
        CurrentWindowBackdrop = backdropJson != null
            ? (WindowBackdropType)Convert.ToInt32(backdropJson)
            : WindowBackdropType.Acrylic;

        var hamburgerButtonJson = await _localSettingsService.ReadSettingAsync("IsHamburgerButtonEnabled");
        IsHamburgerButtonEnabled = hamburgerButtonJson != null && Convert.ToBoolean(hamburgerButtonJson);
    }

    private async Task LoadLaunchConfigSectionAsync()
    {
        var wasLoadingLaunchParams = _isLoadingLaunchParams;
        _isLoadingLaunchParams = true;
        try
        {
            var paramsJson = await _localSettingsService.ReadSettingAsync("CustomLaunchParameters");
            if (paramsJson != null)
            {
                CustomLaunchParameters = paramsJson.ToString();
                ParseLaunchParameters(CustomLaunchParameters);
            }

            var customExeJson = await _localSettingsService.ReadSettingAsync(GameExeManager.CustomExeNameKey);
            CustomGameExeName = customExeJson?.ToString() ?? string.Empty;

            var behaviorJson = await _localSettingsService.ReadSettingAsync("PostLaunchBehavior");
            var postLaunchBehavior = PostLaunchBehavior.None;
            if (behaviorJson is string behaviorStr && Enum.TryParse<PostLaunchBehavior>(behaviorStr, out var parsed))
                postLaunchBehavior = parsed;

            _postLaunchBehavior = postLaunchBehavior;
            SelectedPostLaunchBehaviorItem = PostLaunchBehaviorItems.First(i => i.Value == postLaunchBehavior);

            var usingHoyolabJson = await _localSettingsService.ReadSettingAsync("UsingHoyolabAccount");
            IsUsingHoyolabAccount = usingHoyolabJson != null && Convert.ToBoolean(usingHoyolabJson);

            var redeemNotifyJson = await _localSettingsService.ReadSettingAsync("IsRedeemCodeNotificationEnabled");
            IsRedeemCodeNotificationEnabled = redeemNotifyJson == null || Convert.ToBoolean(redeemNotifyJson);

            var conflictCheckJson =
                await _localSettingsService.ReadSettingAsync(PluginConflictSettings.CheckEnabledKey);
            IsPluginConflictCheckEnabled = conflictCheckJson == null || Convert.ToBoolean(conflictCheckJson);

            var conflictMainOnlyJson =
                await _localSettingsService.ReadSettingAsync(PluginConflictSettings.MainDllOnlyKey);
            IsPluginConflictMainDllOnly = conflictMainOnlyJson == null || Convert.ToBoolean(conflictMainOnlyJson);

            LoadMonitors();
        }
        finally
        {
            _isLoadingLaunchParams = wasLoadingLaunchParams;
        }
    }

    private async Task LoadScreenshotSectionAsync()
    {
        var screenshotEnabledJson = await _localSettingsService.ReadSettingAsync("IsScreenshotEnabled");
        IsScreenshotEnabled = screenshotEnabledJson != null && Convert.ToBoolean(screenshotEnabledJson);

        var screenshotHotkeyJson = await _localSettingsService.ReadSettingAsync("ScreenshotHotkey");
        ScreenshotHotkey = screenshotHotkeyJson?.ToString() ?? "F12";

        var screenshotPathJson = await _localSettingsService.ReadSettingAsync("ScreenshotSavePath");
        ScreenshotSavePath = screenshotPathJson?.ToString();
        HasScreenshotSavePath = !string.IsNullOrEmpty(ScreenshotSavePath);
    }

    private async Task LoadCheckinSectionAsync()
    {
        var gameCheckinJson = await _localSettingsService.ReadSettingAsync("IsGameCheckinEnabled");
        IsGameCheckinEnabled = gameCheckinJson == null || Convert.ToBoolean(gameCheckinJson);

        var communityCheckinJson = await _localSettingsService.ReadSettingAsync("IsCommunityCheckinEnabled");
        IsCommunityCheckinEnabled = communityCheckinJson == null || Convert.ToBoolean(communityCheckinJson);

        var communityLikeJson = await _localSettingsService.ReadSettingAsync("IsCommunityLikeEnabled");
        IsCommunityLikeEnabled = communityLikeJson != null && Convert.ToBoolean(communityLikeJson);

        var communityReadJson = await _localSettingsService.ReadSettingAsync("IsCommunityReadEnabled");
        IsCommunityReadEnabled = communityReadJson != null && Convert.ToBoolean(communityReadJson);

        var communityShareJson = await _localSettingsService.ReadSettingAsync("IsCommunityShareEnabled");
        IsCommunityShareEnabled = communityShareJson != null && Convert.ToBoolean(communityShareJson);

        var cloudGameCheckinJson = await _localSettingsService.ReadSettingAsync("IsCloudGameCheckinEnabled");
        IsCloudGameCheckinEnabled = cloudGameCheckinJson != null && Convert.ToBoolean(cloudGameCheckinJson);

        var autoCheckinJson = await _localSettingsService.ReadSettingAsync("IsAutoCheckinEnabled");
        IsAutoCheckinEnabled = autoCheckinJson != null && Convert.ToBoolean(autoCheckinJson);

        var batchCheckinJson = await _localSettingsService.ReadSettingAsync("IsBatchCheckinEnabled");
        IsBatchCheckinEnabled = batchCheckinJson != null && Convert.ToBoolean(batchCheckinJson);

        await LoadCheckinGamesAsync();
        await LoadCheckinAccountsAsync();
    }

    private async Task LoadLanguageSectionAsync()
    {
        var languageJson = await _localSettingsService.ReadSettingAsync("AppLanguage");
        SelectedLanguage = (AppLanguage)(languageJson != null ? Convert.ToInt32(languageJson) : 0);
    }

    private async Task LoadWindowBehaviorSectionAsync()
    {
        var trayJson = await _localSettingsService.ReadSettingAsync("MinimizeToTray");
        MinimizeToTray = trayJson != null && Convert.ToBoolean(trayJson);

        var saveWindowSizeJson = await _localSettingsService.ReadSettingAsync("IsSaveWindowSizeEnabled");
        IsSaveWindowSizeEnabled = saveWindowSizeJson != null && Convert.ToBoolean(saveWindowSizeJson);

        var minSizeLimitJson = await _localSettingsService.ReadSettingAsync("IsMinWindowSizeLimitEnabled");
        IsMinWindowSizeLimitEnabled = minSizeLimitJson == null || Convert.ToBoolean(minSizeLimitJson);

        var notifPosJson = await _localSettingsService.ReadSettingAsync("NotificationPosition");
        NotificationPosition = notifPosJson != null
            ? (NotificationPosition)Convert.ToInt32(notifPosJson)
            : NotificationPosition.BottomRight;

        await InitializeNavItemsAsync();
    }

    private async Task LoadStartupSectionAsync()
    {
        var startupJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.IsStartupEnabledKey);
        var startupRequested = startupJson != null && Convert.ToBoolean(startupJson);
        var startupEnabled = StartupManager.ResolveEnabledState(startupRequested);
        IsStartupEnabled = startupEnabled;

        if (startupEnabled != startupRequested)
        {
            await _localSettingsService.SaveSettingAsync(LocalSettingsService.IsStartupEnabledKey, startupEnabled);
        }
    }

    private async Task LoadStartupSoundSectionAsync()
    {
        var soundJson = await _localSettingsService.ReadSettingAsync("IsStartupSoundEnabled");
        IsStartupSoundEnabled = soundJson != null && Convert.ToBoolean(soundJson);

        var soundPathJson = await _localSettingsService.ReadSettingAsync("StartupSoundPath");
        if (soundPathJson != null)
        {
            StartupSoundPath = soundPathJson.ToString();
            HasCustomStartupSound = File.Exists(StartupSoundPath);
        }
        else
        {
            StartupSoundPath = null;
            HasCustomStartupSound = false;
        }
    }

    private async Task LoadAdvancedOptionsSectionAsync()
    {
        UpdateWebView2CacheSizeAsync();

        var cpuWarningEnabledJson =
            await _localSettingsService.ReadSettingAsync(ProcessCpuUsageMonitor.IsEnabledSettingKey);
        IsCpuUsageWarningEnabled = cpuWarningEnabledJson == null || Convert.ToBoolean(cpuWarningEnabledJson);

        var cpuWarningThresholdJson =
            await _localSettingsService.ReadSettingAsync(ProcessCpuUsageMonitor.ThresholdSettingKey);
        CpuUsageWarningThreshold = cpuWarningThresholdJson != null
            ? Math.Clamp(Convert.ToDouble(cpuWarningThresholdJson), 5.0, 100.0)
            : ProcessCpuUsageMonitor.DefaultCpuThreshold;

        var priorityJson = await _localSettingsService.ReadSettingAsync("AppProcessPriority");
        AppProcessPriority = priorityJson != null
            ? (AppProcessPriority)Convert.ToInt32(priorityJson)
            : AppProcessPriority.Normal;

        var captchaPopupJson = await _localSettingsService.ReadSettingAsync("IsCaptchaPopupDisabled");
        IsCaptchaPopupDisabled = captchaPopupJson != null && Convert.ToBoolean(captchaPopupJson);

        var captchaNoticeJson = await _localSettingsService.ReadSettingAsync("IsCaptchaNoticeEnabled");
        IsCaptchaNoticeEnabled = captchaNoticeJson == null || Convert.ToBoolean(captchaNoticeJson);

        var ignoreConstraintJson = await _localSettingsService.ReadSettingAsync(ConstraintService.IgnoreSettingKey);
        IgnoreConstraintRestrictions = ignoreConstraintJson != null && Convert.ToBoolean(ignoreConstraintJson);

        var shortTermJson = await _localSettingsService.ReadSettingAsync("IsShortTermSupportEnabled");
        IsShortTermSupportEnabled = shortTermJson != null && Convert.ToBoolean(shortTermJson);

        var betterGIJson = await _localSettingsService.ReadSettingAsync("IsBetterGIIntegrationEnabled");
        IsBetterGIIntegrationEnabled = betterGIJson != null && Convert.ToBoolean(betterGIJson);

        var betterGICloseJson = await _localSettingsService.ReadSettingAsync("IsBetterGICloseOnExitEnabled");
        IsBetterGICloseOnExitEnabled = betterGICloseJson != null && Convert.ToBoolean(betterGICloseJson);

        var betterGIDelayJson = await _localSettingsService.ReadSettingAsync("BetterGIStartupDelaySeconds");
        BetterGIStartupDelaySeconds = betterGIDelayJson != null
            ? Math.Clamp(Convert.ToDouble(betterGIDelayJson), 0.0, 60.0)
            : 0.0;
    }

    private Task LoadStoragePathSectionAsync()
    {
        RefreshStoragePaths();
        return Task.CompletedTask;
    }

    private async Task LoadUpdateSectionAsync()
    {
        var useThirdPartyCDNJson = await _localSettingsService.ReadSettingAsync("IsUseThirdPartyCDNEnabled");
        IsUseThirdPartyCDNEnabled = useThirdPartyCDNJson == null || Convert.ToBoolean(useThirdPartyCDNJson);

        var previewAnnouncementJson =
            await _localSettingsService.ReadSettingAsync("IsPreviewUpdateAnnouncementEnabled");
        IsPreviewUpdateAnnouncementEnabled =
            previewAnnouncementJson == null || Convert.ToBoolean(previewAnnouncementJson);

        var suppressAnnouncementJson =
            await _localSettingsService.ReadSettingAsync(LocalSettingsService.SuppressAnnouncementInGameKey);
        IsSuppressAnnouncementInGameEnabled =
            suppressAnnouncementJson == null || Convert.ToBoolean(suppressAnnouncementJson);

        var pluginMirrorJson = await _localSettingsService.ReadSettingAsync(PluginMirrorDownloadService.SettingKey);
        IsPluginMirrorAccelerationEnabled = pluginMirrorJson == null || Convert.ToBoolean(pluginMirrorJson);
    }

    private static Task LoadAboutSectionAsync() => Task.CompletedTask;

    private static Task LoadSecurityAuthSectionAsync() => Task.CompletedTask;

    partial void OnIsPluginConflictCheckEnabledChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync(PluginConflictSettings.CheckEnabledKey, value);
    }

    partial void OnIsPluginConflictMainDllOnlyChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync(PluginConflictSettings.MainDllOnlyKey, value);
    }

    partial void OnIgnoreConstraintRestrictionsChanged(bool value)
    {
        if (_isInitializing) return;
        _ = _localSettingsService.SaveSettingAsync(ConstraintService.IgnoreSettingKey, value);
        _ = App.GetService<ConstraintService>().SetIgnoreRestrictionsAsync(value);
    }

    #endregion
}
