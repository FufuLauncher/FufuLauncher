/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using FufuLauncher.Activation;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.ViewModels;
using FufuLauncher.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Media.Core;
using Windows.Media.Playback;
using System.Net.Sockets;
using Sentry;

namespace FufuLauncher;

public partial class App : Application
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;
    public IHost Host
    {
        get;
    }
    
    private void ShowCrashDialog(string source, Exception? ex)
    {
        if (ex == null) return;

        SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).Wait();

        var message = string.Format("Crash_Message".GetLocalized(), source, ex.Message, ex.StackTrace);
        
        MessageBox(IntPtr.Zero, message, "Crash_Title".GetLocalized(), MB_OK | MB_ICONERROR);
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

    public static WindowEx MainWindow { get; private set; }

    /// <summary>
    /// Tracks the language selected on the first-run language selection page,
    /// so the AgreementPage can show the appropriate agreement text.
    /// </summary>
    public static ViewModels.AppLanguage? FirstRunSelectedLanguage { get; set; }

    public static UIElement? AppTitlebar
    {
        get; set;
    }
    private static Microsoft.UI.Dispatching.DispatcherQueue _mainDispatcherQueue = null!;
    private ProcessCpuUsageMonitor? _cpuUsageMonitor;
    public App()
    {
        Helpers.AppPaths.EnsureDirectories();

        string userDataFolder = Path.Combine(Helpers.AppPaths.CacheDir, "WebView2Data");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userDataFolder);

        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        UnhandledException += App_UnhandledException;
        
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        InitializeComponent();

        var appInstance = AppInstance.GetCurrent();
        appInstance.Activated += App_Activated!;

        try
        {
            Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
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

                    services.AddSingleton<ILocalSettingsService, LocalSettingsService>();
                    services.AddSingleton<IThemeSelectorService, ThemeSelectorService>();

                    services.AddSingleton<IHoyoverseBackgroundService, HoyoverseBackgroundService>();
                    services.AddSingleton<IHoyoverseContentService, HoyoverseContentService>();
                    services.AddSingleton<IBackgroundRenderer, BackgroundRenderer>();

                    services.AddSingleton<IActivationService, ActivationService>();
                    services.AddSingleton<IPageService, PageService>();
                    services.AddSingleton<INavigationService, NavigationService>();
                    services.AddSingleton<IFileService, FileService>();

                    services.AddSingleton<MainViewModel>();
                    services.AddTransient<MainPage>();
                    
                    services.AddTransient<DataViewModel>();
                    services.AddTransient<DataPage>();
                    
                    services.AddTransient<SettingsViewModel>();
                    services.AddTransient<SettingsPage>();
                    services.AddTransient<BlankPage>();

                    services.AddTransient<NullToVisibilityConverter>();
                    services.AddTransient<BoolToVisibilityConverter>();
                    services.AddTransient<BoolToGlyphConverter>();
                    services.AddTransient<IntToVisibilityConverter>();

                    services.AddTransient<AccountViewModel>();
                    services.AddTransient<AccountPage>();

                    services.AddSingleton<IGameLauncherService, GameLauncherService>();
                    services.AddSingleton<IGameConfigService, GameConfigService>();

                    services.AddSingleton<IHoyoverseCheckinService, HoyoverseCheckinService>();
                    services.AddSingleton<ICommunityCheckinService, CommunityCheckinService>();
                    services.AddSingleton<ICloudGameCheckinService, CloudGameCheckinService>();
                    services.AddSingleton<IHoyolabRoleResolverService, HoyolabRoleResolverService>();
                    services.AddSingleton<IUnifiedCheckinService, UnifiedCheckinService>();
                    services.AddSingleton<DailyNoteCardService>();
                    services.AddSingleton<IDeviceFingerprintService, Services.MiHoYo.DeviceFingerprintService>();
                    services.AddSingleton<BlankViewModel>();
                    services.AddTransient<BlankPage>();
                    services.AddSingleton<ILauncherService, LauncherService>();
                    services.AddTransient<OtherViewModel>();
                    services.AddTransient<OtherPage>();
                    services.AddSingleton<IAutoClickerService, AutoClickerService>();
                    services.AddSingleton<IScreenshotService, ScreenshotService>();
                    services.AddTransient<LanguageSelectionViewModel>();
                    services.AddTransient<LanguageSelectionPage>();
                    services.AddTransient<AgreementViewModel>();
                    services.AddTransient<AgreementPage>();
                    services.AddSingleton<IUpdateService, UpdateService>();
                    services.AddSingleton<ControlPanelModel>();
                    services.AddTransient<PanelPage>();
                    services.AddSingleton<IUserInfoService, UserInfoService>();

                    services.AddSingleton<AccountManager>();

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
                    services.AddTransient<CalculatorViewModel>();
                    services.AddTransient<CalculatorPage>();
                    services.AddTransient<PluginViewModel>();
                    services.AddTransient<PluginPage>();
                    services.AddTransient<GachaViewModel>();
                    services.AddSingleton<GachaService>();
                    services.AddSingleton<IAnnouncementService, AnnouncementService>();
                    services.AddTransient<IPluginUpdateService, PluginUpdateService>();
                    services.AddTransient<GachaAnalysisModel>();
                    services.AddTransient<CommunityViewModel>();
                    services.AddTransient<CommunityPage>();
                    services.AddSingleton<Services.PluginStoreService>();
                    services.AddSingleton<Services.LuaPluginInstaller>();
                    services.AddTransient<ViewModels.PluginStoreViewModel>();
                    services.AddTransient<Views.PluginStorePage>();

                    services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
                })
                .Build();

            CleanupOldSettings();
        }
        catch (Exception ex)
        {
            LogException(ex, "App Constructor (核心配置初始化异常)");
            ShowCrashDialog("核心配置及服务初始化异常", ex);
            Environment.Exit(-1);
        }
    }
    
    private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        
        var baseEx = e.Exception?.GetBaseException();
        if (baseEx is SocketException
            || baseEx is ObjectDisposedException
            || baseEx is OperationCanceledException)
        {
            System.Diagnostics.Debug.WriteLine($"[UnobservedTask] 已忽略的后台异常: {baseEx.GetType().Name}: {baseEx.Message}");
            return;
        }

        LogException(e.Exception, "UnobservedTaskException");
        ShowCrashDialog("后台异步任务异常", e.Exception);
    }
    
    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        LogException(e.Exception, "App_UnhandledException");
        ShowCrashDialog("UI 界面交互异常", e.Exception);
        Environment.Exit(-1);
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        LogException(ex, "CurrentDomain_UnhandledException");
        ShowCrashDialog("应用程序域致命异常", ex);
        Environment.Exit(-1);
    }

    private void App_Activated(object sender, AppActivationArguments e)
    {
        _mainDispatcherQueue?.TryEnqueue(() =>
        {
            MainWindow.Activate();
        });
    }

    private void LaunchLocalUpdater()
    {
        try
        {
            var mainExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(mainExePath))
            {
                Debug.WriteLine("[Updater] 无法获取主程序路径，更新程序启动中止");
                return;
            }
            
            var currentPid = Process.GetCurrentProcess().Id;
            
            var baseDirectory = Path.GetDirectoryName(mainExePath);
            if (string.IsNullOrEmpty(baseDirectory)) return;
            
            var updaterPath = Path.Combine(baseDirectory, "update.exe");

            if (File.Exists(updaterPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = $"\"{mainExePath}\" {currentPid}", 
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process.Start(psi);
                Debug.WriteLine($"[Updater] 已成功启动更新程序，路径: {updaterPath}");
            }
            else
            {
                Debug.WriteLine($"[Updater] 找不到更新程序，预期路径: {updaterPath}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Updater] 启动更新程序失败: {ex.Message}");
            LogException(ex, "LaunchLocalUpdater");
        }
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
                    Debug.WriteLine("清理了旧的无效设置文件");
                }
            }
        }
        catch
        {
            // ignored
        }
    }

    private void LogException(Exception? ex, string source)
    {
        if (ex == null) return;

        try
        {
            SentrySdk.CaptureException(ex, scope => 
            {
                scope.SetTag("source", source);
            });

            var logPath = Path.Combine(Helpers.AppPaths.RootDir, "CrashLog.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

            var log = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n" +
                     $"Exception: {ex.GetType().Name}\n" +
                     $"Message: {ex.Message}\n" +
                     $"StackTrace: {ex.StackTrace}\n" +
                     new string('-', 80) + "\n";

            File.AppendAllText(logPath, log);
        }
        catch
        {

        }
    }

    protected async override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            base.OnLaunched(args);
            Debug.WriteLine("=== App 启动开始 ===");

            _mainDispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            _ = Task.Run(() => LaunchLocalUpdater());

            await VerifyResourceFilesAsync();

            if (!Helpers.AppPaths.IsFirstRun)
            {
                await ApplyLanguageSettingAsync();
                await SetDefaultThemeAsync();
            }
            else
            {
                // On first run, apply system default language so the language
                // selection page shows in the user's OS language.
                await ApplyLanguageSettingAsync();

                WeakReferenceMessenger.Default.Register<Messages.AgreementAcceptedMessage>(this, (r, m) =>
                {
                    WeakReferenceMessenger.Default.Unregister<Messages.AgreementAcceptedMessage>(r);
                    _mainDispatcherQueue.TryEnqueue(async () =>
                    {
                        await SetDefaultThemeAsync();
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

            Debug.WriteLine("=== App 主窗口已激活 ===");
            var shouldRunBackgroundTasks = !Helpers.AppPaths.IsFirstRun && 
                !(MainWindow is MainWindow mw && mw.IsAgreementShowing);
            if (shouldRunBackgroundTasks)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(800);
                        await PlayStartupSoundAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"启动语音播放失败: {ex.Message}");
                    }
                });

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1500);

                        var announcementService = GetService<IAnnouncementService>();
                        var announcementUrl = await announcementService.CheckForNewAnnouncementAsync();

                        if (!string.IsNullOrEmpty(announcementUrl))
                        {
                            await _mainDispatcherQueue.EnqueueAsync(() =>
                            {
                                var announcementWindow = new AnnouncementWindowL(announcementUrl);
                                announcementWindow.Activate();
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Announcement] 公告检查失败: {ex.Message}");
                    }
                });

                _ = Task.Run(async () =>
                {
                    try
                    {
                        Debug.WriteLine("[Background] 后台任务开始，等待主窗口渲染...");
                        await Task.Delay(500);

                        Debug.WriteLine("[Background] 准备调度到UI线程...");

                        await _mainDispatcherQueue.EnqueueAsync(async () =>
                        {
                            Debug.WriteLine("[Background] 已在UI线程，执行更新检查...");
                            await CheckAndShowUpdateWindowAsync();
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Background] 后台更新检查失败: {ex.Message}");
                        Debug.WriteLine($"[Background] 异常类型: {ex.GetType().FullName}");
                        Debug.WriteLine($"[Background] 堆栈: {ex.StackTrace}");
                    }
                });
            }

            Debug.WriteLine("=== App 启动完成 ===");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动失败: {ex.Message}");
            LogException(ex, "OnLaunched (启动流程异常)");
            ShowCrashDialog("应用启动流程异常", ex);
            Environment.Exit(-1);
        }
    }
    
    private async Task SetDefaultThemeAsync()
    {
        try
        {
            var localSettings = GetService<ILocalSettingsService>();
            // Check if we have already initialized the theme preference
            var isThemeInitialized = await localSettings.ReadSettingAsync("IsThemeInitialized");

            // If null, this is the first run (or first run after this update)
            if (isThemeInitialized == null)
            {
                Debug.WriteLine("Initializing default theme to Dark.");
                var themeService = GetService<IThemeSelectorService>();

                // Force the theme service to set Dark mode
                await themeService.SetThemeAsync(ElementTheme.Dark);

                // Mark as initialized so we don't overwrite user preference later
                await localSettings.SaveSettingAsync("IsThemeInitialized", true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set default theme: {ex.Message}");
        }
    }

    internal async Task PlayStartupSoundAsync()
    {
        try
        {
            var localSettingsService = GetService<ILocalSettingsService>();

            var soundEnabled = await localSettingsService.ReadSettingAsync("IsStartupSoundEnabled");
            bool isSoundEnabled = soundEnabled != null && Convert.ToBoolean(soundEnabled);

            if (!isSoundEnabled) return;

            var soundPath = await localSettingsService.ReadSettingAsync("StartupSoundPath");
            if (soundPath == null || string.IsNullOrEmpty(soundPath.ToString())) return;

            string path = soundPath.ToString();
            if (!File.Exists(path))
            {
                Debug.WriteLine($"启动语音文件不存在: {path}");
                return;
            }

            await _mainDispatcherQueue.EnqueueAsync(() =>
            {
                try
                {
                    var mediaPlayer = new MediaPlayer();
                    MediaPlayerHelper.DisableSystemMediaControls(mediaPlayer);
                    mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(path));
                    mediaPlayer.Volume = 0.7;

                    int disposed = 0;
                    void DisposeOnce()
                    {
                        if (Interlocked.Exchange(ref disposed, 1) == 0)
                        {
                            try { mediaPlayer.Dispose(); } catch { }
                        }
                    }

                    mediaPlayer.MediaEnded += (s, e) => DisposeOnce();
                    mediaPlayer.MediaFailed += (s, e) => DisposeOnce();
                    mediaPlayer.Play();

                    var timer = _mainDispatcherQueue.CreateTimer();
                    timer.Interval = TimeSpan.FromSeconds(30);
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        DisposeOnce();
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"启动语音播放异常: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动语音处理失败: {ex.Message}");
        }
    }
    private async Task CheckAndShowUpdateWindowAsync()
    {
        try
        {
            var updateService = GetService<IUpdateService>();
            var result = await updateService.CheckUpdateAsync();

            if (result.ShouldShowUpdate)
            {
                Debug.WriteLine($"准备显示更新窗口，版本: {result.ServerVersion}");
                Debug.WriteLine($"[App] 动态更新公告URL: {result.UpdateInfoUrl}");

                MainWindow.Activate();

                var updateWindow = new Views.UpdateNotificationWindow(result.UpdateInfoUrl);
                updateWindow.Title = $"版本更新公告 - v{result.ServerVersion}";
                updateWindow.Activate();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"更新检查失败: {ex.Message}");
            Debug.WriteLine($"[App] 异常详情: {ex.StackTrace}");
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
                Debug.WriteLine($"资源加载成功: {test}");
            }
            else
            {
                Debug.WriteLine("警告: 找不到资源 AppDisplayName");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"资源加载严重失败: {ex.Message}");
        }
    }
    private async Task ApplyLanguageSettingAsync()
    {
        try
        {
            var localSettingsService = GetService<ILocalSettingsService>();
            var languageValue = await localSettingsService.ReadSettingAsync("AppLanguage");

            Debug.WriteLine($"[App] ApplyLanguageSettingAsync: raw value='{languageValue}' (type={languageValue?.GetType().Name ?? "null"})");

            if (languageValue != null && int.TryParse(languageValue.ToString(), out int languageCode))
            {
                var language = (AppLanguage)languageCode;
                string culture = language switch
                {
                    AppLanguage.zhCN => "zh-CN",
                    AppLanguage.zhTW => "zh-TW",
                    AppLanguage.enUS => "en-US",
                    AppLanguage.fr => "fr-FR",
                    AppLanguage.de => "de-DE",
                    AppLanguage.ru => "ru-RU",
                    AppLanguage.ja => "ja-JP",
                    AppLanguage.es => "es-ES",
                    AppLanguage.esMX => "es-MX",
                    AppLanguage.ko => "ko-KR",
                    AppLanguage.it => "it-IT",
                    AppLanguage.id => "id-ID",
                    AppLanguage.pt => "pt-BR",
                    _ => Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault() ?? "zh-CN"
                };

                Debug.WriteLine($"[App] ApplyLanguageSettingAsync: language={language}, culture='{culture}'");

                // PrimaryLanguageOverride is NOT available for unpackaged apps.
                // Use ResourceExtensions.SetLanguage to control MRT via ResourceContext instead.
                Helpers.ResourceExtensions.SetLanguage(
                    language == AppLanguage.Default ? null : culture);
            }
            else
            {
                Debug.WriteLine($"[App] ApplyLanguageSettingAsync: skipped (languageValue is null or unparseable)");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[App] ApplyLanguageSettingAsync ERROR: {ex.Message}");
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
                var languageCode = JsonSerializer.Deserialize<int>(languageValue.ToString() ?? string.Empty);
                var language = (AppLanguage)languageCode;

                var culture = language switch
                {
                    AppLanguage.zhCN => "zh-CN",
                    AppLanguage.zhTW => "zh-TW",
                    AppLanguage.enUS => "en-US",
                    AppLanguage.fr => "fr-FR",
                    AppLanguage.de => "de-DE",
                    AppLanguage.ru => "ru-RU",
                    AppLanguage.ja => "ja-JP",
                    AppLanguage.es => "es-ES",
                    AppLanguage.esMX => "es-MX",
                    AppLanguage.ko => "ko-KR",
                    AppLanguage.it => "it-IT",
                    AppLanguage.id => "id-ID",
                    AppLanguage.pt => "pt-BR",
                    _ => Windows.System.UserProfile.GlobalizationPreferences.Languages.FirstOrDefault() ?? "zh-CN"
                };

                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = culture;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"应用语言设置失败: {ex.Message}");
        }
    }
}

