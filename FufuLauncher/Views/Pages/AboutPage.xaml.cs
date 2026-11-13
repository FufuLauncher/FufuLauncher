/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public class ContributorItem
{
    public string Name
    {
        get;
        set;
    }

    public string Url
    {
        get;
        set;
    }

    public string AvatarUrl
    {
        get;
        set;
    }

    public int Contributions
    {
        get;
        set;
    }
}

public sealed partial class AboutPage : Page
{
    private static readonly HttpClient httpClient = new();

    public AboutPage()
    {
        InitializeComponent();
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        VersionText.Text = $"Version {version?.Major}.{version?.Minor}.{version?.Build}.{version?.Revision}";

        if (!httpClient.DefaultRequestHeaders.UserAgent.Any())
        {
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0");
        }

        _ = LoadContributorsAsync();
    }

    private async Task LoadContributorsAsync()
    {
        try
        {
            string[] apiUrls = new[]
            {
                ApiEndpoints.FufuLauncherContributorsUrl,
                ApiEndpoints.UnlockerIslandContributorsUrl
            };

            var allContributors = new Dictionary<string, ContributorItem>(StringComparer.OrdinalIgnoreCase);
            bool anySuccess = false;

            foreach (var apiUrl in apiUrls)
            {
                try
                {
                    var jsonDocument = await GetJsonFromUrl(apiUrl);

                    if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
                    {
                        string errorMessage = "API限制或返回结构异常";
                        if (jsonDocument.RootElement.ValueKind == JsonValueKind.Object &&
                            jsonDocument.RootElement.TryGetProperty("message", out JsonElement messageElement))
                        {
                            errorMessage = messageElement.GetString();
                        }

                        DebugLog.Info("AboutPage",
                            $"[LoadContributorsAsync] Failed to get contributors ({apiUrl}): {errorMessage}");
                        continue;
                    }

                    anySuccess = true;
                    var elements = jsonDocument.RootElement.EnumerateArray();

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

                        if (allContributors.TryGetValue(login, out var existing))
                        {
                            existing.Contributions += contributions;
                        }
                        else
                        {
                            allContributors[login] = new ContributorItem
                                { Name = login, Url = url, AvatarUrl = avatarUrl, Contributions = contributions };
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Info("AboutPage",
                        $"[LoadContributorsAsync] Failed to get contributors ({apiUrl}): {ex.Message}");
                }
            }

            if (!anySuccess)
            {
                ContributorsLoadingRing.IsActive = false;
                ContributorsErrorPanel.Visibility = Visibility.Visible;
                ContributorsErrorText.Text = "AboutPage_FetchFailed".GetLocalized();
                return;
            }

            var owner = allContributors.Values.FirstOrDefault(c =>
                c.Name.Equals("CodeCubist", StringComparison.OrdinalIgnoreCase));
            if (owner == null)
            {
                owner = new ContributorItem
                {
                    Name = "CodeCubist", Url = ApiEndpoints.AuthorGithubUrl,
                    AvatarUrl = ApiEndpoints.AuthorAvatarUrl, Contributions = 999
                };
            }

            var others = allContributors.Values
                .Where(c => !c.Name.Equals("CodeCubist", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.Contributions)
                .ToList();

            var sortedContributors = new List<ContributorItem> { owner };
            sortedContributors.AddRange(others);

            ContributorsContentPanel.Children.Clear();

            var stackPanel = new StackPanel { Spacing = 12 };

            for (int i = 0; i < sortedContributors.Count; i += 5)
            {
                var rowPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };

                for (int j = i; j < Math.Min(i + 5, sortedContributors.Count); j++)
                {
                    var contributor = sortedContributors[j];

                    var flyout = new Flyout();
                    var flyoutContentPanel = new StackPanel { Spacing = 12, Width = 260, Padding = new Thickness(8) };

                    var flyoutHeaderPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
                    var flyoutAvatar = new Ellipse { Width = 48, Height = 48 };
                    flyoutAvatar.Fill = new ImageBrush
                    {
                        ImageSource = new BitmapImage(new Uri(contributor.AvatarUrl)), Stretch = Stretch.UniformToFill
                    };
                    var flyoutName = new TextBlock
                    {
                        Text = contributor.Name, FontSize = 16, FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    flyoutHeaderPanel.Children.Add(flyoutAvatar);
                    flyoutHeaderPanel.Children.Add(flyoutName);

                    var flyoutBio = new TextBlock
                    {
                        Text = "AboutPage_LoadingBio".GetLocalized(), TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    };

                    var openBrowserBtn = new Button
                    {
                        Content = "AboutPage_OpenInBrowser".GetLocalized(),
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
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
                            var userJson =
                                await GetJsonFromUrl($"{ApiEndpoints.GithubApiBase}/users/{contributor.Name}");
                            string bioStr = "AboutPage_NoBio".GetLocalized();
                            if (userJson.RootElement.TryGetProperty("bio", out JsonElement bioElement) &&
                                bioElement.ValueKind != JsonValueKind.Null)
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
            DebugLog.Info("AboutPage", $"[LoadContributorsAsync] Failed to get contributors: {ex}");
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
            Foreground =
                (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemControlErrorTextForegroundBrush"]
        };

        ComboBox platformCombo = new()
        {
            Header = "AboutPage_SelectContact".GetLocalized(),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedIndex = 0
        };
        platformCombo.Items.Add(new ComboBoxItem { Content = "Telegram", Tag = "Telegram" });
        platformCombo.Items.Add(new ComboBoxItem { Content = "Discord", Tag = "Discord" });
        platformCombo.Items.Add(new ComboBoxItem
        {
            Content = string.Format("AboutPage_ContactEmailOption".GetLocalized(), ApiEndpoints.ContactEmail),
            Tag = "Email"
        });

        contentPanel.Children.Add(warningText);
        contentPanel.Children.Add(platformCombo);

        ContentDialog contactDialog = new()
        {
            Title = "AboutPage_ContactAuthor".GetLocalized(),
            Content = contentPanel,
            PrimaryButtonText = "AboutPage_ConfirmNav".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        ContentDialogResult result = await contactDialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            var selectedPlatform = (platformCombo.SelectedItem as ComboBoxItem)?.Tag as string;

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
                await CopyContactAndNotifyAsync(sender as HyperlinkButton, "codecubist", "AboutPage_DiscordCopied");
            }
            else if (selectedPlatform == "Email")
            {
                await CopyContactAndNotifyAsync(sender as HyperlinkButton, ApiEndpoints.ContactEmail,
                    "AboutPage_EmailCopied");
            }
        }
    }

    private static async Task CopyContactAndNotifyAsync(HyperlinkButton? contactButton, string textToCopy,
        string copiedResourceKey)
    {
        DataPackage dataPackage = new();
        dataPackage.SetText(textToCopy);
        Clipboard.SetContent(dataPackage);
        try
        {
            Clipboard.Flush();
        }
        catch
        {
        }

        if (contactButton == null)
        {
            return;
        }

        var originalContent = contactButton.Content;
        contactButton.Content = copiedResourceKey.GetLocalized();
        contactButton.IsEnabled = false;
        try
        {
            await Task.Delay(2000);
        }
        finally
        {
            contactButton.Content = originalContent;
            contactButton.IsEnabled = true;
        }
    }


    private async Task<JsonDocument> GetJsonFromUrl(string url)
    {
        var responseString = await httpClient.GetAsync(url);
        var responseContent = await responseString.Content.ReadAsStringAsync();
        DebugLog.Info("AboutPage", "[GetBuildFromActions] Fetched from <" + url + ">: " + responseContent);
        return JsonDocument.Parse(responseContent);
    }
}