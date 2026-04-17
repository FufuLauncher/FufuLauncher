using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Diagnostics;
using System.Text.Json;

namespace FufuLauncher.Views
{
    public class BrowserMessage
    {
        public string type { get; set; }
        public string name { get; set; }
        public string url { get; set; }
    }

    public sealed partial class UpdateWindow : Window
    {
        private readonly string _shareUrl = "https://wwaoi.lanzouu.com/b00wnb99ef";
        private string _targetFileName = string.Empty;
        private string _targetFileUrl = string.Empty;
        private string _directDownloadUrl = string.Empty;
        
        private const string CUSTOM_UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36";

        private bool _isPwdSubmitted = false;
        private bool _isFileFoundProcessed = false;
        private bool _isDownloadLinkProcessed = false;
        private string _localVersion = string.Empty;

        private DispatcherTimer _timeoutTimer;

        public UpdateWindow()
        {
            this.InitializeComponent();
            LoadLocalVersion();

            ExtendsContentIntoTitleBar = true;
            IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            Microsoft.UI.WindowId windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            Microsoft.UI.Windowing.AppWindow appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            
            MiniWebView.Loaded += MiniWebView_Loaded;
            this.Closed += UpdateWindow_Closed;
        }

        private void MiniWebView_Loaded(object sender, RoutedEventArgs e)
        {
            MiniWebView.Loaded -= MiniWebView_Loaded;
            InitializeWebViewAsync();
        }

        private void UpdateWindow_Closed(object sender, WindowEventArgs args)
        {
            StopTimeout();
            MiniWebView?.Close();
        }
        
        private void LoadLocalVersion()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Update.json");
                if (File.Exists(jsonPath))
                {
                    string jsonContent = File.ReadAllText(jsonPath);
                    var updateData = JsonSerializer.Deserialize<JsonElement>(jsonContent);
                    if (updateData.TryGetProperty("Version", out var versionElement))
                    {
                        _localVersion = versionElement.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"读取本地配置异常: {ex.Message}");
            }
        }

