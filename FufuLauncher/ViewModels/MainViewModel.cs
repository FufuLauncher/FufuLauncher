/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Activation;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Playback;
using Windows.UI;
using FufuLauncher.Views;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Storage.Streams;

namespace FufuLauncher.ViewModels
{
    public partial class MainViewModel : ObservableRecipient
    {
        private readonly IHoyoverseContentService _contentService;
        private readonly IBackgroundRenderer _backgroundRenderer;
        private readonly ILocalSettingsService _localSettingsService;
        private readonly IHoyoverseCheckinService _checkinService;
        private readonly IUnifiedCheckinService _unifiedCheckinService;
        private readonly IGameLauncherService _gameLauncherService;
        private readonly INotificationService _notificationService;
        private readonly DailyNoteCardService _dailyNoteCardService;
        private readonly DispatcherQueue _dispatcherQueue;
        private static bool _isFirstLoad = true;
        private bool _hasAttemptedAutoCheckin = false;
        private bool _isInternationalAccount = false;
        [ObservableProperty] private Visibility _presetCardVisibility = Visibility.Collapsed;
        [ObservableProperty] private ObservableCollection<PresetModel> _pinnedPresets = new();
        public bool IsPinnedPresetsEmpty => PinnedPresets.Count == 0;

        public IAsyncRelayCommand OpenPresetManagerCommand { get; }
        public IRelayCommand<PresetModel> QuickSwitchPresetCommand { get; }

        [ObservableProperty] private bool _isGameNotLaunching;

        [ObservableProperty] private ImageSource _backgroundImageSource;
        [ObservableProperty] private MediaPlayer _backgroundVideoPlayer;
        private InMemoryRandomAccessStream _backgroundVideoStream;
        [ObservableProperty] private bool _isVideoBackground;
        [ObservableProperty] private bool _isBackgroundLoading;

        [ObservableProperty] private string _customBackgroundPath;
        [ObservableProperty] private bool _hasCustomBackground;
        
        [ObservableProperty] private Visibility _widgetCardVisibility = Visibility.Collapsed;
        [ObservableProperty] private Visibility _widgetGachaVisibility = Visibility.Visible;
        [ObservableProperty] private Visibility _widgetAchievementVisibility = Visibility.Visible;
        [ObservableProperty] private Visibility _widgetInventoryVisibility = Visibility.Visible;
        [ObservableProperty] private Visibility _widgetPlayerRoleVisibility = Visibility.Visible;
        [ObservableProperty] private Visibility _widgetDailyNoteWindowVisibility = Visibility.Visible;
        [ObservableProperty] private Visibility _widgetVideoVisibility = Visibility.Visible;
        [ObservableProperty] private Visibility _widgetBBSVisibility = Visibility.Visible;

        [ObservableProperty] private ObservableCollection<BannerItem> _banners = new();
        [ObservableProperty] private ObservableCollection<PostItem> _activityPosts = new();
        [ObservableProperty] private ObservableCollection<PostItem> _announcementPosts = new();
        [ObservableProperty] private ObservableCollection<PostItem> _infoPosts = new();
        [ObservableProperty] private ObservableCollection<SocialMediaItem> _socialMediaList = new();
        [ObservableProperty] private Brush _panelBackgroundBrush;
        [ObservableProperty] private double _infoCardHeight = 285;
        [ObservableProperty] private string _infoExpandIcon = "\uE70E";
        [ObservableProperty] private ObservableCollection<BackgroundUrlInfo> _availableBackgrounds = new();
        public IAsyncRelayCommand<BackgroundUrlInfo> SelectSpecificBackgroundCommand { get; }
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
        [ObservableProperty] private Visibility _gameNewsCardVisibility = Visibility.Visible;
        [ObservableProperty] private Visibility _checkinCardVisibility = Visibility.Visible;
        [ObservableProperty] private Visibility _dailyNoteCardVisibility = Visibility.Visible;

        [ObservableProperty] private int _currentResin;
        [ObservableProperty] private int _maxResin;
        [ObservableProperty] private string _resinRecoveryTime = "";
        [ObservableProperty] private int _finishedTaskNum;
        [ObservableProperty] private int _totalTaskNum;
        [ObservableProperty] private int _currentHomeCoin;
        [ObservableProperty] private int _maxHomeCoin;
        [ObservableProperty] private int _currentExpeditionNum;
        [ObservableProperty] private int _maxExpeditionNum;
        [ObservableProperty] private bool _isTransformerObtained;
        [ObservableProperty] private string _transformerRecoveryTime = "";

        [ObservableProperty] private Visibility _showResin = Visibility.Visible;
        [ObservableProperty] private Visibility _showDailyTasks = Visibility.Visible;
        [ObservableProperty] private Visibility _showHomeCoin = Visibility.Visible;
        [ObservableProperty] private Visibility _showExpeditions = Visibility.Visible;
        [ObservableProperty] private Visibility _showTransformer = Visibility.Visible;
        [ObservableProperty] private bool _isDailyNoteLoaded;
        
        [ObservableProperty] private SolidColorBrush _launchButtonOverlayBrush = new(Microsoft.UI.Colors.Transparent);

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
        [ObservableProperty] private string _checkinButtonText = "立即签到";
        [ObservableProperty] private string _checkinSummary = "";
        
