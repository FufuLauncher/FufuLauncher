/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Text;
using Windows.ApplicationModel.DataTransfer;
using FufuLauncher.Constants;
using Path = System.IO.Path;

namespace FufuLauncher.Views;

public class ContributorItem
{
    public string Name { get; set; }
    public string Url { get; set; }
    public string AvatarUrl { get; set; }
    public int Contributions { get; set; }
}

public sealed partial class AboutPage : Page
{
    private static readonly HttpClient httpClient = new();
    private static readonly string batchFilePath = Path.Combine(Environment.CurrentDirectory, "..\\download_build.bat");

    public AboutPage()
    {
        InitializeComponent();
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}.{version?.Revision}";

        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0");
        }

        _ = LoadContributorsAsync();
    }

    private async Task LoadContributorsAsync()
    {
        try
        {
            string apiUrl = "https://api.github.com/repos/FufuLauncher/FufuLauncher/contributors";
            var jsonDocument = await GetJsonFromUrl(apiUrl);

            if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                string errorMessage = "API限制或返回结构异常";
                if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object &&
                    jsonDocument.RootElement.TryGetProperty("message", out JsonElement messageElement))
                {
                    errorMessage = messageElement.GetString();
                }

                Debug.WriteLine($"[LoadContributorsAsync] 获取贡献者失败: {errorMessage}");
                ContributorsLoadingRing.IsActive = false;
                ContributorsErrorPanel.Visibility = Visibility.Visible;
                ContributorsErrorText.Text = "获取失败";
                return;
            }

            var elements = jsonDocument.RootElement.EnumerateArray();

            var allContributors = new List<ContributorItem>();
            foreach (var element in elements)
            {
                string login = element.GetProperty("login").GetString();
                string url = element.GetProperty("html_url").GetString();
                string avatarUrl = element.GetProperty("avatar_url").GetString();
                
                int contributions = 0;
                if (element.TryGetProperty("contributions", out JsonElement contElement))
                {
                    contributions = contElement.GetInt32();
                }

                allContributors.Add(new ContributorItem { Name = login, Url = url, AvatarUrl = avatarUrl, Contributions = contributions });
            }

            var owner = allContributors.FirstOrDefault(c => c.Name.Equals("CodeCubist", StringComparison.OrdinalIgnoreCase));
            if (owner == null)
            {
                owner = new ContributorItem { Name = "CodeCubist", Url = "https://github.com/CodeCubist", AvatarUrl = "https://avatars.githubusercontent.com/u/249788103?v=4", Contributions = 999 };
            }

            var others = allContributors
                .Where(c => !c.Name.Equals("CodeCubist", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var sortedContributors = new List<ContributorItem> { owner };
            sortedContributors.AddRange(others);

            ContributorsContentPanel.Children.Clear();

            var stackPanel = new StackPanel { Spacing = 12 };

            for (int i = 0; i < sortedContributors.Count; i += 3)
            {
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

                for (int j = i; j < Math.Min(i + 3, sortedContributors.Count); j++)
                {
                    var contributor = sortedContributors[j];

                    var flyout = new Flyout();
                    var flyoutContentPanel = new StackPanel { Spacing = 12, Width = 260, Padding = new Thickness(8) };
                    
                    var flyoutHeaderPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    var flyoutAvatar = new Ellipse { Width = 48, Height = 48 };
                    flyoutAvatar.Fill = new ImageBrush { ImageSource = new BitmapImage(new Uri(contributor.AvatarUrl)), Stretch = Stretch.UniformToFill };
                    var flyoutName = new TextBlock { Text = contributor.Name, FontSize = 16, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center };
                    flyoutHeaderPanel.Children.Add(flyoutAvatar);
                    flyoutHeaderPanel.Children.Add(flyoutName);

                    var flyoutBio = new TextBlock { Text = "正在获取简介信息...", TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
                    
                    var openBrowserBtn = new Button { Content = "在外部浏览器中打开主页", HorizontalAlignment = HorizontalAlignment.Stretch };
                    openBrowserBtn.Click += (s, e) =>
                    {
                        Process.Start(new ProcessStartInfo { FileName = contributor.Url, UseShellExecute = true });
                    };

                    flyoutContentPanel.Children.Add(flyoutHeaderPanel);
                    flyoutContentPanel.Children.Add(flyoutBio);
                    flyoutContentPanel.Children.Add(openBrowserBtn);
                    flyout.Content = flyoutContentPanel;

                    bool isBioLoaded = false;
                    flyout.Opened += async (s, e) =>
                    {
                        if (isBioLoaded) return;
                        try
                        {
                            var userJson = await GetJsonFromUrl($"https://api.github.com/users/{contributor.Name}");
                            string bioStr = "该用户暂无简介。";
                            if (userJson.RootElement.TryGetProperty("bio", out JsonElement bioElement) && bioElement.ValueKind != JsonValueKind.Null)
                            {
                                string rawBio = bioElement.GetString();
                                if (!string.IsNullOrWhiteSpace(rawBio)) bioStr = rawBio;
                            }
                            flyoutBio.Text = $"贡献次数: {contributor.Contributions} 次\n\n简介: {bioStr}";
                            isBioLoaded = true;
                        }
                        catch
                        {
                            flyoutBio.Text = $"贡献次数: {contributor.Contributions} 次\n\n简介加载失败。";
                        }
                    };

                    var button = new Button
                    {
                        Width = 100,
                        Padding = new Thickness(4),
                        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                        Flyout = flyout
                    };

                    var innerStackPanel = new StackPanel
                    {
                        Orientation = Orientation.Vertical,
                        Spacing = 6,
                        HorizontalAlignment = HorizontalAlignment.Center
                    };

                    var ellipse = new Ellipse
                    {
                        Width = 48,
                        Height = 48,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Opacity = 0
                    };

                    var imageBrush = new ImageBrush
                    {
                        ImageSource = new BitmapImage(new Uri(contributor.AvatarUrl)),
                        Stretch = Stretch.UniformToFill
                    };
                    
                    ellipse.Fill = imageBrush;

                    ellipse.Loaded += (s, e) =>
                    {
                        var animation = new DoubleAnimation
                        {
                            From = 0.0,
                            To = 1.0,
                            Duration = new Duration(TimeSpan.FromMilliseconds(500))
                        };
                        var storyboard = new Storyboard();
                        storyboard.Children.Add(animation);
                        Storyboard.SetTarget(animation, ellipse);
                        Storyboard.SetTargetProperty(animation, "Opacity");
                        storyboard.Begin();
                    };

                    var textBlock = new TextBlock
                    {
                        Text = contributor.Name,
                        FontSize = 12,
                        FontWeight = FontWeights.Normal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        TextAlignment = TextAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 90,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"]
                    };

                    innerStackPanel.Children.Add(ellipse);
                    innerStackPanel.Children.Add(textBlock);
                    button.Content = innerStackPanel;

                    rowPanel.Children.Add(button);
                }

                stackPanel.Children.Add(rowPanel);
            }

            ContributorsContentPanel.Children.Add(stackPanel);
            ContributorsLoadingRing.IsActive = false;
            ContributorsContentPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[LoadContributorsAsync] 获取贡献者失败: {ex}");
            ContributorsLoadingRing.IsActive = false;
            ContributorsErrorPanel.Visibility = Visibility.Visible;
            ContributorsErrorText.Text = "获取贡献者失败，请检查网络连接或 API 状态";
        }
    }

    private async void ContactAuthor_Click(object sender, RoutedEventArgs e)
    {
        StackPanel contentPanel = new() { Spacing = 10 };

        TextBlock warningText = new()
        {
            Text = "请注意：联系时请直入主题，说明来意\n请不要发送“在吗”、“你好”等无意义的开场白",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlErrorTextForegroundBrush"]
        };

        ComboBox platformCombo = new()
        {
            Header = "选择联系方式",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = 0
        };
        platformCombo.Items.Add("Telegram");
        platformCombo.Items.Add("Discord");

        contentPanel.Children.Add(warningText);
        contentPanel.Children.Add(platformCombo);

        ContentDialog contactDialog = new()
        {
            Title = "联系作者",
            Content = contentPanel,
            PrimaryButtonText = "确认跳转/复制",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        ContentDialogResult result = await contactDialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            string selectedPlatform = platformCombo.SelectedValue as string;

            if (selectedPlatform == "Telegram")
            {
                ProcessStartInfo psi = new()
                {
                    FileName = ApiEndpoints.TelegramContactUrl,
                    UseShellExecute = true
                };
                Process.Start(psi);
            }
            else if (selectedPlatform == "Discord")
            {
                DataPackage dataPackage = new();
                dataPackage.SetText("codecubist");
                Clipboard.SetContent(dataPackage);

                var originalContent = (sender as HyperlinkButton).Content;
                (sender as HyperlinkButton).Content = "Discord ID 已复制!";
                (sender as HyperlinkButton).IsEnabled = false;
                await Task.Delay(2000);
                (sender as HyperlinkButton).Content = originalContent;
                (sender as HyperlinkButton).IsEnabled = true;
            }
        }
    }

    private async void GetBuildFormActions(object sender, RoutedEventArgs e)
    {
        GetBuildFormActionsToggle.IsEnabled = false;
        GetBuildFormActionsToggle.Content = "正在获取...";
        
        try
        {
            var jsonString = await GetJsonFromUrl(ApiEndpoints.GithubWorkflowsApiUrl);
            var workflows = jsonString.RootElement.GetProperty("workflows").EnumerateArray();
            string workflowBaseUrl = "";
            foreach (var workflow in workflows)
            {
                if (workflow.GetProperty("name").GetString() == ".NET Core Desktop")
                {
                    workflowBaseUrl = workflow.GetProperty("url").GetString();
                    Debug.WriteLine("[GetBuildFromActions] 找到工作流ID: " + workflowBaseUrl);
                    break;
                }
            }
            if (workflowBaseUrl != "")
            {
                string workflowRunsUrl = workflowBaseUrl + "/runs";
                var runsJson = await GetJsonFromUrl(workflowRunsUrl);
                var runs = runsJson.RootElement.GetProperty("workflow_runs").EnumerateArray();
                var lastSuccessfulRunUrl = "";
                foreach (var run in runs)
                {
                    if (run.GetProperty("conclusion").GetString() == "success")
                    {
                        lastSuccessfulRunUrl = run.GetProperty("url").GetString();
                        Debug.WriteLine("[GetBuildFromActions] 找到最近成功的运行: " + lastSuccessfulRunUrl);
                        break;
                    }
                }
                if (lastSuccessfulRunUrl != "")
                {
                    string artifactsUrl = lastSuccessfulRunUrl + "/artifacts";
                    var artifactsJson = await GetJsonFromUrl(artifactsUrl);
                    var artifacts = artifactsJson.RootElement.GetProperty("artifacts").EnumerateArray();
                    string downloadUrl = "";
                    foreach (var artifact in artifacts)
                    {
                        if (artifact.GetProperty("name").GetString() == "FufuLauncher_Release")
                        {
                            downloadUrl = artifact.GetProperty("archive_download_url").GetString();
                            Debug.WriteLine("[GetBuildFromActions] 找到构建工件下载链接: " + downloadUrl);
                            break;
                        }
                    }
                    if (downloadUrl != "")
                    {
                        var userToken = await PromptForTokenAsync();
                        if (!string.IsNullOrEmpty(userToken))
                        {
                            string DownloadShell = "";
                            DownloadShell += $"taskkill /F /IM FufuLauncher.exe *> $null\n";
                            DownloadShell += $"del \"{Environment.CurrentDirectory}\\*\" /f /s /q\n";
                            DownloadShell += $"curl -H \"Authorization: Bearer {userToken}\" -L \"{downloadUrl}\" --ssl-no-revoke -o \"{Environment.CurrentDirectory}\\FufuLauncher_Build.zip\"\n";
                            DownloadShell += $"tar -xf \"{Environment.CurrentDirectory}\\FufuLauncher_Build.zip\" -C \"{Environment.CurrentDirectory}\"\n";
                            DownloadShell += $"del \"{Environment.CurrentDirectory}\\FufuLauncher_Build.zip\" /f /s /q\n";
                            DownloadShell += $"start {Environment.CurrentDirectory}\\FufuLauncher.exe\n";
                            DownloadShell += $"del %0";
                            GetBuildFormActionsToggle.Content = "获取成功! 已生成下载脚本.";
                            Debug.WriteLine("[GetBuildFromActions] 生成的下载脚本内容: \n" + DownloadShell);
                            Debug.WriteLine("[GetBuildFromActions] 下载脚本路径: " + batchFilePath);
                            File.WriteAllText(batchFilePath, DownloadShell, System.Text.Encoding.UTF8);
                            ProcessStartInfo psi = new()
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c \"{batchFilePath}\"",
                                UseShellExecute = true,
                                Verb = "runas"
                            };
                            Process.Start(psi);
                            Environment.Exit(0);
                        }
                        else
                        {
                            GetBuildFormActionsToggle.Content = "请输入Token! ";
                            await Task.Delay(1000);
                            GetBuildFormActionsToggle.Content = "从Github Actions获取构建";
                            GetBuildFormActionsToggle.IsEnabled = true;
                        }
                    }
                    else
                    {
                        await ReportError("获取失败 (未找到工件)");
                    }
                }
                else
                {
                    await ReportError("获取失败 (未找到成功运行)");
                }
            }
            else
            {
                await ReportError("获取失败 (未找到工作流)");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            await ReportError("发生异常");
        }
    }

    private async Task ReportError(string msg)
    {
        GetBuildFormActionsToggle.Content = msg;
        await Task.Delay(1000);
        GetBuildFormActionsToggle.Content = "从Github Actions获取构建";
        GetBuildFormActionsToggle.IsEnabled = true;
    }

    private async Task<JsonDocument> GetJsonFromUrl(string url)
    {
        var responseString = await httpClient.GetAsync(url);
        var responseContent = await responseString.Content.ReadAsStringAsync();
        Debug.WriteLine("[GetBuildFromActions] 从<" + url + ">获取到: " + responseContent);
        return JsonDocument.Parse(responseContent);
    }

    private async Task<string> PromptForTokenAsync()
    {
        TextBox tokenInput = new()
        {
            PlaceholderText = "请输入你的 GitHub Token",
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap
        };
        ContentDialog tokenDialog = new()
        {
            Title = "GitHub Token",
            Content = tokenInput,
            PrimaryButtonText = "确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        tokenDialog.XamlRoot = XamlRoot;
        ContentDialogResult result = await tokenDialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            return tokenInput.Text;
        }
        return string.Empty;
    }
}
