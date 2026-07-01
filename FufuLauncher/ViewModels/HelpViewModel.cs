/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public partial class HelpViewModel : ObservableObject
{
    private static string NormalizeMarkdownForWinUi(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private readonly HttpClient _httpClient = new();
    private const string ConfigUrl = "https://fu1.fun/api/docs-config";
    private const string ContentBaseUrl = "https://fu1.fun/api/docs/zh-CN";

    public ObservableCollection<DocCategory> AllCategories { get; } = new();

    public ObservableCollection<DocSearchHit> SearchHits { get; } = new();
    
    public readonly Dictionary<DocItem, string> PreloadedContents = new();

    private static readonly Regex s_whitespaceCollapse = new(@"\s+", RegexOptions.Compiled);

    private static string CollapseForPreview(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var one = s_whitespaceCollapse.Replace(text.Trim(), " ");
        if (one.Length <= maxLen)
            return one;
        return one[..maxLen].TrimEnd() + "…";
    }

    private static string SnippetAroundMatch(string content, string lowerFilter)
    {
        var lower = content.ToLowerInvariant();
        var idx = lower.IndexOf(lowerFilter, StringComparison.Ordinal);
        if (idx < 0)
            return CollapseForPreview(content, 200);

        const int radius = 96;
        var start = Math.Max(0, idx - radius);
        var end = Math.Min(content.Length, idx + lowerFilter.Length + radius);
        var slice = content[start..end];
        var collapsed = s_whitespaceCollapse.Replace(slice.Replace("\r", "").Replace("\n", " "), " ").Trim();
        if (start > 0)
            collapsed = "…" + collapsed;
        if (end < content.Length)
            collapsed += "…";
        return collapsed;
    }
    
    public void UpdateSearchHits(string? filter)
    {
        SearchHits.Clear();
        var f = filter?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(f))
            return;

        var lowerFilter = f.ToLowerInvariant();

        foreach (var cat in AllCategories)
        {
            foreach (var item in cat.Items)
            {
                var titleMatch = item.Title.ToLowerInvariant().Contains(lowerFilter);
                var fileMatch = item.File.ToLowerInvariant().Contains(lowerFilter);
                PreloadedContents.TryGetValue(item, out var body);
                var contentMatch = !string.IsNullOrEmpty(body) &&
                                   body.ToLowerInvariant().Contains(lowerFilter);

                if (!titleMatch && !fileMatch && !contentMatch)
                    continue;

                string preview;
                if (contentMatch && body != null)
                    preview = SnippetAroundMatch(body, lowerFilter);
                else if (!string.IsNullOrEmpty(body))
                    preview = CollapseForPreview(body, 220);
                else
                    preview = "正文仍在后台预加载，匹配来自标题或路径";

                SearchHits.Add(new DocSearchHit
                {
                    Item = item,
                    CategoryName = cat.CategoryName,
                    Preview = preview
                });
            }
        }
    }

    [ObservableProperty]
    private string _markdownContent = "从左侧目录选择一个项目以查看详细内容";

    [ObservableProperty]
    private string _currentTitle = "请选择文档";

    [ObservableProperty]
    private string _currentAuthor = "";

    [ObservableProperty]
    private string _currentDate = "";

    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private string _markdownUriPrefix = $"{ContentBaseUrl}/";
    
    private static string GetMarkdownDirectoryPrefix(string relativeFilePath)
    {
        var normalized = relativeFilePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrEmpty(normalized))
            return $"{ContentBaseUrl}/";

        var lastSlash = normalized.LastIndexOf('/');
        var dirPart = lastSlash >= 0 ? normalized[..lastSlash] : "";
        if (string.IsNullOrEmpty(dirPart))
            return $"{ContentBaseUrl}/";

        var encoded = string.Join("/", dirPart.Split('/').Select(Uri.EscapeDataString));
        return $"{ContentBaseUrl}/{encoded}/";
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var json = await _httpClient.GetStringAsync(ConfigUrl);
            var categories = JsonSerializer.Deserialize<List<DocCategory>>(json);
            
            AllCategories.Clear();
            if (categories != null)
            {
                foreach (var category in categories)
                {
                    foreach (var item in category.Items)
                    {
                        item.Category = category.CategoryName;
                    }
                    AllCategories.Add(category);
                }
            }

            _ = Task.Run(PreloadAllDocumentsAsync);
        }
        catch (Exception ex)
        {
            CurrentTitle = "初始化失败";
            MarkdownContent = $"无法加载目录配置: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task PreloadAllDocumentsAsync()
    {
        foreach (var category in AllCategories)
        {
            foreach (var item in category.Items)
            {
                try
                {
                    string filePart = string.Join("/", item.File.Split('/').Select(Uri.EscapeDataString));
                    string requestUrl = $"{ContentBaseUrl}/{filePart}";
                    var response = await _httpClient.GetAsync(requestUrl);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var raw = await response.Content.ReadAsStringAsync();
                        PreloadedContents[item] = NormalizeMarkdownForWinUi(raw);
                    }
                }
                catch
                {
                }
            }
        }
    }

    public async Task LoadDocumentAsync(DocItem item)
    {
        IsLoading = true;
        MarkdownUriPrefix = GetMarkdownDirectoryPrefix(item.File);
        CurrentTitle = item.Title;
        CurrentAuthor = $"作者: {item.Author}";
        CurrentDate = "获取日期中...";
        MarkdownContent = "加载中...";

        try
        {
            string filePart = string.Join("/", item.File.Split('/').Select(Uri.EscapeDataString));
            string requestUrl = $"{ContentBaseUrl}/{filePart}";
            var response = await _httpClient.GetAsync(requestUrl);

            if (response.IsSuccessStatusCode)
            {
                var raw = await response.Content.ReadAsStringAsync();
                MarkdownContent = NormalizeMarkdownForWinUi(raw);
                PreloadedContents[item] = MarkdownContent;

                if (response.Content.Headers.LastModified.HasValue)
                {
                    CurrentDate = $"最后修改: {response.Content.Headers.LastModified.Value.LocalDateTime:yyyy-MM-dd HH:mm}";
                }
                else
                {
                    CurrentDate = "最后修改: 未知";
                }
            }
            else
            {
                MarkdownContent = $"无法获取文档内容 (HTTP {response.StatusCode})";
                CurrentDate = "";
            }
        }
        catch (Exception ex)
        {
            MarkdownContent = $"文档加载发生异常: {ex.Message}";
            CurrentDate = "";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