        [ObservableProperty] private string _checkinStateGlyph = "\uE730";
        [ObservableProperty] private SolidColorBrush _checkinStateBrush = new(Microsoft.UI.Colors.Gray);
        [ObservableProperty] private string _checkinStateTooltip = "\u6E38\u620F\u7B7E\u5230\u72B6\u6001\u52A0\u8F7D\u4E2D";
        
        [ObservableProperty] private string _launchButtonText = "LaunchBtn_SelectPath".GetLocalized();
        [ObservableProperty] private bool _isLaunchButtonEnabled = true;
        [ObservableProperty] private bool _isGameLaunching;

        [ObservableProperty] private bool _useInjection;

        [ObservableProperty] private string _injectionModule = "DLL";
        [ObservableProperty] private ObservableCollection<InjectionModuleInfo> _availableInjectionModules = new();
        public IRelayCommand<InjectionModuleInfo> SelectInjectionModuleCommand { get; }

        [ObservableProperty] private bool _preferVideoBackground = true;

        [ObservableProperty] private SolidColorBrush _gameNewsCardTextBrush = new(Microsoft.UI.Colors.White);
        [ObservableProperty] private SolidColorBrush _launchButtonTextBrush = new(Microsoft.UI.Colors.White);
        [ObservableProperty] private SolidColorBrush _gameCheckinTextBrush = new(Microsoft.UI.Colors.White);
        public string BackgroundTypeToggleText => "切换背景";

        [ObservableProperty] private bool _isGameRunning;
        [ObservableProperty] private string _launchButtonIcon = "\uE768";
        [ObservableProperty] private bool _isBackgroundToggleEnabled = true;
        
        private List<string> _cachedProcessNames;

        private async Task<List<string>> GetTargetProcessNamesAsync()
        {
            if (_cachedProcessNames == null)
            {
                var exeNames = await FufuLauncher.Helpers.GameExeManager.GetExeNamesAsync();
                _cachedProcessNames = exeNames.Select(System.IO.Path.GetFileNameWithoutExtension).ToList();
            }
            return _cachedProcessNames;
        }
        
