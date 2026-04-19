using System.Diagnostics;
using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.RegularExpressions;

namespace FufuLauncher.ViewModels
{

    public enum WindowBackdropType
    {
        None = 0,
        Acrylic = 1,
        Mica = 2
    }
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
        [ObservableProperty] private string _launchArgsWidth;
        [ObservableProperty] private string _launchArgsHeight;
        [ObservableProperty] private string _launchArgsPreview = "";
        [ObservableProperty] private string _customBackgroundPath;
        [ObservableProperty] private bool _hasCustomBackground;
        [ObservableProperty] private double _panelBackgroundOpacity = 0.5;
        [ObservableProperty] private bool _isShortTermSupportEnabled;
        [ObservableProperty] private bool _isBetterGIIntegrationEnabled;
        [ObservableProperty] private bool _isBetterGICloseOnExitEnabled;
        [ObservableProperty] private double _globalBackgroundOverlayOpacity = 0.0;
        [ObservableProperty] private double _contentFrameBackgroundOpacity = 0.5;
        [ObservableProperty] private bool _isSaveWindowSizeEnabled;
        [ObservableProperty] private double _globalBackgroundImageOpacity = 1.0;
        [ObservableProperty] private bool _isAcrylicOverlayEnabled;

        [ObservableProperty] private WindowBackdropType _currentWindowBackdrop;
        [ObservableProperty] private string _webView2CacheSize;
        [ObservableProperty] private bool _isAutoCheckinEnabled;

