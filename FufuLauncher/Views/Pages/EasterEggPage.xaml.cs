/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Windows.Foundation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using FufuLauncher.Helpers;
using FufuLauncher.Services.EasterEgg;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;

namespace FufuLauncher.Views;

public sealed partial class EasterEggPage : Page
{
    private const string HardcodedName = "■■■■■";

    private readonly List<string> _welcomeLines = new()
    {
        "EasterEgg_Welcome_1".GetLocalized(),
        "EasterEgg_Welcome_2".GetLocalized(),
        "EasterEgg_Welcome_3".GetLocalized(),
        "EasterEgg_Welcome_4".GetLocalized(),
        "EasterEgg_Welcome_5".GetLocalized(),
        "EasterEgg_Welcome_6".GetLocalized(),
        "EasterEgg_Welcome_7".GetLocalized(),
        "EasterEgg_Welcome_8".GetLocalized()
    };

    private readonly List<string> _syaLines = new()
    {
        "EasterEgg_Sya_1".GetLocalized(),
        "EasterEgg_Sya_2".GetLocalized(),
        string.Format("EasterEgg_Sya_3_Format".GetLocalized(), HardcodedName),
        "EasterEgg_Sya_4".GetLocalized(),
        "EasterEgg_Sya_5".GetLocalized(),
        "EasterEgg_Sya_6".GetLocalized(),
        string.Format("EasterEgg_Sya_7_Format".GetLocalized(), HardcodedName),
        string.Format("EasterEgg_Sya_8_Format".GetLocalized(), HardcodedName),
        "EasterEgg_Sya_9".GetLocalized(),
        string.Format("EasterEgg_Sya_10_Format".GetLocalized(), HardcodedName)
    };

    private DispatcherTimer _typewriterTimer;
    private MediaPlayer? _musicPlayer;
    private MediaPlayer? _videoPlayer;
    private TypedEventHandler<MediaPlayer, object>? _mediaOpenedHandler;
    private TypedEventHandler<MediaPlayer, MediaPlayerFailedEventArgs>? _mediaFailedHandler;
    private int _currentWelcomeIndex = 0;
    private int _charIndex = 0;
    private bool _isDeleting = false;
    private bool _isPaused = false;
    private int _pauseTicks = 0;
    private bool _cleaned = false;

    public UIElement AppTitleBarElement => AppTitleBar;

    public EasterEggPage()
    {
        this.InitializeComponent();

        InitializeMedia();
        StartTypewriter();
        ShowRandomSyaLine();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (_videoPlayer != null && _videoPlayer.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
        {
            _videoPlayer.IsLoopingEnabled = true;
            _videoPlayer.Play();
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Cleanup();
    }

    private void InitializeMedia()
    {
        _ = InitializeVideoAsync();
        _ = InitializeMusicAsync();
    }

    private async Task InitializeVideoAsync()
    {
        try
        {
            string? videoPath = await EasterEggMediaCache.GetVideoAsync();
            if (videoPath == null || _cleaned)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_cleaned)
                {
                    CreateVideoPlayer(videoPath);
                }
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("EasterEggPage", $"Video load failed: {ex.Message}");
        }
    }

    private async Task InitializeMusicAsync()
    {
        try
        {
            string? musicPath = await EasterEggMediaCache.GetMusicAsync();
            if (musicPath == null || _cleaned)
            {
                return;
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                if (!_cleaned)
                {
                    CreateMusicPlayer(musicPath);
                }
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("EasterEggPage", $"Music load failed: {ex.Message}");
        }
    }

    private void CreateVideoPlayer(string videoPath)
    {
        try
        {
            _videoPlayer = new MediaPlayer();
            MediaPlayerHelper.DisableSystemMediaControls(_videoPlayer);
            _videoPlayer.Source = MediaSource.CreateFromUri(new Uri(videoPath));
            _videoPlayer.IsLoopingEnabled = true;
            BgVideoPlayer.SetMediaPlayer(_videoPlayer);

            _mediaOpenedHandler = (_, _) =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_cleaned && _videoPlayer != null)
                    {
                        _videoPlayer.Play();
                    }
                });
            };
            _videoPlayer.MediaOpened += _mediaOpenedHandler;

            _mediaFailedHandler = (_, args) =>
            {
                DebugLog.Info("EasterEggPage", $"Video playback failed: {args.ErrorMessage}");
                DispatcherQueue.TryEnqueue(() => ShowVideoFallbackFrame(videoPath));
            };
            _videoPlayer.MediaFailed += _mediaFailedHandler;

