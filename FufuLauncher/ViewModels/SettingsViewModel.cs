using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel;
using Windows.Storage;

namespace FufuLauncher.ViewModels
{
    public enum AppLanguage
    {
        Default = 0,
        zhCN = 1,
        zhTW = 2
    }

    public enum WindowModeType
    {
        Normal,
        Popup
    }

    public partial class SettingsViewModel : ObservableRecipient
    {
        private readonly IThemeSelectorService _themeSelectorService;
        private readonly IBackgroundRenderer _backgroundRenderer;
        private readonly ILocalSettingsService _localSettingsService;
        private readonly INavigationService _navigationService;
        private readonly IGameLauncherService _gameLauncherService;
        private readonly IFilePickerService _filePickerService;

        [ObservableProperty] private ElementTheme _elementTheme;
        [ObservableProperty] private string _versionDescription;
        [ObservableProperty] private ServerType _selectedServer;
        [ObservableProperty] private bool _isBackgroundEnabled = true;
        [ObservableProperty] private AppLanguage _selectedLanguage;
        [ObservableProperty] private bool _minimizeToTray;
        [ObservableProperty] private string _customLaunchParameters = "";
        [ObservableProperty] private WindowModeType _launchArgsWindowMode = WindowModeType.Normal;
        [ObservableProperty] private string _launchArgsWidth = "1920";
        [ObservableProperty] private string _launchArgsHeight = "1080";
        [ObservableProperty] private string _launchArgsPreview = "";
        [ObservableProperty] private string _customBackgroundPath;
        [ObservableProperty] private bool _hasCustomBackground;
        [ObservableProperty]
        private string _backgroundCacheFolderPath;
        [ObservableProperty]
        private bool _isShortTermSupportEnabled;
        [ObservableProperty]
        private bool _isBetterGIIntegrationEnabled;
        [ObservableProperty]
        private bool _isBetterGICloseOnExitEnabled;

        public IAsyncRelayCommand OpenBackgroundCacheFolderCommand
        {
            get;
        }

        [ObservableProperty] private bool _isAcrylicEnabled = true;
        public ICommand SwitchThemeCommand
        {
            get;
        }
        public ICommand SwitchLanguageCommand
        {
            get;
        }
        public ICommand SetResolutionPresetCommand
        {
            get;
        }
        public IAsyncRelayCommand SelectCustomBackgroundCommand
        {
            get;
        }
        public IAsyncRelayCommand ClearCustomBackgroundCommand
        {
            get;
        }
        private bool _isInitializing = false;

        [ObservableProperty] private bool _isStartupSoundEnabled;
        [ObservableProperty] private string _startupSoundPath;
        [ObservableProperty] private bool _hasCustomStartupSound;

        public IAsyncRelayCommand SelectStartupSoundCommand
        {
            get;
        }
        public IAsyncRelayCommand ClearStartupSoundCommand
        {
            get;
        }
        public SettingsViewModel(
            IThemeSelectorService themeSelectorService,
            IBackgroundRenderer backgroundRenderer,
            ILocalSettingsService localSettingsService,
            INavigationService navigationService,
            IGameLauncherService gameLauncherService,
            IFilePickerService filePickerService)
        {
            _themeSelectorService = themeSelectorService;
            _backgroundRenderer = backgroundRenderer;
            _localSettingsService = localSettingsService;
            _navigationService = navigationService;
            _gameLauncherService = gameLauncherService;
            _filePickerService = filePickerService;
            SelectStartupSoundCommand = new AsyncRelayCommand(SelectStartupSoundAsync);
            ClearStartupSoundCommand = new AsyncRelayCommand(ClearStartupSound);
            ElementTheme = _themeSelectorService.Theme;
            _versionDescription = GetVersionDescription();

            SwitchThemeCommand = new RelayCommand<ElementTheme>(
                async (param) =>
                {
                    if (ElementTheme != param)
                    {
                        ElementTheme = param;
                        await _themeSelectorService.SetThemeAsync(param);
                    }
                });

            SwitchLanguageCommand = new RelayCommand<object>(
                async (param) =>
                {
                    try
                    {
                        int languageCode = System.Convert.ToInt32(param);
                        var language = (AppLanguage)languageCode;

                        if (SelectedLanguage != language)
                        {
                            SelectedLanguage = language;
                            await ApplyLanguageChangeAsync(language);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"语言切换失败: {ex.Message}");
                    }
                });

            SetResolutionPresetCommand = new RelayCommand<string>(
                (param) =>
                {
                    var parts = param.Split(' ');
                    if (parts.Length == 2)
                    {
                        LaunchArgsWidth = parts[0];
                        LaunchArgsHeight = parts[1];
                    }
                });

            SelectCustomBackgroundCommand = new AsyncRelayCommand(SelectCustomBackgroundAsync);
            ClearCustomBackgroundCommand = new AsyncRelayCommand(ClearCustomBackground);
            OpenBackgroundCacheFolderCommand = new AsyncRelayCommand(OpenBackgroundCacheFolderAsync);
        }
        private async Task LoadBackgroundCachePathAsync()
        {
            try
            {
                var localCacheFolder = ApplicationData.Current.LocalCacheFolder;
                var backgroundCacheFolder = await localCacheFolder.CreateFolderAsync("BackgroundCache", CreationCollisionOption.OpenIfExists);
                BackgroundCacheFolderPath = backgroundCacheFolder.Path;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载缓存路径失败: {ex.Message}");
                BackgroundCacheFolderPath = "无法获取路径";
            }
        }

