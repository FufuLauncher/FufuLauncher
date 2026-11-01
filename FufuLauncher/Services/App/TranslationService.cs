/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public sealed class TranslationService
{
    private static readonly Lazy<TranslationService> _instance = new(() => new TranslationService());
    public static TranslationService Instance => _instance.Value;

    private readonly HttpClient _httpClient = new();
    private const string ApiBaseUrl = ApiEndpoints.MyMemoryTranslateUrl;
    private const int MaxCharsPerRequest = 500;

    private readonly ConcurrentDictionary<string, string> _cache = new();

    private static readonly Regex s_codeBlockRegex = new(
        @"(```[\s\S]*?```|`[^`\n]+`)",
        RegexOptions.Compiled);

    private TranslationService()
    {
    }

    public static string GetLangPair(string targetCulture)
    {
        var target = TryGetTargetLanguage(targetCulture, out var resolved) ? resolved : "en";
        return $"zh-CN|{target}";
    }

    public static bool TryGetTargetLanguage(string? targetCulture, out string targetLanguage)
    {
        targetLanguage = targetCulture?.Trim().ToLowerInvariant() switch
        {
            "en-us" or "en" => "en",
            "fr-fr" or "fr" => "fr",
            "de-de" or "de" => "de",
            "ru-ru" or "ru" => "ru",
            "ja-jp" or "ja" => "ja",
            "es-es" or "es" => "es",
            "es-mx" => "es",
            "ko-kr" or "ko" => "ko",
            "it-it" or "it" => "it",
            "id-id" or "id" => "id",
            "pt-br" or "pt" => "pt",
            "hi-in" or "hi" => "hi",
            "zh-tw" => "zh-TW",
            _ => string.Empty
        };
        return targetLanguage.Length > 0;
    }

    public static bool IsSupportedTargetCulture(string? targetCulture)
    {
        if (string.IsNullOrWhiteSpace(targetCulture))
            return false;

        var normalized = targetCulture.Trim();
        if (normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return false;

        return TryGetTargetLanguage(normalized, out var target) &&
               !target.Equals("zh-TW", StringComparison.OrdinalIgnoreCase);
    }

    public static List<(string Text, bool IsCode)> SplitMarkdownIntoParagraphs(string markdown)
    {
        var result = new List<(string Text, bool IsCode)>();
        if (string.IsNullOrEmpty(markdown))
            return result;

        var segments = SplitByCodeBlocks(markdown);
        foreach (var (text, isCode) in segments)
        {
            if (isCode)
            {
                result.Add((text, true));
            }
            else
            {
                var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.None);
                foreach (var p in paragraphs)
                {
                    result.Add((p, false));
                }
            }
        }

        return result;
    }

    private static List<(string Text, bool IsCode)> SplitByCodeBlocks(string text)
    {
        var result = new List<(string, bool)>();
        var fenceRegex = new Regex(@"^```", RegexOptions.Multiline);
        var matches = fenceRegex.Matches(text);

        if (matches.Count < 2)
        {
            result.Add((text, false));
            return result;
        }

        int lastIndex = 0;
        bool inCode = false;
        int codeStart = 0;

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            if (!inCode)
            {
                if (match.Index > lastIndex)
                {
                    var before = text[lastIndex..match.Index];
                    if (!string.IsNullOrEmpty(before))
                        result.Add((before, false));
                }

                codeStart = match.Index;
                inCode = true;
            }
            else
            {
                var lineEnd = text.IndexOf('\n', match.Index);
                var endPos = lineEnd >= 0 ? lineEnd + 1 : text.Length;
                var codeBlock = text[codeStart..endPos];
                result.Add((codeBlock, true));
                lastIndex = endPos;
                inCode = false;
            }
        }

        if (inCode)
        {
            result.Add((text[codeStart..], true));
        }
        else if (lastIndex < text.Length)
        {
            result.Add((text[lastIndex..], false));
        }

        return result;
    }

    public static List<string> SplitLongText(string text, int maxLen = MaxCharsPerRequest)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
            return new List<string> { text };

        var result = new List<string>();
        var sentences = Regex.Split(text, @"(?<=[。？！\.\?\!\n])");
        var current = string.Empty;

        foreach (var sentence in sentences)
        {
            if (string.IsNullOrEmpty(sentence))
                continue;

            if (current.Length + sentence.Length <= maxLen)
            {
                current += sentence;
            }
            else
            {
                if (!string.IsNullOrEmpty(current))
                    result.Add(current);

                if (sentence.Length > maxLen)
                {
                    for (int i = 0; i < sentence.Length; i += maxLen)
                    {
                        var chunk = sentence.Substring(i, Math.Min(maxLen, sentence.Length - i));
                        result.Add(chunk);
                    }

                    current = string.Empty;
                }
                else
                {
                    current = sentence;
                }
            }
        }

        if (!string.IsNullOrEmpty(current))
            result.Add(current);

        return result;
    }

    public async Task<string> TranslateTextAsync(string text, string langPair, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        var cacheKey = $"{langPair}:{text.GetHashCode():X8}:{text.Length}";
        if (_cache.TryGetValue(cacheKey, out var cached))
            return cached;

        try
        {
            var encodedText = Uri.EscapeDataString(text);
            var url = $"{ApiBaseUrl}?q={encodedText}&langpair={Uri.EscapeDataString(langPair)}";

            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                DebugLog.Info("TranslationService", $"[Translation] API returned {response.StatusCode}");
                return text;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!HasUsableResponseStatus(root))
            {
                DebugLog.Info("TranslationService", "[Translation] API returned a non-200 responseStatus");
                return text;
            }

            if (root.TryGetProperty("responseData", out var responseData) &&
                responseData.TryGetProperty("translatedText", out var translatedText))
            {
                var result = translatedText.GetString() ?? text;
                _cache[cacheKey] = result;
                return result;
            }

            return text;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog.Info("TranslationService", $"[Translation] Error: {ex.Message}");
            return text;
        }
    }

    private static bool HasUsableResponseStatus(JsonElement root)
    {
        if (!root.TryGetProperty("responseStatus", out var status))
            return true;

        return status.ValueKind switch
        {
            JsonValueKind.Number => status.TryGetInt32(out var code) && code == 200,
            JsonValueKind.String => int.TryParse(status.GetString(), out var code) && code == 200,
            _ => false
        };
    }

    public async Task<string> TranslateParagraphAsync(string paragraph, string langPair, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(paragraph))
            return paragraph;

        var chunks = SplitLongText(paragraph);
        if (chunks.Count == 1)
            return await TranslateTextAsync(chunks[0], langPair, ct);

        var translatedChunks = new List<string>();
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();
            var translated = await TranslateTextAsync(chunk, langPair, ct);
            translatedChunks.Add(translated);
            await Task.Delay(100, ct);
        }

        return string.Join("", translatedChunks);
    }

    public async Task<string> TranslateMarkdownAsync(
        string markdown,
        string targetCulture,
        Action<int, int, string>? onProgress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        var langPair = GetLangPair(targetCulture);
        var paragraphs = SplitMarkdownIntoParagraphs(markdown);

        int totalTranslatable = paragraphs.Count(p => !p.IsCode && !string.IsNullOrWhiteSpace(p.Text));
        int completedCount = 0;

        var translatedParts = new List<string>();

        for (int i = 0; i < paragraphs.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var (text, isCode) = paragraphs[i];

            if (isCode || string.IsNullOrWhiteSpace(text))
            {
                translatedParts.Add(text);
            }
            else
            {
                var translated = await TranslateParagraphAsync(text, langPair, ct);
                translatedParts.Add(translated);
                completedCount++;

                var currentResult = JoinParagraphs(translatedParts, paragraphs, i + 1);
                onProgress?.Invoke(completedCount, totalTranslatable, currentResult);

                if (i < paragraphs.Count - 1)
                    await Task.Delay(150, ct);
            }
        }

        return JoinParagraphs(translatedParts, paragraphs, paragraphs.Count);
    }

    private static string JoinParagraphs(List<string> translatedParts, List<(string Text, bool IsCode)> original,
        int upTo)
    {
        var result = new System.Text.StringBuilder();

        for (int i = 0; i < Math.Min(upTo, translatedParts.Count); i++)
        {
            if (i > 0 && !original[i].IsCode && !original[i - 1].IsCode)
                result.Append("\n\n");
            else if (i > 0 && (original[i].IsCode || original[i - 1].IsCode))
            {
            }

            result.Append(translatedParts[i]);
        }

        for (int i = translatedParts.Count; i < Math.Min(upTo, original.Count); i++)
        {
            if (i > 0)
                result.Append("\n\n");
            result.Append(original[i].Text);
        }

        return result.ToString();
    }

    public void ClearCache() => _cache.Clear();
}