using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Playback;
using Windows.UI;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.ViewModels
{
    public partial class MainViewModel : ObservableRecipient
    {
        private readonly IHoyoverseContentService _contentService;
        private readonly IBackgroundRenderer _backgroundRenderer;
        private readonly ILocalSettingsService _localSettingsService;
        private readonly IHoyoverseCheckinService _checkinService;
        private readonly IGameLauncherService _gameLauncherService;
        private readonly INotificationService _notificationService;
        private readonly DispatcherQueue _dispatcherQueue;
        private static bool _isFirstLoad = true;

        [ObservableProperty] private bool _isGameNotLaunching;

        [ObservableProperty] private ImageSource _backgroundImageSource;
        [ObservableProperty] private MediaPlayer _backgroundVideoPlayer;
        [ObservableProperty] private bool _isVideoBackground;
        [ObservableProperty] private bool _isBackgroundLoading;

        [ObservableProperty] private string _customBackgroundPath;
        [ObservableProperty] private bool _hasCustomBackground;

        [ObservableProperty] private ObservableCollection<BannerItem> _banners = new();
        [ObservableProperty] private ObservableCollection<PostItem> _activityPosts = new();
        [ObservableProperty] private ObservableCollection<PostItem> _announcementPosts = new();
        [ObservableProperty] private ObservableCollection<PostItem> _infoPosts = new();
        [ObservableProperty] private ObservableCollection<SocialMediaItem> _socialMediaList = new();
        [ObservableProperty] private Brush _panelBackgroundBrush;
        [ObservableProperty] private double _infoCardHeight = 275;
        [ObservableProperty] private string _infoExpandIcon = "\uE70E";
        private bool _isInfoCardExpanded = true;
        private double _panelOpacityValue = 0.5;
        private BannerItem _currentBanner;
        public string CurrentDayText => DateTime.Now.Day.ToString();
        public BannerItem CurrentBanner
        {
            get => _currentBanner;
            set
            {
                SetProperty(ref _currentBanner, value);
            }
        }

        partial void OnIsGameLaunchingChanged(bool value) => IsGameNotLaunching = !value;

        [ObservableProperty] private bool _isPanelExpanded = true;

        private DispatcherQueueTimer _bannerTimer;

        public Visibility ImageVisibility => IsVideoBackground ? Visibility.Collapsed : Visibility.Visible;
        public Visibility VideoVisibility => IsVideoBackground ? Visibility.Visible : Visibility.Collapsed;

        partial void OnIsVideoBackgroundChanged(bool value)
        {
            OnPropertyChanged(nameof(ImageVisibility));
            OnPropertyChanged(nameof(VideoVisibility));
        }

        [ObservableProperty] private string _checkinStatusText = "正在加载状态...";
        [ObservableProperty] private bool _isCheckinButtonEnabled = true;
        [ObservableProperty] private string _checkinButtonText = "一键签到";
        [ObservableProperty] private string _checkinSummary = "";
        
        [ObservableProperty] private string _checkinStateGlyph = "\uE730"; 
        [ObservableProperty] private SolidColorBrush _checkinStateBrush = new(Microsoft.UI.Colors.White);
        
        [ObservableProperty] private string _launchButtonText = "请选择游戏路径";
        [ObservableProperty] private bool _isLaunchButtonEnabled = true;
        [ObservableProperty] private bool _isGameLaunching;

        [ObservableProperty] private bool _useInjection;

        [ObservableProperty] private bool _preferVideoBackground = true;
        public string BackgroundTypeToggleText => "切换背景";

        [ObservableProperty] private bool _isGameRunning;
        [ObservableProperty] private string _launchButtonIcon = "\uE768";
        [ObservableProperty] private bool _isBackgroundToggleEnabled = true;

        private const string TargetProcessName = "yuanshen";
        private const string TargetProcessNameAlt = "GenshinImpact";
        private CancellationTokenSource _gameMonitoringCts;

        public IAsyncRelayCommand LoadBackgroundCommand
        {
            get;
        }
        public IRelayCommand TogglePanelCommand
        {
            get;
        }

        public IRelayCommand ToggleInfoCardCommand
        {
            get;
        }

        public IRelayCommand ToggleBackgroundTypeCommand
        {
            get;
        }
        public IAsyncRelayCommand ExecuteCheckinCommand
        {
            get;
        }
        public IAsyncRelayCommand LaunchGameCommand
        {
            get;
        }
        public IAsyncRelayCommand OpenScreenshotFolderCommand
        {
            get;
        }

        public MainViewModel(
            IHoyoverseBackgroundService backgroundService,
            IHoyoverseContentService contentService,
            IBackgroundRenderer backgroundRenderer,
            ILocalSettingsService localSettingsService,
            IHoyoverseCheckinService checkinService,
            IGameLauncherService gameLauncherService,
            ILauncherService launcherService,
            INavigationService navigationService,
            INotificationService notificationService)
        {
            _contentService = contentService;
            _backgroundRenderer = backgroundRenderer;
            _localSettingsService = localSettingsService;
            _checkinService = checkinService;
            _gameLauncherService = gameLauncherService;
            _notificationService = notificationService;
            _dispatcherQueue = App.MainWindow.DispatcherQueue;

            _bannerTimer = _dispatcherQueue.CreateTimer();
            _bannerTimer.Interval = TimeSpan.FromSeconds(5);
            _bannerTimer.Tick += (s, e) => RotateBanner();

            LoadBackgroundCommand = new AsyncRelayCommand(LoadBackgroundAsync);
            TogglePanelCommand = new RelayCommand(() => IsPanelExpanded = !IsPanelExpanded);
            ToggleInfoCardCommand = new RelayCommand(ToggleInfoCard);
            ToggleBackgroundTypeCommand = new RelayCommand(ToggleBackgroundType);
            ExecuteCheckinCommand = new AsyncRelayCommand(ExecuteCheckinAsync);
            LaunchGameCommand = new AsyncRelayCommand(LaunchGameAsync);
            OpenScreenshotFolderCommand = new AsyncRelayCommand(OpenScreenshotFolderAsync);

            WeakReferenceMessenger.Default.Register<GamePathChangedMessage>(this, (r, m) =>
            {
                _cachedGamePath = null;
                _dispatcherQueue?.TryEnqueue(() => UpdateLaunchButtonState());
            });

            _gameMonitoringCts = new CancellationTokenSource();
            StartGameMonitoringLoopAsync(_gameMonitoringCts.Token);

            WeakReferenceMessenger.Default.Register<PanelOpacityChangedMessage>(this, (r, m) =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    _panelOpacityValue = m.Value;
                    UpdatePanelBackgroundBrush();
                });
            });
        }

        private void ToggleInfoCard()
        {
            _isInfoCardExpanded = !_isInfoCardExpanded;
            if (_isInfoCardExpanded)
            {
                InfoCardHeight = 275;
                InfoExpandIcon = "\uE70E";
            }
            else
            {
                InfoCardHeight = 135;
                InfoExpandIcon = "\uE70D";
            }
        }

        public async Task InitializeAsync()
        {
            await LoadUserPreferencesAsync();
            await LoadCustomBackgroundPathAsync();
            await LoadBackgroundAsync();
            await LoadContentAsync();
            await LoadCheckinStatusAsync();
            UseInjection = await _gameLauncherService.GetUseInjectionAsync();

            try
            {
                var savedOpacity = await _localSettingsService.ReadSettingAsync("PanelBackgroundOpacity");
                if (savedOpacity != null)
                {
                    _panelOpacityValue = Convert.ToDouble(savedOpacity);
                }
            }
            catch
            {
                // ignored
            }

            UpdatePanelBackgroundBrush();
            UpdateLaunchButtonState();
        }

        partial void OnHasCustomBackgroundChanged(bool value)
        {
            // Background switching is global-only now; keep button disabled on main page.
            IsBackgroundToggleEnabled = !value;
        }

        private void UpdatePanelBackgroundBrush()
        {
            try
            {
                var themeService = App.GetService<IThemeSelectorService>();
                var currentTheme = themeService.Theme;

                if (currentTheme == ElementTheme.Default)
                {
                    currentTheme = Application.Current.RequestedTheme == ApplicationTheme.Light
                        ? ElementTheme.Light
                        : ElementTheme.Dark;
                }

                Color baseColor;
                if (currentTheme == ElementTheme.Light)
                {
                    baseColor = Microsoft.UI.Colors.White;
                }
                else
                {
                    baseColor = Color.FromArgb(255, 32, 32, 32);
                }

                PanelBackgroundBrush = new SolidColorBrush(baseColor) { Opacity = _panelOpacityValue };
                Debug.WriteLine($"[MainViewModel] 背景已更新 - 主题: {currentTheme}, 透明度: {_panelOpacityValue}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainViewModel] 更新背景失败: {ex.Message}");
            }
        }

        public async Task OnPageReturnedAsync()
        {
            await LoadUserPreferencesAsync();
            await ForceRefreshGameStateAsync();
        }

        private async Task LoadUserPreferencesAsync()
        {
            var pref = await _localSettingsService.ReadSettingAsync("PreferVideoBackground");
            if (pref != null)
            {
                PreferVideoBackground = Convert.ToBoolean(pref);
            }

            var panelOpacityJson = await _localSettingsService.ReadSettingAsync("PanelBackgroundOpacity");
            try
            {
                _panelOpacityValue = panelOpacityJson != null ? Convert.ToDouble(panelOpacityJson) : 0.5;
            }
            catch
            {
                _panelOpacityValue = 0.5;
            }
        }

        private async Task LoadCustomBackgroundPathAsync()
        {
            var path = await _localSettingsService.ReadSettingAsync("CustomBackgroundPath");
            if (path != null)
            {
                CustomBackgroundPath = path.ToString();
                HasCustomBackground = File.Exists(CustomBackgroundPath);
            }
            else
            {
                HasCustomBackground = false;
            }

            IsBackgroundToggleEnabled = !HasCustomBackground;
        }

        private async Task LoadBackgroundAsync()
        {
            await UpdateUI(() => IsBackgroundLoading = true);
            
            ClearBackground();

            await UpdateUI(() =>
            {
                try
                {
                    string targetPath;
                    
                    if (HasCustomBackground && !string.IsNullOrEmpty(CustomBackgroundPath) && File.Exists(CustomBackgroundPath))
                    {
                        targetPath = CustomBackgroundPath;
                        TryLoadImage(targetPath);
                    }
                    else
                    {
                        LoadFallbackImage();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"背景加载流程发生异常: {ex.Message}");
                    LoadFallbackImage();
                }
            });

            await UpdateUI(() => IsBackgroundLoading = false);
        }

        private void TryLoadImage(string path)
        {
            try
            {
                var bitmap = new BitmapImage();
                
                bitmap.UriSource = new Uri(path);
                
                bitmap.ImageFailed += (_, _) =>
                {
                    Debug.WriteLine($"图片解码失败: {path}，正在切换至默认背景。");
                    _dispatcherQueue.TryEnqueue(LoadFallbackImage);
                };

                BackgroundImageSource = bitmap;
                IsVideoBackground = false;
            }
            catch
            {
                LoadFallbackImage();
            }
        }

        private void LoadFallbackImage()
        {
            try
            {
                string fallbackPath = Path.Combine(AppContext.BaseDirectory, "Assets", "bg.png");

                if (File.Exists(fallbackPath))
                {
                    if (BackgroundImageSource is BitmapImage currentBmp && 
                        currentBmp.UriSource?.LocalPath == fallbackPath)
                    {
                        return;
                    }

                    var bitmap = new BitmapImage();
                    bitmap.UriSource = new Uri(fallbackPath);
                    BackgroundImageSource = bitmap;
                    IsVideoBackground = false;
                    Debug.WriteLine("已加载默认背景: Assets/bg.png");
                }
                else
                {
                    Debug.WriteLine($"严重错误: 默认背景文件不存在 -> {fallbackPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载默认背景失败: {ex.Message}");
            }
        }

        private void ClearBackground()
        {
            BackgroundImageSource = null;
            BackgroundVideoPlayer?.Pause();
            BackgroundVideoPlayer = null;
            IsVideoBackground = false;
        }

        private void ToggleBackgroundType()
        {
            PreferVideoBackground = !PreferVideoBackground;
            OnPropertyChanged(nameof(BackgroundTypeToggleText));
            _ = _localSettingsService.SaveSettingAsync("UserPreferVideoBackground", PreferVideoBackground);
            _ = _localSettingsService.SaveSettingAsync("PreferVideoBackground", PreferVideoBackground);
            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }

        private async Task LoadContentAsync()
        {
            if (Banners != null && Banners.Count > 0)
            {
                if (CurrentBanner == null)
                {
                    CurrentBanner = Banners[0];
                }

                _bannerTimer?.Start();

                return;
            }

            try
            {
                var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
                int serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
                var server = (ServerType)serverValue;

                var content = await _contentService.GetGameContentAsync(server);

                if (content != null)
                {
                    await UpdateUI(() =>
                    {
                        _bannerTimer?.Stop();
                        CurrentBanner = null;

                        Banners.Clear();
                        foreach (var banner in content.Banners ?? Array.Empty<BannerItem>())
                        {
                            Banners.Add(banner);
                        }

                        var posts = content.Posts ?? Array.Empty<PostItem>();

                        ActivityPosts.Clear();
                        foreach (var post in posts.Where(p => p.Type == "POST_TYPE_ACTIVITY"))
                            ActivityPosts.Add(post);

                        AnnouncementPosts.Clear();
                        foreach (var post in posts.Where(p => p.Type == "POST_TYPE_ANNOUNCE"))
                            AnnouncementPosts.Add(post);

                        InfoPosts.Clear();
                        foreach (var post in posts.Where(p => p.Type == "POST_TYPE_INFO"))
                            InfoPosts.Add(post);

                        SocialMediaList.Clear();
                        foreach (var item in content.SocialMediaList ?? Array.Empty<SocialMediaItem>())
                        {
                            SocialMediaList.Add(item);
                        }

                        if (Banners.Count > 0)
                        {
                            _dispatcherQueue.TryEnqueue(async () =>
                            {
                                try
                                {
                                    await Task.Delay(50);

                                    if (Banners.Count > 0)
                                    {
                                        CurrentBanner = Banners[0];
                                        _bannerTimer?.Start();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"设置 Banner 选中项失败: {ex.Message}");
                                }
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"内容加载失败: {ex.Message}");
            }
        }

        private void RotateBanner()
        {
            if (Banners == null || Banners.Count < 2) return;

            if (CurrentBanner == null)
            {
                CurrentBanner = Banners[0];
                return;
            }

            try
            {
                var currentIndex = Banners.IndexOf(CurrentBanner);
                if (currentIndex == -1) currentIndex = 0;

                var nextIndex = (currentIndex + 1) % Banners.Count;
                CurrentBanner = Banners[nextIndex];
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"轮播图切换错误: {ex.Message}");
            }
        }

        public void Cleanup()
        {
            _bannerTimer?.Stop();
            _gameMonitoringCts?.Cancel();

            if (BackgroundVideoPlayer != null)
            {
                try
                {
                    BackgroundVideoPlayer.Pause();
                    BackgroundVideoPlayer = null;
                }
                catch { }
            }
        }

        private void UpdateCheckinIconState(string statusText)
        {
            bool isSigned = !string.IsNullOrEmpty(statusText) && 
                            (statusText.Contains("成功") || statusText.Contains("已"));
            
            CheckinStateGlyph = "\uE73E"; 

            if (isSigned)
            {
                CheckinStateBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGreen);
            }
            else
            {
                CheckinStateBrush = new SolidColorBrush(Microsoft.UI.Colors.White) { Opacity = 0.3 };
            }
        }
        
        private async Task LoadCheckinStatusAsync()
        {
            try
            {
                Debug.WriteLine($"主界面开始加载签到状态");
                var (status, summary) = await _checkinService.GetCheckinStatusAsync();

                Debug.WriteLine($"状态更新: {status}, {summary}");
                CheckinStatusText = status;
                CheckinSummary = summary;
                
                UpdateCheckinIconState(status);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载失败: {ex.Message}");
                CheckinStatusText = "加载失败";
                CheckinSummary = ex.Message;
                UpdateCheckinIconState("Fail");
            }
        }

        private async Task ExecuteCheckinAsync()
        {
            Debug.WriteLine($"用户点击签到按钮");
            IsCheckinButtonEnabled = false;
            CheckinButtonText = "签到中...";

            try
            {
                var (success, message) = await _checkinService.ExecuteCheckinAsync();

                Debug.WriteLine($"签到结果: success={success}, message={message}");
                CheckinStatusText = success ? "签到成功" : "签到失败";
                CheckinSummary = message;
                UpdateCheckinIconState(success ? "已签到" : "Fail");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"执行失败: {ex.Message}");
                CheckinStatusText = "执行失败";
                CheckinSummary = ex.Message;
            }
            finally
            {
                IsCheckinButtonEnabled = true;
                CheckinButtonText = "一键签到";
                await Task.Delay(500);
                await LoadCheckinStatusAsync();
            }
        }

        public void UpdateLaunchButtonState()
        {
            var pathTask = _localSettingsService.ReadSettingAsync("GameInstallationPath");
            var savedPath = pathTask.Result as string;

            var hasPath = !string.IsNullOrEmpty(savedPath) &&
                          Directory.Exists(savedPath.Trim('"').Trim());

            if (IsGameRunning)
            {
                LaunchButtonText = "点击退出游戏";
                LaunchButtonIcon = "\uE711";
            }
            else
            {
                if (hasPath)
                {
                    LaunchButtonText = "点击启动游戏";
                }
                else
                {
                    LaunchButtonText = "请选择游戏路径";
                }

                LaunchButtonIcon = "\uE768";
            }

            OnPropertyChanged(nameof(LaunchButtonText));
            OnPropertyChanged(nameof(LaunchButtonIcon));

            IsLaunchButtonEnabled = true;
        }

        private async Task LaunchGameAsync()
        {
            await ForceRefreshGameStateAsync();

            if (IsGameRunning)
            {
                await TerminateGameAsync();
                await Task.Delay(1200);
                await ForceRefreshGameStateAsync();
                return;
            }

            if (!_gameLauncherService.IsGamePathSelected())
            {
                _notificationService.Show("未设置游戏路径", "请先前往设置页面选择游戏安装路径", NotificationType.Error, 0);
                return;
            }

            IsGameLaunching = true;
            IsLaunchButtonEnabled = false;

            try
            {
                var result = await _gameLauncherService.LaunchGameAsync();

                if (result.Success)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(300);
                        await ForceRefreshGameStateAsync();
                        if (IsGameRunning) break;
                    }
                }
                else
                {
                    _notificationService.Show("游戏启动失败", result.ErrorMessage, NotificationType.Error, 0);
                }
            }
            finally
            {
                IsGameLaunching = false;
                IsLaunchButtonEnabled = true;
                await ForceRefreshGameStateAsync();
            }
        }

        private async Task OpenScreenshotFolderAsync()
        {
            var savedPath = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
            var gamePath = savedPath?.ToString()?.Trim('"')?.Trim();

            if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            {
                _notificationService.Show("未设置游戏路径", "请先前往设置页面选择游戏安装路径", NotificationType.Error, 0);
                return;
            }

            var screenshotPath = Path.Combine(gamePath, "ScreenShot");
            if (!Directory.Exists(screenshotPath))
            {
                _notificationService.Show("截图文件夹不存在", $"未找到截图文件夹: {screenshotPath}", NotificationType.Error, 0);
                return;
            }

            try
            {
                await Windows.System.Launcher.LaunchFolderPathAsync(screenshotPath);
            }
            catch (Exception ex)
            {
                _notificationService.Show("打开失败", $"无法打开截图文件夹: {ex.Message}", NotificationType.Error, 0);
            }
        }

        partial void OnUseInjectionChanged(bool value)
        {
            _ = Task.Run(async () =>
            {
                await _gameLauncherService.SetUseInjectionAsync(value);
                var actual = await _gameLauncherService.GetUseInjectionAsync();
                if (actual != value)
                {
                    await UpdateUI(() => UseInjection = actual);
                }

                await UpdateUI(() => UpdateLaunchButtonState());
            });
        }

        private Task UpdateUI(Action uiAction)
        {
            if (_dispatcherQueue == null)
            {
                uiAction();
                return Task.CompletedTask;
            }

            return _dispatcherQueue.EnqueueAsync(() => uiAction());
        }

        private async Task ForceRefreshGameStateAsync()
        {
            bool actualState = await CheckGameProcessRunningAsync();
            if (actualState != IsGameRunning)
            {
                await SetGameRunningStateAsync(actualState);
            }
        }
        private string _cachedGamePath;
        private async Task<bool> CheckGameProcessRunningAsync()
        {
            try
            {
                var processes = Process.GetProcessesByName(TargetProcessName)
                    .Concat(Process.GetProcessesByName(TargetProcessNameAlt))
                    .ToList();

                if (processes.Count == 0) return false;
                
                if (string.IsNullOrEmpty(_cachedGamePath))
                {
                    var savedPathTask = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
                    _cachedGamePath = savedPathTask?.ToString()?.Trim('"')?.Trim();
                }
                var gamePath = _cachedGamePath;

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.HasExited) continue;
                        
                        if (!string.IsNullOrEmpty(gamePath))
                        {
                            var processPath = p.MainModule?.FileName;
                            if (!string.IsNullOrEmpty(processPath))
                            {
                                if (processPath.StartsWith(gamePath, StringComparison.OrdinalIgnoreCase))
                                {
                                    return true;
                                }
                                continue; 
                            }
                        }
                        
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            return true;
                        }
                
                        return true;
                    }
                    catch (Win32Exception)
                    {
                        return true;
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task SetGameRunningStateAsync(bool isRunning, string temporaryText = null)
        {
            await UpdateUI(() =>
            {
                IsGameRunning = isRunning;
                LaunchButtonIcon = isRunning ? "\uE711" : "\uE768";

                if (temporaryText != null)
                {
                    LaunchButtonText = temporaryText;
                }
                else
                {
                    UpdateLaunchButtonState();
                }

                OnPropertyChanged(nameof(LaunchButtonText));
                OnPropertyChanged(nameof(LaunchButtonIcon));
                OnPropertyChanged(nameof(IsGameRunning));
            });
        }

        private async Task TerminateGameAsync()
{
    IsLaunchButtonEnabled = false;
    await SetGameRunningStateAsync(true, "正在终止游戏...");

    try
    {
        var savedPathObj = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
        var gamePath = savedPathObj?.ToString()?.Trim('"')?.Trim();

        var processes = Process.GetProcessesByName(TargetProcessName)
            .Concat(Process.GetProcessesByName(TargetProcessNameAlt))
            .ToList();

        if (processes.Count == 0)
        {
            await SetGameRunningStateAsync(false);
            UpdateLaunchButtonState();
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited) continue;
                
                if (!string.IsNullOrEmpty(gamePath))
                {
                    try
                    {
                        var processPath = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath) &&
                            !processPath.StartsWith(gamePath, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }
                    catch (Win32Exception)
                    {
                        // ignored
                    }
                    catch (InvalidOperationException) { continue; }
                }

                process.Kill();
                await process.WaitForExitAsync();
            }
            catch
            {
                // ignored
            }
        }

        try
        {
            await _gameLauncherService.StopBetterGIAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"关闭 BetterGI 时发生错误: {ex.Message}");
        }

        await Task.Delay(1000);
        await SetGameRunningStateAsync(false);
        UpdateLaunchButtonState();
    }
    catch (Exception ex)
    {
        _notificationService.Show("终止失败", ex.Message, NotificationType.Error, 0);
        await SetGameRunningStateAsync(false);
        UpdateLaunchButtonState();
    }
    finally
    {
        IsLaunchButtonEnabled = true;
    }
}

        private async Task StartGameMonitoringLoopAsync(CancellationToken token)
        {
            bool lastState = false;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    bool currentState = await CheckGameProcessRunningAsync(); 

                    if (currentState != lastState || currentState != IsGameRunning)
                    {
                        await UpdateUI(() =>
                        {
                            IsGameRunning = currentState;
                            UpdateLaunchButtonState();
                        });
                    }

                    lastState = currentState;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"进程监控错误: {ex.Message}");
                }
                
                await Task.Delay(1000, token); 
            }
        }
    }
}