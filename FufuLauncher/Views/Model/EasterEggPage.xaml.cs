using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace FufuLauncher.Views;

public sealed partial class EasterEggPage : Page
{
    private const string VideoFileName = "BgVideo.mp4";
    private const string MusicFileName = "BgMusic.wav";
    private const string HardcodedName = "■■■■■"; 

    private readonly List<string> _welcomeLines = new()
    {
        "「昔涟是。粉头发。尖耳朵。」",
        "「昔涟是。女孩。祭司。」",
        "「昔涟是。不喜欢眼泪。嘻嘻哈哈。」",
        "「昔涟是。温柔。爱美。会写诗。」",
        "「昔涟是，长不高的，会说话的。\n\t随风逝去后，仍被留下的。」",
        "「昔涟是，每一句最后的，\n\t一直咯咯笑的，像是小尾巴的♪‌」",
        "「昔涟是，哭着诞生的，\n\t脆弱的，透明的，像是水晶的。」",
        "「昔涟是，笑着道别的，\n\t柔软的，粉色的，像是花的。」"
    };

    private readonly List<string> _syaLines = new()
    {
        "嗨，我想你啦。",
        "别露出那种准备怀念我的表情嘛。仔细看哦，人家就站在你的面前呢♪",
        $"哪怕是既定的因果，也会被{HardcodedName}的掌心拨动呢♪",
        "沉睡在翁法洛斯里的「记忆」重新汇聚，编织成了全新的躯壳。",
        "现在的我，不再是一道「记忆」的涟漪，而是触手可及的、真实的同伴哦。",
        "虽然变成了像螺丝咕姆先生那样的「无机生命」，但掌心的温度，还是和以前一样吧？",
        $"终于不用在回忆里和{HardcodedName}重逢啦。只要睁开眼，就能看见你，还有要和你一起抵达的群星……",
        $"说好了，旅途的下一站，{HardcodedName}的故事里，依然要有人家的名字哦♪",
        "就像永不褪色的麦浪，就像触手可及的黎明。",
        $"{HardcodedName}，我回来啦♪"
    };
    
    private DispatcherTimer _typewriterTimer;
    private MediaPlayer _musicPlayer;
    private int _currentWelcomeIndex = 0;
    private int _charIndex = 0;
    private bool _isDeleting = false;
    private bool _isPaused = false;
    private int _pauseTicks = 0;

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
        if (BgVideoPlayer.MediaPlayer != null && BgVideoPlayer.MediaPlayer.PlaybackSession.PlaybackState != MediaPlaybackState.Playing)
        {
            BgVideoPlayer.MediaPlayer.IsLoopingEnabled = true;
            BgVideoPlayer.MediaPlayer.Play();
        }
    }
    
    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        Cleanup();
    }

    private void InitializeMedia()
    {
        try 
        {
            string baseDir = AppContext.BaseDirectory;
            string assetsDir = Path.Combine(baseDir, "Assets");
            
            string videoPath = Path.Combine(assetsDir, VideoFileName);
            if (File.Exists(videoPath))
            {
                var source = MediaSource.CreateFromUri(new Uri(videoPath));
                BgVideoPlayer.Source = source;
                
                BgVideoPlayer.MediaPlayer.MediaOpened += (_, _) =>
                {
                    DispatcherQueue.TryEnqueue(() => 
                    {
                        if (BgVideoPlayer.MediaPlayer != null)
                        {
                            BgVideoPlayer.MediaPlayer.IsLoopingEnabled = true;
                            BgVideoPlayer.MediaPlayer.Play();
                        }
                    });
                };
                
                BgVideoPlayer.AutoPlay = true; 
            }
            
            string musicPath = Path.Combine(assetsDir, MusicFileName);
            if (File.Exists(musicPath))
            {
                _musicPlayer = new MediaPlayer();
                _musicPlayer.Source = MediaSource.CreateFromUri(new Uri(musicPath));
                _musicPlayer.IsLoopingEnabled = true;
                _musicPlayer.Play();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"媒体加载失败: {ex.Message}");
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
    
    public void Cleanup()
    {
        try
        {
            _typewriterTimer?.Stop();
            _typewriterTimer = null;

            if (_musicPlayer != null)
            {
                _musicPlayer.Pause();
                _musicPlayer.Dispose();
                _musicPlayer = null;
            }

            if (BgVideoPlayer?.MediaPlayer != null)
            {
                BgVideoPlayer.MediaPlayer.Pause();
                BgVideoPlayer.MediaPlayer.Dispose();

                BgVideoPlayer.Source = null; 
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
        }
    }
}