        private CancellationTokenSource _gameMonitoringCts;
        private bool _cachedGameRunning;
        private DateTimeOffset _lastGameProcessCheck = DateTimeOffset.MinValue;

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
            IUnifiedCheckinService unifiedCheckinService,
            IGameLauncherService gameLauncherService,
            ILauncherService launcherService,
            INavigationService navigationService,
            INotificationService notificationService,
            DailyNoteCardService dailyNoteCardService)
        {
            _contentService = contentService;
            _backgroundRenderer = backgroundRenderer;
            _localSettingsService = localSettingsService;
            _checkinService = checkinService;
            _unifiedCheckinService = unifiedCheckinService;
            _gameLauncherService = gameLauncherService;
            _notificationService = notificationService;
            _dailyNoteCardService = dailyNoteCardService;
            _dispatcherQueue = App.MainWindow.DispatcherQueue;

            WeakReferenceMessenger.Default.Register<FufuLauncher.Messages.TextStyleChangedMessage>(this, async (r, m) =>
            {
                await LoadTextStylesAsync();
            });

            WeakReferenceMessenger.Default.Register<CardVisibilityChangedMessage>(this, async (r, m) =>
            {
                await LoadCardVisibilityAsync();
            });

            WeakReferenceMessenger.Default.Register<AccountChangedMessage>(this, async (r, m) =>
            {
                await ClearDailyNoteDataAsync();
                await LoadDailyNoteAsync();
            });

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
            SelectSpecificBackgroundCommand = new AsyncRelayCommand<BackgroundUrlInfo>(SelectSpecificBackgroundAsync);
            
            OpenPresetManagerCommand = new AsyncRelayCommand(OpenPresetManagerAsync);
            QuickSwitchPresetCommand = new RelayCommand<PresetModel>(QuickSwitchPreset);
            SelectInjectionModuleCommand = new RelayCommand<InjectionModuleInfo>(SelectInjectionModule);

            InitializeInjectionModules();

            WeakReferenceMessenger.Default.Register<GamePathChangedMessage>(this, (r, m) =>
            {
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

        public event Action<bool> InfoCardToggledRequested;

        private void ToggleInfoCard()
        {
            _isInfoCardExpanded = !_isInfoCardExpanded;
            if (_isInfoCardExpanded)
            {
                InfoExpandIcon = "\uE70E";
            }
            else
            {
                InfoExpandIcon = "\uE70D";
            }
            InfoCardToggledRequested?.Invoke(_isInfoCardExpanded);
        }
        
        private async Task LoadAvailableBackgroundsAsync()
        {
            try
            {
                var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
                int serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
                var server = (Models.ServerType)serverValue;

                var backgroundService = App.GetService<IHoyoverseBackgroundService>();
                var backgrounds = await backgroundService.GetAvailableBackgroundsAsync(server);

                await UpdateUI(() =>
                {
                    AvailableBackgrounds.Clear();
                    foreach (var bg in backgrounds)
                    {
                        AvailableBackgrounds.Add(bg);
                    }
                });

                // 后台预加载所有图片背景到文件缓存
                var imageUrls = backgrounds
                    .Where(b => !b.IsVideo && !string.IsNullOrEmpty(b.Url))
                    .Select(b => b.Url);
                _ = _backgroundRenderer.PreloadImageBackgroundsAsync(imageUrls);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载可选背景失败: {ex.Message}");
            }
        }
        
        private async Task SelectSpecificBackgroundAsync(BackgroundUrlInfo info)
        {
            if (info == null) return;
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundUrl", info.Url);
            await _localSettingsService.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", info.IsVideo);
            
            WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
        }

        public async Task InitializeAsync()
        {
            await LoadTextStylesAsync();
            await LoadUserPreferencesAsync();
            await LoadCustomBackgroundPathAsync();
            
            var loadBackgroundTask = LoadBackgroundAsync();
            var loadAvailableBackgroundsTask = LoadAvailableBackgroundsAsync();
            var loadContentTask = LoadContentAsync();
            var loadCheckinStatusTask = LoadCheckinStatusAsync();
            var loadDailyNoteTask = LoadDailyNoteAsync();
            var getInjectionTask = _gameLauncherService.GetUseInjectionAsync();
            
            var loadPinnedPresetsTask = LoadPinnedPresetsAsync();
            
            await Task.WhenAll(
                loadBackgroundTask,
                loadAvailableBackgroundsTask,
                loadContentTask,
                loadCheckinStatusTask,
                loadDailyNoteTask,
                getInjectionTask,
                loadPinnedPresetsTask
            );

            UseInjection = getInjectionTask.Result;
            await LoadInjectionModuleAsync();
            
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
            Debug.WriteLine("[MainViewModel] 页面已返回，正在刷新服务器配置...");
            
            await RefreshSettingsAsync();
            await LoadUserPreferencesAsync();
            
            var refreshGameTask = ForceRefreshGameStateAsync();
            var checkinTask = LoadCheckinStatusAsync();
            var dailyNoteTask = LoadDailyNoteAsync();

            await Task.WhenAll(refreshGameTask, checkinTask, dailyNoteTask);
        }
        
        private async Task RefreshSettingsAsync()
        {
            _cachedProcessNames = null;
            var isInternationalObj = await _localSettingsService.ReadSettingAsync("IsInternationalAccount");
            _isInternationalAccount = isInternationalObj != null && isInternationalObj.ToString().ToLower() == "true";
            Debug.WriteLine($"[MainViewModel] 配置刷新: {_isInternationalAccount}");
        }

        private async Task LoadCardVisibilityAsync()
        {
            var hideNewsCardJson = await _localSettingsService.ReadSettingAsync("IsHideGameNewsCardEnabled");
            bool isNewsCardHidden = hideNewsCardJson != null && Convert.ToBoolean(hideNewsCardJson);
            GameNewsCardVisibility = isNewsCardHidden ? Visibility.Collapsed : Visibility.Visible;

            var hideCheckinCardJson = await _localSettingsService.ReadSettingAsync("IsHideCheckinCardEnabled");
            bool isCheckinCardHidden = hideCheckinCardJson != null && Convert.ToBoolean(hideCheckinCardJson);
            CheckinCardVisibility = isCheckinCardHidden ? Visibility.Collapsed : Visibility.Visible;

            var hideDailyNoteCardJson = await _localSettingsService.ReadSettingAsync("IsHideDailyNoteCardEnabled");
            bool isDailyNoteCardHidden = hideDailyNoteCardJson == null || Convert.ToBoolean(hideDailyNoteCardJson);
            DailyNoteCardVisibility = isDailyNoteCardHidden ? Visibility.Collapsed : Visibility.Visible;

            int activeCount = 0;

            var showResinJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteResin");
            bool showResin = showResinJson == null || Convert.ToBoolean(showResinJson);
            if (showResin) activeCount++;
            ShowResin = showResin ? Visibility.Visible : Visibility.Collapsed;

            var showDailyTasksJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteDailyTasks");
            bool showDailyTasks = (showDailyTasksJson == null || Convert.ToBoolean(showDailyTasksJson)) && activeCount < 3;
            if (showDailyTasks) activeCount++;
            ShowDailyTasks = showDailyTasks ? Visibility.Visible : Visibility.Collapsed;

            var showHomeCoinJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteHomeCoin");
            bool showHomeCoin = (showHomeCoinJson == null || Convert.ToBoolean(showHomeCoinJson)) && activeCount < 3;
            if (showHomeCoin) activeCount++;
            ShowHomeCoin = showHomeCoin ? Visibility.Visible : Visibility.Collapsed;

            var showExpeditionsJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteExpeditions");
            bool showExpeditions = (showExpeditionsJson == null || Convert.ToBoolean(showExpeditionsJson)) && activeCount < 3;
            if (showExpeditions) activeCount++;
            ShowExpeditions = showExpeditions ? Visibility.Visible : Visibility.Collapsed;

            var showTransformerJson = await _localSettingsService.ReadSettingAsync("ShowDailyNoteTransformer");
            bool showTransformer = (showTransformerJson == null || Convert.ToBoolean(showTransformerJson)) && activeCount < 3;
            ShowTransformer = showTransformer ? Visibility.Visible : Visibility.Collapsed;
            
            var showPresetCardJson = await _localSettingsService.ReadSettingAsync("IsShowPresetCardEnabled");
            bool isShowPresetCard = showPresetCardJson != null && Convert.ToBoolean(showPresetCardJson);
            PresetCardVisibility = isShowPresetCard ? Visibility.Visible : Visibility.Collapsed;
            
            var isShowWidgetCardJson = await _localSettingsService.ReadSettingAsync("IsShowWidgetCardEnabled");
            bool isShowWidgetCard = isShowWidgetCardJson != null && Convert.ToBoolean(isShowWidgetCardJson);
            WidgetCardVisibility = isShowWidgetCard ? Visibility.Visible : Visibility.Collapsed;

            var showWidgetGachaJson = await _localSettingsService.ReadSettingAsync("ShowWidgetGacha");
            WidgetGachaVisibility = (showWidgetGachaJson == null || Convert.ToBoolean(showWidgetGachaJson)) ? Visibility.Visible : Visibility.Collapsed;

            var showWidgetAchievementJson = await _localSettingsService.ReadSettingAsync("ShowWidgetAchievement");
            WidgetAchievementVisibility = (showWidgetAchievementJson == null || Convert.ToBoolean(showWidgetAchievementJson)) ? Visibility.Visible : Visibility.Collapsed;

            var showWidgetInventoryJson = await _localSettingsService.ReadSettingAsync("ShowWidgetInventory");
            WidgetInventoryVisibility = (showWidgetInventoryJson == null || Convert.ToBoolean(showWidgetInventoryJson)) ? Visibility.Visible : Visibility.Collapsed;

            var showWidgetPlayerRoleJson = await _localSettingsService.ReadSettingAsync("ShowWidgetPlayerRole");
            WidgetPlayerRoleVisibility = (showWidgetPlayerRoleJson == null || Convert.ToBoolean(showWidgetPlayerRoleJson)) ? Visibility.Visible : Visibility.Collapsed;

            var showWidgetDailyNoteWindowJson = await _localSettingsService.ReadSettingAsync("ShowWidgetDailyNoteWindow");
            WidgetDailyNoteWindowVisibility = (showWidgetDailyNoteWindowJson == null || Convert.ToBoolean(showWidgetDailyNoteWindowJson)) ? Visibility.Visible : Visibility.Collapsed;

            var showWidgetVideoJson = await _localSettingsService.ReadSettingAsync("ShowWidgetVideo");
            WidgetVideoVisibility = (showWidgetVideoJson == null || Convert.ToBoolean(showWidgetVideoJson)) ? Visibility.Visible : Visibility.Collapsed;

            var showWidgetBBSJson = await _localSettingsService.ReadSettingAsync("ShowWidgetBBS");
            WidgetBBSVisibility = (showWidgetBBSJson == null || Convert.ToBoolean(showWidgetBBSJson)) ? Visibility.Visible : Visibility.Collapsed;
        }
        
private async Task OpenPresetManagerAsync()
{
    var window = new PresetManagerWindow();
    window.Closed += async (s, e) =>
    {
        await LoadPinnedPresetsAsync();
    };
    window.Activate();
}

        private async Task LoadUserPreferencesAsync()
        {
            await LoadCardVisibilityAsync();
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

        private async Task LoadTextStylesAsync()
        {
            var newsColor = await _localSettingsService.ReadSettingAsync("GameNewsCardTextColor") as string ?? "#FFFFFF";
            var newsOpacity = Convert.ToDouble(await _localSettingsService.ReadSettingAsync("GameNewsCardTextOpacity") ?? 1.0);
            GameNewsCardTextBrush = CreateBrush(newsColor, newsOpacity);

            var launchColor = await _localSettingsService.ReadSettingAsync("LaunchButtonTextColor") as string ?? "#FFFFFF";
            var launchOpacity = Convert.ToDouble(await _localSettingsService.ReadSettingAsync("LaunchButtonTextOpacity") ?? 1.0);
            LaunchButtonTextBrush = CreateBrush(launchColor, launchOpacity);

            var checkinColor = await _localSettingsService.ReadSettingAsync("GameCheckinTextColor") as string ?? "#FFFFFF";
            var checkinOpacity = Convert.ToDouble(await _localSettingsService.ReadSettingAsync("GameCheckinTextOpacity") ?? 1.0);
            GameCheckinTextBrush = CreateBrush(checkinColor, checkinOpacity);
            
            var launchOverlayColor = await _localSettingsService.ReadSettingAsync("LaunchButtonOverlayColor") as string ?? "#0078D7";
            LaunchButtonOverlayBrush = CreateBrush(launchOverlayColor, 0.4);
        }

        private SolidColorBrush CreateBrush(string hex, double opacity)
        {
            try
            {
                if (string.IsNullOrEmpty(hex)) hex = "#FFFFFF";
                if (!hex.StartsWith("#")) hex = "#" + hex;
                if (hex.Length == 4)
                {
                    hex = "#" + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3];
                }
                if (hex.Length != 7 && hex.Length != 9) hex = "#FFFFFF";
                
                byte a = 255;
                byte r, g, b;
                
                if (hex.Length == 9)
                {
                    a = Convert.ToByte(hex.Substring(1, 2), 16);
                    r = Convert.ToByte(hex.Substring(3, 2), 16);
                    g = Convert.ToByte(hex.Substring(5, 2), 16);
                    b = Convert.ToByte(hex.Substring(7, 2), 16);
                }
                else
                {
                    r = Convert.ToByte(hex.Substring(1, 2), 16);
                    g = Convert.ToByte(hex.Substring(3, 2), 16);
                    b = Convert.ToByte(hex.Substring(5, 2), 16);
                }
                
                a = (byte)(a * opacity);
                
                return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
            }
            catch
            {
                return new SolidColorBrush(Windows.UI.Color.FromArgb((byte)(255 * opacity), 255, 255, 255));
            }
        }

        public async Task LoadCustomBackgroundPathAsync()
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

    try
    {
        if (HasCustomBackground && !string.IsNullOrEmpty(CustomBackgroundPath) && File.Exists(CustomBackgroundPath))
        {
            await UpdateUI(() => TryLoadImage(CustomBackgroundPath));
        }
        else
        {
            var serverJson = await _localSettingsService.ReadSettingAsync("BackgroundServerKey");
            var server = Models.ServerType.CN;
            try { if (serverJson != null) server = (Models.ServerType)Convert.ToInt32(serverJson); } catch { }
            
            var bgResult = await _backgroundRenderer.GetBackgroundAsync(server, PreferVideoBackground);

            await UpdateUI(() =>
            {
                if (bgResult != null)
                {
                    if (bgResult.IsVideo && bgResult.VideoSource != null)
                    {
                        SetupVideoPlayer(bgResult.VideoSource, bgResult.VideoStream);
                    }
                    else if (!bgResult.IsVideo && bgResult.ImageSource != null)
                    {
                        BackgroundImageSource = bgResult.ImageSource;
                        IsVideoBackground = false;
                    }
                    else
                    {
                        LoadFallbackImage();
                    }
                }
                else
                {
                    LoadFallbackImage();
                }
            });
        }
    }
    catch (NotSupportedException ex) when (ex.Message == "IMAGE_DECODE_FAILED")
    {
        await UpdateUI(() =>
        {
            _notificationService.Show("背景解码失败", "系统缺少 WebP 图像扩展。已回退至静态背景。", NotificationType.Error, 6000);
            LoadFallbackImage();
        });
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"背景加载异常: {ex.Message}");
        await UpdateUI(LoadFallbackImage);
    }
    finally
    {
        await UpdateUI(() => IsBackgroundLoading = false);
    }
}

private void SetupVideoPlayer(MediaSource source, InMemoryRandomAccessStream stream)
{
    _backgroundVideoStream = stream;

    if (BackgroundVideoPlayer == null)
    {
        BackgroundVideoPlayer = MediaPlayerHelper.CreateLoopingMutedPlayer();
        BackgroundVideoPlayer.MediaFailed += BackgroundVideoPlayer_MediaFailed;
    }
    BackgroundVideoPlayer.Source = source; 
    BackgroundVideoPlayer.Play();
    IsVideoBackground = true;
}

private void ClearBackground()
{
    BackgroundImageSource = null;
    if (BackgroundVideoPlayer != null)
    {
        BackgroundVideoPlayer.Pause();
        BackgroundVideoPlayer.MediaFailed -= BackgroundVideoPlayer_MediaFailed;
        try
        {
            BackgroundVideoPlayer.Dispose();
        }
        catch { }
        BackgroundVideoPlayer = null;
    }
    _backgroundVideoStream?.Dispose();
    _backgroundVideoStream = null;
    IsVideoBackground = false;
}

private void BackgroundVideoPlayer_MediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
{
    Debug.WriteLine($"背景视频触发MediaFailed，错误类型: {args.Error}");
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
                var server = (Models.ServerType)serverValue;

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
            try
            {
                _gameMonitoringCts?.Cancel();
                _gameMonitoringCts?.Dispose();
            }
            catch { }

            if (BackgroundVideoPlayer != null)
            {
                try
                {
                    BackgroundVideoPlayer.Pause();
                    BackgroundVideoPlayer = null;
                }
                catch { }
            }

            WeakReferenceMessenger.Default.UnregisterAll(this);
        }

        private void UpdateCheckinIconState(string statusText)
        {
            bool isSigned = !string.IsNullOrEmpty(statusText) &&
                            (statusText.Contains("成功") || statusText.Contains("已"));

            if (isSigned)
            {
                CheckinStateGlyph = "";
                CheckinStateBrush = new SolidColorBrush(Microsoft.UI.Colors.LightGreen);
                CheckinStateTooltip = "Checkin_Signed".GetLocalized();
            }
            else
            {
                CheckinStateGlyph = "";
                CheckinStateBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray) { Opacity = 0.8 };
                CheckinStateTooltip = "Checkin_Unsigned".GetLocalized();
            }

            IsCheckinButtonEnabled = true;
            CheckinButtonText = "立即签到";
        }

