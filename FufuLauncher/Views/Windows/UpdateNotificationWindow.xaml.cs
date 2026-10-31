/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class UpdateNotificationWindow : WindowEx
{
    private static readonly HttpClient AnnouncementClient = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly Regex UrlRegex = new(@"https?://[A-Za-z0-9\-._~:/?#\[\]@!$&()*+,;=%]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly bool _isPreview;
    private readonly string _updateInfoUrl;
    private readonly CancellationTokenSource _loadCancellation = new();
    private bool _initialHeightAdjustmentPending;

    public UpdateNotificationWindow(string updateInfoUrl, bool isPreview = false, bool showUpdateButton = true)
    {
        InitializeComponent();

        _isPreview = isPreview;
        _updateInfoUrl = updateInfoUrl;

        if (!showUpdateButton)
            UpdateFooter.Visibility = Visibility.Collapsed;

        RootGrid.RequestedTheme = App.GetService<IThemeSelectorService>().Theme;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        TitleBarText.Text = _isPreview
            ? "UpdateNotification_PreviewTitle".GetLocalized()
            : "UpdateNotification_Title".GetLocalized();
        Title = TitleBarText.Text;

        if (_isPreview)
        {
            PreviewBanner.Visibility = Visibility.Visible;
            AppTitleBar.Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x40, 0xFF, 0xD7, 0x00));
            AppTitleBar.BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xFF, 0xD7, 0x00));
        }

        this.CenterOnScreen();
        SystemBackdrop = new DesktopAcrylicBackdrop();
        IsShownInSwitchers = true;
        Closed += (_, _) => _loadCancellation.Cancel();
        _ = LoadAnnouncementAsync();
    }

    private async Task LoadAnnouncementAsync()
    {
        StatusPanel.Visibility = Visibility.Visible;
        AnnouncementScrollViewer.Visibility = Visibility.Collapsed;
        LoadingRing.IsActive = true;
        LoadingRing.Visibility = Visibility.Visible;
        RetryButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "GameAnnouncement_Loading".GetLocalized();

        try
        {
            using var response = await AnnouncementClient.GetAsync(_updateInfoUrl, _loadCancellation.Token);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(_loadCancellation.Token);
            var sections = ExtractAnnouncementSections(html);

            if (sections.Count == 0)
            {
                LoadingRing.IsActive = false;
                LoadingRing.Visibility = Visibility.Collapsed;
                StatusText.Text = "GameAnnouncement_NoContent".GetLocalized();
                RetryButton.Visibility = Visibility.Visible;
                return;
            }

            AnnouncementContent.Children.Clear();
            foreach (var section in sections)
            {
                if (section.Tag == "body")
                {
                    var lines = section.Text.Split('\n');
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (string.IsNullOrWhiteSpace(lines[i]))
                            continue;

                        var nextIsBlank = i + 1 < lines.Length && string.IsNullOrWhiteSpace(lines[i + 1]);
                        AnnouncementContent.Children.Add(CreateAnnouncementTextBlock(
                            lines[i], section, new Thickness(0, 0, 0, nextIsBlank ? 14 : 6)));
                    }
                }
                else
                {
                    AnnouncementContent.Children.Add(CreateAnnouncementTextBlock(
                        section.Text, section, new Thickness(0, section.Tag == "h1" ? 0 : 24, 0, 0)));

                    if (section.Tag == "h1")
                    {
                        AnnouncementContent.Children.Add(new Border
                        {
                            Height = 1,
                            Background = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                            Margin = new Thickness(0, 16, 0, 20)
                        });
                    }
                }
            }

            _initialHeightAdjustmentPending = true;
            StatusPanel.Visibility = Visibility.Collapsed;
            AnnouncementScrollViewer.Visibility = Visibility.Visible;
            DispatcherQueue.TryEnqueue(() => TryAdjustInitialHeight(AnnouncementContent.ActualHeight));
        }
        catch (OperationCanceledException) when (_loadCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info("UpdateNotificationWindow", $"[UpdateNotificationWindow] {ex.Message}");
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            StatusText.Text = "GameAnnouncement_LoadFailed".GetLocalized();
            RetryButton.Visibility = Visibility.Visible;
        }
    }

    private TextBlock CreateAnnouncementTextBlock(string text, AnnouncementSection section, Thickness margin)
    {
        var isHeading = section.Tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
        var block = new TextBlock
        {
            Style = (Style)RootGrid.Resources["AnnouncementTextBlockStyle"],
            FontFamily = (FontFamily)Application.Current.Resources["AppGlobalFont"],
            FontSize = section.FontSize,
            FontWeight = isHeading ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            TextAlignment = section.Alignment,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            LineHeight = section.FontSize * 1.6,
            Margin = margin
        };

        var nextIndex = 0;
        foreach (Match match in UrlRegex.Matches(text))
        {
            if (match.Index > nextIndex)
                block.Inlines.Add(new Run { Text = text[nextIndex..match.Index] });

            var urlText = match.Value.TrimEnd('.', ',', ';', ':', ')', ']', '。', '，', '；', '：', '）', '】');
            if (Uri.TryCreate(urlText, UriKind.Absolute, out var url) &&
                url.Scheme is "http" or "https")
            {
                var hyperlink = new Hyperlink { NavigateUri = url };
                hyperlink.Inlines.Add(new Run { Text = urlText });
                block.Inlines.Add(hyperlink);
            }
            else
            {
                block.Inlines.Add(new Run { Text = urlText });
            }

            if (urlText.Length < match.Length)
                block.Inlines.Add(new Run { Text = match.Value[urlText.Length..] });
            nextIndex = match.Index + match.Length;
        }

        if (nextIndex < text.Length)
            block.Inlines.Add(new Run { Text = text[nextIndex..] });

        return block;
    }

    private void OnAnnouncementContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        TryAdjustInitialHeight(e.NewSize.Height);
    }

    private void TryAdjustInitialHeight(double contentHeight)
    {
        if (!_initialHeightAdjustmentPending ||
            AnnouncementScrollViewer.Visibility != Visibility.Visible ||
            contentHeight <= 0)
            return;

        _initialHeightAdjustmentPending = false;
        var bannerHeight = PreviewBanner.Visibility == Visibility.Visible
            ? PreviewBanner.ActualHeight + PreviewBanner.Margin.Top + PreviewBanner.Margin.Bottom
            : 0;
        var desiredHeight = Math.Clamp(Math.Ceiling(
            AppTitleBar.ActualHeight + bannerHeight + UpdateFooter.ActualHeight +
            contentHeight + AnnouncementContent.Margin.Top + AnnouncementContent.Margin.Bottom + 8), 360, 730);
        if (Math.Abs(Height - desiredHeight) > 1)
        {
            Height = desiredHeight;
            this.CenterOnScreen();
        }
    }

    private sealed record AnnouncementSection(string Text, string Tag, double FontSize, TextAlignment Alignment);

    private static List<AnnouncementSection> ExtractAnnouncementSections(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var body = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        var content = body.SelectSingleNode(".//article") ?? body.SelectSingleNode(".//main") ?? body;
        var sections = new List<AnnouncementSection>();
        var bodyText = new StringBuilder();
        var bodyFontSize = ResolveFontSize(document, body, 16, 16);
        AppendSections(content, document, bodyFontSize, bodyText, sections);
        FlushBodyText(bodyText, bodyFontSize, sections);
        return sections;
    }

    private static void AppendSections(HtmlNode node, HtmlDocument document, double bodyFontSize,
        StringBuilder bodyText, List<AnnouncementSection> sections)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            AppendTextNode(node, bodyText);
            return;
        }

        var name = node.Name.ToLowerInvariant();
        if (name is "script" or "style" or "noscript" or "template" or "svg" or "nav" or "aside")
            return;

        if (name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6")
        {
            FlushBodyText(bodyText, bodyFontSize, sections);
            var headingText = new StringBuilder();
            foreach (var child in node.ChildNodes)
                AppendVisibleText(child, headingText);
            var heading = headingText.ToString().Trim();
            if (heading.Length > 0)
            {
                var defaultSize = name switch
                {
                    "h1" => bodyFontSize * 2,
                    "h2" => bodyFontSize * 1.5,
                    "h3" => bodyFontSize * 1.17,
                    "h4" => bodyFontSize,
                    "h5" => bodyFontSize * 0.83,
                    _ => bodyFontSize * 0.67
                };
                sections.Add(new AnnouncementSection(heading, name,
                    ResolveFontSize(document, node, defaultSize, bodyFontSize),
                    ResolveTextAlignment(document, node)));
            }

            return;
        }

        if (name == "br")
        {
            AppendLineBreak(bodyText, 1);
            return;
        }

        if (name == "a")
        {
            var start = bodyText.Length;
            foreach (var child in node.ChildNodes)
                AppendSections(child, document, bodyFontSize, bodyText, sections);
            AppendLinkTarget(node, bodyText, start);
            return;
        }

        foreach (var child in node.ChildNodes)
            AppendSections(child, document, bodyFontSize, bodyText, sections);

        if (name == "p")
            AppendLineBreak(bodyText, 2);
        else if (name is "div" or "section" or "article" or "li" or "blockquote" or "tr")
            AppendLineBreak(bodyText, 1);
    }

    private static void FlushBodyText(StringBuilder text, double fontSize, List<AnnouncementSection> sections)
    {
        var content = text.ToString().Trim();
        if (content.Length > 0)
            sections.Add(new AnnouncementSection(content, "body", fontSize, TextAlignment.Left));
        text.Clear();
    }

    private static void AppendVisibleText(HtmlNode node, StringBuilder text)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            AppendTextNode(node, text);
            return;
        }

        var name = node.Name.ToLowerInvariant();
        if (name is "script" or "style" or "noscript" or "template" or "svg" or "nav" or "aside")
            return;

        if (name == "br")
        {
            AppendLineBreak(text, 1);
            return;
        }

        if (name == "a")
        {
            var start = text.Length;
            foreach (var child in node.ChildNodes)
                AppendVisibleText(child, text);
            AppendLinkTarget(node, text, start);
            return;
        }

        foreach (var child in node.ChildNodes)
            AppendVisibleText(child, text);

        if (name is "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "p")
            AppendLineBreak(text, 2);
        else if (name is "div" or "section" or "article" or "li" or "blockquote" or "tr")
            AppendLineBreak(text, 1);
    }

    private static void AppendLinkTarget(HtmlNode node, StringBuilder text, int start)
    {
        var href = WebUtility.HtmlDecode(node.GetAttributeValue("href", string.Empty)).Trim();
        if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return;

        var uriBuilder = new UriBuilder(uri) { Host = uri.IdnHost };
        var linkText = uriBuilder.Uri.AbsoluteUri;
        start = Math.Min(start, text.Length);
        var visibleText = text.ToString(start, text.Length - start);
        if (visibleText.Contains(linkText, StringComparison.OrdinalIgnoreCase) ||
            (href.All(character => character < 128) &&
             visibleText.Contains(href, StringComparison.OrdinalIgnoreCase)))
            return;

        text.Append(' ').Append(linkText).Append(' ');
    }

    private static void AppendTextNode(HtmlNode node, StringBuilder text)
    {
        var value = Regex.Replace(WebUtility.HtmlDecode(node.InnerText), @"\s+", " ");
        if (value == " ")
        {
            if (text.Length > 0 && text[^1] is not (' ' or '\n'))
                text.Append(' ');
        }
        else if (value.Length > 0)
            text.Append(value);
    }

    private static double ResolveFontSize(HtmlDocument document, HtmlNode node, double fallback, double bodyFontSize)
    {
        var style = GetStyleDeclarations(document, node);
        var matches = Regex.Matches(style, @"font-size\s*:\s*(?<number>\d+(?:\.\d+)?)\s*(?<unit>px|em|rem|%)",
            RegexOptions.IgnoreCase);
        if (matches.Count == 0)
            return fallback;

        var match = matches[matches.Count - 1];
        if (!double.TryParse(match.Groups["number"].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return fallback;

        return match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "em" => number * bodyFontSize,
            "rem" => number * 16,
            "%" => number * bodyFontSize / 100,
            _ => number
        };
    }

    private static TextAlignment ResolveTextAlignment(HtmlDocument document, HtmlNode node)
    {
        var style = GetStyleDeclarations(document, node);
        var matches = Regex.Matches(style, @"text-align\s*:\s*(center|left|right|justify)\b", RegexOptions.IgnoreCase);
        return matches.Count > 0 &&
               matches[matches.Count - 1].Groups[1].Value.Equals("center", StringComparison.OrdinalIgnoreCase)
            ? TextAlignment.Center
            : TextAlignment.Left;
    }

    private static string GetStyleDeclarations(HtmlDocument document, HtmlNode node)
    {
        var declarations = new StringBuilder();
        foreach (var styleNode in document.DocumentNode.SelectNodes("//style") ?? Enumerable.Empty<HtmlNode>())
        {
            foreach (Match rule in Regex.Matches(styleNode.InnerText, @"(?<selectors>[^{}]+)\{(?<rules>[^{}]*)\}"))
            {
                if (rule.Groups["selectors"].Value.Split(',').Any(selector =>
                        string.Equals(selector.Trim(), node.Name, StringComparison.OrdinalIgnoreCase)))
                    declarations.Append(rule.Groups["rules"].Value).Append(';');
            }
        }

        declarations.Append(node.GetAttributeValue("style", string.Empty));
        return declarations.ToString();
    }

    private static void AppendLineBreak(StringBuilder text, int count)
    {
        while (text.Length > 0 && text[^1] == ' ')
            text.Length--;

        if (text.Length == 0)
            return;

        var existing = 0;
        for (var i = text.Length - 1; i >= 0 && text[i] == '\n'; i--)
            existing++;
        for (var i = existing; i < count; i++)
            text.Append('\n');
    }

    private void OnRetryBtnClicked(object sender, RoutedEventArgs e) => _ = LoadAnnouncementAsync();

    private async void OnUpdateBtnClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) button.IsEnabled = false;
        if (await LaunchUpdaterAsync(_isPreview))
            Close();
        else if (sender is Button retryButton)
            retryButton.IsEnabled = true;
    }

    private async Task<bool> LaunchUpdaterAsync(bool isPreview)
    {
        try
        {
            string updaterPath = Path.Combine(AppContext.BaseDirectory, "UpdateFufuLauncher.exe");

            if (!File.Exists(updaterPath))
            {
                DebugLog.Info("UpdateNotificationWindow", "UpdateFufuLauncher.exe not found");
                return false;
            }

            bool useThirdPartyCdn = true;
            try
            {
                var localSettingsService = App.GetService<ILocalSettingsService>();
                var cdnSetting = await localSettingsService.ReadSettingAsync("IsUseThirdPartyCDNEnabled");
                if (cdnSetting != null)
                {
                    useThirdPartyCdn = Convert.ToBoolean(cdnSetting);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("UpdateNotificationWindow",
                    $"[UpdateNotificationWindow] Failed to read CDN settings: {ex.Message}");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--use-third-party-cdn={useThirdPartyCdn.ToString().ToLower()}" +
                            $" --installed-version={AppVersionHelper.FullVersion}" +
                            (isPreview ? " --preview" : string.Empty)
            };
            return Process.Start(startInfo) != null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("UpdateNotificationWindow", $"Failed to start updater: {ex.Message}");
            return false;
        }
    }
}