        private async Task OpenBackgroundCacheFolderAsync()
        {
            try
            {
                var localCacheFolder = ApplicationData.Current.LocalCacheFolder;
                var backgroundCacheFolder = await localCacheFolder.CreateFolderAsync("BackgroundCache", CreationCollisionOption.OpenIfExists);

                await backgroundCacheFolder.GetFilesAsync();

                Process.Start(new ProcessStartInfo
                {
                    FileName = backgroundCacheFolder.Path,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开缓存文件夹失败: {ex.Message}");

                var dialog = new ContentDialog
                {
                    Title = "无法打开文件夹",
                    Content = $"错误: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }

        public async Task ReloadSettingsAsync()
        {
            _isInitializing = true;

            await LoadUserPreferencesAsync();
            await LoadCustomBackgroundSettingsAsync();
            await LoadBackgroundCachePathAsync();
            UpdateLaunchArgsPreview();
            OnPropertyChanged(nameof(IsStartupSoundEnabled));
            OnPropertyChanged(nameof(StartupSoundPath));
            OnPropertyChanged(nameof(HasCustomStartupSound));

            OnPropertyChanged(nameof(ElementTheme));
            OnPropertyChanged(nameof(SelectedServer));
            OnPropertyChanged(nameof(IsBackgroundEnabled));
            OnPropertyChanged(nameof(SelectedLanguage));
            OnPropertyChanged(nameof(MinimizeToTray));
            OnPropertyChanged(nameof(CustomLaunchParameters));
            OnPropertyChanged(nameof(LaunchArgsWindowMode));
            OnPropertyChanged(nameof(LaunchArgsWidth));
            OnPropertyChanged(nameof(LaunchArgsHeight));
            OnPropertyChanged(nameof(CustomBackgroundPath));
            OnPropertyChanged(nameof(HasCustomBackground));
            OnPropertyChanged(nameof(IsAcrylicEnabled));
            OnPropertyChanged(nameof(IsShortTermSupportEnabled));
            OnPropertyChanged(nameof(IsBetterGIIntegrationEnabled));
            OnPropertyChanged(nameof(IsBetterGICloseOnExitEnabled));

            _isInitializing = false;
        }

        private async Task LoadUserPreferencesAsync()
        {
            var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            int serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
            SelectedServer = (ServerType)serverValue;

            var enabledJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.IsBackgroundEnabledKey);
            IsBackgroundEnabled = enabledJson == null ? true : Convert.ToBoolean(enabledJson);

            var languageJson = await _localSettingsService.ReadSettingAsync("AppLanguage");
            int languageValue = languageJson != null ? Convert.ToInt32(languageJson) : 0;
            SelectedLanguage = (AppLanguage)languageValue;

            var trayJson = await _localSettingsService.ReadSettingAsync("MinimizeToTray");
            MinimizeToTray = trayJson != null && Convert.ToBoolean(trayJson);

            var paramsJson = await _localSettingsService.ReadSettingAsync("CustomLaunchParameters");
            if (paramsJson != null)
            {
                CustomLaunchParameters = paramsJson.ToString();
                ParseLaunchParameters(CustomLaunchParameters);
            }

            var acrylicJson = await _localSettingsService.ReadSettingAsync("IsAcrylicEnabled");
            IsAcrylicEnabled = acrylicJson == null ? true : Convert.ToBoolean(acrylicJson);

            var shortTermJson = await _localSettingsService.ReadSettingAsync("IsShortTermSupportEnabled");
            IsShortTermSupportEnabled = shortTermJson != null && Convert.ToBoolean(shortTermJson);

            var betterGIJson = await _localSettingsService.ReadSettingAsync("IsBetterGIIntegrationEnabled");
            IsBetterGIIntegrationEnabled = betterGIJson != null && Convert.ToBoolean(betterGIJson);

            var betterGICloseJson = await _localSettingsService.ReadSettingAsync("IsBetterGICloseOnExitEnabled");
            IsBetterGICloseOnExitEnabled = betterGICloseJson != null && Convert.ToBoolean(betterGICloseJson);

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
        private async Task SelectStartupSoundAsync()
        {
            try
            {
                var path = await _filePickerService.PickAudioFileAsync();
                if (!string.IsNullOrEmpty(path))
                {
                    StartupSoundPath = path;
                    HasCustomStartupSound = true;

                    await _localSettingsService.SaveSettingAsync<string>("StartupSoundPath", path);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择启动语音失败: {ex.Message}");
            }
        }

        private async Task ClearStartupSound()
        {
            try
            {

                await _localSettingsService.SaveSettingAsync<string>("StartupSoundPath", null);
                StartupSoundPath = null;
                HasCustomStartupSound = false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除启动语音失败: {ex.Message}");
            }
        }

        partial void OnIsStartupSoundEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存启动语音开关 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsStartupSoundEnabled", value);
        }
        private async Task LoadCustomBackgroundSettingsAsync()
        {
            var path = await _localSettingsService.ReadSettingAsync("CustomBackgroundPath");
            if (path != null)
            {
                CustomBackgroundPath = path.ToString();
                HasCustomBackground = File.Exists(CustomBackgroundPath);
            }
            else
            {
                CustomBackgroundPath = null;
                HasCustomBackground = false;
            }
        }

        private void ParseLaunchParameters(string args)
        {
            if (string.IsNullOrWhiteSpace(args)) return;

            try
            {
                args = args.Trim('"').Trim();

                if (args.Contains("-popupwindow"))
                {
                    LaunchArgsWindowMode = WindowModeType.Popup;
                }
                else
                {
                    LaunchArgsWindowMode = WindowModeType.Normal;
                }

                var parts = args.Split(' ');
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (parts[i] == "-screen-width" && int.TryParse(parts[i + 1], out var width))
                    {
                        LaunchArgsWidth = width.ToString();
                    }
                    else if (parts[i] == "-screen-height" && int.TryParse(parts[i + 1], out var height))
                    {
                        LaunchArgsHeight = height.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"解析启动参数失败: {ex.Message}");
            }
        }

        private string BuildLaunchArgsString()
        {
            var args = new System.Text.StringBuilder();

            if (LaunchArgsWindowMode == WindowModeType.Popup)
            {
                args.Append("-popupwindow ");
            }

            if (!string.IsNullOrWhiteSpace(LaunchArgsWidth) && !string.IsNullOrWhiteSpace(LaunchArgsHeight))
            {
                args.Append($"-screen-width {LaunchArgsWidth} ");
                args.Append($"-screen-height {LaunchArgsHeight} ");
            }

            return args.ToString().Trim();
        }

        private void UpdateLaunchArgsPreview()
        {
            LaunchArgsPreview = BuildLaunchArgsString();
            CustomLaunchParameters = LaunchArgsPreview;
        }

        partial void OnLaunchArgsWindowModeChanged(WindowModeType value)
        {
            UpdateLaunchArgsPreview();
        }

        partial void OnLaunchArgsWidthChanged(string value)
        {
            UpdateLaunchArgsPreview();
        }

        partial void OnLaunchArgsHeightChanged(string value)
        {
            UpdateLaunchArgsPreview();
        }

        private async Task ApplyLanguageChangeAsync(AppLanguage language)
        {
            try
            {
                await _localSettingsService.SaveSettingAsync("AppLanguage", (int)language);
                var culture = language == AppLanguage.zhCN ? "zh-CN" : "zh-TW";
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = culture;

                var dialog = new ContentDialog
                {
                    Title = "语言已更改",
                    Content = "语言设置已更改。由于技术限制，部分UI需要重启才能完全生效。",
                    PrimaryButtonText = "立即重启",
                    CloseButtonText = "稍后手动重启",
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };

                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    RestartApp();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"应用语言失败: {ex.Message}");
            }
        }

        private void RestartApp()
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Environment.ProcessPath,
                        Arguments = "restart",
                        UseShellExecute = true
                    }
                };
                process.Start();
                App.MainWindow.Close();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"重启应用失败: {ex.Message}");
            }
        }