        partial void OnIsAutoCheckinEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 自动签到设置变更为 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsAutoCheckinEnabled", value);
        }

        public IAsyncRelayCommand ClearWebView2CacheCommand { get; }
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

        public ICommand CheckUpdateCommand
        {
            get;
        }
        
        private bool _isInitializing;

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

            InitializeDefaultResolution();

            SelectStartupSoundCommand = new AsyncRelayCommand(SelectStartupSoundAsync);
            ClearStartupSoundCommand = new AsyncRelayCommand(ClearStartupSound);
            CheckUpdateCommand = new RelayCommand(CheckUpdate);
            ElementTheme = _themeSelectorService.Theme;
            _versionDescription = GetVersionDescription();
            ClearWebView2CacheCommand = new AsyncRelayCommand(ClearWebView2CacheAsync);
            UpdateWebView2CacheSize();

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
                        int languageCode = Convert.ToInt32(param);
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
        }
        
        private string FormatSize(long bytes)
        {
            if (bytes == 0) return "0 B";
    
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
    
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return string.Format("{0:n2} {1}", number, suffixes[counter]);
        }
        
        private void UpdateWebView2CacheSize()
        {
            try
            {
                string cacheFolder = Path.Combine(AppContext.BaseDirectory, "FufuLauncher.exe.WebView2");
                if (Directory.Exists(cacheFolder))
                {
                    long size = GetDirectorySize(new DirectoryInfo(cacheFolder));
                    WebView2CacheSize = FormatSize(size);
                }
                else
                {
                    WebView2CacheSize = "0 MB";
                }
            }
            catch
            {
                WebView2CacheSize = "未知大小";
            }
        }
        
        partial void OnIsAcrylicOverlayEnabledChanged(bool value)
        {
            _ = _localSettingsService.SaveSettingAsync("IsAcrylicOverlayEnabled", value);
            WeakReferenceMessenger.Default.Send(new OverlayStyleChangedMessage(value));
        }

        private long GetDirectorySize(DirectoryInfo d)
        {
            long size = 0;
            try
            {
                FileInfo[] fis = d.GetFiles();
                foreach (FileInfo fi in fis)
                {
                    size += fi.Length;
                }
                
                DirectoryInfo[] dis = d.GetDirectories();
                foreach (DirectoryInfo di in dis)
                {
                    size += GetDirectorySize(di);
                }
            }
            catch
            {
                // ignored
            }

            return size;
        }

        private async Task ClearWebView2CacheAsync()
        {
            try
            {
                var cacheFolder = Path.Combine(AppContext.BaseDirectory, "FufuLauncher.exe.WebView2");
        
                if (Directory.Exists(cacheFolder))
                {
                    await Task.Run(() => SafeDeleteDirectory(cacheFolder));
                }
                
                UpdateWebView2CacheSize();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清除 WebView2 缓存失败: {ex.Message}");
            }
        }
        
        private void SafeDeleteDirectory(string targetDir)
        {
            try
            {
                var files = Directory.GetFiles(targetDir);
                var dirs = Directory.GetDirectories(targetDir);
                
                foreach (var file in files)
                {
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file);
                    }
                    catch
                    {
                        // ignored
                    }
                }
                
                foreach (var dir in dirs)
                {
                    SafeDeleteDirectory(dir);
                    try
                    {
                        Directory.Delete(dir, false);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            catch
            {
                // ignored
            }
        }
        
        private void CheckUpdate()
        {
            try
            {
                var updateWindow = new Views.UpdateWindow();
                updateWindow.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"启动更新程序失败: {ex.Message}");
            }
        }
        
        partial void OnLaunchArgsWidthChanged(string value) => ApplyPresetsToText();
        partial void OnLaunchArgsHeightChanged(string value) => ApplyPresetsToText();
        partial void OnLaunchArgsWindowModeChanged(WindowModeType value) => ApplyPresetsToText();

        private void InitializeDefaultResolution()
        {
            try
            {
                var displayArea = DisplayArea.Primary;

                if (displayArea != null)
                {
                    _launchArgsWidth = displayArea.OuterBounds.Width.ToString();
                    _launchArgsHeight = displayArea.OuterBounds.Height.ToString();
                }
                else
                {
                    _launchArgsWidth = "1920";
                    _launchArgsHeight = "1080";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取屏幕分辨率失败: {ex.Message}，使用默认值。");
                _launchArgsWidth = "1920";
                _launchArgsHeight = "1080";
            }
        }

        public async Task ReloadSettingsAsync()
        {
            _isLoadingLaunchParams = true;

            try
            {
                await LoadUserPreferencesAsync();
                await LoadCustomBackgroundSettingsAsync();
                
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
                OnPropertyChanged(nameof(CurrentWindowBackdrop));
                OnPropertyChanged(nameof(IsShortTermSupportEnabled));
                OnPropertyChanged(nameof(IsBetterGIIntegrationEnabled));
                OnPropertyChanged(nameof(IsBetterGICloseOnExitEnabled));
                OnPropertyChanged(nameof(GlobalBackgroundOverlayOpacity));
                OnPropertyChanged(nameof(ContentFrameBackgroundOpacity));
                OnPropertyChanged(nameof(IsSaveWindowSizeEnabled));
                OnPropertyChanged(nameof(IsAcrylicOverlayEnabled));
                OnPropertyChanged(nameof(IsAutoCheckinEnabled));
            }
            finally
            {
                _isLoadingLaunchParams = false;
            }
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
            
            var acrylicOverlayJson = await _localSettingsService.ReadSettingAsync("IsAcrylicOverlayEnabled");
            IsAcrylicOverlayEnabled = acrylicOverlayJson != null && Convert.ToBoolean(acrylicOverlayJson);

            var paramsJson = await _localSettingsService.ReadSettingAsync("CustomLaunchParameters");
            if (paramsJson != null)
            {
                CustomLaunchParameters = paramsJson.ToString();
                ParseLaunchParameters(CustomLaunchParameters);
            }

            var backdropJson = await _localSettingsService.ReadSettingAsync("WindowBackdrop");
            if (backdropJson != null)
            {
                CurrentWindowBackdrop = (WindowBackdropType)Convert.ToInt32(backdropJson);
            }
            else
            {
                CurrentWindowBackdrop = WindowBackdropType.None;
            }

            var shortTermJson = await _localSettingsService.ReadSettingAsync("IsShortTermSupportEnabled");
            IsShortTermSupportEnabled = shortTermJson != null && Convert.ToBoolean(shortTermJson);

            var betterGIJson = await _localSettingsService.ReadSettingAsync("IsBetterGIIntegrationEnabled");
            IsBetterGIIntegrationEnabled = betterGIJson != null && Convert.ToBoolean(betterGIJson);

            var betterGICloseJson = await _localSettingsService.ReadSettingAsync("IsBetterGICloseOnExitEnabled");
            IsBetterGICloseOnExitEnabled = betterGICloseJson != null && Convert.ToBoolean(betterGICloseJson);

            var soundJson = await _localSettingsService.ReadSettingAsync("IsStartupSoundEnabled");
            IsStartupSoundEnabled = soundJson != null && Convert.ToBoolean(soundJson);
            
            var autoCheckinJson = await _localSettingsService.ReadSettingAsync("IsAutoCheckinEnabled");
            IsAutoCheckinEnabled = autoCheckinJson != null && Convert.ToBoolean(autoCheckinJson);

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

            var overlayOpacityJson = await _localSettingsService.ReadSettingAsync("GlobalBackgroundOverlayOpacity");
            try
            {
                GlobalBackgroundOverlayOpacity = overlayOpacityJson != null ? Convert.ToDouble(overlayOpacityJson) : 0.3;
            }
            catch
            {
                GlobalBackgroundOverlayOpacity = 0.3;
            }

            var frameOpacityJson = await _localSettingsService.ReadSettingAsync("ContentFrameBackgroundOpacity");
            try
            {
                ContentFrameBackgroundOpacity = frameOpacityJson != null ? Convert.ToDouble(frameOpacityJson) : 0.5;
            }
            catch
            {
                ContentFrameBackgroundOpacity = 0.5;
            }

            var saveWindowSizeJson = await _localSettingsService.ReadSettingAsync("IsSaveWindowSizeEnabled");
            IsSaveWindowSizeEnabled = saveWindowSizeJson != null && Convert.ToBoolean(saveWindowSizeJson);

            var panelOpacityJson = await _localSettingsService.ReadSettingAsync("PanelBackgroundOpacity");
            try
            {
                PanelBackgroundOpacity = panelOpacityJson != null ? Convert.ToDouble(panelOpacityJson) : 0.5;
            }
            catch
            {
                PanelBackgroundOpacity = 0.5;
            }
            var bgImageOpacityJson = await _localSettingsService.ReadSettingAsync("GlobalBackgroundImageOpacity");
            try
            {
                GlobalBackgroundImageOpacity = bgImageOpacityJson != null ? Convert.ToDouble(bgImageOpacityJson) : 1.0;
            }
            catch
            {
                GlobalBackgroundImageOpacity = 1.0;
            }

        }
        partial void OnGlobalBackgroundImageOpacityChanged(double value)
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(clamped - value) > 0.0001)
            {
                GlobalBackgroundImageOpacity = clamped;
                return;
            }

            _ = _localSettingsService.SaveSettingAsync("GlobalBackgroundImageOpacity", clamped);
            WeakReferenceMessenger.Default.Send(new BackgroundImageOpacityChangedMessage(clamped));
        }

        partial void OnPanelBackgroundOpacityChanged(double value)
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);

            if (Math.Abs(clamped - value) > 0.001)
            {
                PanelBackgroundOpacity = clamped;
                return;
            }

            _localSettingsService.SaveSettingAsync("PanelBackgroundOpacity", clamped);

            WeakReferenceMessenger.Default.Send(new PanelOpacityChangedMessage(clamped));
        }


        partial void OnCurrentWindowBackdropChanged(WindowBackdropType value)
        {
            Debug.WriteLine($"[ViewModel] 属性已更新为: {value}");

            if (!_isInitializing)
            {
                _localSettingsService.SaveSettingAsync("WindowBackdrop", (int)value);

                WeakReferenceMessenger.Default.Send(new ValueChangedMessage<WindowBackdropType>(value));
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
                    if (parts[i] == "-screen-width")
                        LaunchArgsWidth = parts[i + 1];
                    if (parts[i] == "-screen-height")
                        LaunchArgsHeight = parts[i + 1];
                }
            }
            catch
            {
                // ignored
            }
        }
        
        private void ApplyPresetsToText()
        {
            if (_isLoadingLaunchParams) return;

            var currentArgs = CustomLaunchParameters ?? "";
            
            currentArgs = Regex.Replace(currentArgs, @"-screen-width\s+\S+", "");
            currentArgs = Regex.Replace(currentArgs, @"-screen-height\s+\S+", "");
            currentArgs = Regex.Replace(currentArgs, @"-popupwindow", "");
            
            var sb = new System.Text.StringBuilder(currentArgs);
            if (!string.IsNullOrWhiteSpace(LaunchArgsWidth) && !string.IsNullOrWhiteSpace(LaunchArgsHeight))
            {
                sb.Append($" -screen-width {LaunchArgsWidth} -screen-height {LaunchArgsHeight}");
            }
            if (LaunchArgsWindowMode == WindowModeType.Popup)
            {
                sb.Append(" -popupwindow");
            }

            var finalArgs = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
            if (CustomLaunchParameters != finalArgs)
            {
                CustomLaunchParameters = finalArgs;
            }
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
            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }

        partial void OnIsBackgroundEnabledChanged(bool value)
        {
            // Now means: whether custom background is allowed. If disabled, we fall back to official background.
            Debug.WriteLine($"SettingsViewModel: 保存自定义背景开关 {value}");
            _ = _localSettingsService.SaveSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, value);

            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());

            if (!value)
            {
                _backgroundRenderer.ClearCustomBackground();
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

        partial void OnGlobalBackgroundOverlayOpacityChanged(double value)
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);

            if (Math.Abs(clamped - value) > 0.0001)
            {
                GlobalBackgroundOverlayOpacity = clamped;
                return;
            }

            _ = _localSettingsService.SaveSettingAsync("GlobalBackgroundOverlayOpacity", clamped);
            WeakReferenceMessenger.Default.Send(new BackgroundOverlayOpacityChangedMessage(clamped));
        }

        partial void OnContentFrameBackgroundOpacityChanged(double value)
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(clamped - value) > 0.0001)
            {
                ContentFrameBackgroundOpacity = clamped;
                return;
            }

            _ = _localSettingsService.SaveSettingAsync("ContentFrameBackgroundOpacity", clamped);
            WeakReferenceMessenger.Default.Send(new FrameBackgroundOpacityChangedMessage(clamped));
        }

        partial void OnIsSaveWindowSizeEnabledChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存窗口大小记忆设置 {value}");
            _ = _localSettingsService.SaveSettingAsync("IsSaveWindowSizeEnabled", value);
        }
        private bool _isLoadingLaunchParams = false;

        partial void OnMinimizeToTrayChanged(bool value)
        {
            Debug.WriteLine($"SettingsViewModel: 保存托盘设置 {value}");
            _ = _localSettingsService.SaveSettingAsync("MinimizeToTray", value);
            WeakReferenceMessenger.Default.Send(new MinimizeToTrayChangedMessage(value));
        }
        

        partial void OnCustomLaunchParametersChanged(string value)
        {
            _localSettingsService.SaveSettingAsync("CustomLaunchParameters", value);
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
                    await _localSettingsService.SaveSettingAsync("CustomBackgroundPath", path);

                    WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
                    await RefreshMainPageBackground();

                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"选择自定义背景失败: {ex.Message}");
            }
        }

        private async Task RefreshMainPageBackground()
        {
            // removed: main page background no longer applies; global background refresh is handled by MainWindow.
            await Task.CompletedTask;
        }

        private static string GetVersionDescription()
        {
            var version = Assembly.GetEntryAssembly().GetName().Version;
            if (version == null) version = new Version(1, 0, 0, 0);

            return $"FufuLauncher - {version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }
}