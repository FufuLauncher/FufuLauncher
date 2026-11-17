/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Services.GameServer;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.System;

namespace FufuLauncher.Views;

internal sealed partial class CutscenePlayerWindow : Window
{
    private readonly string _mkvPath;
    private readonly string _cacheDir;
    private readonly string? _ffmpegPath;
    private readonly IReadOnlyList<CutsceneAudioTrack> _audioTracks;
    private readonly IReadOnlyList<CutsceneExtractedSubtitleTrack> _subtitleTracks;
    private readonly Func<Task>? _onExportRequested;

    private MediaPlayer? _player;
    private MediaSource? _mediaSource;
    private MediaPlaybackItem? _playbackItem;
    private DispatcherTimer? _positionTimer;
    private DispatcherTimer? _hudHideTimer;
    private DispatcherTimer? _spaceHoldTimer;
    private CancellationTokenSource? _transcodeCts;

    private IReadOnlyList<CutsceneSubtitleCue> _activeCues = [];
    private string _currentVideoPath;
    private bool _hasAttemptedMp4Fallback;
    private bool _closed;
    private bool _updatingTimeline;
    private bool _isFullscreen;
    private bool _isSpaceDown;
    private bool _isSpaceFastForward;
    private double _normalPlaybackRate = 1.0;
    private double _lastNonZeroVolume = 1.0;
    private int _preferredAudioIndex;

    public CutscenePlayerWindow(
        string title,
        string subtitle,
        string mkvPath,
        string cacheDir,
        string? ffmpegPath,
        IReadOnlyList<CutsceneAudioTrack> audioTracks,
        IReadOnlyList<CutsceneExtractedSubtitleTrack> subtitleTracks,
        Func<Task>? onExportRequested = null)
    {
        InitializeComponent();

        _mkvPath = mkvPath;
        _currentVideoPath = mkvPath;
        _cacheDir = cacheDir;
        _ffmpegPath = ffmpegPath;
        _audioTracks = audioTracks;
        _subtitleTracks = subtitleTracks;
        _onExportRequested = onExportRequested;

        Title = title;
        PlayerTitleText.Text = title;
        PlayerSubtitleText.Text = subtitle;
        RootGrid.RequestedTheme = ElementTheme.Dark;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        RectInt32 area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(1280, area.Width - 48);
        int height = Math.Min(760, area.Height - 48);
        AppWindow.MoveAndResize(new RectInt32(
            area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2,
            width,
            height));

        ExportButton.Visibility = onExportRequested is not null ? Visibility.Visible : Visibility.Collapsed;

        InitializeSelectors();
        InitializeTimers();

        Closed += OnWindowClosed;

        // If a pre-transcoded compatible MP4 already exists in cache, prefer it only when MKV fails; try native MKV first.
        _ = LoadMediaAsync(_mkvPath);
    }

    private void InitializeSelectors()
    {
        if (_audioTracks.Count > 0)
        {
            AudioTrackSelector.ItemsSource = _audioTracks
                .Select(CutsceneResourcesWindow.GetAudioTrackLabel)
                .ToArray();
            _preferredAudioIndex = FindPreferredAudioIndex();
            AudioTrackSelector.SelectedIndex = _preferredAudioIndex;
            AudioTrackSelector.Visibility = _audioTracks.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_subtitleTracks.Count > 0)
        {
            var items = new List<string> { "Cutscene_PlayerSubtitleOff".GetLocalized() };
            items.AddRange(_subtitleTracks.Select(t => GetSubtitleLabel(t.Track)));
            SubtitleTrackSelector.ItemsSource = items;
            int defaultSub = FindPreferredSubtitleIndex();
            SubtitleTrackSelector.SelectedIndex = defaultSub >= 0 ? defaultSub + 1 : 0;
            SubtitleTrackSelector.Visibility = Visibility.Visible;
            ApplySubtitleSelection();
        }
    }

