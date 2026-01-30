using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views;

public sealed partial class AchievementWindow : Window
{
    public AchievementViewModel ViewModel { get; } = new();
    private WebView2 _crawlerWebView;
    private const string TargetUrl = "https://paimon.moe/achievement";
    
    private AchievementUserData _localData = new();
    private readonly string _saveFolderPath;
    private readonly string _saveFilePath;
    private bool _isImporting = false;

    public AchievementWindow()
    {
        InitializeComponent();
        
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        
        string docPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _saveFolderPath = Path.Combine(docPath, "fufu");
        _saveFilePath = Path.Combine(_saveFolderPath, "achievements.json");
        
        LoadLocalData();
        InitializeCrawler();
    }
    
    private async void OnExecuteImportScript(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.ImportScriptContent))
        {
            ViewModel.StatusMessage = "脚本内容不能为空";
            return;
        }

        ViewModel.IsLoading = true;
        ViewModel.StatusMessage = "正在执行导入脚本...";
        _isImporting = true; // 标记开始导入流程

        try
        {
            // 1. 执行用户输入的 JS 脚本
            // 注意：通常这些脚本会修改 localStorage，但不会立即更新 UI
            string result = await _crawlerWebView.ExecuteScriptAsync(ViewModel.ImportScriptContent);
            
            Debug.WriteLine($"脚本执行结果: {result}");

            // 2. 脚本执行后，通常需要刷新页面才能让 paimon.moe 读取新的 LocalStorage 并渲染
            ViewModel.StatusMessage = "脚本执行完毕，正在刷新页面以应用更改...";
            
            // 触发刷新，这会再次触发 Crawler_NavigationCompleted
            _crawlerWebView.Reload();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"脚本执行失败: {ex.Message}");
            ViewModel.StatusMessage = "脚本执行失败，请检查脚本格式";
            ViewModel.IsLoading = false;
            _isImporting = false;
        }
    }
    private async Task LoadCategoryDataAsync(AchievementCategory category)
    {
        if (category == null) return;

        // 1. 尝试加载本地数据
        if (_localData.AchievementData.ContainsKey(category.Name))
        {
            var localItems = _localData.AchievementData[category.Name];
            ViewModel.CurrentAchievements.Clear();
            foreach (var item in localItems)
            {
                ViewModel.CurrentAchievements.Add(item);
            }

            RegisterItemEvents();
            UpdateCategoryProgress(category);
            return;
        }

        // 2. 本地没有，尝试从网页抓取
        ViewModel.IsLoading = true;

        string clickScript = $@"
    (function() {{
        const cats = document.querySelectorAll('.category > div');
        for (let c of cats) {{
            if (c.innerText.includes('{category.Name}')) {{
                c.click();
                return true;
            }}
        }}
        return false;
    }})();";

        string result = await _crawlerWebView.ExecuteScriptAsync(clickScript);

        if (result == "true")
        {
            await Task.Delay(1500);
            await ScrapeCurrentAchievementsAsync();
        }
        else
        {
            ViewModel.StatusMessage = "同步失败";
            ViewModel.IsLoading = false;
        }
    }
    private void LoadLocalData()
    {
        try
        {
            if (File.Exists(_saveFilePath))
            {
                string json = File.ReadAllText(_saveFilePath);
                var data = JsonSerializer.Deserialize<AchievementUserData>(json);
                if (data != null)
                {
                    _localData = data;
                    
                    ViewModel.Categories.Clear();
                    foreach (var cat in _localData.Categories)
                    {
                        UpdateCategoryProgress(cat);
                        ViewModel.Categories.Add(cat);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"读取本地数据失败: {ex.Message}");
            ViewModel.StatusMessage = "读取本地存档失败";
        }
    }

    private void SaveData()
    {
        try
        {
            if (!Directory.Exists(_saveFolderPath))
            {
                Directory.CreateDirectory(_saveFolderPath);
            }
            
            if (ViewModel.SelectedCategory != null)
            {
                _localData.AchievementData[ViewModel.SelectedCategory.Name] = ViewModel.CurrentAchievements.ToList();
            }
            
            string json = JsonSerializer.Serialize(_localData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_saveFilePath, json);
            Debug.WriteLine("数据已自动保存");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"保存失败: {ex.Message}");
        }
    }
    
    private void UpdateCategoryProgress(AchievementCategory cat)
    {
        if (cat == null) return;
        
        if (_localData.AchievementData.ContainsKey(cat.Name))
        {
            var items = _localData.AchievementData[cat.Name];
            cat.TotalCount = items.Count;
            cat.CompletedCount = items.Count(x => x.IsCompleted);
        }
        else
        {
            ParseProgressString(cat);
        }
    }

    private void ParseProgressString(AchievementCategory cat)
    {
        if (string.IsNullOrEmpty(cat.Progress)) return;

        try
        {
            var match = Regex.Match(cat.Progress, @"(\d+)\s*/\s*(\d+)");
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int current))
                    cat.CompletedCount = current;
                if (int.TryParse(match.Groups[2].Value, out int total))
                    cat.TotalCount = total;
            }
        }
        catch
        {
            // ignored
        }
    }
    
    private void RegisterItemEvents()
    {
        foreach (var item in ViewModel.CurrentAchievements)
        {
            item.PropertyChanged -= Item_PropertyChanged;
            item.PropertyChanged += Item_PropertyChanged;
        }
    }

    private void Item_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AchievementItem.IsCompleted))
        {
            SaveData();

            if (ViewModel.SelectedCategory != null)
            {
                UpdateCategoryProgress(ViewModel.SelectedCategory);
            }
        }
    }
    

    private async void InitializeCrawler()
    {
        if (ViewModel.Categories.Count == 0)
        {
            ViewModel.IsLoading = true;
        }

        _crawlerWebView = new WebView2();
        WebViewContainer.Children.Add(_crawlerWebView);

        await _crawlerWebView.EnsureCoreWebView2Async();

        _crawlerWebView.NavigationCompleted += Crawler_NavigationCompleted;
        _crawlerWebView.Source = new Uri(TargetUrl);
    }

    private async void Crawler_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            ViewModel.IsLoading = false;
            _isImporting = false;
            return;
        }

        if (ViewModel.Categories.Count == 0 || _isImporting)
        {
            if (_isImporting) await Task.Delay(2000); // 导入后的缓冲
            else await Task.Delay(2000); // 初次加载的缓冲

            await ScrapeCategoriesAsync();

            // [修复] 这里不再调用 OnCategorySelectionChanged，而是调用新方法
            if (_isImporting && ViewModel.SelectedCategory != null)
            {
                await LoadCategoryDataAsync(ViewModel.SelectedCategory);
            }

            _isImporting = false;
        }
        else
        {
            ViewModel.IsLoading = false;
        }
    }

    // [新增] 切换导入面板显示的辅助方法 (绑定到界面上的“导入”按钮)
    private void ToggleImportPanel(object sender, RoutedEventArgs e)
    {
        ViewModel.IsImportPanelVisible = !ViewModel.IsImportPanelVisible;
    }
    
    private void OnViewDetailClick(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton btn && btn.DataContext is AchievementItem item)
        {
            try
            {
                string keyword = Uri.EscapeDataString(item.Title);
                string url = $"https://www.miyoushe.com/ys/search?keyword={keyword}";
                
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"无法打开链接: {ex.Message}");
            }
        }
    }
    private async Task ScrapeCategoriesAsync()
    {
        string script = @"
        (function() {
            const items = [];
            const cats = document.querySelectorAll('.category > div');
            cats.forEach(c => {
                const nameEl = c.querySelector('p.font-semibold');
                const progressEl = c.querySelector('div > p.text-gray-900') || c.querySelector('div > p.text-gray-400');
                if(nameEl) {
                    items.push({
                        Name: nameEl.innerText,
                        Progress: progressEl ? progressEl.innerText.split('\n')[0] : '0/0',
                        IsActive: false
                    });
                }
            });
            return JSON.stringify(items);
        })();";

        try
        {
            string json = await _crawlerWebView.ExecuteScriptAsync(script);
            string unescaped = JsonSerializer.Deserialize<string>(json);
            var categories = JsonSerializer.Deserialize<List<AchievementCategory>>(unescaped);

            DispatcherQueue.TryEnqueue(() =>
            {
                ViewModel.Categories.Clear();
                foreach (var cat in categories)
                {
                    ParseProgressString(cat);
                    ViewModel.Categories.Add(cat);
                }
                
                _localData.Categories = categories;
                SaveData();

                ViewModel.IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Scrape Categories Failed: {ex.Message}");
        }
    }

// 修改原有的事件处理函数
    private async void OnCategorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel.SelectedCategory != null)
        {
            // 调用提取出来的方法
            await LoadCategoryDataAsync(ViewModel.SelectedCategory);
        }
    }

    private async Task ScrapeCurrentAchievementsAsync()
    {
        string script = @"
        (function() {
            const items = [];
            const cards = document.querySelectorAll('.bg-item.rounded-xl.px-2.py-1');
            
            cards.forEach(card => {
                const titleEl = card.querySelector('p.font-semibold');
                const descEl = card.querySelector('p.text-gray-400');
                const verEl = card.querySelector('span.bg-background'); 
                const gemEl = card.querySelector('p.mr-1'); 

                if(titleEl && descEl) {
                     items.push({
                        Title: titleEl.innerText,
                        Description: descEl.innerText,
                        RewardCount: gemEl ? gemEl.innerText : '0',
                        Version: verEl ? verEl.innerText : '',
                        IsCompleted: false 
                     });
                }
            });
            return JSON.stringify(items);
        })();";

        try
        {
            string json = await _crawlerWebView.ExecuteScriptAsync(script);
            string unescaped = JsonSerializer.Deserialize<string>(json);
            var scrapedItems = JsonSerializer.Deserialize<List<AchievementItem>>(unescaped);

            DispatcherQueue.TryEnqueue(() =>
            {
                var existingItems = _localData.AchievementData.ContainsKey(ViewModel.SelectedCategory.Name)
                    ? _localData.AchievementData[ViewModel.SelectedCategory.Name]
                    : new List<AchievementItem>();

                ViewModel.CurrentAchievements.Clear();
                if (scrapedItems != null)
                {
                    foreach (var newItem in scrapedItems)
                    {
                        if (string.IsNullOrEmpty(newItem.Description)) continue;
                        
                        var oldItem = existingItems.FirstOrDefault(x => x.Title == newItem.Title);
                        if (oldItem != null)
                        {
                            newItem.IsCompleted = oldItem.IsCompleted;
                        }
                        else
                        {
                            newItem.IsCompleted = false;
                        }

                        ViewModel.CurrentAchievements.Add(newItem);
                    }
                }

                RegisterItemEvents();
                SaveData();
                
                UpdateCategoryProgress(ViewModel.SelectedCategory);

                ViewModel.IsLoading = false;
                ViewModel.StatusMessage = $"已更新 {ViewModel.CurrentAchievements.Count} 个成就";
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Scrape Content Failed: {ex.Message}");
            DispatcherQueue.TryEnqueue(() => ViewModel.IsLoading = false);
        }
    }
}