        private async Task LoadCheckinStatusAsync()
        {
            if (_localSettingsService == null) return;

            var isIntlRaw = await _localSettingsService.ReadSettingAsync("IsInternationalAccount");
            _isInternationalAccount = isIntlRaw != null && isIntlRaw.ToString().ToLower() == "true";

            try
            {
                var targetUidObj = await _localSettingsService.ReadSettingAsync("CustomCheckinUid");
                string targetUid = targetUidObj?.ToString();

                
                var accountManager = App.GetService<AccountManager>();
                var activeId = accountManager.ActiveAccountId;
                if (activeId == null)
                {
                    CheckinStatusText = "未登录";
                    CheckinSummary = "请先登录账户";
                    UpdateCheckinIconState("Fail");
                    return;
                }

                var cookies = await accountManager.LoadCookiesAsync(activeId);
                var entry = accountManager.GetActiveAccountEntry();
                if (cookies == null || entry == null)
                {
                    CheckinStatusText = "凭证加载失败";
                    CheckinSummary = "无法获取账户凭证";
                    UpdateCheckinIconState("Fail");
                    return;
                }

                string serverType = entry.ServerType; 

                var (status, summary) = await _checkinService.GetCheckinStatusAsync(targetUid, cookies, serverType);

                CheckinStatusText = status;
                CheckinSummary = summary;
                UpdateCheckinIconState(status);

                if (!_hasAttemptedAutoCheckin)
                {
                    var autoCheckinObj = await _localSettingsService.ReadSettingAsync("IsAutoCheckinEnabled");
                    bool isAutoCheckinEnabled = autoCheckinObj != null && Convert.ToBoolean(autoCheckinObj);
                    bool isSigned = !string.IsNullOrEmpty(status) && (status.Contains("成功") || status.Contains("已"));

                    if (isAutoCheckinEnabled && !isSigned)
                    {
                        _hasAttemptedAutoCheckin = true;
                        await ExecuteCheckinAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                CheckinStatusText = "加载失败";
                CheckinSummary = ex.Message;
                UpdateCheckinIconState("Fail");
            }
        }



        private async Task ExecuteCheckinAsync()
        {
            IsCheckinButtonEnabled = false;
            CheckinButtonText = "签到中...";
            CheckinStatusText = "签到中...";
            CheckinSummary = "正在执行签到任务...";

            //await RefreshSettingsAsync();

            try
            {
                var progress = new Progress<string>(msg =>
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        CheckinButtonText = "签到中...";
                        CheckinSummary = msg;
                    });
                });

                var unifiedResult = await _unifiedCheckinService.ExecuteAllCheckinsAsync(progress);

                CheckinStatusText = unifiedResult.OverallSuccess ? "签到完成" : "签到部分失败";
                CheckinSummary = unifiedResult.SummaryMessage;
                UpdateCheckinIconState(unifiedResult.OverallSuccess ? "已签到" : "Fail");

                var notificationTitle = unifiedResult.NotificationType switch
                {
                    NotificationType.Success => "签到完成",
                    NotificationType.Warning => "签到部分失败",
                    _ => "签到失败"
                };
                _notificationService.Show(notificationTitle, unifiedResult.GetDetailedSummary(), unifiedResult.NotificationType, 5000);
            }
            catch (Exception ex)
            {
                CheckinStatusText = "执行失败";
                CheckinSummary = ex.Message;
                UpdateCheckinIconState("Fail");
                _notificationService.Show("签到异常", ex.Message, NotificationType.Error, 3000);
            }
            finally
            {
                await Task.Delay(2000);
                await LoadCheckinStatusAsync();
            }
        }




        private void RefreshPresetsActiveStatus(string activeId)
{
    foreach (var preset in PinnedPresets)
    {
        preset.IsActive = (preset.Id == activeId);
    }
}

