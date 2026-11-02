/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;

namespace FufuLauncher;

public partial class App
{
    #region Startup

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            base.OnLaunched(args);
            DebugLog.Info("App.Startup", "App startup begin");

            _mainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();


            GetService<ILocalSettingsService>().StartBackgroundLoad();

            await GetService<LightweightPluginService>().InitializeAsync();
            await GetService<ConstraintService>().InitializeAsync();

            await VerifyResourceFilesAsync();

            await ApplyLanguageSettingAsync();
            await PluginContentTranslationService.Instance.InitializeAsync();

            var localUids = await LoadUidLookupAsync();
            if (await EnforceBanListAsync(localUids))
            {
                return;
            }

            if (!AppPaths.IsFirstRun)
            {
                await SetDefaultThemeAsync();
            }
            else
            {
                WeakReferenceMessenger.Default.Register<Messages.AgreementAcceptedMessage>(this, (r, m) =>
                {
                    WeakReferenceMessenger.Default.Unregister<Messages.AgreementAcceptedMessage>(r);
                    _mainDispatcherQueue.TryEnqueue(async () =>
                    {
                        await SetDefaultThemeAsync();
                        await RefreshPluginConfigBaselineAsync();
                    });
                });
            }

            var accountManager = GetService<AccountManager>();
            await accountManager.InitializeAsync();
            MainWindow = new MainWindow();
            _cpuUsageMonitor = new ProcessCpuUsageMonitor(_mainDispatcherQueue, GetService<ILocalSettingsService>());
            _cpuUsageMonitor.Start();
            if (MainWindow is MainWindow mainWindow)
            {
                await mainWindow.InitializeWindowSizeAsync();
            }

            var activationService = GetService<IActivationService>();
            await activationService.ActivateAsync(args);

            _ = GetService<PluginConfigGuardService>();

            DebugLog.Info("App.Startup", "App main window activated");

            try
            {
                var mcpHost = GetService<FufuLauncher.Services.Mcp.McpHostService>();
                _ = mcpHost.StartIfEnabledAsync();
            }
            catch (Exception ex)
            {
                DebugLog.Info("App.Startup", $"[App] MCP service start skipped: {ex.Message}");
            }

            var shouldRunBackgroundTasks = !AppPaths.IsFirstRun &&
                                           !(MainWindow is MainWindow mw && mw.IsAgreementShowing);
            if (shouldRunBackgroundTasks)
            {
                _ = Task.Run(RefreshPluginConfigBaselineAsync);

                _ = Task.Run(PlayStartupSoundDelayedAsync);

                _ = Task.Run(CheckForAnnouncementAsync);

                _ = Task.Run(async () => await GetService<ConstraintService>().RefreshAsync());

                _ = Task.Run(RunStartupUpdateCheckAsync);

                _ = Task.Run(CheckStoragePathsAsync);

                _ = Task.Run(SyncStartupRegistrationAsync);

                ProcessStartupLaunchArguments();
            }

            DebugLog.Info("App.Startup", "App startup complete");
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.Startup", $"Startup failed: {ex.Message}");
            LogException(ex, "OnLaunched (启动流程异常)");
            ShowCrashDialog("应用启动流程异常", ex);
            Environment.Exit(-1);
        }
    }

    private static async Task RefreshPluginConfigBaselineAsync()
    {
        try
        {
            await GetService<PluginConfigGuardService>().RefreshBaselineAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.Startup", $"[PluginConfigGuard] Baseline refresh failed: {ex.Message}");
        }
    }

    private async Task SetDefaultThemeAsync()
    {
        try
        {
            var localSettings = GetService<ILocalSettingsService>();
            var isThemeInitialized = await localSettings.ReadSettingAsync("IsThemeInitialized");

            if (isThemeInitialized == null)
            {
                DebugLog.Info("App.Startup", "Initializing default theme to Dark.");
                var themeService = GetService<IThemeSelectorService>();

                await themeService.SetThemeAsync(ElementTheme.Dark);

                await localSettings.SaveSettingAsync("IsThemeInitialized", true);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.Startup", $"Failed to set default theme: {ex.Message}");
        }
    }

    private static async Task SyncStartupRegistrationAsync()
    {
        try
        {
            var settingsService = GetService<ILocalSettingsService>();
            var startupJson = await settingsService.ReadSettingAsync(LocalSettingsService.IsStartupEnabledKey);
            var requestedEnabled = startupJson != null && Convert.ToBoolean(startupJson);

            if (requestedEnabled || StartupManager.IsRegistered())
            {
                StartupManager.ResolveEnabledState(requestedEnabled);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.Startup", $"[App] Failed to sync startup registry entry: {ex.Message}");
        }
    }

    private async Task VerifyResourceFilesAsync()
    {
        try
        {
            var resourceManager = new Microsoft.Windows.ApplicationModel.Resources.ResourceManager();

            var resourceMap = resourceManager.MainResourceMap;

            var resourceCandidate = resourceMap.GetValue("Resources/AppDisplayName");

            if (resourceCandidate != null)
            {
                var test = resourceCandidate.ValueAsString;
                DebugLog.Info("App.Startup", $"Resource load succeeded: {test}");
            }
            else
            {
                DebugLog.Info("App.Startup", "Warning: resource AppDisplayName not found");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.Startup", $"Resource load failed critically: {ex.Message}");
        }
    }

    private async Task ApplyLanguageSettingAsync()
    {
        try
        {
            var localSettingsService = GetService<ILocalSettingsService>();
            var languageValue = await localSettingsService.ReadSettingAsync("AppLanguage");

            DebugLog.Info("App.Startup",
                $"[App] ApplyLanguageSettingAsync: raw value='{languageValue}' (type={languageValue?.GetType().Name ?? "null"})");

            var language = languageValue != null && int.TryParse(languageValue.ToString(), out var languageCode)
                ? (AppLanguage)languageCode
                : AppLanguage.Default;
            var culture = LanguagePreferenceResolver.Resolve(
                language,
                Windows.System.UserProfile.GlobalizationPreferences.Languages);

            DebugLog.Info("App.Startup", $"[App] ApplyLanguageSettingAsync: language={language}, culture='{culture}'");
            ResourceExtensions.SetLanguage(culture);
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.Startup", $"[App] ApplyLanguageSettingAsync ERROR: {ex.Message}");
        }
    }

    private void ApplyLanguageSetting()
    {
        try
        {
            var localSettingsService = GetService<ILocalSettingsService>();
            var languageValue = localSettingsService.ReadSettingAsync("AppLanguage").Result;

            if (languageValue != null)
            {
                if (!int.TryParse(languageValue.ToString(), out var languageCode))
                    return;

                var culture = LanguagePreferenceResolver.Resolve(
                    (AppLanguage)languageCode,
                    Windows.System.UserProfile.GlobalizationPreferences.Languages);
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = culture;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.Startup", $"Failed to apply language setting: {ex.Message}");
        }
    }

    #endregion
}