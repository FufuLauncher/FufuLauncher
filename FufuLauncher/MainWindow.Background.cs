/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Services.Background;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    #region Background Management

    private BackgroundRenderResult? _randomBackgroundResult;
    private bool _randomBackgroundFetchStarted;
    private RandomBackgroundType? _randomBackgroundLoadType;
    private BackgroundImageQuality? _randomBackgroundQuality;
    private int _randomBackgroundReloadVersion;

    public event Action<BackgroundRenderResult>? GlobalBackgroundApplied;
    public event Action<ImageSource>? GlobalBackgroundImageAvailable;

    public BackgroundRenderResult? CurrentGlobalBackground
    {
        get;
        private set;
    }

    private async Task LoadBackgroundImageOpacityAsync()
    {
        try
        {
            var valueObj = await _localSettingsService.ReadSettingAsync("GlobalBackgroundImageOpacity");
            var opacity = 1.0;
            if (valueObj != null && double.TryParse(valueObj.ToString(), out var parsed)) opacity = parsed;
            ApplyBackgroundImageOpacity(opacity);
        }
        catch
        {
            ApplyBackgroundImageOpacity(1.0);
        }
    }

    private void ApplyBackgroundImageOpacity(double value)
    {
        StopBackgroundImageAnimation();
        var clamped = double.IsFinite(value) ? Math.Clamp(value, 0.0, 1.0) : 1.0;
        if (GlobalBackgroundImage != null) GlobalBackgroundImage.Opacity = clamped;
        if (GlobalBackgroundVideo != null) GlobalBackgroundVideo.Opacity = clamped;
    }

    private void UpdateBackgroundOverlayTheme()
    {
        try
        {
            if (_isExit) return;
            if (Content is not FrameworkElement rootElement) return;

            var currentTheme = rootElement.ActualTheme;
            if (currentTheme == ElementTheme.Default)
                currentTheme = Application.Current.RequestedTheme == ApplicationTheme.Dark
                    ? ElementTheme.Dark
                    : ElementTheme.Light;

            var themeBgColor = currentTheme == ElementTheme.Dark
                ? Color.FromArgb(255, 32, 32, 32)
                : Color.FromArgb(255, 243, 243, 243);

            GlobalBackgroundOverlay.Fill = new SolidColorBrush(themeBgColor);

            if ((_isAcrylicOverlayEnabled || _isOnboardingBackdrop) && !_isVideoBackground)
            {
                PageBackgroundOverlay.Background = new AcrylicBrush
                {
                    TintColor = themeBgColor,
                    TintOpacity = 0.6,
                    FallbackColor = themeBgColor
                };
            }
            else
            {
                PageBackgroundOverlay.Background = new SolidColorBrush(themeBgColor);
            }

            ApplyFrameBackgroundOpacity(_frameBackgroundOpacity);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    private void ShowOnboardingBackdrop()
    {
        try
        {
            _isOnboardingBackdrop = true;
            UpdateBackgroundOverlayTheme();

            OverlayTranslate.Y = 0;
            PageBackgroundOverlay.Opacity = 1.0;
            _isOverlayShown = true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Background", $"Onboarding backdrop setup failed: {ex.Message}");
        }
    }

    private async Task LoadOnboardingBackdropImageAsync()
    {
        try
        {
            var version = Interlocked.Increment(ref _backgroundLoadVersion);
            await ApplyGlobalBackgroundAsync(await _backgroundRenderer.GetFallbackBackgroundAsync(), version);
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Background", $"Onboarding backdrop image load failed: {ex.Message}");
        }
    }

    private bool _isFirstBackgroundLoad = true;

    private async Task ApplyAutoThemeColorAsync(ImageSource? backgroundImage)
    {
        try
        {
            await _autoThemeColorService.ApplyForBackgroundAsync(backgroundImage);
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Background", $"Auto theme color apply failed: {ex.Message}");
        }
    }

    private async Task LoadGlobalBackgroundAsync()
    {
        var version = Interlocked.Increment(ref _backgroundLoadVersion);
        var isInitialBackgroundLoad = _isFirstBackgroundLoad;
        _isFirstBackgroundLoad = false;
        StopSlideshow();
        try
        {
            var randomBackgroundEnabledJson = await _localSettingsService.ReadSettingAsync("IsRandomBackgroundEnabled");
            var isRandomBackgroundEnabled =
                randomBackgroundEnabledJson != null && Convert.ToBoolean(randomBackgroundEnabledJson);
            if (isRandomBackgroundEnabled)
            {
                var randomTypeJson = await _localSettingsService.ReadSettingAsync("RandomBackgroundType");
                var randomType = RandomBackgroundService.ParseType(randomTypeJson?.ToString());
                await LoadRandomBackgroundAsync(randomType);
                return;
            }

            var bingEnabledJson = await _localSettingsService.ReadSettingAsync("IsBingBackgroundEnabled");
            var isBingEnabled = bingEnabledJson != null && Convert.ToBoolean(bingEnabledJson);
            if (isBingEnabled)
            {
                ResetRandomBackgroundState();
                await LoadBingBackgroundAsync(version, isInitialBackgroundLoad);
                return;
            }

            ResetRandomBackgroundState();

            if (GlobalBackgroundImage.Source == null && !_isVideoBackground)
                await ApplyGlobalBackgroundAsync(await _backgroundRenderer.GetFallbackBackgroundAsync(), version);
            if (version != _backgroundLoadVersion || _isExit) return;

            var enabledJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.IsBackgroundEnabledKey);
            var isCustomEnabled = enabledJson == null ? true : Convert.ToBoolean(enabledJson);

            if (isCustomEnabled)
            {
                var isSlideshowEnabledJson =
                    await _localSettingsService.ReadSettingAsync("IsBackgroundSlideshowEnabled");
                var isSlideshowEnabled = isSlideshowEnabledJson != null && Convert.ToBoolean(isSlideshowEnabledJson);

                if (isSlideshowEnabled)
                {
                    var slideshowFolderJson = await _localSettingsService.ReadSettingAsync("BackgroundSlideshowFolder");
                    var slideshowFolder = slideshowFolderJson?.ToString();

                    if (!string.IsNullOrEmpty(slideshowFolder) && Directory.Exists(slideshowFolder))
                    {
                        var slideshowIntervalJson =
                            await _localSettingsService.ReadSettingAsync("BackgroundSlideshowInterval");
                        var interval = slideshowIntervalJson != null ? Convert.ToInt32(slideshowIntervalJson) : 60;
                        if (interval < 1) interval = 1;

                        if (await StartSlideshowAsync(slideshowFolder, interval, version)) return;
                    }
                }

                var customPathObj = await _localSettingsService.ReadSettingAsync("CustomBackgroundPath");
                var customPath = customPathObj?.ToString();

                if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                {
                    var customResult = await _backgroundRenderer.GetCustomBackgroundAsync(customPath);
                    if (customResult != null)
                    {
                        await ApplyGlobalBackgroundAsync(customResult, version);
                        return;
                    }
                }
            }

            var preferVideoObj = await _localSettingsService.ReadSettingAsync("PreferVideoBackground");
            var preferVideo = preferVideoObj != null && Convert.ToBoolean(preferVideoObj) &&
                              _devBuildDetectionService.IsDevBuild;

            var serverJson = await _localSettingsService.ReadSettingAsync(LocalSettingsService.BackgroundServerKey);
            var serverValue = serverJson != null ? Convert.ToInt32(serverJson) : 0;
            var server = (ServerType)serverValue;

            var result = await _backgroundRenderer.GetBackgroundAsync(server, preferVideo);
            if (result != null)
                await ApplyGlobalBackgroundAsync(result, version);
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Background", $"Global background load failed: {ex}");
            if (version != _backgroundLoadVersion || _isExit) return;
            StopSlideshow();
            await ApplyGlobalBackgroundAsync(await _backgroundRenderer.GetFallbackBackgroundAsync(), version);
        }
    }

    private void ResetRandomBackgroundState()
    {
        _randomBackgroundResult = null;
        _randomBackgroundFetchStarted = false;
        _randomBackgroundLoadType = null;
        _randomBackgroundQuality = null;
        _randomBackgroundReloadVersion++;
        RandomBackgroundService.ClearCache();
    }

    private async Task LoadBingBackgroundAsync(int version, bool isInitialBackgroundLoad)
    {
        try
        {
            var savedUrl = (await _localSettingsService.ReadSettingAsync("BingWallpaperUrl"))?.ToString();
            var changeOnStartup = true;
            var changeSetting = await _localSettingsService.ReadSettingAsync("IsBingBackgroundChangeOnStartupEnabled");
            if (changeSetting != null)
                changeOnStartup = Convert.ToBoolean(changeSetting);

            var yearSetting = await _localSettingsService.ReadSettingAsync("BingWallpaperYear");
            var bingYear = 0;
            if (yearSetting != null)
                int.TryParse(yearSetting.ToString(), out bingYear);
            var officialSetting = await _localSettingsService.ReadSettingAsync("IsBingWallpaperOfficial");
            var bingOfficial = officialSetting != null && Convert.ToBoolean(officialSetting);

            var shouldChange = isInitialBackgroundLoad && changeOnStartup;
            var appliedCached = false;
            var fetchAttempted = false;

            if (shouldChange)
            {
                var cached = await _bingWallpaperService.LoadCachedAsync();
                if (version != _backgroundLoadVersion || _isExit) return;

                if (cached != null)
                {
                    await SaveBingWallpaperSettingsAsync(cached.Wallpaper);
                    await ApplyGlobalBackgroundAsync(cached.Result, version);
                    appliedCached = true;
                }

                var fetched = await _bingWallpaperService.FetchAndCacheAsync(savedUrl, bingYear, bingOfficial);
                fetchAttempted = true;
                if (version != _backgroundLoadVersion || _isExit) return;

                if (fetched != null)
                {
                    await SaveBingWallpaperSettingsAsync(fetched.Wallpaper);
                    await ApplyGlobalBackgroundAsync(fetched.Result, version);
                    return;
                }

                if (appliedCached) return;
            }

            if (!string.IsNullOrWhiteSpace(savedUrl))
            {
                var savedResult = await _backgroundRenderer.GetSpecificOnlineBackgroundAsync(savedUrl, false);
                if (version != _backgroundLoadVersion || _isExit) return;
                savedResult ??= await _backgroundRenderer.GetFallbackBackgroundAsync();
                await ApplyGlobalBackgroundAsync(savedResult, version);
                return;
            }

            if (!fetchAttempted)
            {
                var fetched = await _bingWallpaperService.FetchAndCacheAsync(null, bingYear, bingOfficial);
                if (version != _backgroundLoadVersion || _isExit) return;
                if (fetched != null)
                {
                    await SaveBingWallpaperSettingsAsync(fetched.Wallpaper);
                    await ApplyGlobalBackgroundAsync(fetched.Result, version);
                    return;
                }
            }

            await ApplyGlobalBackgroundAsync(await _backgroundRenderer.GetFallbackBackgroundAsync(), version);
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Background", $"[BingWallpaper] Failed to load background: {ex.Message}");
            if (version == _backgroundLoadVersion && !_isExit)
                await ApplyGlobalBackgroundAsync(await _backgroundRenderer.GetFallbackBackgroundAsync(), version);
        }
    }

    private async Task SaveBingWallpaperSettingsAsync(BingWallpaperItem wallpaper)
    {
        await _localSettingsService.SaveSettingAsync("BingWallpaperUrl", wallpaper.Url);
        await _localSettingsService.SaveSettingAsync("BingWallpaperCopyright", wallpaper.Copyright);
    }

    public void CancelPendingBackgroundLoad()
    {
        Interlocked.Increment(ref _backgroundLoadVersion);
        CancelRandomBackgroundLoad();
        StopSlideshow();
    }

    private void CancelRandomBackgroundLoad()
    {
        _randomBackgroundResult = null;
        _randomBackgroundFetchStarted = false;
        _randomBackgroundLoadType = null;
        _randomBackgroundQuality = null;
        _randomBackgroundReloadVersion++;
    }

    public async Task ReloadRandomBackgroundAsync(bool forceNewImage)
    {
        if (_isExit) return;

        if (forceNewImage)
            ResetRandomBackgroundState();

        await LoadGlobalBackgroundAsync();
    }

    public async Task<bool> StepSlideshowAsync(int direction)
    {
        if (_isExit || _slideshowImages.Count == 0) return false;

        var count = _slideshowImages.Count;
        _currentSlideshowIndex = (_currentSlideshowIndex - 1 + direction + count) % count;
        return await ShowNextSlideshowImageAsync(_backgroundLoadVersion);
    }

    public void StopSlideshowBackground()
    {
        StopSlideshow();
        _slideshowImages.Clear();
        _currentSlideshowIndex = 0;
    }

    private async Task LoadRandomBackgroundAsync(RandomBackgroundType type)
    {
        if (_randomBackgroundLoadType != type)
        {
            _randomBackgroundLoadType = type;
            _randomBackgroundResult = null;
            _randomBackgroundFetchStarted = false;
            _randomBackgroundQuality = null;
            _randomBackgroundReloadVersion++;
        }

        var loadId = _randomBackgroundReloadVersion;
        var quality = await BackgroundImageQualitySettings.GetEffectiveQualityAsync();
        if (!IsRandomBackgroundLoadCurrent(loadId, type)) return;

        var qualityChanged = _randomBackgroundQuality.HasValue && _randomBackgroundQuality.Value != quality;
        if (qualityChanged)
        {
            _randomBackgroundResult = null;
            _randomBackgroundFetchStarted = false;
            _randomBackgroundQuality = quality;
            _randomBackgroundReloadVersion++;
            loadId = _randomBackgroundReloadVersion;
        }
        else
        {
            _randomBackgroundQuality = quality;
        }

        var pinnedJson = await _localSettingsService.ReadSettingAsync(RandomBackgroundService.PinnedSettingKey);
        var isPinned = pinnedJson != null && Convert.ToBoolean(pinnedJson);
        if (isPinned)
        {
            var pinned = await RandomBackgroundService.LoadPinnedAsync();
            if (!IsRandomBackgroundLoadCurrent(loadId, type)) return;

            if (pinned != null)
            {
                _randomBackgroundResult = pinned;
                if (!ReferenceEquals(GlobalBackgroundImage.Source, pinned.ImageSource))
                    await ApplyGlobalBackgroundAsync(pinned, _backgroundLoadVersion);
            }
            else if (GlobalBackgroundImage.Source == null && !_isVideoBackground)
            {
                var fallback = await _backgroundRenderer.GetFallbackBackgroundAsync();
                if (fallback != null && IsRandomBackgroundLoadCurrent(loadId, type))
                    await ApplyGlobalBackgroundAsync(fallback, _backgroundLoadVersion);
            }

            return;
        }

        if (_randomBackgroundResult != null)
        {
            if (IsRandomBackgroundLoadCurrent(loadId, type) &&
                !ReferenceEquals(GlobalBackgroundImage.Source, _randomBackgroundResult.ImageSource))
                await ApplyGlobalBackgroundAsync(_randomBackgroundResult, _backgroundLoadVersion);
            return;
        }

        if (_randomBackgroundFetchStarted) return;
        _randomBackgroundFetchStarted = true;

        var cached = await RandomBackgroundService.LoadCachedAsync(type);
        if (!IsRandomBackgroundLoadCurrent(loadId, type)) return;
        if (cached != null)
        {
            _randomBackgroundResult = cached;
            await ApplyGlobalBackgroundAsync(cached, _backgroundLoadVersion);
            if (qualityChanged) return;
        }
        else if (GlobalBackgroundImage.Source == null && !_isVideoBackground)
        {
            var fallback = await _backgroundRenderer.GetFallbackBackgroundAsync();
            if (fallback != null && IsRandomBackgroundLoadCurrent(loadId, type))
                await ApplyGlobalBackgroundAsync(fallback, _backgroundLoadVersion);
        }

        if (!IsRandomBackgroundLoadCurrent(loadId, type)) return;

        var fetched = await RandomBackgroundService.FetchAndCacheAsync(type);
        if (fetched == null || !IsRandomBackgroundLoadCurrent(loadId, type)) return;

        var enabledJson = await _localSettingsService.ReadSettingAsync("IsRandomBackgroundEnabled");
        if (enabledJson == null || !Convert.ToBoolean(enabledJson)) return;

        var typeJson = await _localSettingsService.ReadSettingAsync("RandomBackgroundType");
        if (RandomBackgroundService.ParseType(typeJson?.ToString()) != type) return;

        if (!IsRandomBackgroundLoadCurrent(loadId, type)) return;

        _randomBackgroundResult = fetched;
        await ApplyGlobalBackgroundAsync(fetched, _backgroundLoadVersion);
    }

    private bool IsRandomBackgroundLoadCurrent(int loadId, RandomBackgroundType type) =>
        loadId == _randomBackgroundReloadVersion && _randomBackgroundLoadType == type && !_isExit;

    private void StopSlideshow()
    {
        if (_slideshowTimer != null)
        {
            _slideshowTimer.Stop();
            _slideshowTimer = null;
        }
    }

    private async Task<bool> StartSlideshowAsync(string folder, int intervalSeconds, int version)
    {
        if (version != _backgroundLoadVersion || _isExit) return true;
        StopSlideshow();

        try
        {
            string[] extensions = { ".jpg", ".jpeg", ".png", ".bmp" };
            _slideshowImages = Directory.GetFiles(folder)
                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            if (_slideshowImages.Count == 0)
            {
                return false;
            }

            _currentSlideshowIndex = 0;
            if (!await ShowNextSlideshowImageAsync(version)) return false;
            if (version != _backgroundLoadVersion || _isExit) return true;

            _slideshowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(intervalSeconds) };
            _slideshowTimer.Tick += async (_, _) => await ShowNextSlideshowImageAsync(version);
            _slideshowTimer.Start();
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Background", $"Failed to start slideshow: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ShowNextSlideshowImageAsync(int version)
    {
        if (_slideshowLoadingVersion == version || version != _backgroundLoadVersion || _isExit) return false;
        _slideshowLoadingVersion = version;
        try
        {
            for (var attempt = 0; attempt < _slideshowImages.Count; attempt++)
            {
                if (version != _backgroundLoadVersion || _isExit) return false;
                if (_currentSlideshowIndex >= _slideshowImages.Count) _currentSlideshowIndex = 0;
                var imagePath = _slideshowImages[_currentSlideshowIndex++];
                var result = await _backgroundRenderer.GetCustomBackgroundAsync(imagePath);
                if (result == null) continue;
                await ApplyGlobalBackgroundAsync(result, version);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Background", $"Failed to load slideshow image: {ex.Message}");
            return false;
        }
        finally
        {
            if (_slideshowLoadingVersion == version) _slideshowLoadingVersion = null;
        }
    }

    private void DisposeGlobalBackgroundPlayer()
    {
        var player = _globalBackgroundPlayer;
        _globalBackgroundPlayer = null;
        _suspendedVideoSource = null;
        try
        {
            GlobalBackgroundVideo.SetMediaPlayer(null);
        }
        catch
        {
        }

        if (player != null)
        {
            if (_bgVideoFailedHandler != null)
            {
                player.MediaFailed -= _bgVideoFailedHandler;
            }

            player.Pause();
            player.Source = null;
            _ = Task.Run(() =>
            {
                try
                {
                    player.Dispose();
                }
                catch
                {
                }
            });
        }

        _bgVideoFailedHandler = null;
    }

    private static string DescribeBackgroundImage(ImageSource? source)
    {
        if (source is WriteableBitmap bitmap)
            return $"{bitmap.PixelWidth}x{bitmap.PixelHeight}";
        return source?.GetType().Name ?? "null";
    }

    private async Task ApplyGlobalBackgroundAsync(BackgroundRenderResult? result, int? version = null,
        MediaSource? expectedVideoSource = null)
    {
        if (_isExit || (version.HasValue && version != _backgroundLoadVersion)) return;
        if (result == null)
        {
            return;
        }

        var applyStarted = Environment.TickCount64;
        DebugLog.Info("MainWindow.Background", "Bg.Apply.Start",
            $"version={version} video={result.IsVideo} fallback={result.IsFallback} image={DescribeBackgroundImage(result.ImageSource)}");

        double finalOpacity = 1.0;
        try
        {
            DebugLog.Info("MainWindow.Background", "Bg.OpacityRead.Start");
            var valueObj = await _localSettingsService.ReadSettingAsync("GlobalBackgroundImageOpacity");
            DebugLog.Info("MainWindow.Background", "Bg.OpacityRead.End", $"{Environment.TickCount64 - applyStarted}ms");
            if (valueObj != null && double.TryParse(valueObj.ToString(), out var parsed) && double.IsFinite(parsed))
            {
                finalOpacity = Math.Clamp(parsed, 0.0, 1.0);
            }
        }
        catch
        {
        }

        DebugLog.Info("MainWindow.Background", "Bg.BeforeUI", $"{Environment.TickCount64 - applyStarted}ms");
        await RunOnUIThreadAsync(() =>
        {
            DebugLog.Info("MainWindow.Background", "Bg.UILambda.Start");
            if (_isExit || (version.HasValue && version != _backgroundLoadVersion)) return;
            if (expectedVideoSource != null &&
                !ReferenceEquals(_globalBackgroundPlayer?.Source, expectedVideoSource)) return;
            CurrentGlobalBackground = result;
            StopBackgroundImageAnimation();
            _isFallbackBackground = result.IsFallback;
            if (result.IsVideo)
            {
                _isVideoBackground = true;
                _bgVideoFallbackSource = null;
                GlobalBackgroundImage.Source = null;
                GlobalBackgroundImage.Visibility = Visibility.Collapsed;
                GlobalBackgroundVideo.Visibility = Visibility.Visible;
                GlobalBackgroundVideo.Opacity = finalOpacity;

                if (_globalBackgroundPlayer == null)
                {
                    _globalBackgroundPlayer = MediaPlayerHelper.CreateLoopingMutedPlayer();
                    _bgVideoFailedHandler = OnGlobalBackgroundVideoFailed;
                    _globalBackgroundPlayer.MediaFailed += _bgVideoFailedHandler;
                    GlobalBackgroundVideo.SetMediaPlayer(_globalBackgroundPlayer);
                }

                if (!ReferenceEquals(_globalBackgroundPlayer.Source, result.VideoSource))
                {
                    _globalBackgroundPlayer.Pause();
                    _globalBackgroundPlayer.Source = null;
                    _globalBackgroundPlayer.Source = result.VideoSource;
                }

                if (_globalBackgroundPlayer.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
                {
                    _globalBackgroundPlayer.Play();
                }
            }
            else
            {
                var wasVideoBackground = _isVideoBackground;
                _isVideoBackground = false;
                DisposeGlobalBackgroundPlayer();
                GlobalBackgroundVideo.Visibility = Visibility.Collapsed;

                DebugLog.Info("MainWindow.Background", "Bg.MainSourceSet.Start",
                    DescribeBackgroundImage(result.ImageSource));
                GlobalBackgroundImage.Source = result.ImageSource;
                DebugLog.Info("MainWindow.Background", "Bg.MainSourceSet.Done");
                if (result.ImageSource != null && !result.IsFallback)
                {
                    DebugLog.Info("MainWindow.Background", "Bg.ImageAvailable.Invoke");
                    GlobalBackgroundImageAvailable?.Invoke(result.ImageSource);
                    DebugLog.Info("MainWindow.Background", "Bg.ImageAvailable.Returned");
                }

                GlobalBackgroundImage.Opacity = finalOpacity;
                GlobalBackgroundImage.Visibility = Visibility.Visible;

                var anim = new DoubleAnimation
                {
                    From = 0.0,
                    To = finalOpacity,
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                };

                Storyboard.SetTarget(anim, GlobalBackgroundImage);
                Storyboard.SetTargetProperty(anim, "Opacity");

                _bgImageFadeStoryboard = new Storyboard();
                _bgImageFadeStoryboard.Children.Add(anim);
                try
                {
                    _bgImageFadeStoryboard.Begin();
                }
                catch (Exception ex)
                {
                    StopBackgroundImageAnimation();
                    DebugLog.Info("MainWindow.Background", $"Background fade-in animation unavailable: {ex.Message}");
                }

                if (wasVideoBackground)
                {
                    FlushMemory();
                }
            }

            UpdateBackgroundOverlayTheme();

            if (!result.IsVideo)
                _ = ApplyAutoThemeColorAsync(result.ImageSource);
        });

        DebugLog.Info("MainWindow.Background", "Bg.UICompleted", $"{Environment.TickCount64 - applyStarted}ms");
        if (_isExit || (version.HasValue && version != _backgroundLoadVersion)) return;

        CurrentGlobalBackground = result;
        GlobalBackgroundApplied?.Invoke(result);
        DebugLog.Info("MainWindow.Background", "Bg.Apply.End", $"{Environment.TickCount64 - applyStarted}ms");
    }

    private void OnGlobalBackgroundVideoFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        DebugLog.Info("MainWindow.Background", $"Background video playback failed({args.Error}): {args.ErrorMessage}");

        if (_isExit || !ReferenceEquals(sender, _globalBackgroundPlayer)) return;

        var failedSource = sender.Source as MediaSource;
        if (failedSource == null || ReferenceEquals(failedSource, _bgVideoFallbackSource)) return;
        _bgVideoFallbackSource = failedSource;

        dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await ShowVideoFirstFrameFallbackAsync(failedSource);
            }
            catch (Exception ex)
            {
                DebugLog.Info("MainWindow.Background", $"Video first-frame fallback exception: {ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_bgVideoFallbackSource, failedSource))
                    _bgVideoFallbackSource = null;
            }
        });
    }

    private async Task ShowVideoFirstFrameFallbackAsync(MediaSource failedSource)
    {
        var version = _backgroundLoadVersion;
        var videoPath = failedSource.Uri?.IsFile == true ? failedSource.Uri.LocalPath : null;

        if (string.IsNullOrEmpty(videoPath) || !File.Exists(videoPath))
        {
            await ApplyFallbackBackgroundImageAsync(failedSource);
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(videoPath);
            var clip = await MediaClip.CreateFromFileAsync(file);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var thumbnail = await composition.GetThumbnailAsync(
                TimeSpan.Zero, 1920, 1080, VideoFramePrecision.NearestKeyFrame);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);

            if (!ReferenceEquals(_globalBackgroundPlayer?.Source, failedSource)) return;

            await ApplyGlobalBackgroundAsync(new BackgroundRenderResult { ImageSource = bitmap }, version,
                failedSource);
            DebugLog.Info("MainWindow.Background", "Video playback failed, captured first frame as static background");
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Background", $"Failed to extract video first frame: {ex.Message}");
            await ApplyFallbackBackgroundImageAsync(failedSource);
        }
    }

    private async Task ApplyFallbackBackgroundImageAsync(MediaSource failedSource)
    {
        if (_isExit) return;
        var version = _backgroundLoadVersion;
        var fallback = await _backgroundRenderer.GetFallbackBackgroundAsync();
        if (!ReferenceEquals(_globalBackgroundPlayer?.Source, failedSource)) return;
        await ApplyGlobalBackgroundAsync(fallback, version, failedSource);
    }

    private async void OnGlobalBackgroundImageFailed(object sender, ExceptionRoutedEventArgs args)
    {
        DebugLog.Info("MainWindow.Background", $"Global background display failed: {args.ErrorMessage}");
        if (_isExit || _isFallbackBackground) return;
        _isFallbackBackground = true;
        var version = _backgroundLoadVersion;
        var failedSource = GlobalBackgroundImage.Source;
        try
        {
            var fallback = await _backgroundRenderer.GetFallbackBackgroundAsync();
            if (!ReferenceEquals(GlobalBackgroundImage.Source, failedSource)) return;
            await ApplyGlobalBackgroundAsync(fallback, version);
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Background", $"Global background display fallback failed: {ex}");
        }
    }

    private void StopBackgroundImageAnimation()
    {
        _bgImageFadeStoryboard?.Stop();
        _bgImageFadeStoryboard = null;
    }

    #endregion
}