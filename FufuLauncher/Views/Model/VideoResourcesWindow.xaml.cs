using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Windows.System;
using Windows.UI;
using FufuLauncher.Constants;

namespace FufuLauncher.Views;

public class VideoItem
{
    public string Title
    {
        get; set;
    }
    public string Cover
    {
        get; set;
    }
    public string PageUrl
    {
        get; set;
    }
}

public sealed partial class VideoResourcesWindow : Window, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public ObservableCollection<VideoItem> CharacterVideos { get; } = new();
    public ObservableCollection<VideoItem> CutsceneVideos { get; } = new();

    private bool _isLoading;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value) { _isLoading = value; OnPropertyChanged(); }
        }
    }

    public Visibility ToVisibility(bool isLoading) => isLoading ? Visibility.Visible : Visibility.Collapsed;

    private const string CHAR_VIDEO_URL = ApiEndpoints.CharVideoUrl;
    private const string CUTSCENE_URL = ApiEndpoints.CutsceneVideoUrl;

    public VideoResourcesWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        _ = InitializeAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private async Task InitializeAsync()
    {
        try
        {
            await CrawlerWebView.EnsureCoreWebView2Async();
            await LoadVideosAsync(CHAR_VIDEO_URL, CharacterVideos);
        }
        catch (Exception ex) { Debug.WriteLine($"Init failed: {ex.Message}"); }
    }

    private async void OnPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var pivot = sender as Pivot;
        var item = pivot.SelectedItem as PivotItem;
        if (item?.Tag?.ToString() == "Cutscene" && CutsceneVideos.Count == 0)
            await LoadVideosAsync(CUTSCENE_URL, CutsceneVideos);
        else if (item?.Tag?.ToString() == "Character" && CharacterVideos.Count == 0)
            await LoadVideosAsync(CHAR_VIDEO_URL, CharacterVideos);
    }

    private async Task LoadVideosAsync(string url, ObservableCollection<VideoItem> targetCollection)
{
    if (IsLoading) return;
    IsLoading = true;
    try
    {
        await CrawlerWebView.EnsureCoreWebView2Async();

        var tcs = new TaskCompletionSource<bool>();
        void OnNav(CoreWebView2 s, CoreWebView2NavigationCompletedEventArgs a) => tcs.TrySetResult(true);

        CrawlerWebView.CoreWebView2.NavigationCompleted += OnNav;
        CrawlerWebView.CoreWebView2.Navigate(url);
        await tcs.Task;
        CrawlerWebView.CoreWebView2.NavigationCompleted -= OnNav;

        await Task.Delay(4000);

        string jsCode = @"
    (function() {
        var items = [];
        var nodes = document.querySelectorAll('a[href*=""/ys/obc/content/""]');
        nodes.forEach(function(a) {
            var img = a.querySelector('.item__img');
            var title = a.querySelector('h5');
            if(img && title) {
                var link = a.getAttribute('href');
                if(link && !link.startsWith('http')) {
                    link = 'https://baike.mihoyo.com' + link;
                }
                
                var cover = img.getAttribute('data-src');
                var text = title.innerText || title.textContent;
                
                if(text && link) {
                    items.push({
                        Title: text.trim(),
                        Cover: cover ? cover.trim() : '',
                        PageUrl: link.trim()
                    });
                }
            }
        });
        return JSON.stringify(items);
    })();
";

        var json = await CrawlerWebView.ExecuteScriptAsync(jsCode);
        if (!string.IsNullOrEmpty(json) && json != "null")
        {
            var unescapedJson = JsonSerializer.Deserialize<string>(json);
            var items = JsonSerializer.Deserialize<List<VideoItem>>(unescapedJson);
            if (items != null)
            {
                targetCollection.Clear();
                foreach (var item in items) targetCollection.Add(item);
            }
        }
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"[VideoResource] Load List Failed: {ex.Message}");
    }
    finally
    {
        IsLoading = false;
    }
}

    private async Task<string> GetVideoSourceUrlAsync(string pageUrl)
    {
        try
        {
            await CrawlerWebView.EnsureCoreWebView2Async();
            
            var tcs = new TaskCompletionSource<bool>();
            void OnNav(CoreWebView2 s, CoreWebView2NavigationCompletedEventArgs a) => tcs.TrySetResult(true);

            CrawlerWebView.CoreWebView2.NavigationCompleted += OnNav;
            CrawlerWebView.CoreWebView2.Navigate(pageUrl);
            await tcs.Task;
            CrawlerWebView.CoreWebView2.NavigationCompleted -= OnNav;

            await Task.Delay(2500);

            string jsExtract = @"
                (function() {
                    var v = document.querySelector('video');
                    if(v && v.src) return v.src;
                    var src = document.querySelector('source');
                    if(src && src.src) return src.src;
                    return '';
                })();
            ";

            var json = await CrawlerWebView.ExecuteScriptAsync(jsExtract);
            var videoUrl = JsonSerializer.Deserialize<string>(json);

            if (string.IsNullOrEmpty(videoUrl))
            {
                await CrawlerWebView.ExecuteScriptAsync("document.querySelector('.custom-video-wrapper')?.click()");
                await Task.Delay(1000);
                json = await CrawlerWebView.ExecuteScriptAsync(jsExtract);
                videoUrl = JsonSerializer.Deserialize<string>(json);
            }

            return videoUrl;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Extract Video Failed: {ex.Message}");
            return null;
        }
    }

    private async void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoItem item)
        {
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                string videoUrl = await GetVideoSourceUrlAsync(item.PageUrl);

                if (!string.IsNullOrEmpty(videoUrl))
                {
                    OpenImmersivePlayer(item.Title, videoUrl);
                }
                else
                {
                    Debug.WriteLine("播放失败：无法解析视频地址");
                }
            }
            finally
            {
                IsLoading = false;
            }
        }
    }

    private async void OpenImmersivePlayer(string title, string videoUrl)
    {
        var playerWindow = new Window();
        playerWindow.Title = title;

        playerWindow.ExtendsContentIntoTitleBar = true;

        var rootGrid = new Grid();
        rootGrid.Background = new SolidColorBrush(Colors.Black);
        rootGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(32) });
        rootGrid.RowDefinitions.Add(new RowDefinition() { Height = new GridLength(1, GridUnitType.Star) });

        var titleBar = new Grid();
        titleBar.Background = new SolidColorBrush(Color.FromArgb(100, 0, 0, 0));
        titleBar.ColumnDefinitions.Add(new ColumnDefinition() { Width = new GridLength(16) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition() { Width = GridLength.Auto });
        var titleText = new TextBlock()
        {
            Text = title,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = new SolidColorBrush(Colors.White)
        };
        Grid.SetColumn(titleText, 2);
        titleBar.Children.Add(titleText);
        rootGrid.Children.Add(titleBar);
        playerWindow.SetTitleBar(titleBar);
        var webView = new WebView2();
        webView.DefaultBackgroundColor = Colors.Black;
        Grid.SetRow(webView, 1);
        rootGrid.Children.Add(webView);

        playerWindow.Content = rootGrid;
        playerWindow.Activate();

        await webView.EnsureCoreWebView2Async();

        string htmlContent = $@"
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset='utf-8'>
                <style>
                    body {{ 
                        margin:0; 
                        background-color:black; 
                        display:flex; 
                        justify-content:center; 
                        align-items:center; 
                        height:100vh; 
                        overflow:hidden; 
                        user-select: none;
                        font-family: 'Segoe UI', sans-serif;
                    }}
                    video {{ 
                        max-width:100%; 
                        max-height:100%; 
                        outline:none; 
                        box-shadow: 0 0 20px rgba(0,0,0,0.5);
                    }}
                    /* 倍速提示层 */
                    #speed-overlay {{
                        position: absolute;
                        top: 10%;
                        background: rgba(0, 0, 0, 0.6);
                        color: white;
                        padding: 8px 16px;
                        border-radius: 20px;
                        font-size: 14px;
                        display: none;
                        pointer-events: none;
                        backdrop-filter: blur(5px);
                        z-index: 999;
                        transition: opacity 0.2s;
                    }}
                </style>
            </head>
            <body>
                <div id='speed-overlay'>3.0x 速进中</div>
                
                <video controls autoplay loop name='media' id='player'>
                    <source src='{videoUrl}' type='video/mp4'>
                </video>

                <script>
                    const v = document.getElementById('player');
                    const overlay = document.getElementById('speed-overlay');
                    let isLongPress = false;
                    let pressTimer = null;
                    let lastRate = 1.0;

                    document.addEventListener('keydown', (e) => {{
                        if (e.code === 'Space' && !e.repeat) {{
                            
                            pressTimer = setTimeout(() => {{
                                isLongPress = true;
                                lastRate = v.playbackRate;
                                v.playbackRate = 3.0;
                                overlay.style.display = 'block';
                            }}, 200);
                        }}
                    }});

                    document.addEventListener('keyup', (e) => {{
                        if (e.code === 'Space') {{
                            clearTimeout(pressTimer);
                           
                            if (isLongPress) {{
                                v.playbackRate = (lastRate === 3.0) ? 1.0 : lastRate;
                                overlay.style.display = 'none';
                                isLongPress = false;
                            }} else {{
                                // if(v.paused) v.play(); else v.pause();
                            }}
                        }}
                    }});

                    window.addEventListener('keydown', function(e) {{
                        if(e.code === 'Space' && e.target == document.body) {{
                            e.preventDefault();
                        }}
                    }});
                </script>
            </body>
            </html>";

        webView.NavigateToString(htmlContent);
    }

    private async void OnDownloadClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is VideoItem item)
        {
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                string videoUrl = await GetVideoSourceUrlAsync(item.PageUrl);
                if (!string.IsNullOrEmpty(videoUrl))
                {
                    await Launcher.LaunchUriAsync(new Uri(videoUrl));
                }
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}