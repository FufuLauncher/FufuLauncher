using System.Diagnostics;
using System.IO;
using System.Net;
using ColorCode.Styling;
using Markdig;
using Markdown.ColorCode;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Help;

public static class HelpDocumentRenderer
{
    private const string FallbackTemplate =
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><base href=\"__BASE__\"></head><body>__BODY__</body></html>";

    private const string FontFaceRule =
        $"@font-face {{ font-family: 'FufuDoc'; src: url('{ApiEndpoints.HelpCustomFontUrl}') format('truetype'); font-display: swap; }}";

    private static readonly string s_fontPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "MiSans-Regular.ttf");

    public static bool HasCustomFont
    {
        get;
    } = File.Exists(s_fontPath);

    public static string CustomFontDirectory
    {
        get;
    } = Path.GetDirectoryName(s_fontPath) ?? string.Empty;

    private static readonly MarkdownPipeline s_pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseColorCode(HtmlFormatterType.Style, StyleDictionary.DefaultDark)
        .Build();

    private static string? _template;

    public static bool TryExternalUri(string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(WebUtility.HtmlDecode(value), UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme is not ("http" or "https" or "mailto"))
            return false;

        if (parsed.Scheme is "http" or "https" && parsed.UserInfo.Length > 0)
            return false;

        uri = parsed;
        return true;
    }

    public static string Render(string markdown, HelpDocumentMeta meta, string? baseHref, bool dark, string accent)
    {
        var body = Markdig.Markdown.ToHtml(markdown ?? string.Empty, s_pipeline);

        return Template
            .Replace("__SCHEME__", dark ? "dark" : "light")
            .Replace("__BG__", dark ? "#1C1C1E" : "#FBFBFB")
            .Replace("__TEXT__", dark ? "#E8E8E8" : "#1A1A1A")
            .Replace("__HEAD2__", dark ? "#D2D2D2" : "#333333")
            .Replace("__HEAD__", dark ? "#FFFFFF" : "#111111")
            .Replace("__MUTED__", dark ? "#9E9EA3" : "#5A5A5A")
            .Replace("__LINK__", dark ? "#63C6FF" : "#005FB8")
            .Replace("__BORDER__", dark ? "#353538" : "#E2E2E2")
            .Replace("__INLINEBG__", dark ? "#2E2E31" : "#EAEAEA")
            .Replace("__INLINEFG__", dark ? "#E8E8E8" : "#1A1A1A")
            .Replace("__QUOTEBG__", dark ? "rgba(255,255,255,0.045)" : "rgba(0,0,0,0.032)")
            .Replace("__CHIPBG__", dark ? "rgba(255,255,255,0.05)" : "#F2F2F4")
            .Replace("__TOPBG__", dark ? "rgba(40,40,42,0.9)" : "#FFFFFF")
            .Replace("__TOPFG__", dark ? "#E8E8E8" : "#333333")
            .Replace("__ACCENT__", SafeHex(accent))
            .Replace("__FONTFACE__", HasCustomFont ? FontFaceRule : string.Empty)
            .Replace("__FONTFAMILY__", HasCustomFont ? "'FufuDoc', " : string.Empty)
            .Replace("__TITLE__", SafeText(meta.Title))
            .Replace("__CATEGORY__", SafeText(meta.Category))
            .Replace("__AUTHOR__", SafeText(meta.Author))
            .Replace("__UPDATED__", SafeText(meta.Updated))
            .Replace("__BASE__", BuildBaseTag(baseHref))
            .Replace("__BODY__", body);
    }

    private static string Template => _template ??= LoadTemplate();

    private static string LoadTemplate()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Web", "doc-template.html");
            return File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            DebugLog.Info("HelpDocumentRenderer",
                $"[HelpDocumentRenderer] Failed to read document template: {ex.Message}");
            return FallbackTemplate;
        }
    }

    private static string SafeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : WebUtility.HtmlEncode(value.Trim());
    }

    private static string SafeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value[0] != '#' || value.Length != 7)
            return "#0078D4";

        foreach (var c in value.AsSpan(1))
        {
            if (!Uri.IsHexDigit(c))
                return "#0078D4";
        }

        return value;
    }

    private static string BuildBaseTag(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri is not { Scheme: "https" or "http", UserInfo.Length: 0 })
            return string.Empty;

        return $"<base href=\"{WebUtility.HtmlEncode(uri.ToString())}\">";
    }
}