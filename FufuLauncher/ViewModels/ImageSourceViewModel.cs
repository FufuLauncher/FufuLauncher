/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.ViewModels;

public enum ImageSourceTab
{
    Official,
    RandomBlueArchive,
    Bing,
    Custom,
    Slideshow
}

public sealed record BingYearOption(int Year, string Label);

public partial class ImageSourceViewModel : ObservableObject
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IHoyoverseBackgroundService _hoyoverseBackgroundService;
    private readonly BingWallpaperService _bingWallpaperService;
    private readonly IFilePickerService _filePickerService;
    private readonly IDevBuildDetectionService _devBuildDetectionService;

    private readonly SemaphoreSlim _sourceFlagsGate = new(1, 1);
    private bool _isLoadingSettings;
    private bool _isStepping;
    private bool _isUpdatingRandomPin;
    private int _requestVersion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOfficialSelected))]
    [NotifyPropertyChangedFor(nameof(IsRandomSelected))]
    [NotifyPropertyChangedFor(nameof(IsBingSelected))]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    [NotifyPropertyChangedFor(nameof(IsSlideshowSelected))]
    [NotifyPropertyChangedFor(nameof(CanStep))]
    private ImageSourceTab _selectedTab;

    [ObservableProperty] private string _sourceTitle = string.Empty;
    [ObservableProperty] private string _sourceCaption = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusText = string.Empty;

    [ObservableProperty] private string _bingCopyright = string.Empty;
    [ObservableProperty] private bool _bingChangeOnStartup = true;
    private bool _isBingOfficial;
    private BingYearOption? _selectedBingYearOption;

    public IReadOnlyList<BingYearOption> BingYearOptions
    {
        get;
    }

    public bool IsBingOfficial
    {
        get => _isBingOfficial;
        set
        {
            if (_isBingOfficial == value) return;
            OnPropertyChanging(nameof(IsBingOfficial));
            _isBingOfficial = value;
            OnPropertyChanged(nameof(IsBingOfficial));
            OnIsBingOfficialChanged(value);
        }
    }

    public BingYearOption? SelectedBingYearOption
    {
        get => _selectedBingYearOption;
        set
        {
            if (Equals(_selectedBingYearOption, value)) return;
            OnPropertyChanging(nameof(SelectedBingYearOption));
            _selectedBingYearOption = value;
            OnPropertyChanged(nameof(SelectedBingYearOption));
            OnSelectedBingYearOptionChanged(value);
        }
    }

    [ObservableProperty] private string _customBackgroundApiUrl = string.Empty;
    [ObservableProperty] private string _currentBackgroundApiUrl = string.Empty;
    [ObservableProperty] private string _customPath = string.Empty;
    [ObservableProperty] private string _slideshowFolder = string.Empty;
    [ObservableProperty] private int _slideshowInterval = 60;
    [ObservableProperty] private ServerType _selectedServer;
    [ObservableProperty] private RandomBackgroundType _randomType = RandomBackgroundType.All;
    private bool _isRandomBackgroundPinned;

    public bool IsRandomBackgroundPinned
    {
        get => _isRandomBackgroundPinned;
        set
        {
            if (_isRandomBackgroundPinned == value) return;
            OnPropertyChanging(nameof(IsRandomBackgroundPinned));
            _isRandomBackgroundPinned = value;
            OnPropertyChanged(nameof(IsRandomBackgroundPinned));
            OnIsRandomBackgroundPinnedChanged(value);
        }
    }

    [ObservableProperty] private bool _hasCustomBackground;
    [ObservableProperty] private bool _hasSlideshowFolder;
    [ObservableProperty] private bool _isPreviewLoading;
    [ObservableProperty] private bool _isPreviewFallback;
    [ObservableProperty] private bool _isPreviewVideo;
    [ObservableProperty] private string _previewGlyph = "\uEB9F";
    [ObservableProperty] private ImageSource? _previewImage;

    public bool IsOfficialSelected => SelectedTab == ImageSourceTab.Official;

    public bool IsRandomSelected => SelectedTab == ImageSourceTab.RandomBlueArchive;

    public bool IsBingSelected => SelectedTab == ImageSourceTab.Bing;

    public bool IsCustomSelected => SelectedTab == ImageSourceTab.Custom;

    public bool IsSlideshowSelected => SelectedTab == ImageSourceTab.Slideshow;

    public bool CanStep => SelectedTab != ImageSourceTab.Custom;

    public bool IsBusy => _isStepping;

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

    public IAsyncRelayCommand NextCommand
    {
        get;
    }

    public IAsyncRelayCommand PreviousCommand
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

    public IAsyncRelayCommand SelectSlideshowFolderCommand
    {
        get;
    }

    public IAsyncRelayCommand ClearSlideshowFolderCommand
    {
        get;
    }

    public IAsyncRelayCommand SaveBackgroundApiCommand
    {
        get;
    }

    public IAsyncRelayCommand ResetBackgroundApiCommand
    {
        get;
    }

    public ImageSourceViewModel(
        ILocalSettingsService localSettingsService,
        IHoyoverseBackgroundService hoyoverseBackgroundService,
        BingWallpaperService bingWallpaperService,
        IFilePickerService filePickerService,
        IDevBuildDetectionService devBuildDetectionService)
    {
        _localSettingsService = localSettingsService;
        _hoyoverseBackgroundService = hoyoverseBackgroundService;
        _bingWallpaperService = bingWallpaperService;
        _filePickerService = filePickerService;
        _devBuildDetectionService = devBuildDetectionService;

        NextCommand = new AsyncRelayCommand(() => StepAsync(1));
        PreviousCommand = new AsyncRelayCommand(() => StepAsync(-1));
        SelectCustomBackgroundCommand = new AsyncRelayCommand(SelectCustomBackgroundAsync);
        ClearCustomBackgroundCommand = new AsyncRelayCommand(ClearCustomBackgroundAsync);
        SelectSlideshowFolderCommand = new AsyncRelayCommand(SelectSlideshowFolderAsync);
        ClearSlideshowFolderCommand = new AsyncRelayCommand(ClearSlideshowFolderAsync);
        SaveBackgroundApiCommand = new AsyncRelayCommand(SaveBackgroundApiAsync);
        ResetBackgroundApiCommand = new AsyncRelayCommand(ResetBackgroundApiAsync);

        var years = new List<BingYearOption>
        {
            new(0, "ImageSource_BingYearAll".GetLocalized())
        };
        for (var year = DateTime.Now.Year; year >= 2019; year--)
            years.Add(new BingYearOption(year, year.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        BingYearOptions = years;
    }

    private sealed record ImageSourceSettingsSnapshot(
        ServerType Server,
        string CustomApiUrl,
        string CustomPathValue,
        string SlideshowFolderValue,
        int SlideshowIntervalValue,
        RandomBackgroundType RandomTypeValue,
        string BingCopyrightValue,
        bool BingChangeOnStartupValue,
        bool IsRandom,
        bool IsPinned,
        bool IsBing,
        bool IsBackgroundEnabled,
        bool IsSlideshow,
        int BingYear,
        bool IsBingOfficial);

    public async Task InitializeAsync()
    {
        var started = Environment.TickCount64;
        DebugLog.Info("ImageSourceViewModel", "VM.Initialize.Start");
        _isLoadingSettings = true;
        try
        {
            var snapshot = await Task.Run(LoadSettingsSnapshotAsync);

            SelectedServer = snapshot.Server;
            CustomBackgroundApiUrl = snapshot.CustomApiUrl;
            CurrentBackgroundApiUrl = string.IsNullOrWhiteSpace(CustomBackgroundApiUrl)
                ? GetDefaultBackgroundApiUrl(SelectedServer)
                : CustomBackgroundApiUrl;
            CustomPath = snapshot.CustomPathValue;
            HasCustomBackground = !string.IsNullOrWhiteSpace(CustomPath) && File.Exists(CustomPath);
            SlideshowFolder = snapshot.SlideshowFolderValue;
            HasSlideshowFolder = !string.IsNullOrWhiteSpace(SlideshowFolder) && Directory.Exists(SlideshowFolder);
            SlideshowInterval = snapshot.SlideshowIntervalValue;
            RandomType = snapshot.RandomTypeValue;
            IsRandomBackgroundPinned = snapshot.IsPinned;
            BingCopyright = snapshot.BingCopyrightValue;
            BingChangeOnStartup = snapshot.BingChangeOnStartupValue;
            SelectedBingYearOption = BingYearOptions.FirstOrDefault(option => option.Year == snapshot.BingYear)
                                     ?? BingYearOptions[0];
            IsBingOfficial = snapshot.IsBingOfficial;

            SelectedTab = snapshot.IsRandom
                ? ImageSourceTab.RandomBlueArchive
                : snapshot.IsBing
                    ? ImageSourceTab.Bing
                    : snapshot.IsBackgroundEnabled && snapshot.IsSlideshow && HasSlideshowFolder
                        ? ImageSourceTab.Slideshow
                        : snapshot.IsBackgroundEnabled && HasCustomBackground
                            ? ImageSourceTab.Custom
                            : ImageSourceTab.Official;
        }
        finally
        {
            _isLoadingSettings = false;
        }

        UpdateSourceInfo();
        IsPreviewLoading = false;

        if (SelectedTab == ImageSourceTab.Custom && !HasCustomBackground)
            ClearPreview("ImageSource_CustomEmpty");
        else if (SelectedTab == ImageSourceTab.Slideshow && !HasSlideshowFolder)
            ClearPreview("ImageSource_SlideshowEmpty");

        DebugLog.Info("ImageSourceViewModel", "VM.Initialize.End",
            $"{Environment.TickCount64 - started}ms tab={SelectedTab}");
    }

    private async Task<ImageSourceSettingsSnapshot> LoadSettingsSnapshotAsync()
    {
        var started = Environment.TickCount64;
        DebugLog.Info("ImageSourceViewModel", "VM.Snapshot.Start");
        var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
        var server = serverJson != null ? (ServerType)Convert.ToInt32(serverJson) : ServerType.CN;

        var customApiUrl =
            (await _localSettingsService.ReadSettingAsync("CustomBackgroundApiUrl"))?.ToString() ?? string.Empty;
        var customPath = (await _localSettingsService.ReadSettingAsync("CustomBackgroundPath"))?.ToString() ??
                         string.Empty;
        var slideshowFolder =
            (await _localSettingsService.ReadSettingAsync("BackgroundSlideshowFolder"))?.ToString() ?? string.Empty;
        var intervalJson = await _localSettingsService.ReadSettingAsync("BackgroundSlideshowInterval");
        var randomTypeJson = await _localSettingsService.ReadSettingAsync("RandomBackgroundType");
        var bingCopyright =
            (await _localSettingsService.ReadSettingAsync("BingWallpaperCopyright"))?.ToString() ?? string.Empty;
        var bingChange = await ReadBoolSettingAsync("IsBingBackgroundChangeOnStartupEnabled", true);
        var bingYearJson = await _localSettingsService.ReadSettingAsync("BingWallpaperYear");
        var bingYear = 0;
        if (bingYearJson != null)
            int.TryParse(bingYearJson.ToString(), out bingYear);
        var isBingOfficial = await ReadBoolSettingAsync("IsBingWallpaperOfficial");
        var isRandom = await ReadBoolSettingAsync("IsRandomBackgroundEnabled");
        var isPinned = await ReadBoolSettingAsync(RandomBackgroundService.PinnedSettingKey);
        var isBing = await ReadBoolSettingAsync("IsBingBackgroundEnabled");
        var isBackgroundEnabled = await ReadBoolSettingAsync(LocalSettingsService.IsBackgroundEnabledKey, true);
        var isSlideshow = await ReadBoolSettingAsync("IsBackgroundSlideshowEnabled");

        DebugLog.Info("ImageSourceViewModel", "VM.Snapshot.End", $"{Environment.TickCount64 - started}ms");
        return new ImageSourceSettingsSnapshot(
            server,
            customApiUrl,
            string.IsNullOrWhiteSpace(customPath) ? string.Empty : customPath,
            string.IsNullOrWhiteSpace(slideshowFolder) ? string.Empty : slideshowFolder,
            intervalJson != null ? Math.Max(1, Convert.ToInt32(intervalJson)) : 60,
            RandomBackgroundService.ParseType(randomTypeJson?.ToString()),
            bingCopyright,
            bingChange,
            isRandom,
            isPinned,
            isBing,
            isBackgroundEnabled,
            isSlideshow,
            bingYear,
            isBingOfficial);
    }

    private async Task<bool> ReadBoolSettingAsync(string key, bool fallback = false)
    {
        try
        {
            var value = await _localSettingsService.ReadSettingAsync(key);
            if (value is bool boolean) return boolean;
            if (value is string text && bool.TryParse(text, out var parsed)) return parsed;
            return value != null ? Convert.ToBoolean(value) : fallback;
        }
        catch
        {
            return fallback;
        }
    }
}
