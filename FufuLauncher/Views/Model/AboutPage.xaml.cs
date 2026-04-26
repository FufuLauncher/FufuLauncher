using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using FufuLauncher.Constants;

namespace FufuLauncher.Views;

public sealed partial class AboutPage : Page
{
    private static readonly HttpClient httpClient = new();
    private static readonly string batchFilePath = Path.Combine(Environment.CurrentDirectory, "..\\download_build.bat");

    public AboutPage()
    {
        InitializeComponent();
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
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0");
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