using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Security.Principal;
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
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Playback;

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

        private BannerItem _currentBanner;
        public BannerItem CurrentBanner
        {
            get => _currentBanner;
            set => SetProperty(ref _currentBanner, value);
        }

        partial void OnIsGameLaunchingChanged(bool value) => IsGameNotLaunching = !value;

        [ObservableProperty] private bool _isPanelExpanded = true;
        [ObservableProperty] private bool _isActivityPostsExpanded = true;
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

        [ObservableProperty] private string _launchButtonText = "请选择游戏路径";
        [ObservableProperty] private bool _isLaunchButtonEnabled = true;
        [ObservableProperty] private bool _isGameLaunching;

        [ObservableProperty] private bool _useInjection;

        [ObservableProperty] private bool _preferVideoBackground = true;
        public string BackgroundTypeToggleText => "切换背景";

        [ObservableProperty] private bool _isGameRunning;
        [ObservableProperty] private string _launchButtonIcon = "\uE768";

        private const string TargetProcessName = "yuanshen";
        private const string TargetProcessNameAlt = "GenshinImpact";
        private CancellationTokenSource _gameMonitoringCts;
        private Task _monitoringTask;

        public IAsyncRelayCommand LoadBackgroundCommand
        {
            get;
        }
        public IRelayCommand TogglePanelCommand
        {
            get;
        }
        public IRelayCommand ToggleActivityCommand
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
            ToggleActivityCommand = new RelayCommand(() => IsActivityPostsExpanded = !IsActivityPostsExpanded);
            ToggleBackgroundTypeCommand = new RelayCommand(ToggleBackgroundType);
            ExecuteCheckinCommand = new AsyncRelayCommand(ExecuteCheckinAsync);
            LaunchGameCommand = new AsyncRelayCommand(LaunchGameAsync);
            OpenScreenshotFolderCommand = new AsyncRelayCommand(OpenScreenshotFolderAsync);

            WeakReferenceMessenger.Default.Register<GamePathChangedMessage>(this, (r, m) =>
            {
                _dispatcherQueue?.TryEnqueue(() => UpdateLaunchButtonState());
            });

            _gameMonitoringCts = new CancellationTokenSource();
            _monitoringTask = StartGameMonitoringLoopAsync(_gameMonitoringCts.Token);
        }

        public async Task InitializeAsync()
        {
            await LoadUserPreferencesAsync();
            await LoadCustomBackgroundPathAsync();
            await LoadBackgroundAsync();
            await LoadContentAsync();
            await LoadCheckinStatusAsync();
            UseInjection = await _gameLauncherService.GetUseInjectionAsync();
            UpdateLaunchButtonState();
        }

        public async Task OnPageReturnedAsync()
        {
            await ForceRefreshGameStateAsync();
        }

        private async Task LoadUserPreferencesAsync()
        {
            var pref = await _localSettingsService.ReadSettingAsync("PreferVideoBackground");
            if (pref != null)
            {
                PreferVideoBackground = Convert.ToBoolean(pref);
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
        }

        private async Task LoadBackgroundAsync()
        {
            await UpdateUI(() => IsBackgroundLoading = true);
            try
            {
                if (HasCustomBackground && !string.IsNullOrEmpty(CustomBackgroundPath))
                {
                    var customResult = await _backgroundRenderer.GetCustomBackgroundAsync(CustomBackgroundPath);
                    if (customResult != null)
                    {
                        await UpdateUI(() =>
                        {
                            if (customResult.IsVideo)
                            {
                                BackgroundVideoPlayer = new MediaPlayer
                                {
                                    Source = customResult.VideoSource,
                                    IsMuted = true,
                                    IsLoopingEnabled = true,
                                    AutoPlay = true
                                };
                                IsVideoBackground = true;
                                BackgroundImageSource = null;
                            }
                            else
                            {
                                BackgroundImageSource = customResult.ImageSource;
                                IsVideoBackground = false;
                                BackgroundVideoPlayer?.Pause();
                                BackgroundVideoPlayer = null;
                            }
                        });
                        return;
                    }
                }

                var enabledJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.IsBackgroundEnabledKey);
                bool isEnabled = enabledJson == null ? true : Convert.ToBoolean(enabledJson);

                if (!isEnabled)
                {
                    ClearBackground();
                    return;
                }

                var userPreferVideo = await _localSettingsService.ReadSettingAsync("UserPreferVideoBackground");
                bool useVideo = false;

                if (userPreferVideo != null && Convert.ToBoolean(userPreferVideo))
                {
                    useVideo = true;
                }

                var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
                int serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
                var server = (ServerType)serverValue;

                var result = await _backgroundRenderer.GetBackgroundAsync(server, useVideo);

                if (result == null) return;

                await UpdateUI(() =>
                {
                    if (result.IsVideo)
                    {
                        var player = new MediaPlayer
                        {
                            Source = result.VideoSource,
                            IsMuted = true,
                            IsLoopingEnabled = true,
                            AutoPlay = true
                        };

                        BackgroundVideoPlayer = player;
                        IsVideoBackground = true;
                        BackgroundImageSource = null;
                    }
                    else
                    {
                        BackgroundImageSource = result.ImageSource;
                        IsVideoBackground = false;
                        BackgroundVideoPlayer?.Pause();
                        BackgroundVideoPlayer = null;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"背景加载失败: {ex.Message}");
                ClearBackground();
            }
            finally
            {
                await UpdateUI(() => IsBackgroundLoading = false);
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

            BackgroundVideoPlayer?.Pause();
            BackgroundVideoPlayer = null;
            BackgroundImageSource = null;
            IsVideoBackground = false;

            _ = _localSettingsService.SaveSettingAsync("PreferVideoBackground", PreferVideoBackground);
            _ = LoadBackgroundAsync();
        }

        private async Task LoadContentAsync()
        {
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
                        Banners.Clear();
                        foreach (var banner in content.Banners ?? Array.Empty<BannerItem>())
                            Banners.Add(banner);

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
                            SocialMediaList.Add(item);

                        if (Banners.Count > 0)
                        {
                            CurrentBanner = Banners[0];
                            _bannerTimer?.Start();
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
            if (Banners?.Count > 1)
            {
                var currentIndex = CurrentBanner != null ? Math.Max(0, Banners.IndexOf(CurrentBanner)) : 0;
                var nextIndex = (currentIndex + 1) % Banners.Count;
                CurrentBanner = Banners[nextIndex];
            }
        }

        public bool IsAdministrator
        {
            get
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
        }

        public bool IsInjectionToggleEnabled => IsAdministrator;

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

        private async Task LoadCheckinStatusAsync()
        {
            try
            {
                Debug.WriteLine($"主界面开始加载签到状态");
                var (status, summary) = await _checkinService.GetCheckinStatusAsync();

                Debug.WriteLine($"状态更新: {status}, {summary}");
                CheckinStatusText = status;
                CheckinSummary = summary;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载失败: {ex.Message}");
                CheckinStatusText = "加载失败";
                CheckinSummary = ex.Message;
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
                          System.IO.Directory.Exists(savedPath.Trim('"').Trim());

            if (IsGameRunning)
            {
                LaunchButtonText = "点击退出游戏";
                LaunchButtonIcon = "\uE711";
            }
            else
            {
                LaunchButtonText = hasPath ? "点击启动游戏" : "请选择游戏路径";
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
                    for (int i = 0; i < 3; i++)
                    {
                        await Task.Delay(1000);
                        await ForceRefreshGameStateAsync();
                        if (IsGameRunning) break;
                    }
                }
                else
                {
                    _notificationService.Show("启动失败", result.ErrorMessage, NotificationType.Error, 0);
                }
            }
            catch (Exception ex)
            {
                _notificationService.Show("启动错误", $"发生异常: {ex.Message}", NotificationType.Error, 0);
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

            if (string.IsNullOrEmpty(gamePath) || !System.IO.Directory.Exists(gamePath))
            {
                _notificationService.Show("未设置游戏路径", "请先前往设置页面选择游戏安装路径", NotificationType.Error, 0);
                return;
            }

            var screenshotPath = System.IO.Path.Combine(gamePath, "ScreenShot");
            if (!System.IO.Directory.Exists(screenshotPath))
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
                if (value && !IsAdministrator)
                {
                    await UpdateUI(() =>
                    {
                        UseInjection = false;
                        ShowAdminRequiredDialog();
                    });
                    return;
                }

                await _gameLauncherService.SetUseInjectionAsync(value);
                var actual = await _gameLauncherService.GetUseInjectionAsync();
                if (actual != value)
                {
                    await UpdateUI(() => UseInjection = actual);
                }
            });
        }

        private async Task ShowAdminRequiredDialog()
        {
            await _dispatcherQueue.EnqueueAsync(async () =>
            {
                var dialog = new ContentDialog
                {
                    Title = "需要管理员权限",
                    Content = "注入功能需要以管理员身份运行此程序。请右键点击启动器，选择\"以管理员身份运行\"。",
                    CloseButtonText = "确定",
                    XamlRoot = App.MainWindow.Content.XamlRoot
                };
                await dialog.ShowAsync();
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
            bool actualState = CheckGameProcessRunning();
            if (actualState != IsGameRunning)
            {
                await SetGameRunningStateAsync(actualState);
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

        private bool CheckGameProcessRunning()
        {
            try
            {
                return Process.GetProcessesByName(TargetProcessName).Length > 0 ||
                       Process.GetProcessesByName(TargetProcessNameAlt).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private async Task TerminateGameAsync()
        {
            IsLaunchButtonEnabled = false;
            await SetGameRunningStateAsync(true, "正在终止游戏...");

            try
            {
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
                        process.Kill();
                        await process.WaitForExitAsync();
                    }
                    catch { }
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
                    bool currentState = CheckGameProcessRunning();

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

                await Task.Delay(3000, token);
            }
        }
    }
}