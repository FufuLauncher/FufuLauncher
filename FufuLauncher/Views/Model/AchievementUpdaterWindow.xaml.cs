using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;
using System.Text.Json.Nodes;
using FufuLauncher.Constants;

namespace FufuLauncher.Views
{
    public sealed partial class AchievementUpdaterWindow : Window
    {
        private ContentDialog _progressDialog;
        private ProgressBar _dialogProgressBar;
        private TextBlock _dialogStatusText;
        private TextBlock _currentAchievementText;
        private JsonArray _allCategoriesData;
        
        private Window _browserWindow;
        private WebView2 _scraperWebView;

        public AchievementUpdaterWindow()
        {
            InitializeComponent();
        }

        private async void OnStartClick(object sender, RoutedEventArgs e)
        {
            StartButton.IsEnabled = false;
            _allCategoriesData = new JsonArray();

            InitializeBrowserWindow();

            CreateProgressDialog();
            _ = _progressDialog.ShowAsync();
            
            await Task.Delay(2000);
            await RunUpdateScript();
        }

        private async void InitializeBrowserWindow()
        {
            _browserWindow = new Window();
            _browserWindow.Title = "请勿操作此窗口！";
            
            _browserWindow.SystemBackdrop = new MicaBackdrop();

            _scraperWebView = new WebView2();
            _browserWindow.Content = _scraperWebView;

            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(_browserWindow);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            
            appWindow.SetIcon("WindowIcon.ico");
            appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 800));
            
            _browserWindow.Closed += (s, args) =>
            {
                if (!StartButton.IsEnabled && _progressDialog != null)
                {
                    _progressDialog.Hide();
                    StartButton.IsEnabled = true;
                }
            };

            _browserWindow.Activate();

