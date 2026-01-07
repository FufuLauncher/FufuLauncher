using System.Diagnostics;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel
    {
        get;
    }
    public XamlUICommand OpenLinkCommand
    {
        get;
    }
    
    private void Copyright_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateCopyrightOpacity(0.8);
    }
    
    private void Copyright_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateCopyrightOpacity(0.05);
    }
    
    private void AnimateCopyrightOpacity(double toOpacity)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(300)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, CopyrightText);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
    private void ScreenshotButton_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateBlurOpacity(0);
    }

    private void ScreenshotButton_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateBlurOpacity(1.0);
    }

    private void AnimateBlurOpacity(double toOpacity)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, ScreenshotBlurBorder);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
    
    private void InfoCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateInfoButtonOpacity(1.0);
    }

    private void InfoCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        AnimateInfoButtonOpacity(0.0);
    }

    private void AnimateInfoButtonOpacity(double toOpacity)
    {
        if (InfoExpandButton == null) return;

        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            To = toOpacity,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EnableDependentAnimation = true
        };

        Storyboard.SetTarget(animation, InfoExpandButton);
        Storyboard.SetTargetProperty(animation, "Opacity");

        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private bool _isInitialized = false;

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        InitializeComponent();

        ViewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ViewModel.BackgroundVideoPlayer))
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    BackgroundVideo.SetMediaPlayer(ViewModel.BackgroundVideoPlayer);
                    Debug.WriteLine($"MainPage: MediaPlayer 已设置");
                });
            }
        };

        OpenLinkCommand = new XamlUICommand();
        OpenLinkCommand.ExecuteRequested += (sender, args) =>
        {
            if (args.Parameter is string url)
            {
                OpenLink(url);
            }
        };

    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _ = ViewModel.OnPageReturnedAsync();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isInitialized)
        {
            await ViewModel.InitializeAsync();
            _isInitialized = true;
        }
    }

    private void BannerImage_Loaded(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("轮播图加载成功");
    }

    private void BannerImage_ImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        Debug.WriteLine($"轮播图加载失败: {e.ErrorMessage}");
        if (sender is Image image && image.Parent is Border border)
        {
            border.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }

    private async void OpenLink(string url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            try
            {
                var uri = new Uri(url);
                await Windows.System.Launcher.LaunchUriAsync(uri);
                Debug.WriteLine($"打开链接: {url}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"打开链接失败: {ex.Message}");
            }
        }
    }
}