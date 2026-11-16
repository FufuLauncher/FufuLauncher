/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Services.Background;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using QuestPDF.Infrastructure;

namespace FufuLauncher;

public partial class App : Application
{
    #region Core

    public IHost Host
    {
        get;
    }

    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static WindowEx MainWindow
    {
        get;
        private set;
    }


    public static ViewModels.AppLanguage? FirstRunSelectedLanguage
    {
        get;
        set;
    }

    public static UIElement? AppTitlebar
    {
        get;
        set;
    }

    private static Microsoft.UI.Dispatching.DispatcherQueue _mainDispatcherQueue = null!;
    private ProcessCpuUsageMonitor? _cpuUsageMonitor;

    public App()
    {
        Helpers.AppPaths.EnsureDirectories();
        QuestPDF.Settings.License = LicenseType.Community;

        string userDataFolder = Path.Combine(Helpers.AppPaths.CacheDir, "WebView2Data");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userDataFolder);

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        UnhandledException += App_UnhandledException;

        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        InitializeComponent();


        try
        {
            string oldPri = Path.Combine(AppContext.BaseDirectory, "resources.pri");
            if (File.Exists(oldPri))
            {
                File.Delete(oldPri);
                DebugLog.Info("App", "[App] Removed leftover resources.pri");
            }
        }
        catch
        {
        }

        var appInstance = AppInstance.GetCurrent();
        appInstance.Activated += App_Activated!;

        try
        {
            Host = CreateHost();

            CleanupOldSettings();

            Services.GameServer.GameServerCacheMaintenance.CleanLegacyCaches();
        }
        catch (Exception ex)
        {
            LogException(ex, "App Constructor");
            ShowCrashDialog("Crash_Source_CoreInit".GetLocalized(), ex);
            Environment.Exit(-1);
        }
    }

    private void App_Activated(object sender, AppActivationArguments e)
    {
        var trigger = ParseRedirectedTrigger(e);
        _mainDispatcherQueue?.TryEnqueue(() =>
        {
            MainWindow?.Activate();
            if (trigger != LaunchTriggerAction.None)
            {
                _ = TriggerGameUpdateOperationAsync(trigger);
            }
        });
    }

    private void CleanupOldSettings()
    {
        try
        {
            var filePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FufuLauncher", "ApplicationData", "LocalSettings.json"
            );

            if (File.Exists(filePath))
            {
                var content = File.ReadAllText(filePath);
                if (content.Contains("System.Private.CoreLib") || content.Contains("True") || content.Contains("False"))
                {
                    File.Delete(filePath);
                    DebugLog.Info("App", "Cleaned up old invalid settings file");
                }
            }
        }
        catch
        {
        }
    }

    #endregion
}