            await _scraperWebView.EnsureCoreWebView2Async();
            _scraperWebView.WebMessageReceived += ScraperWebView_WebMessageReceived;
            _scraperWebView.Source = new Uri(ApiEndpoints.SeelieAchievementsUrl);
        }

        private void CreateProgressDialog()
        {
            var stackPanel = new StackPanel { Spacing = 10, Width = 400 };
            
            _dialogStatusText = new TextBlock { Text = "正在初始化组件...", TextWrapping = TextWrapping.Wrap };
            _dialogProgressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, IsIndeterminate = true };
            _currentAchievementText = new TextBlock { 
                Text = "等待页面响应", 
                FontSize = 12, 
                Opacity = 0.6, 
                FontStyle = Windows.UI.Text.FontStyle.Italic 
            };

            stackPanel.Children.Add(_dialogStatusText);
            stackPanel.Children.Add(_dialogProgressBar);
            stackPanel.Children.Add(new TextBlock { Text = "当前进度状态：", FontSize = 12, Margin = new Thickness(0,10,0,0) });
            stackPanel.Children.Add(_currentAchievementText);

            _progressDialog = new ContentDialog
            {
                Title = "正在获取成就数据",
                Content = stackPanel,
                CloseButtonText = "取消",
                XamlRoot = Content.XamlRoot
            };
        }

        private async Task RunUpdateScript()
        {
            string script = @"
                (async function() {
                    const wait = ms => new Promise(res => setTimeout(res, ms));
                    const sendMsg = (obj) => window.chrome.webview.postMessage(obj);

                    const waitForSelector = async (sel, timeout=15000) => {
                        let time = 0;
                        while(!document.querySelector(sel) && time < timeout) {
                            await wait(500);
                            time += 500;
                        }
                        return document.querySelector(sel);
                    };

                    const URL_ICON_SINGLE = 'https://act-upload.mihoyo.com/wiki-user-upload/2024/09/16/76362111/27ec1c00c5bb06745e16039a10ff3aaa_145617018918069405.png';
                    const URL_ICON_MULTI = 'https://act-upload.mihoyo.com/wiki-user-upload/2024/09/16/76362111/42fd361ea1336daf7cd33bf187e62fa2_8745920187606549502.png';
                    const URL_ICON_PRIMO = 'https://upload-bbs.mihoyo.com/upload/2022/12/26/300350281/fb334cf8fd21e669ed6e7aa95890e2b5_2523388253479178875.png';

                    async function md5_short(str) {
                        const encoder = new TextEncoder();
                        const data = encoder.encode(str);
                        const hashBuffer = await crypto.subtle.digest('SHA-256', data);
                        const hashArray = Array.from(new Uint8Array(hashBuffer));
                        return hashArray.map(b => b.toString(16).padStart(2, '0')).join('').substring(0, 8);
                    }

                    try {
                        if (!location.pathname.includes('/achievements')) {
                            location.href = 'https://seelie.me/achievements';
                            let initWait = 0;
                            while (!location.pathname.endsWith('/achievements') && initWait < 10000) {
                                await wait(500);
                                initWait += 500;
                            }
                            await wait(2000);
                        }
                        
                        await waitForSelector('div[role=""link""]');
                        let catElements = Array.from(document.querySelectorAll('div[role=""link""]'));
                        
                        let categories = catElements.map(el => {
                            let title = el.querySelector('p.text-xl')?.textContent.trim() || '未知分类';
                            let imgSrc = el.querySelector('img')?.getAttribute('src') || '';
                            if (imgSrc && !imgSrc.startsWith('http')) imgSrc = 'https://seelie.me' + imgSrc;
                            return { title, icon_url: imgSrc, achievements: [] };
                        });

                        for (let idx = 0; idx < categories.length; idx++) {
                            let cat = categories[idx];
                            
                            if (location.pathname !== '/achievements') {
                                let backBtn = document.querySelector('button[aria-label=""Back""]');
                                if (backBtn) {
                                    backBtn.click();
                                } else {
                                    window.history.back();
                                }
                                
                                let backWaitTime = 0;
                                while (location.pathname !== '/achievements' && backWaitTime < 10000) {
                                    await wait(500);
                                    backWaitTime += 500;
                                }
                                await wait(1500);
                                await waitForSelector('div[role=""link""]');
                            }
                            
                            let links = document.querySelectorAll('div[role=""link""]');
                            if (!links[idx]) continue;

                            sendMsg({ 
                                type: 'progress', 
                                val: Math.floor((idx / categories.length) * 100), 
                                msg: `正在处理分类: ${cat.title} (${idx + 1}/${categories.length})`,
                                current: '读取节点数据...' 
                            });

                            let previousUrl = location.href;
                            links[idx].click();
                            
                            let forwardWaitTime = 0;
                            while (location.href === previousUrl && forwardWaitTime < 10000) {
                                await wait(500);
                                forwardWaitTime += 500;
                            }
                            await wait(2000);
                            
                            let cardContainer = await waitForSelector('div.rounded-3xl', 10000);
                            if (cardContainer) {
                                await wait(500); 
                                let cards = document.querySelectorAll('div.rounded-3xl');
                                
                                for (let card of cards) {

                                    let rows = Array.from(card.querySelectorAll('div[class*=""justify-between""]'));
                                    
                                    let series_achievements = [];
                                    for (let row of rows) {
                                        let titleEl = row.querySelector('p.font-semibold');
                                        if (!titleEl) continue;
                                        
                                        // 使用 textContent 提取文本，避免 innerText 因元素隐藏而返回空字符串
                                        let version = titleEl.querySelector('span')?.textContent || '';
                                        let title = titleEl.textContent.replace(version, '').trim();
                                        
                                        if (!title) continue;

                                        series_achievements.push({
                                            title, version,
                                            description: row.querySelector('p.opacity-75')?.textContent?.trim() || '',
                                            reward: row.querySelector('div.font-semibold.text-lg')?.textContent?.trim() || '0',
                                            is_completed: false,
                                            status: 'Incomplete',
                                            primogem_icon: URL_ICON_PRIMO
                                        });
                                    }

                                    if (series_achievements.length > 0) {
                                        let masterTitle = series_achievements[0].title;
                                        let sid = await md5_short(masterTitle);
                                        series_achievements.forEach((ach, i) => {
                                            ach.series_id = sid;
                                            ach.series_master_title = masterTitle;
                                            ach.stage_index = i + 1;
                                            ach.stage_total = series_achievements.length;
                                            ach.is_sub_stage = i > 0;
                                            ach.parent_ref = i > 0 ? masterTitle : null;
                                            ach.sibling_refs = series_achievements.map(x => x.title).filter(t => t !== ach.title);
                                            ach.icon_url = series_achievements.length > 1 ? URL_ICON_MULTI : URL_ICON_SINGLE;
                                            cat.achievements.push(ach);
                                        });
                                    }
                                }
                            }
                            
                            sendMsg({ type: 'category_data', data: cat });
                        }
                        
                        sendMsg({ type: 'done' });
                    } catch (err) {
                        sendMsg({ type: 'error', msg: err.message });
                    }
                })();
            ";

            if (_scraperWebView != null)
            {
                await _scraperWebView.ExecuteScriptAsync(script);
            }
        }

        private async void ScraperWebView_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                string type = root.GetProperty("type").GetString();

                if (type == "progress")
                {
                    _dialogProgressBar.IsIndeterminate = false;
                    _dialogProgressBar.Value = root.GetProperty("val").GetInt32();
                    _dialogStatusText.Text = root.GetProperty("msg").GetString();
                    _currentAchievementText.Text = root.GetProperty("current").GetString();
                }
                else if (type == "category_data")
                {
                    var categoryJson = root.GetProperty("data").GetRawText();
                    var categoryNode = JsonNode.Parse(categoryJson);
                    _allCategoriesData.Add(categoryNode);
                }
                else if (type == "done")
                {
                    _dialogProgressBar.Value = 100;
                    _dialogStatusText.Text = "数据结构构建完成，正在写入本地磁盘...";
                    _currentAchievementText.Text = "完成";

                    var options = new JsonSerializerOptions { 
                        WriteIndented = true, 
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping 
                    };
                    string finalJsonData = _allCategoriesData.ToJsonString(options);

                    string assetsDir = Path.Combine(AppContext.BaseDirectory, "Assets");
                    if (!Directory.Exists(assetsDir)) Directory.CreateDirectory(assetsDir);
                    
                    string filePath = Path.Combine(assetsDir, "genshin_achievements_linked.json");
                    await File.WriteAllTextAsync(filePath, finalJsonData);

                    _progressDialog.Hide();
                    StartButton.IsEnabled = true;
                    
                    if (_browserWindow != null)
                    {
                        _browserWindow.Close();
                    }
                    
                    var completeDialog = new ContentDialog {
                        Title = "执行完毕",
                        Content = "成就数据已成功从网络拉取并覆盖本地文件",
                        CloseButtonText = "确认退出",
                        XamlRoot = Content.XamlRoot
                    };
                    await completeDialog.ShowAsync();
                }
                else if (type == "error")
                {
                    _progressDialog.Hide();
                    StartButton.IsEnabled = true;
                    string errorMsg = root.GetProperty("msg").GetString();

                    if (_browserWindow != null)
                    {
                        _browserWindow.Close();
                    }
                    
                    var errorDialog = new ContentDialog {
                        Title = "执行异常",
                        Content = $"获取脚本发生错误: {errorMsg}",
                        CloseButtonText = "关闭",
                        XamlRoot = Content.XamlRoot
                    };
                    await errorDialog.ShowAsync();
                }
            }
            catch
            {
                // ignored
            }
        }
    }
}