    private void InitializeTimers()
    {
        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _positionTimer.Tick += (_, _) => UpdatePlaybackProgress();
        _positionTimer.Start();

        _hudHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hudHideTimer.Tick += (_, _) =>
        {
            _hudHideTimer?.Stop();
            if (!_closed && _player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                HudOverlay.Opacity = 0;
        };

        _spaceHoldTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _spaceHoldTimer.Tick += (_, _) =>
        {
            _spaceHoldTimer?.Stop();
            if (_isSpaceDown && _player is not null && !_closed)
            {
                _isSpaceFastForward = true;
                _player.PlaybackSession.PlaybackRate = 3.0;
                FastForwardBadge.Visibility = Visibility.Visible;
                if (_player.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
                    _player.Play();
            }
        };
    }

    private async Task LoadMediaAsync(string path)
    {
        if (_closed)
            return;

        DisposePlayer();
        HideStatusOverlay();

        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            if (_closed)
                return;

            var source = MediaSource.CreateFromStorageFile(file);
            var item = new MediaPlaybackItem(source);
            var player = new MediaPlayer
            {
                AutoPlay = true,
                Volume = VolumeSlider.Value / 100.0
            };

            _mediaSource = source;
            _playbackItem = item;
            _player = player;

            item.AudioTracksChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplySelectedAudioTrack);
            player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (_closed) return;
                ApplySelectedAudioTrack();
                player.PlaybackSession.PlaybackRate = _normalPlaybackRate;
                ResetHudTimer();
            });
            player.PlaybackSession.PlaybackStateChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayStateUi);
            player.MediaFailed += (_, _) => DispatcherQueue.TryEnqueue(() => _ = HandleMediaFailedAsync());

            PlayerElement.SetMediaPlayer(player);
            player.Source = item;
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutscenePlayerWindow), $"Load media failed ({ex.GetType().Name}).");
            await HandleMediaFailedAsync();
        }
    }

    private async Task HandleMediaFailedAsync()
    {
        if (_closed)
            return;

        if (!_hasAttemptedMp4Fallback && _ffmpegPath is not null)
        {
            _hasAttemptedMp4Fallback = true;
            string cachedMp4 = Path.Combine(_cacheDir, Path.GetFileNameWithoutExtension(_mkvPath) + ".preview.mp4");
            if (File.Exists(cachedMp4) && new FileInfo(cachedMp4).Length > 0)
            {
                _currentVideoPath = cachedMp4;
                await LoadMediaAsync(cachedMp4);
                return;
            }

            await TranscodeAndPlayMp4Async(cachedMp4);
            return;
        }

        ShowStatusOverlay(
            "Cutscene_PlayCodecFailed".GetLocalized(),
            showRing: false,
            showRetryButton: _ffmpegPath is not null && !_hasAttemptedMp4Fallback);
    }

    private async Task TranscodeAndPlayMp4Async(string cachedMp4)
    {
        if (_ffmpegPath is null || _closed)
            return;

        _transcodeCts?.Cancel();
        _transcodeCts?.Dispose();
        _transcodeCts = new CancellationTokenSource();
        CancellationToken token = _transcodeCts.Token;

        ShowStatusOverlay("Cutscene_AutoTranscoding".GetLocalized(), showRing: true, showRetryButton: false);
        string tempMp4 = Path.Combine(_cacheDir, $".preview-{Guid.NewGuid():N}.mp4");
        try
        {
            await CutsceneMediaService.ConvertToMp4Async(_ffmpegPath, _mkvPath, tempMp4, token);
            token.ThrowIfCancellationRequested();
            if (File.Exists(tempMp4) && new FileInfo(tempMp4).Length > 0)
            {
                File.Move(tempMp4, cachedMp4, overwrite: true);
                _currentVideoPath = cachedMp4;
                await LoadMediaAsync(cachedMp4);
                return;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutscenePlayerWindow), $"Auto MP4 transcode failed ({ex.GetType().Name}).");
        }
        finally
        {
            try { if (File.Exists(tempMp4)) File.Delete(tempMp4); } catch { }
        }

        if (!_closed)
            ShowStatusOverlay("Cutscene_Mp4Unavailable".GetLocalized(), showRing: false, showRetryButton: false);
    }

    private void ApplySelectedAudioTrack()
    {
        if (_playbackItem is null)
            return;

        int index = AudioTrackSelector.SelectedIndex >= 0 ? AudioTrackSelector.SelectedIndex : _preferredAudioIndex;
        if (index >= 0 && index < _playbackItem.AudioTracks.Count)
            _playbackItem.AudioTracks.SelectedIndex = index;
    }

    private void UpdatePlaybackProgress()
    {
        if (_closed || _player is null)
            return;

        TimeSpan duration = _player.PlaybackSession.NaturalDuration;
        TimeSpan position = _player.PlaybackSession.Position;

        if (duration.TotalSeconds > 0)
        {
            _updatingTimeline = true;
            TimelineSlider.Maximum = duration.TotalSeconds;
            TimelineSlider.Value = Math.Clamp(position.TotalSeconds, 0, duration.TotalSeconds);
            _updatingTimeline = false;
            CurrentTimeText.Text = FormatTime(position);
            DurationTimeText.Text = FormatTime(duration);
        }

        UpdateSubtitleOverlay(position);
    }

    private void UpdateSubtitleOverlay(TimeSpan position)
    {
        if (_activeCues.Count == 0)
        {
            if (SubtitleContainer.Visibility != Visibility.Collapsed)
                SubtitleContainer.Visibility = Visibility.Collapsed;
            return;
        }

        string? text = FindCueText(_activeCues, position);
        if (string.IsNullOrEmpty(text))
        {
            SubtitleContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            SubtitleText.Text = text;
            SubtitleShadowText.Text = text;
            SubtitleContainer.Visibility = Visibility.Visible;
        }
    }

    private static string? FindCueText(IReadOnlyList<CutsceneSubtitleCue> cues, TimeSpan position)
    {
        int lo = 0;
        int hi = cues.Count - 1;
        int candidate = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (cues[mid].Start <= position)
            {
                candidate = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (candidate < 0)
            return null;

        // Check nearby entries in case overlapping cues exist.
        for (int i = candidate; i >= Math.Max(0, candidate - 4); i--)
        {
            if (cues[i].Start <= position && position <= cues[i].End)
                return cues[i].Text;
        }
        return null;
    }

    private void UpdatePlayStateUi()
    {
        if (_player is null)
            return;
        bool playing = _player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing;
        PlayPauseIcon.Symbol = playing ? Symbol.Pause : Symbol.Play;
        if (!playing)
            ShowHud();
        else
            ResetHudTimer();
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void OnCanvasTapped(object sender, TappedRoutedEventArgs e)
    {
        RootGrid.Focus(FocusState.Programmatic);
        TogglePlayPause();
    }

    private void OnCanvasDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => ToggleFullscreen();

    private void TogglePlayPause()
    {
        if (_player is null)
            return;

        if (_player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            _player.Pause();
        else
            _player.Play();
        ResetHudTimer();
    }

    private void OnSeekBackClick(object sender, RoutedEventArgs e) => SeekBy(TimeSpan.FromSeconds(-5));
    private void OnSeekForwardClick(object sender, RoutedEventArgs e) => SeekBy(TimeSpan.FromSeconds(5));

    private void SeekBy(TimeSpan delta)
    {
        if (_player is null)
            return;
        TimeSpan duration = _player.PlaybackSession.NaturalDuration;
        TimeSpan target = _player.PlaybackSession.Position + delta;
        if (target < TimeSpan.Zero) target = TimeSpan.Zero;
        if (duration > TimeSpan.Zero && target > duration) target = duration;
        _player.PlaybackSession.Position = target;
        ResetHudTimer();
    }

    private void OnTimelineValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingTimeline || _player is null)
            return;
        _player.PlaybackSession.Position = TimeSpan.FromSeconds(e.NewValue);
        ResetHudTimer();
    }

    private void OnTimelinePointerReleased(object sender, PointerRoutedEventArgs e) => ResetHudTimer();

    private void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_player is null)
            return;
        double vol = Math.Clamp(e.NewValue / 100.0, 0, 1);
        _player.Volume = vol;
        _player.IsMuted = vol <= 0.001;
        if (vol > 0.001)
            _lastNonZeroVolume = vol;
        VolumeIcon.Symbol = _player.IsMuted ? Symbol.Mute : Symbol.Volume;
    }

    private void OnMuteClick(object sender, RoutedEventArgs e)
    {
        if (_player is null)
            return;
        if (_player.IsMuted || _player.Volume <= 0.001)
        {
            double restore = _lastNonZeroVolume > 0.05 ? _lastNonZeroVolume : 1.0;
            VolumeSlider.Value = restore * 100;
        }
        else
        {
            _lastNonZeroVolume = _player.Volume;
            VolumeSlider.Value = 0;
        }
    }

    private void OnSpeedChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeedSelector.SelectedItem is ComboBoxItem { Tag: string tag }
            && double.TryParse(tag, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double rate))
        {
            _normalPlaybackRate = rate;
            if (_player is not null && !_isSpaceFastForward)
                _player.PlaybackSession.PlaybackRate = rate;
        }
    }

    private void OnAudioTrackChanged(object sender, SelectionChangedEventArgs e) => ApplySelectedAudioTrack();

    private void OnSubtitleTrackChanged(object sender, SelectionChangedEventArgs e) => ApplySubtitleSelection();

    private void ApplySubtitleSelection()
    {
        int idx = SubtitleTrackSelector.SelectedIndex;
        if (idx <= 0 || idx - 1 >= _subtitleTracks.Count)
        {
            _activeCues = [];
            SubtitleContainer.Visibility = Visibility.Collapsed;
            return;
        }

        _activeCues = _subtitleTracks[idx - 1].Cues;
        if (_player is not null)
            UpdateSubtitleOverlay(_player.PlaybackSession.Position);
    }

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        AppWindow.SetPresenter(_isFullscreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);
        FullscreenIcon.Symbol = _isFullscreen ? Symbol.BackToWindow : Symbol.FullScreen;
        ResetHudTimer();
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        if (_onExportRequested is null)
            return;
        if (_player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            _player.Pause();
        await _onExportRequested();
    }

    private void OnRetryTranscodeClick(object sender, RoutedEventArgs e)
    {
        string cachedMp4 = Path.Combine(_cacheDir, Path.GetFileNameWithoutExtension(_mkvPath) + ".preview.mp4");
        _hasAttemptedMp4Fallback = true;
        _ = TranscodeAndPlayMp4Async(cachedMp4);
    }

    private void OnRootPointerMoved(object sender, PointerRoutedEventArgs e) => ResetHudTimer();

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Space:
                if (!_isSpaceDown)
                {
                    _isSpaceDown = true;
                    _isSpaceFastForward = false;
                    _spaceHoldTimer?.Start();
                }
                e.Handled = true;
                break;
            case VirtualKey.Left:
                SeekBy(TimeSpan.FromSeconds(-5));
                e.Handled = true;
                break;
            case VirtualKey.Right:
                SeekBy(TimeSpan.FromSeconds(5));
                e.Handled = true;
                break;
            case VirtualKey.Up:
                VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
                e.Handled = true;
                break;
            case VirtualKey.Down:
                VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
                e.Handled = true;
                break;
            case VirtualKey.F:
            case VirtualKey.F11:
                ToggleFullscreen();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                if (_isFullscreen)
                    ToggleFullscreen();
                else
                    Close();
                e.Handled = true;
                break;
        }
    }

    private void OnRootKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space)
            return;

        _spaceHoldTimer?.Stop();
        _isSpaceDown = false;
        if (_isSpaceFastForward)
        {
            _isSpaceFastForward = false;
            FastForwardBadge.Visibility = Visibility.Collapsed;
            if (_player is not null)
                _player.PlaybackSession.PlaybackRate = _normalPlaybackRate;
        }
        else
        {
            TogglePlayPause();
        }
        e.Handled = true;
    }

    private void ShowHud()
    {
        _hudHideTimer?.Stop();
        HudOverlay.Opacity = 1;
    }

    private void ResetHudTimer()
    {
        HudOverlay.Opacity = 1;
        _hudHideTimer?.Stop();
        if (_player?.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
            _hudHideTimer?.Start();
    }

    private void ShowStatusOverlay(string message, bool showRing, bool showRetryButton)
    {
        StatusOverlayText.Text = message;
        StatusRing.IsActive = showRing;
        StatusRing.Visibility = showRing ? Visibility.Visible : Visibility.Collapsed;
        RetryTranscodeButton.Visibility = showRetryButton ? Visibility.Visible : Visibility.Collapsed;
        StatusOverlay.Visibility = Visibility.Visible;
    }

    private void HideStatusOverlay()
    {
        StatusRing.IsActive = false;
        StatusOverlay.Visibility = Visibility.Collapsed;
    }

    private int FindPreferredAudioIndex()
    {
        string lang = ResourceExtensions.CurrentCulture?.Split('-')[0].ToLowerInvariant() ?? "zh";
        for (int i = 0; i < _audioTracks.Count; i++)
        {
            string? trackLang = _audioTracks[i].Language?.Split('-')[0].ToLowerInvariant();
            if (trackLang is not null && ((lang == "zh" && trackLang is "zh" or "zho" or "chi")
                    || (lang == "en" && trackLang is "en" or "eng")
                    || (lang == "ja" && trackLang is "ja" or "jpn")
                    || (lang == "ko" && trackLang is "ko" or "kor")))
            {
                return i;
            }
        }
        return 0;
    }

    private int FindPreferredSubtitleIndex()
    {
        string culture = ResourceExtensions.CurrentCulture ?? "zh-CN";
        string[] preferred = culture switch
        {
            "zh-TW" => ["cht", "zh-hant", "zh"],
            "zh-CN" => ["chs", "zh-hans", "zh"],
            "ja-JP" => ["jp", "ja", "jpn"],
            "ko-KR" => ["kr", "ko", "kor"],
            _ => ["en", "eng"]
        };

        foreach (string target in preferred)
        {
            for (int i = 0; i < _subtitleTracks.Count; i++)
            {
                string? code = _subtitleTracks[i].Track.Language?.ToLowerInvariant();
                if (code is not null && (code == target || code.StartsWith(target, StringComparison.Ordinal)))
                    return i;
            }
        }
        return _subtitleTracks.Count > 0 ? 0 : -1;
    }

    private static string GetSubtitleLabel(CutsceneSubtitleTrack track)
    {
        string? code = track.Language?.ToLowerInvariant();
        string? mapped = code switch
        {
            "chs" or "zh-hans" or "zh-cn" => "简体中文",
            "cht" or "zh-hant" or "zh-tw" => "繁體中文",
            "zh" or "zho" or "chi" => "Cutscene_VoiceChinese".GetLocalized(),
            "en" or "eng" or "en-us" => "Cutscene_VoiceEnglish".GetLocalized(),
            "ja" or "jp" or "jpn" => "Cutscene_VoiceJapanese".GetLocalized(),
            "ko" or "kr" or "kor" => "Cutscene_VoiceKorean".GetLocalized(),
            _ => null
        };
        return mapped ?? track.Name ?? track.Language ?? $"字幕 {track.Index + 1}";
    }

    private static string FormatTime(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");

    private void DisposePlayer()
    {
        PlayerElement.SetMediaPlayer(null);
        _player?.Dispose();
        _player = null;
        _playbackItem = null;
        _mediaSource?.Dispose();
        _mediaSource = null;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _transcodeCts?.Cancel();
        _transcodeCts?.Dispose();
        _positionTimer?.Stop();
        _hudHideTimer?.Stop();
        _spaceHoldTimer?.Stop();
        DisposePlayer();
    }
}