        partial void OnSelectedServerChanged(ServerType value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存服务器设置 {value}");
            _ = _localSettingsService.SaveSettingAsync(LocalSettingsService.BackgroundServerKey, (int)value);

            if (IsBackgroundEnabled)
            {
                _ = RefreshMainPageBackground();
            }
        }

        partial void OnIsBackgroundEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存背景开关 {value}");
            _ = _localSettingsService.SaveSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, value);

            if (!value)
            {
                _backgroundRenderer.ClearBackground();
            }
            else
            {
                _ = RefreshMainPageBackground();
            }
        }

        partial void OnIsAcrylicEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存亚克力设置 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsAcrylicEnabled", value);

            WeakReferenceMessenger.Default.Send(new AcrylicSettingChangedMessage(value));
        }
        partial void OnIsShortTermSupportEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 短期支持版本设置变更为 {value}");

            if (!_isInitializing)
            {
                _ = _localSettingsService.SaveSettingAsync("IsShortTermSupportEnabled", value);
                _ = SwitchInjectionModuleAsync(value);
            }
        }

        partial void OnIsBetterGIIntegrationEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: BetterGI联动设置变更为 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsBetterGIIntegrationEnabled", value);
        }
        partial void OnIsBetterGICloseOnExitEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: BetterGI 关闭随游戏退出设置变更为 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsBetterGICloseOnExitEnabled", value);
        }

        private async Task SwitchInjectionModuleAsync(bool enableShortTerm)
        {
            try
            {

                string appDirectory = AppContext.BaseDirectory;
                string dllPath = Path.Combine(appDirectory, "Genshin.UnlockerIsland.API.dll");
                string bakPath = Path.Combine(appDirectory, "Genshin.UnlockerIsland.API.dll.bak");
                string tempPath = Path.Combine(appDirectory, "Genshin.UnlockerIsland.API.temp");

                if (!File.Exists(dllPath) || !File.Exists(bakPath))
                {
                    Debug.WriteLine("错误：找不到必需的模块文件");
                    var errorDialog = new ContentDialog
                    {
                        Title = "切换失败",
                        Content = "找不到注入模块文件，请确保以下文件存在：\n\n" +
                                 "Genshin.UnlockerIsland.API.dll\n" +
                                 "Genshin.UnlockerIsland.API.dll.bak",
                        CloseButtonText = "确定",
                        XamlRoot = App.MainWindow.Content.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                    return;
                }


                File.Move(dllPath, tempPath, true);

                File.Move(bakPath, dllPath, true);

                File.Move(tempPath, bakPath, true);

                string message = enableShortTerm ? "已切换到短期支持版本" : "已切换回标准版本";
                Debug.WriteLine(message);

                var successDialog = new ContentDialog
                {
                    Title = "切换成功",
                    Content = $"{message}\n\n下次启动游戏时将使用新的注入模块。",
                    CloseButtonText = "确定",
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };
                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"切换注入模块失败: {ex.Message}");

                var dialog = new ContentDialog
                {
                    Title = "切换失败",
                    Content = $"无法切换注入模块: {ex.Message}\n\n请确保：\n" +
                             "1. 程序具有文件操作权限\n" +
                             "2. 文件未被其他程序占用\n" +
                             "3. 文件存在于程序目录中",
                    CloseButtonText = "确定",
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
        partial void OnMinimizeToTrayChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存托盘设置 {value}");
            _ = _localSettingsService.SaveSettingAsync("MinimizeToTray", value);
        }

        partial void OnCustomLaunchParametersChanged(string value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存自定义启动参数: {value}");
            _ = _localSettingsService.SaveSettingAsync("CustomLaunchParameters", value);
            _ = _gameLauncherService.SetCustomLaunchParametersAsync(value);
        }

        private async Task SelectCustomBackgroundAsync()
        {
            try
            {
                var path = await _filePickerService.PickImageOrVideoAsync();
                if (!string.IsNullOrEmpty(path))
                {
                    CustomBackgroundPath = path;
                    HasCustomBackground = true;
                    await _localSettingsService.SaveSettingAsync<string>("CustomBackgroundPath", path);

                    await RefreshMainPageBackground();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择自定义背景失败: {ex.Message}");
            }
        }

        private async Task ClearCustomBackground()
        {
            try
            {
                await _localSettingsService.SaveSettingAsync<string>("CustomBackgroundPath", null);
                CustomBackgroundPath = null;
                HasCustomBackground = false;

                _backgroundRenderer.ClearCustomBackground();
                await RefreshMainPageBackground();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除自定义背景失败: {ex.Message}");
            }
        }

        private async Task RefreshMainPageBackground()
        {
            try
            {
                var mainViewModel = App.GetService<MainViewModel>();
                await mainViewModel.InitializeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"刷新背景失败: {ex.Message}");
            }
        }

        private static string GetVersionDescription()
        {
            Version version;

            if (RuntimeHelper.IsMSIX)
            {
                var packageVersion = Package.Current.Id.Version;
                version = new Version(packageVersion.Major, packageVersion.Minor, packageVersion.Build, packageVersion.Revision);
            }
            else
            {
                version = Assembly.GetExecutingAssembly().GetName().Version!;
            }

            return $"FufuLauncher - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }
}