private string GetActivePresetIdFromFile()
{
    string stateFile = Path.Combine(Helpers.AppPaths.PluginPresetsDir, "active_state.json");
    if (File.Exists(stateFile))
    {
        try
        {
            var stateContent = File.ReadAllText(stateFile);
            var stateDict = JsonSerializer.Deserialize<Dictionary<string, string>>(stateContent);
            if (stateDict != null && stateDict.TryGetValue("ActiveId", out var id))
            {
                return id;
            }
        }
        catch { }
    }
    return string.Empty;
}

public async Task LoadPinnedPresetsAsync()
{
    var pinnedIdsJson = await _localSettingsService.ReadSettingAsync("PinnedPresetIds");
    List<string> pinnedIds = new();
    if (pinnedIdsJson != null)
    {
        try { pinnedIds = JsonSerializer.Deserialize<List<string>>(pinnedIdsJson.ToString()); } catch { }
    }

    string presetsDir = Helpers.AppPaths.PluginPresetsDir;
    string activeId = GetActivePresetIdFromFile();
    
    await _dispatcherQueue.EnqueueAsync(() =>
    {
        PinnedPresets.Clear();
        if (Directory.Exists(presetsDir))
        {
            foreach (var file in Directory.GetFiles(presetsDir, "*.json"))
            {
                if (file.EndsWith("active_state.json")) continue;
                try
                {
                    var content = File.ReadAllText(file);
                    var preset = JsonSerializer.Deserialize<PresetModel>(content);
                    if (preset != null && pinnedIds.Contains(preset.Id))
                    {
                        preset.FilePath = file;
                        preset.IsActive = (preset.Id == activeId);
                        PinnedPresets.Add(preset);
                    }
                }
                catch { }
            }
        }
        OnPropertyChanged(nameof(IsPinnedPresetsEmpty));
    });
}

