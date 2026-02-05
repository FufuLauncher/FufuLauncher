using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views;

public sealed partial class PanelPage
{
    public ControlPanelModel ViewModel
    {
        get;
    }
    public MainViewModel MainViewModel
    {
        get;
    }

    public PanelPage()
    {
        ViewModel = App.GetService<ControlPanelModel>();
        MainViewModel = App.GetService<MainViewModel>();
        DataContext = ViewModel;

        Loaded += PanelPage_Loaded;

        InitializeComponent();

        ViewModel.RequestMetadataScrapeAction = async () => await StartScrapingSequenceAsync();

        _ = InitializeWebViewAsync();
    }
    private void OnOpenAchievementsClick(object sender, RoutedEventArgs e)
    {
        var window = new AchievementWindow();
        window.Activate();
    }
    private void OnOpenInventoryClick(object sender, RoutedEventArgs e)
    {
        var window = new InventoryWindow();
        window.Activate();
    }
    private void OnGachaCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            FlyoutBase.ShowAttachedFlyout(element);
        }
    }
    private void OnOpenPlayerRolesClick(object sender, RoutedEventArgs e)
    {
        var window = new PlayerInfoWindow();
        window.Activate();
    }
    private void OnOpenDailyNoteClick(object sender, RoutedEventArgs e)
    {
        var window = new DailyNoteWindow();
        window.Activate();
    }
    private void OnGridPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);
    }
    private async void BBSButton_Click(object sender, RoutedEventArgs e)
    {
        ContentDialog riskDialog = new()
        {
            Title = "安全提示",
            Content = "进入战绩信息页面可能会导致您的账户被标注为风险账户，进而导致部分功能（如某些自动化工具或特定网页访问）无法正常使用。是否确认继续？",
            PrimaryButtonText = "确认继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        
        ContentDialogResult result = await riskDialog.ShowAsync();
        
        if (result == ContentDialogResult.Primary)
        {
            var bbsWindow = new BBSWindow();
            bbsWindow.Activate();
        }
    }
    private void OnOpenVideoResourcesClick(object sender, RoutedEventArgs e)
    {
        var window = new VideoResourcesWindow();
        window.Activate();
    }

    private void OnGridPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ProtectedCursor = null;
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await MetadataCrawlerWebView.EnsureCoreWebView2Async();
            if (MetadataCrawlerWebView.CoreWebView2 != null)
            {
                MetadataCrawlerWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView2 初始化失败: {ex.Message}");
        }
    }

    private async void PanelPage_Loaded(object sender, RoutedEventArgs e)
    {
        EntranceStoryboard.Begin();
        
        await Task.Delay(600);
        
        await ViewModel.LoadSavedGachaDataAsync();
    }

    private async Task StartScrapingSequenceAsync()
    {
        if (MetadataCrawlerWebView.CoreWebView2 == null)
        {
            Debug.WriteLine("[Scraper] WebView2 未就绪，尝试重新初始化...");
            await InitializeWebViewAsync();
            if (MetadataCrawlerWebView.CoreWebView2 == null) return;
        }

        Debug.WriteLine("[Scraper] 开始爬取");
        var results = new List<ScrapedMetadata>();

        var chars = await ScrapeUrlSmartAsync("https://act.mihoyo.com/ys/event/calculator/index.html#/character", true);
        results.AddRange(chars);

        var weapons = await ScrapeUrlSmartAsync("https://act.mihoyo.com/ys/event/calculator/index.html#/weapon", false);
        results.AddRange(weapons);

        Debug.WriteLine($"[Scraper] 流程结束，获取到 {results.Count} 条数据。");
        ViewModel.UpdateMetadata(results);
    }

    private async Task<List<ScrapedMetadata>> ScrapeUrlSmartAsync(string url, bool isCharacter)
    {
        var typeName = isCharacter ? "角色" : "武器";
        var list = new List<ScrapedMetadata>();

        await ResetWebViewAsync();

        var navTcs = new TaskCompletionSource<bool>();
        void NavHandler(WebView2 s, CoreWebView2NavigationCompletedEventArgs e) => navTcs.TrySetResult(e.IsSuccess);

        MetadataCrawlerWebView.NavigationCompleted += NavHandler;
        MetadataCrawlerWebView.Source = new Uri(url);

        var navTask = navTcs.Task;
        var timeoutTask = Task.Delay(15000);

        var finishedTask = await Task.WhenAny(navTask, timeoutTask);
        MetadataCrawlerWebView.NavigationCompleted -= NavHandler;

        if (finishedTask == timeoutTask)
        {
            Debug.WriteLine($"[Scraper] {typeName}页面导航超时，但仍尝试注入脚本...");
        }
        else if (!navTask.Result)
        {
            Debug.WriteLine($"[Scraper] {typeName}页面导航失败。");
            return list;
        }

        var script = isCharacter ?
            @"
            (function() {
                window.scrollTo(0, document.body.scrollHeight);
                var items = [];
                var elements = document.querySelectorAll('.character-item');
                if (elements.length === 0) return JSON.stringify([]); 
                elements.forEach(el => {
                    var nameEl = el.querySelector('.gt-mobile-caption-c2-3');
                    var imgEl = el.querySelector('.gt-avatar-img img');
                    var eleEl = el.querySelector('.gt-avatar-left-element img');
                    if(nameEl && imgEl) {
                        items.push({
                            Name: nameEl.innerText,
                            ImgSrc: imgEl.src,
                            ElementSrc: eleEl ? eleEl.src : '',
                            Type: 'char'
                        });
                    }
                });
                return JSON.stringify(items);
            })();
            " :
            @"
            (function() {
                window.scrollTo(0, document.body.scrollHeight);
                var items = [];
                var elements = document.querySelectorAll('.weapon-item');
                if (elements.length === 0) return JSON.stringify([]); 
                elements.forEach(el => {
                    var nameEl = el.querySelector('.weapon-name');
                    var imgEl = el.querySelector('.gt-avatar-img img');
                    if(nameEl && imgEl) {
                        items.push({
                            Name: nameEl.innerText,
                            ImgSrc: imgEl.src,
                            ElementSrc: '',
                            Type: 'weapon'
                        });
                    }
                });
                return JSON.stringify(items);
            })();
            ";

        list = await PollForDataAsync(script, 20, 500);

        Debug.WriteLine($"[Scraper] {typeName} - 获取到 {list.Count} 条记录");
        return list;
    }

    private async Task<List<ScrapedMetadata>> PollForDataAsync(string script, int maxRetries, int intervalMs)
    {
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                var json = await MetadataCrawlerWebView.ExecuteScriptAsync(script);
                if (!string.IsNullOrEmpty(json) && json != "null")
                {
                    var outerJson = JsonSerializer.Deserialize<string>(json);

                    if (!string.IsNullOrEmpty(outerJson) && outerJson != "[]")
                    {
                        var items = JsonSerializer.Deserialize<List<ScrapedMetadata>>(outerJson);
                        if (items != null && items.Count > 0)
                        {
                            return items;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Poll] 尝试 {i + 1} 失败: {ex.Message}");
            }
            await Task.Delay(intervalMs);
        }
        return new List<ScrapedMetadata>();
    }

    private async Task ResetWebViewAsync()
    {
        try
        {
            var tcs = new TaskCompletionSource<bool>();
            void Handler(WebView2 s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(true);

            MetadataCrawlerWebView.NavigationCompleted += Handler;
            MetadataCrawlerWebView.Source = new Uri("about:blank");

            await Task.WhenAny(tcs.Task, Task.Delay(1500));
            MetadataCrawlerWebView.NavigationCompleted -= Handler;
        }
        catch
        {
            // ignored
        }
    }
}