        private void CleanupOldWebView2Data(string baseFolder)
        {
            try
            {
                if (Directory.Exists(baseFolder))
                {
                    var dirs = Directory.GetDirectories(baseFolder, "WebView2Data_Update_*");
                    foreach (var dir in dirs)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        private async void InitializeWebViewAsync()
        {
            try
            {
                var envOptions = new CoreWebView2EnvironmentOptions();
                envOptions.AdditionalBrowserArguments = $"--user-agent=\"{CUSTOM_UA}\" --disable-blink-features=AutomationControlled";
                
                string baseDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher");
                CleanupOldWebView2Data(baseDataFolder);
                
                string userDataFolder = Path.Combine(baseDataFolder, $"WebView2Data_Update_{Guid.NewGuid():N}");
                
                var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, userDataFolder, envOptions);

                await MiniWebView.EnsureCoreWebView2Async(env);
                
                if (MiniWebView.CoreWebView2 == null)
                {
                    throw new Exception("WebView2内核加载失败，请检查系统是否已正确安装WebView2 Runtime");
                }

                MiniWebView.CoreWebView2.Settings.UserAgent = CUSTOM_UA;
                string injectionScript = @"
                (function() {
                    Object.defineProperty(navigator, 'webdriver', { get: () => undefined });
                    let linkFound = false;

                    function findDownloadLinkInFrames(doc) {
                        if (!doc) return null;
                        try {
                            var btns = doc.querySelectorAll('a.txt');
                            for (var j = 0; j < btns.length; j++) {
                                if (btns[j].innerText.indexOf('普通下载') !== -1) {
                                    return btns[j].href;
                                }
                            }
                            
                            var frames = doc.querySelectorAll('iframe');
                            for (var i = 0; i < frames.length; i++) {
                                try {
                                    var frameDoc = frames[i].contentDocument || frames[i].contentWindow.document;
                                    var link = findDownloadLinkInFrames(frameDoc);
                                    if (link) return link;
                                } catch (e) {
                                }
                            }
                        } catch (e) {}
                        return null;
                    }

                    function checkAndReport() {
                        if (linkFound) return;

                        try {
                            var pwdInput = document.getElementById('pwd');
                            var subBtn = document.getElementById('sub');
                            var pwdLoad = document.getElementById('pwdload');
                            if (pwdInput && subBtn && pwdLoad && pwdLoad.style.display !== 'none') {
                                window.chrome.webview.postMessage(JSON.stringify({ type: 'pwd_found' }));
                            }

                            var items = document.querySelectorAll('#infos #ready');
                            if (items.length > 0) {
                                for(var i=0; i<items.length; i++) {
                                    var link = items[i].querySelector('#name a');
                                    if (link && link.innerText.toLowerCase().indexOf('.exe') !== -1) {
                                        window.chrome.webview.postMessage(JSON.stringify({ 
                                            type: 'file_found', 
                                            name: link.innerText.trim(), 
                                            url: link.href 
                                        }));
                                        return;
                                    }
                                }
                            }

                            var finalLink = findDownloadLinkInFrames(document);
                            if (finalLink) {
                                linkFound = true;
                                window.chrome.webview.postMessage(JSON.stringify({ type: 'download_link', url: finalLink }));
                                return;
                            }

                        } catch (e) { }
                    }

                    window.chrome.webview.addEventListener('message', function(e) {
                        if (e.data === 'submit_pwd') {
                            var pwdInput = document.getElementById('pwd');
                            var subBtn = document.getElementById('sub');
                            if (pwdInput && subBtn) {
                                pwdInput.value = '6hnh';
                                subBtn.click();
                            }
                        }
                    });

                    var observer = new MutationObserver(function(mutations) { checkAndReport(); });
                    if (document.body || document.documentElement) {
                        observer.observe(document.body || document.documentElement, { childList: true, subtree: true });
                    }
                    
                    window.addEventListener('DOMContentLoaded', checkAndReport);
                    setInterval(checkAndReport, 1000); 
                })();
                ";

                MiniWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                await MiniWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(injectionScript);
                
                StartTimeout(45, "获取下载地址超时。");
                MiniWebView.Source = new Uri(_shareUrl);
            }
            catch (Exception ex)
            {
                UpdateUIStatus(ex.Message, true);
            }
        }

        private void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                string json = args.TryGetWebMessageAsString();
                var msg = JsonSerializer.Deserialize<BrowserMessage>(json);

                if (msg == null) return;

                if (msg.type == "pwd_found" && !_isPwdSubmitted)
                {
                    _isPwdSubmitted = true;
                    UpdateUIStatus("验证提取码");
                    MiniWebView.CoreWebView2.PostWebMessageAsString("submit_pwd");
                }
                else if (msg.type == "file_found" && !_isFileFoundProcessed)
                {
                    _isFileFoundProcessed = true;
                    _targetFileName = msg.name;
                    _targetFileUrl = msg.url;
                    
                    string cloudVersion = _targetFileName
                        .Replace("FufuLauncher_Setup_v", "")
                        .Replace(".exe", "")
                        .Trim();
                    
                    if (cloudVersion == _localVersion)
                    {
                        StopTimeout();
                        this.DispatcherQueue.TryEnqueue(async () => 
                        {
                            LoadingPanel.Visibility = Visibility.Collapsed;
                            var dialog = new ContentDialog
                            {
                                Title = "版本检查",
                                Content = $"当前已是最新版本 (v{_localVersion})，无需更新。",
                                CloseButtonText = "确定",
                                XamlRoot = this.Content.XamlRoot
                            };
                            await dialog.ShowAsync();
                            this.Close();
                        });
                        return;
                    }
    
                    UpdateUIStatus("解析直链中");
                    StartTimeout(45, "获取下载地址超时");
                    MiniWebView.Source = new Uri(_targetFileUrl);
                }
                else if (msg.type == "download_link" && !_isDownloadLinkProcessed)
                {
                    _isDownloadLinkProcessed = true;
                    _directDownloadUrl = msg.url;
                    StopTimeout();
                    
                    this.DispatcherQueue.TryEnqueue(() => 
                    {
                        PromptUserForUpdate();
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"消息回调异常: {ex.Message}");
            }
        }

        private async void PromptUserForUpdate()
        {
            LoadingPanel.Visibility = Visibility.Collapsed;

            var dialog = new ContentDialog
            {
                Title = "版本安装包",
                Content = $"可用更新：\n{_targetFileName}\n\n是否立即下载并进行更新？",
                PrimaryButtonText = "立即更新",
                CloseButtonText = "暂不更新",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await DownloadUpdateAsync();
            }
            else
            {
                this.Close();
            }
        }

        private async Task DownloadUpdateAsync()
        {
            DownloadPanel.Visibility = Visibility.Visible;
            DownloadStatusTextBlock.Text = $"正在下载: {_targetFileName}";

            try
            {
                string extension = Path.GetExtension(_targetFileName);
                string nameWithoutExt = Path.GetFileNameWithoutExtension(_targetFileName);
                string uniqueFileName = $"{nameWithoutExt}_{DateTime.Now:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 6)}{extension}";
                string tempPath = Path.Combine(Path.GetTempPath(), uniqueFileName);
                
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", CUSTOM_UA);
                client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9");
                if (!string.IsNullOrEmpty(_targetFileUrl))
                {
                    client.DefaultRequestHeaders.Referrer = new Uri(_targetFileUrl);
                }

                using HttpResponseMessage response = await client.GetAsync(_directDownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode(); 

                long? totalBytes = response.Content.Headers.ContentLength;
                using Stream contentStream = await response.Content.ReadAsStreamAsync();
                
                using (FileStream fileStream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    byte[] buffer = new byte[8192];
                    bool isMoreToRead = true;
                    long totalRead = 0;

                    while (isMoreToRead)
                    {
                        int read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                        if (read == 0)
                        {
                            isMoreToRead = false;
                        }
                        else
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;

                            if (totalBytes.HasValue)
                            {
                                double progress = (double)totalRead / totalBytes.Value * 100;
                                double totalMb = totalBytes.Value / 1024.0 / 1024.0;
                                double readMb = totalRead / 1024.0 / 1024.0;
                                
                                DispatcherQueue.TryEnqueue(() => {
                                    DownloadProgressBar.Value = progress;
                                    ProgressTextBlock.Text = $"{progress:F1}% ({readMb:F1} MB / {totalMb:F1} MB)";
                                });
                            }
                        }
                    }
                } 

                this.DispatcherQueue.TryEnqueue(() => {
                    DownloadStatusTextBlock.Text = "正在启动...";
                });
                
                await Task.Delay(1000); 

                Process.Start(new ProcessStartInfo
                {
                    FileName = tempPath,
                    UseShellExecute = true
                });
                MiniWebView?.Close();
            }
            catch (Exception ex)
            {
                this.DispatcherQueue.TryEnqueue(() => {
                    UpdateUIStatus($"下载失败: {ex.Message}", true);
                    DownloadPanel.Visibility = Visibility.Collapsed;
                });
            }
        }

        #region 状态与超时
        
        private void UpdateUIStatus(string message, bool isError = false)
        {
            this.DispatcherQueue.TryEnqueue(() =>
            {
                if (isError)
                {
                    StopTimeout();
                    ErrorInfoBar.Message = message;
                    ErrorInfoBar.IsOpen = true;
                    LoadingPanel.Visibility = Visibility.Collapsed;
                }
                else
                {
                    StatusTextBlock.Text = message;
                }
            });
        }

        private void StartTimeout(int seconds, string errorMessage)
        {
            StopTimeout();
            _timeoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
            _timeoutTimer.Tick += (s, e) =>
            {
                UpdateUIStatus(errorMessage, true);
            };
            _timeoutTimer.Start();
        }

        private void StopTimeout()
        {
            if (_timeoutTimer != null)
            {
                _timeoutTimer.Stop();
                _timeoutTimer = null;
            }
        }

        #endregion
    }
}