private void QuickSwitchPreset(PresetModel targetPreset)
{
    if (targetPreset == null) return;
    try
    {
        var pluginVM = new PluginSettingsViewModel();

        var fullPreset = pluginVM.AvailablePresets.FirstOrDefault(p => p.Id == targetPreset.Id);
        
        if (fullPreset != null)
        {
            pluginVM.SwitchPreset(fullPreset);
            RefreshPresetsActiveStatus(targetPreset.Id);
        }
        else
        {
            _notificationService.Show("预设切换失败", "未找到对应的预设配置", NotificationType.Error, 3000);
        }
    }
    catch (Exception ex)
    {
        _notificationService.Show("预设切换失败", ex.Message, NotificationType.Error, 3000);
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
                LaunchButtonText = "LaunchBtn_ExitGame".GetLocalized();
                LaunchButtonIcon = "\uE711";
            }
            else
            {
                if (hasPath)
                {
                    LaunchButtonText = "LaunchBtn_StartGame".GetLocalized();
                }
                else
                {
                    LaunchButtonText = "LaunchBtn_SelectPath".GetLocalized();
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
                _notificationService.Show("LaunchErr_NoGamePath".GetLocalized(), "LaunchErr_NoGamePathMsg".GetLocalized(), NotificationType.Error, 0);
                return;
            }

            IsGameLaunching = true;
            IsLaunchButtonEnabled = false;

            try
            {
                var result = await _gameLauncherService.LaunchGameAsync();

                if (result.Success)
                {
                    await ForceRefreshGameStateAsync();
                    await ApplyPostLaunchBehaviorAsync();
                }
                else
                {
                    _notificationService.Show("LaunchErr_LaunchFailed".GetLocalized(), result.ErrorMessage, NotificationType.Error, 0);
                }
            }
            finally
            {
                IsGameLaunching = false;
                IsLaunchButtonEnabled = true;
                await ForceRefreshGameStateAsync();
            }
        }

        private async Task ApplyPostLaunchBehaviorAsync()
        {
            var obj = await _localSettingsService.ReadSettingAsync("PostLaunchBehavior");
            if (obj is not string s || !Enum.TryParse<PostLaunchBehavior>(s, out var behavior))
                return;

            switch (behavior)
            {
                case PostLaunchBehavior.MinimizeToTray:
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        App.MainWindow.Hide();
                    });
                    break;

                case PostLaunchBehavior.Exit:
                    await SaveStateBeforeExitAsync();
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        Application.Current.Exit();
                    });
                    break;
            }
        }

        private async Task SaveStateBeforeExitAsync()
        {
            try
            {               
                var windowSaveService = App.GetService<ILocalSettingsService>();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                if (appWindow != null)
                {
                    var size = appWindow.Size;
                    await windowSaveService.SaveSettingAsync("WindowWidth", size.Width);
                    await windowSaveService.SaveSettingAsync("WindowHeight", size.Height);
                }
            }
            catch
            {
                // 保存状态失败不影响退出
            }
        }

        private async Task OpenScreenshotFolderAsync()
        {
            var savedPath = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
            var gamePath = savedPath?.ToString()?.Trim('"')?.Trim();

            var gameScreenshotPath = "";
            if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
            {
                gameScreenshotPath = Path.Combine(gamePath, "ScreenShot");
            }
            
            var customPathObj = await _localSettingsService.ReadSettingAsync("ScreenshotSavePath");
            var customScreenshotPath = customPathObj?.ToString()?.Trim('"')?.Trim();
            if (string.IsNullOrEmpty(customScreenshotPath))
            {
                customScreenshotPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "FufuScreenshots");
            }
            
            bool gameExists = !string.IsNullOrEmpty(gameScreenshotPath) && Directory.Exists(gameScreenshotPath);
            bool customExists = Directory.Exists(customScreenshotPath);

            if (!gameExists && !customExists)
            {
                _notificationService.Show("Screenshot_FolderNotFound".GetLocalized(), "Screenshot_FolderNotFoundMsg".GetLocalized(), NotificationType.Error, 0);
                return;
            }

            try
            {
                var galleryWindow = new ScreenshotGalleryWindow(
                    gameScreenshotPath ?? "",
                    customScreenshotPath ?? "");
                galleryWindow.Activate();
            }
            catch (Exception ex)
            {
                _notificationService.Show("打开失败", $"无法初始化截图窗口: {ex.Message}", NotificationType.Error, 0);
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

        private void InitializeInjectionModules()
        {
            AvailableInjectionModules = new ObservableCollection<InjectionModuleInfo>
            {
                new() { Id = "DLL", Name = "InjectionBuiltIn".GetLocalized(), Description = "InjectionBuiltInDesc".GetLocalized(), IsSelected = true },
                new() { Id = "EXE", Name = "InjectionStandalone".GetLocalized(), Description = "InjectionStandaloneDesc".GetLocalized(), IsSelected = false }
            };
        }

        private void SelectInjectionModule(InjectionModuleInfo module)
        {
            if (module == null) return;

            InjectionModule = module.Id;

            foreach (var m in AvailableInjectionModules)
            {
                m.IsSelected = m.Id == module.Id;
            }

            _ = _localSettingsService.SaveSettingAsync("InjectionModule", module.Id);
        }

        private async Task LoadInjectionModuleAsync()
        {
            try
            {
                var saved = await _localSettingsService.ReadSettingAsync("InjectionModule");
                var moduleId = saved?.ToString() ?? "DLL";
                InjectionModule = moduleId;

                foreach (var m in AvailableInjectionModules)
                {
                    m.IsSelected = m.Id == moduleId;
                }
            }
            catch
            {
                // ignored
            }
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
            bool actualState = await CheckGameProcessRunningAsync(forceRefresh: true);
            if (actualState != IsGameRunning)
            {
                await SetGameRunningStateAsync(actualState);
            }
        }
        
        private async Task<bool> CheckGameProcessRunningAsync(bool forceRefresh = false)
        {
            var now = DateTimeOffset.UtcNow;
            var currentInterval = IsGameRunning ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(1);

            if (!forceRefresh && now - _lastGameProcessCheck < currentInterval)
            {
                return _cachedGameRunning;
            }

            try
            {
                var processNames = await GetTargetProcessNamesAsync();
                _cachedGameRunning = processNames.Any(HasRunningProcess);
            }
            catch
            {
                _cachedGameRunning = false;
            }

            _lastGameProcessCheck = now;
            return _cachedGameRunning;
        }

        public async Task LoadDailyNoteAsync()
        {
            // 便签卡片隐藏时不发起任何 API 请求
            var hideJson = await _localSettingsService.ReadSettingAsync("IsHideDailyNoteCardEnabled");
            if (hideJson != null && Convert.ToBoolean(hideJson))
            {
                IsDailyNoteLoaded = false;
                Debug.WriteLine("[DailyNote] 便签卡片已隐藏，跳过API请求");
                return;
            }

            try
            {
                var accountManager = App.GetService<AccountManager>();
                var activeId = accountManager.ActiveAccountId;

                if (activeId == null)
                {
                    Debug.WriteLine("[DailyNote] 未找到绑定账号");
                    await ClearDailyNoteDataAsync();
                    return;
                }

                var cookies = await accountManager.LoadCookiesAsync(activeId);
                var entry = accountManager.GetActiveAccountEntry();
                if (cookies == null || entry == null)
                {
                    await ClearDailyNoteDataAsync();
                    return;
                }

                var customUid = await _localSettingsService.ReadSettingAsync("CustomCheckinUid");
                string targetUid = customUid?.ToString()?.Trim();

                
                var uids = await _checkinService.GetBoundUidsAsync(cookies, entry.ServerType);
                if (uids.Count == 0)
                {
                    Debug.WriteLine("[DailyNote] 未找到绑定账号");
                    return;
                }

                string roleId = string.IsNullOrEmpty(targetUid) ? uids[0] : targetUid;
                string server = roleId.StartsWith("5") ? "cn_qd01" : "cn_gf01";

                var dailyNoteData = await _dailyNoteCardService.LoadCardDataAsync(roleId, server, cookies);





                await UpdateUI(() =>
                {
                    CurrentResin = dailyNoteData.CurrentResin;
                    MaxResin = dailyNoteData.MaxResin;
                    FinishedTaskNum = dailyNoteData.FinishedTaskNum;
                    TotalTaskNum = dailyNoteData.TotalTaskNum;
                    CurrentHomeCoin = dailyNoteData.CurrentHomeCoin;
                    MaxHomeCoin = dailyNoteData.MaxHomeCoin;
                    CurrentExpeditionNum = dailyNoteData.CurrentExpeditionNum;
                    MaxExpeditionNum = dailyNoteData.MaxExpeditionNum;
                    IsTransformerObtained = dailyNoteData.IsTransformerObtained;
                    TransformerRecoveryTime = dailyNoteData.TransformerRecoveryTime;
                    
                    IsDailyNoteLoaded = true;
                });

                Debug.WriteLine("[DailyNote] 便签数据加载成功");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[DailyNote] 加载便签数据失败: {ex.Message}");
            }
        }

        private async Task ClearDailyNoteDataAsync()
        {
            await UpdateUI(() =>
            {
                CurrentResin = 0;
                MaxResin = 0;
                FinishedTaskNum = 0;
                TotalTaskNum = 0;
                CurrentHomeCoin = 0;
                MaxHomeCoin = 0;
                CurrentExpeditionNum = 0;
                MaxExpeditionNum = 0;
                IsTransformerObtained = false;
                TransformerRecoveryTime = "";
                IsDailyNoteLoaded = false;
            });
        }

        private static bool HasRunningProcess(string processName)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    if (!process.HasExited) return true;
                }
            }

            return false;
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
    await SetGameRunningStateAsync(true, "LaunchBtn_Terminating".GetLocalized());

    try
    {
        var savedPathObj = await _localSettingsService.ReadSettingAsync("GameInstallationPath");
        var gamePath = savedPathObj?.ToString()?.Trim('"')?.Trim();

        var exeNames = await Helpers.GameExeManager.GetExeNamesAsync();
        var processNames = exeNames.Select(Path.GetFileNameWithoutExtension).ToList();

        var processes = new List<Process>();
        foreach (var name in processNames)
        {
            processes.AddRange(Process.GetProcessesByName(name));
        }

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
                
                int checkDelay = IsGameRunning ? 1000 : 1000;
                await Task.Delay(checkDelay, token);
            }
        }
    }
}

