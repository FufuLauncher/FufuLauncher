using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using FufuLauncher.Core.Contracts.Services;
using FufuLauncher.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Windows.Media.Playback;
using Windows.Media.Core;
using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using FufuLauncher.ViewModels;
using FufuLauncher.Views;

namespace FufuLauncher;

public partial class App : Application
{
    public IHost Host { get; }

    public static T GetService<T>()
        where T : class
    {
        if ((App.Current as App)!.Host.Services.GetService(typeof(T)) is not T service)
        {
            throw new ArgumentException($"{typeof(T)} needs to be registered in ConfigureServices within App.xaml.cs.");
        }

        return service;
    }

    public static WindowEx MainWindow { get; } = new MainWindow();

    public static UIElement? AppTitlebar { get; set; }
    private static Microsoft.UI.Dispatching.DispatcherQueue _mainDispatcherQueue;
    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        UnhandledException += App_UnhandledException;

        InitializeComponent();

        Host = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
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
                services.AddSingleton<BlankViewModel>();
                services.AddTransient<BlankPage>();
                services.AddSingleton<ILauncherService, LauncherService>();
                services.AddTransient<OtherViewModel>();
                services.AddTransient<OtherPage>();
                services.AddSingleton<IAutoClickerService, AutoClickerService>();
                services.AddTransient<AgreementViewModel>();
                services.AddTransient<AgreementPage>();
                services.AddSingleton<IUpdateService, UpdateService>();
                services.AddSingleton<IUserConfigService, UserConfigService>();

                services.AddSingleton<IUserConfigService, UserConfigService>();
                services.AddSingleton<ControlPanelModel>();
                services.AddTransient<PanelPage>();
                services.AddSingleton<Contracts.Services.IUserInfoService, UserInfoService>();

                services.AddLogging(builder =>
                {
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
                services.Configure<LocalSettingsOptions>(context.Configuration.GetSection(nameof(LocalSettingsOptions)));
            })
            .Build();
            
        CleanupOldSettings();
    }

    private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        LogException(e.ExceptionObject as Exception, "CurrentDomain_UnhandledException");
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

        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        LogException(e.Exception, "App_UnhandledException");
        e.Handled = true;
    }

    private void LogException(Exception? ex, string source)
    {
        if (ex == null) return;

        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher", "CrashLog.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            
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

            await VerifyResourceFilesAsync();
            await ApplyLanguageSettingAsync();

            var activationService = GetService<IActivationService>();
            await activationService.ActivateAsync(args);

            Debug.WriteLine("=== App 主窗口已激活 ===");
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

            Debug.WriteLine("=== App 启动完成 ===");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"启动失败: {ex.Message}");
            MainWindow.Activate();
        }
    }
    private async Task PlayStartupSoundAsync()
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
                mediaPlayer.Source = MediaSource.CreateFromUri(new Uri(path));
                mediaPlayer.Volume = 0.7;

                mediaPlayer.MediaEnded += (s, e) => mediaPlayer.Dispose();

                mediaPlayer.MediaFailed += (s, e) =>
                {
                    Debug.WriteLine($"启动语音播放失败: {e.ErrorMessage}");
                    mediaPlayer.Dispose();
                };
                
                mediaPlayer.Play();

                var timer = _mainDispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(30);
                timer.Tick += (s, e) =>
                {
                    try
                    {
                        mediaPlayer?.Dispose();
                    }
                    catch { }
                    timer.Stop();
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

            var loader = Windows.ApplicationModel.Resources.ResourceLoader.GetForViewIndependentUse();
            var test = loader.GetString("AppDisplayName");
            Debug.WriteLine($"资源加载测试: {test}");
        
            if (string.IsNullOrEmpty(test))
            {
                Debug.WriteLine("⚠️ 警告：资源文件未找到或为空！");
            }
            else
            {
                Debug.WriteLine("✅ 资源文件加载成功");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"资源验证失败: {ex.Message}");
        }
    }
    private async Task ApplyLanguageSettingAsync()
    {
        try
        {
            var localSettingsService = GetService<ILocalSettingsService>();
            var languageValue = await localSettingsService.ReadSettingAsync("AppLanguage");
        
            if (languageValue != null && int.TryParse(languageValue.ToString(), out int languageCode))
            {
                var language = (AppLanguage)languageCode;
                string culture = language switch
                {
                    AppLanguage.zhCN => "zh-CN",
                    AppLanguage.zhTW => "zh-TW",
                    _ => "zh-CN"
                };
            
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = culture;
            }
        }
        catch { }
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