            _videoPlayer.Play();
        }
        catch (Exception ex)
        {
            DebugLog.Info("EasterEggPage", $"Video player creation failed: {ex.Message}");
        }
    }

    private void CreateMusicPlayer(string musicPath)
    {
        try
        {
            _musicPlayer = new MediaPlayer();
            MediaPlayerHelper.DisableSystemMediaControls(_musicPlayer);
            _musicPlayer.Source = MediaSource.CreateFromUri(new Uri(musicPath));
            _musicPlayer.IsLoopingEnabled = true;
            _musicPlayer.Play();
        }
        catch (Exception ex)
        {
            DebugLog.Info("EasterEggPage", $"Music player creation failed: {ex.Message}");
        }
    }

    private void StartTypewriter()
    {
        _typewriterTimer = new DispatcherTimer();
        _typewriterTimer.Interval = TimeSpan.FromMilliseconds(100);
        _typewriterTimer.Tick += TypewriterTimer_Tick;
        _typewriterTimer.Start();
    }

    private void TypewriterTimer_Tick(object sender, object e)
    {
        var currentLine = _welcomeLines[_currentWelcomeIndex];

        if (_isPaused)
        {
            _pauseTicks++;
            if (_pauseTicks > 25)
            {
                _isPaused = false;
                _isDeleting = true;
                _pauseTicks = 0;
            }

            return;
        }

        if (!_isDeleting)
        {
            if (_charIndex < currentLine.Length)
            {
                _charIndex++;
                WelcomeText.Text = currentLine.Substring(0, _charIndex) + "_";
            }
            else
            {
                WelcomeText.Text = currentLine;
                _isPaused = true;
            }
        }
        else
        {
            if (_charIndex > 0)
            {
                _charIndex--;
                WelcomeText.Text = currentLine.Substring(0, _charIndex) + "_";
            }
            else
            {
                _isDeleting = false;
                _currentWelcomeIndex = (_currentWelcomeIndex + 1) % _welcomeLines.Count;
                _charIndex = 0;
                WelcomeText.Text = "";
            }
        }
    }

    private void ShowRandomSyaLine()
    {
        var random = new Random();
        var line = _syaLines[random.Next(_syaLines.Count)];

        if (string.IsNullOrWhiteSpace(line)) line = _syaLines[0];

        SyaShortText.Text = line;

        var anim = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromSeconds(3)),
            EasingFunction = new SineEase()
        };

        var storyboard = new Storyboard();
        Storyboard.SetTarget(anim, SyaShortText);
        Storyboard.SetTargetProperty(anim, "Opacity");
        storyboard.Children.Add(anim);
        storyboard.Begin();
    }

    private async void ShowVideoFallbackFrame(string videoPath)
    {
        if (_cleaned)
        {
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(videoPath);
            var clip = await MediaClip.CreateFromFileAsync(file);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var duration = clip.OriginalDuration;
            var random = new Random();
            var randomTime = TimeSpan.FromMilliseconds(random.NextDouble() * duration.TotalMilliseconds);

            var thumbnail = await composition.GetThumbnailAsync(
                randomTime, 1920, 1080, VideoFramePrecision.NearestKeyFrame);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);

            BgVideoPlayer.Visibility = Visibility.Collapsed;
            BgFallbackImage.Source = bitmap;
            BgFallbackImage.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            DebugLog.Info("EasterEggPage", $"Failed to extract video frame: {ex.Message}");
        }
    }

    public void Cleanup()
    {
        if (_cleaned) return;
        _cleaned = true;

        try
        {
            _typewriterTimer?.Stop();
            _typewriterTimer = null;

            var music = _musicPlayer;
            _musicPlayer = null;
            var videoPlayer = _videoPlayer;
            _videoPlayer = null;

            if (videoPlayer != null && _mediaOpenedHandler != null)
            {
                videoPlayer.MediaOpened -= _mediaOpenedHandler;
                _mediaOpenedHandler = null;
            }

            if (videoPlayer != null && _mediaFailedHandler != null)
            {
                videoPlayer.MediaFailed -= _mediaFailedHandler;
                _mediaFailedHandler = null;
            }

            if (BgVideoPlayer != null)
                BgVideoPlayer.SetMediaPlayer(null);

            Task.Run(() =>
            {
                try
                {
                    music?.Pause();
                    music?.Dispose();
                    videoPlayer?.Pause();
                    videoPlayer?.Dispose();
                }
                catch
                {
                }
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("EasterEggPage", $"Cleanup error: {ex.Message}");
        }
    }
}