using System.IO.Compression;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.ViewModels;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using Windows.System;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage : Page
{
    public PluginSettingsViewModel ViewModel { get; }
    public MainViewModel MainVM { get; }
    public ControlPanelModel ControlPanelVM { get; }

    public PluginSettingsPage()
    {
        ViewModel = new PluginSettingsViewModel();
        MainVM = App.GetService<MainViewModel>();
        ControlPanelVM = App.GetService<ControlPanelModel>();
        InitializeComponent();
    }

    public bool InvertBool(bool value) => !value;

    private async void OnDownloadPluginClick(object sender, RoutedEventArgs e)
    {
        string urlLatest = "http://kr2-proxy.gitwarp.top:9980/https://github.com/CodeCubist/FufuLauncher--Plugins/blob/main/FuFuPlugin.zip";
        await DownloadAndInstallPluginAsync(urlLatest);
    }

    private async Task DownloadAndInstallPluginAsync(string proxyUrl)
    {
        var fileName = proxyUrl.Split('/').Last();
        if (fileName.Contains("?")) fileName = fileName.Split('?')[0];
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) 
            fileName = "FuFuPlugin.zip";
        
        var rawGithubUrl = proxyUrl.Replace("http://kr2-proxy.gitwarp.top:9980/", "");
        if (rawGithubUrl.Contains("github.com") && rawGithubUrl.Contains("/blob/") && !rawGithubUrl.Contains("?raw=true"))
        {
            rawGithubUrl += "?raw=true";
        }
        
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);
        var extractPath = Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(fileName) + "_Extract_" + Guid.NewGuid());
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "Plugins");

        if (!Directory.Exists(pluginsDir)) Directory.CreateDirectory(pluginsDir);
        
        var progressBar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 20, Margin = new Thickness(0, 10, 0, 0) };
        var statusText = new TextBlock { Text = "正在连接...", HorizontalAlignment = HorizontalAlignment.Center };
        var stackPanel = new StackPanel();
        stackPanel.Children.Add(statusText);
        stackPanel.Children.Add(progressBar);

        var progressDialog = new ContentDialog
        {
            Title = $"正在获取插件",
            Content = stackPanel,
            XamlRoot = XamlRoot
        };

        _ = progressDialog.ShowAsync();

        try
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);

            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
            {
                HttpResponseMessage response;
                bool usedFallback = false;
                
                try 
                {
                    response = await client.GetAsync(proxyUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) throw new Exception("主线路失败");
                }
                catch
                {
                    statusText.Text = "连接失败，正在尝试备用线路...";
                    usedFallback = true;
                    response = await client.GetAsync(rawGithubUrl, HttpCompletionOption.ResponseHeadersRead);
                    if (!response.IsSuccessStatusCode) throw new Exception($"下载失败 (HTTP {response.StatusCode})");
                }
                
                using (response)
                {
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                    var totalRead = 0L;
                    var buffer = new byte[8192];
                    
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read);
                            totalRead += read;
                            if (totalBytes != -1)
                            {
                                progressBar.Value = Math.Round((double)totalRead / totalBytes * 100, 0);
                                statusText.Text = $"{(usedFallback ? "备用" : "主")}线路下载中... {progressBar.Value}%";
                            }
                        }
                    }
                }
            }
            
            statusText.Text = "正在解压并安装...";
            progressBar.IsIndeterminate = true;
            
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
            Directory.CreateDirectory(extractPath);

            await Task.Run(() => ZipFile.ExtractToDirectory(tempPath, extractPath));
            
            var targetFolderName = Path.GetFileNameWithoutExtension(tempPath); 
            var finalDestDir = Path.Combine(pluginsDir, targetFolderName);
            
            var subDirs = Directory.GetDirectories(extractPath);
            string sourceDirToMove = (subDirs.Length == 1 && Directory.GetFiles(extractPath).Length == 0) ? subDirs[0] : extractPath;

            if (Directory.Exists(finalDestDir)) Directory.Delete(finalDestDir, true);
            
            await Task.Run(() => MoveDirectorySafe(sourceDirToMove, finalDestDir));
            
            progressDialog.Hide();
            ViewModel.LoadConfiguration();
            
            WeakReferenceMessenger.Default.Send(new NotificationMessage("成功", "插件已安装并刷新", NotificationType.Success));
        }
        catch (Exception ex)
        {
            progressDialog.Hide();
            var failDialog = new ContentDialog
            {
                Title = "错误",
                Content = $"安装失败：{ex.Message}",
                PrimaryButtonText = "手动下载",
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot
            };
            if (await failDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                await Launcher.LaunchUriAsync(new Uri(rawGithubUrl));
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
        }
    }

    private void MoveDirectorySafe(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            MoveDirectorySafe(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
        Directory.Delete(sourceDir, true);
    }
}