using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Constants;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views
{
    public class MaterialGroup
    {
        public string CategoryName
        {
            get; set;
        }
        public ObservableCollection<MaterialItem> Items { get; set; } = new();
    }

    public class MaterialItem
    {
        public string? Name
        {
            get; set;
        }
        public ImageSource IconImage
        {
            get; set;
        }
        public string? DomainName
        {
            get; set;
        }
        public List<ImageSource> Materials { get; set; } = new();
    }

    public class ActivityItem
    {
        public string Title
        {
            get; set;
        }
        public string Subtitle
        {
            get; set;
        }
        public ImageSource Image
        {
            get; set;
        }
        public string Countdown
        {
            get; set;
        }
    }

    internal class RawMaterialItem
    {
        public string name
        {
            get; set;
        }
        public string icon
        {
            get; set;
        }
        public string domain
        {
            get; set;
        }
        public List<string> materials
        {
            get; set;
        }
    }

    internal class RawActivityItem
    {
        public string title
        {
            get; set;
        }
        public string subtitle
        {
            get; set;
        }
        public string img
        {
            get; set;
        }
        public string time
        {
            get; set;
        }
    }

    public sealed partial class DailyNoteWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<MaterialGroup> MaterialGroups { get; } = new();
        public ObservableCollection<ActivityItem> Activities { get; } = new();

        private string _todayDate = "---";
        public string TodayDate
        {
            get => _todayDate;
            set
            {
                _todayDate = value; OnPropertyChanged(nameof(TodayDate));
            }
        }

        private string _statusText = "初始化...";
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value; OnPropertyChanged(nameof(StatusText));
            }
        }

        private string _birthdayRoleName;
        public string? BirthdayRoleName
        {
            get => _birthdayRoleName;
            set
            {
                _birthdayRoleName = value; OnPropertyChanged(nameof(BirthdayRoleName)); UpdateBirthdayVisibility();
            }
        }

        private ImageSource _birthdayRoleIcon;
        public ImageSource BirthdayRoleIcon
        {
            get => _birthdayRoleIcon;
            set
            {
                _birthdayRoleIcon = value; OnPropertyChanged(nameof(BirthdayRoleIcon));
            }
        }

        private Visibility _birthdayInfoVisibility = Visibility.Collapsed;
        public Visibility BirthdayInfoVisibility
        {
            get => _birthdayInfoVisibility;
            set
            {
                _birthdayInfoVisibility = value; OnPropertyChanged(nameof(BirthdayInfoVisibility));
            }
        }

        private bool _isLoadingMaterials = true;
        public bool IsLoadingMaterials
        {
            get => _isLoadingMaterials;
            set
            {
                _isLoadingMaterials = value; OnPropertyChanged(nameof(IsLoadingMaterials));
            }
        }

        private bool _isLoadingActivities = true;
        public bool IsLoadingActivities
        {
            get => _isLoadingActivities;
            set
            {
                _isLoadingActivities = value; OnPropertyChanged(nameof(IsLoadingActivities));
            }
        }

        private bool _isWebViewInitialized;
        private bool _isRefreshing;

        public DailyNoteWindow()
        {
            InitializeComponent();
            Activated += DailyNoteWindow_Activated;
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

        private async void DailyNoteWindow_Activated(object sender, WindowActivatedEventArgs args)
        {
            if (_isWebViewInitialized) return;
            _isWebViewInitialized = true;
            await InitializeAndFetchAsync();
        }

        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (_isRefreshing) return;
            _ = InitializeAndFetchAsync();
        }

        private async Task InitializeAndFetchAsync()
        {
            _isRefreshing = true;
            try
            {
                StatusText = "正在连接...";
                MaterialGroups.Clear();
                Activities.Clear();

                await ScraperWebView.EnsureCoreWebView2Async();
                
                ScraperWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                ScraperWebView.CoreWebView2.Settings.IsScriptEnabled = true;
                
                await ScraperWebView.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                    "{\"width\": 1920, \"height\": 1080, \"deviceScaleFactor\": 1, \"mobile\": false}");
                
                bool materialsSuccess = false;
                int retryCountMat = 0;
                IsLoadingMaterials = true;

                while (!materialsSuccess)
                {
                    if (retryCountMat > 0)
                    {
                        StatusText = $"正在连接...";
                        await Task.Delay(300);
                    }
                    
                    materialsSuccess = await FetchDailyMaterialsAsync();

                    if (!materialsSuccess)
                    {
                        retryCountMat++;
                        // if (retryCountMat > 10) break; 
                    }
                }
                IsLoadingMaterials = false;

                bool activitiesSuccess = false;
                int retryCountAct = 0;
                IsLoadingActivities = true;

                while (!activitiesSuccess)
                {
                    if (retryCountAct > 0)
                    {
                        StatusText = $"正在连接...";
                        await Task.Delay(300);
                    }

                    activitiesSuccess = await FetchActivitiesAsync();

                    if (!activitiesSuccess)
                    {
                        retryCountAct++;
                    }
                }
                IsLoadingActivities = false;
                StatusText = "更新完毕";
            }
            catch (Exception ex)
            {
                StatusText = "初始化异常";
                Debug.WriteLine($"[DailyNote] Init Error: {ex}");
                IsLoadingMaterials = false;
                IsLoadingActivities = false;
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private async Task<bool> WaitForElementAsync(string selector, int timeoutMs = 10000)
        {
            int elapsed = 0;
            while (elapsed < timeoutMs)
            {
                try
                {
                    string script = $"document.querySelectorAll('{selector}').length";
                    string result = await ScraperWebView.ExecuteScriptAsync(script);
                    if (int.TryParse(result, out int count) && count > 0) return true;
                }
                catch { /* ignored */ }
                await Task.Delay(200);
                elapsed += 200;
            }
            Debug.WriteLine($"[DailyNote] Timeout waiting for: {selector}");
            return false;
        }
        
        private async Task<bool> FetchDailyMaterialsAsync()
        {
            StatusText = "获取素材...";
            string url = ApiEndpoints.BaikeDailyMaterialsUrl;

            try
            {
                MaterialGroups.Clear();

                var tcs = new TaskCompletionSource<bool>();
                void NavHandler(WebView2 s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(true);

                ScraperWebView.NavigationCompleted += NavHandler;
                ScraperWebView.Source = new Uri(url);
                
                var navTask = tcs.Task;
                var delayTask = Task.Delay(20000);
                var completedTask = await Task.WhenAny(navTask, delayTask);

                ScraperWebView.NavigationCompleted -= NavHandler;

                if (completedTask == delayTask)
                {
                    Debug.WriteLine("[DailyNote] Navigation Timeout");
                    return false;
                }
                
                if (!await WaitForElementAsync(".channel__calendar"))
                {
                    Debug.WriteLine("[DailyNote] Wait Element Timeout");
                    return false;
                }

                string script = @"
                (function() {
                    var result = { date: '', birthday: null, groups: [] };
                    try {
                        var dateEl = document.querySelector('.calendar__today-date');
                        if(dateEl) result.date = dateEl.innerText.trim().replace(/[\r\n]/g, ' ');
                        var bdayEl = document.querySelector('.calendar__today-event-birthday');
                        if(bdayEl) {
                           var name = bdayEl.querySelector('.calendar__today-name');
                           var img = bdayEl.querySelector('img');
                           result.birthday = { 
                               name: name ? name.textContent.replace('今天是','').replace('的生日哦~','').trim() : '', 
                               img: img ? img.src : '' 
                           };
                        }
                        var rows = document.querySelectorAll('.cal-pc__row');
                        rows.forEach(row => {
                            var colName = row.querySelector('.cal-pc__col');
                            if(!colName || colName.classList.contains('cal-pc__slider') || colName.classList.contains('cal-pc__month')) return;
                            var catName = colName.textContent.trim();
                            if(catName.length < 2 && '一二三四五六日'.includes(catName)) return;
                            var group = { category: catName, items: [] };
                            var items = row.querySelectorAll('.cal-pc__item');
                            items.forEach(item => {
                                var nameEl = item.querySelector('.cal-pc__name');
                                var imgEl = item.querySelector('.cal-pc__img');
                                var imgUrl = imgEl ? (imgEl.getAttribute('data-src') || imgEl.src) : '';
                                var popover = item.querySelector('.cal-material-popover');
                                var domainName = '';
                                var matImages = [];
                                if(popover) {
                                    var domainEl = popover.querySelector('.way__content--title');
                                    if(domainEl) domainName = domainEl.textContent.trim();
                                    popover.querySelectorAll('.cal-material-popover__item img').forEach(m => {
                                        matImages.push(m.getAttribute('data-src') || m.src);
                                    });
                                }
                                if(nameEl) {
                                    group.items.push({
                                        name: nameEl.textContent.trim(),
                                        icon: imgUrl,
                                        domain: domainName,
                                        materials: matImages
                                    });
                                }
                            });
                            if(group.items.length > 0) result.groups.push(group);
                        });
                    } catch(e) { return JSON.stringify({error: e.message}); }
                    return JSON.stringify(result);
                })();";
                var json = await ScraperWebView.ExecuteScriptAsync(script);
                if (json != "null")
                {
                    var unescapedJson = JsonSerializer.Deserialize<string>(json);
                    if (unescapedJson != null && unescapedJson.Contains("\"error\":"))
                    {
                        Debug.WriteLine($"[Script Error] {unescapedJson}");
                        return false;
                    }

                    using var doc = JsonDocument.Parse(unescapedJson);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("date", out var d)) TodayDate = d.GetString();

                    if (root.TryGetProperty("birthday", out var bday) && bday.ValueKind != JsonValueKind.Null)
                    {
                        BirthdayRoleName = bday.GetProperty("name").GetString();
                        var bUrl = bday.GetProperty("img").GetString();
                        if (!string.IsNullOrEmpty(bUrl)) BirthdayRoleIcon = new BitmapImage(new Uri(bUrl));
                    }

                    if (root.TryGetProperty("groups", out var groups))
                    {
                        foreach (var g in groups.EnumerateArray())
                        {
                            var groupObj = new MaterialGroup { CategoryName = g.GetProperty("category").GetString() };
                            foreach (var item in g.GetProperty("items").EnumerateArray())
                            {
                                var rawItem = JsonSerializer.Deserialize<RawMaterialItem>(item.GetRawText());
                                var mItem = new MaterialItem
                                {
                                    Name = rawItem?.name,
                                    DomainName = rawItem?.domain
                                };
                                if (!string.IsNullOrEmpty(rawItem?.icon))
                                    mItem.IconImage = new BitmapImage(new Uri(rawItem.icon));
                                foreach (var matUrl in rawItem?.materials!)
                                {
                                    if (!string.IsNullOrEmpty(matUrl))
                                        mItem.Materials.Add(new BitmapImage(new Uri(matUrl)));
                                }
                                groupObj.Items.Add(mItem);
                            }
                            MaterialGroups.Add(groupObj);
                        }
                    }
                }
                
                return MaterialGroups.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Material Fetch] {ex}");
                return false;
            }
        }
        
        private async Task<bool> FetchActivitiesAsync()
        {
            StatusText = "获取活动...";
            string url = ApiEndpoints.BaikeActivitiesUrl;

            try
            {
                Activities.Clear();

                var tcs = new TaskCompletionSource<bool>();
                void NavHandler(WebView2 s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult(true);
                ScraperWebView.NavigationCompleted += NavHandler;
                ScraperWebView.Source = new Uri(url);
                
                var navTask = tcs.Task;
                var delayTask = Task.Delay(20000);
                var completedTask = await Task.WhenAny(navTask, delayTask);

                ScraperWebView.NavigationCompleted -= NavHandler;

                if (completedTask == delayTask) return false;

                if (!await WaitForElementAsync(".hit-card-list"))
                {
                    Debug.WriteLine("[Activities] Wait Element Timeout");
                    return false;
                }

                string script = @"
                (function() {
                    var list = [];
                    try {
                        var items = document.querySelectorAll('.hit-card-list li');
                        items.forEach(item => {
                           var title = item.querySelector('h5');
                           var subtitle = item.querySelector('p');
                           
                           var imgDiv = item.querySelector('.item__left');
                           var imgUrl = '';
                           if(imgDiv) {
                               imgUrl = imgDiv.getAttribute('data-src');
                               if(!imgUrl && imgDiv.style.backgroundImage) {
                                   imgUrl = imgDiv.style.backgroundImage.slice(4, -1).replace(/['""]/g, '');
                               }
                           }
                           if(!imgUrl) {
                               var imgTag = item.querySelector('img');
                               if(imgTag) imgUrl = imgTag.getAttribute('data-src') || imgTag.src;
                           }

                           var countdown = item.querySelector('.count-down');
                           
                           if(title) {
                               list.push({
                                   title: title.textContent.trim(),
                                   subtitle: subtitle ? subtitle.textContent.trim() : '',
                                   img: imgUrl || '',
                                   time: countdown ? countdown.textContent.trim() : ''
                               });
                           }
                        });
                    } catch(e) { return JSON.stringify({error: e.message}); }
                    return JSON.stringify(list);
                })();";

                var json = await ScraperWebView.ExecuteScriptAsync(script);
                if (json != "null")
                {
                    var unescapedJson = JsonSerializer.Deserialize<string>(json);
                    var items = JsonSerializer.Deserialize<List<RawActivityItem>>(unescapedJson);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            if (!string.IsNullOrEmpty(item.title))
                            {
                                var actItem = new ActivityItem
                                {
                                    Title = item.title,
                                    Subtitle = item.subtitle,
                                    Countdown = item.time
                                };
                                if (!string.IsNullOrEmpty(item.img))
                                    actItem.Image = new BitmapImage(new Uri(item.img));
                                Activities.Add(actItem);
                            }
                        }
                    }
                }
                
                return Activities.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Activity Fetch] {ex}");
                return false;
            }
        }

        private void UpdateBirthdayVisibility()
        {
            BirthdayInfoVisibility = string.IsNullOrEmpty(BirthdayRoleName) ? Visibility.Collapsed : Visibility.Visible;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}