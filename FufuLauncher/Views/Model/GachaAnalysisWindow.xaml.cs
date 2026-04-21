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

public sealed partial class GachaAnalysisWindow : Window
{
    public GachaAnalysisModel ViewModel { get; }

    public GachaAnalysisWindow()
    {
        ViewModel = App.GetService<GachaAnalysisModel>();
        
        InitializeComponent();
        
        RootGrid.DataContext = this;
        ExtendsContentIntoTitleBar = true;
        
        ViewModel.RequestMetadataScrapeAction = async () => await StartScrapingSequenceAsync();
        
        _ = InitializeWebViewAsync();
        _ = ViewModel.LoadSavedGachaDataAsync();
    }

    private void OnGachaCardTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element) FlyoutBase.ShowAttachedFlyout(element);
    }

    private void OnGridPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(element, InputSystemCursor.Create(InputSystemCursorShape.Hand));
        }
    }

    private void OnGridPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is UIElement element)
        {
            typeof(UIElement).GetProperty("ProtectedCursor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(element, null);
        }
    }

    private async void OnDeleteGachaDataClick(object sender, RoutedEventArgs e)
    {
        ContentDialog deleteDialog = new()
        {
            Title = "警告",
            Content = "确定要彻底删除本地保存的所有抽卡记录吗？此操作不可逆转！",
            PrimaryButtonText = "确认删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot
        };

        ContentDialogResult result = await deleteDialog.ShowAsync();
        if (result == ContentDialogResult.Primary) await ViewModel.ClearGachaDataAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await MetadataCrawlerWebView.EnsureCoreWebView2Async();
            if (MetadataCrawlerWebView.CoreWebView2 != null)
                MetadataCrawlerWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView2 初始化失败: {ex.Message}");
        }
    }

    private async Task StartScrapingSequenceAsync()
    {
        try
        {
            if (MetadataCrawlerWebView.CoreWebView2 == null)
            {
                await InitializeWebViewAsync();
                if (MetadataCrawlerWebView.CoreWebView2 == null)
                {
                    ViewModel.UpdateMetadata(null);
                    return;
                }
            }

            var results = new List<ScrapedMetadata>();
            var chars = await ScrapeUrlSmartAsync("https://act.mihoyo.com/ys/event/calculator/index.html#/character", true);
            results.AddRange(chars);

            var weapons = await ScrapeUrlSmartAsync("https://act.mihoyo.com/ys/event/calculator/index.html#/weapon", false);
            results.AddRange(weapons);

            ViewModel.UpdateMetadata(results);
        }
        catch
        {
            ViewModel.UpdateMetadata(null);
        }
    }

    private async Task<List<ScrapedMetadata>> ScrapeUrlSmartAsync(string url, bool isCharacter)
    {
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

        if (finishedTask != timeoutTask && !navTask.Result) return list;

        var script = isCharacter ?
            @"(function() {
                window.scrollTo(0, document.body.scrollHeight);
                var items = [];
                var elements = document.querySelectorAll('.character-item');
                if (elements.length === 0) return JSON.stringify([]); 
                elements.forEach(el => {
                    var nameEl = el.querySelector('.gt-mobile-caption-c2-3');
                    var imgEl = el.querySelector('.gt-avatar-img img');
                    var eleEl = el.querySelector('.gt-avatar-left-element img');
                    if(nameEl && imgEl) items.push({ Name: nameEl.innerText, ImgSrc: imgEl.src, ElementSrc: eleEl ? eleEl.src : '', Type: 'char' });
                });
                return JSON.stringify(items);
            })();" :
            @"(function() {
                window.scrollTo(0, document.body.scrollHeight);
                var items = [];
                var elements = document.querySelectorAll('.weapon-item');
                if (elements.length === 0) return JSON.stringify([]); 
                elements.forEach(el => {
                    var nameEl = el.querySelector('.weapon-name');
                    var imgEl = el.querySelector('.gt-avatar-img img');
                    if(nameEl && imgEl) items.push({ Name: nameEl.innerText, ImgSrc: imgEl.src, ElementSrc: '', Type: 'weapon' });
                });
                return JSON.stringify(items);
            })();";

        return await PollForDataAsync(script, 20, 500);
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
                        if (items != null && items.Count > 0) return items;
                    }
                }
            }
            catch { }
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
        catch { }
    }
}