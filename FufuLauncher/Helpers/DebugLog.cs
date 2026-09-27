/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace FufuLauncher.Helpers;


public static class DebugLog
{
    private const int MaxTagLength = 48;

    private const int MaxCacheEntries = 512;


    private const string UntaggedKey = "\0untagged";

    private const string EnvironmentVariableName = "FUFU_DEBUG_VERBOSE";


    private const int DebounceDelayMs = 250;

    private const int ReadRetryCount = 3;
    private const int ReadRetryDelayMs = 50;


    private static readonly string[] DefaultErrorKeywords =
    {
        // 中文
        "失败", "异常", "错误", "无法", "致命", "崩溃", "超时", "拒绝", "不可用", "警告",
        // 英文
        "fail", "error", "exception", "fatal", "crash", "timeout", "refused", "denied", "invalid", "warning",
    };

    private static readonly string ConfigPath = Path.Combine(AppPaths.SettingsDir, "debuglog.json");


    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static FilterState _state = FilterState.PassthroughAll;
    private static int _watcherStarted;


    private static FileSystemWatcher? _watcher;


    /// <summary>尾沿防抖定时器与它的保护锁。</summary>
    private static readonly object DebounceLock = new();
    private static Timer? _debounceTimer;


    public static bool IsFiltering => !_state.Passthrough;

    /// <summary>当前过滤状态中已缓存的判定数。状态整体替换后该值随新状态从零开始。</summary>
    public static int CachedVerdicts => _state.VerdictCache.Count;


    public static string ConfigurationPath => ConfigPath;

    public static void Initialize()
    {
        try
        {
            if (IsVerboseOverride())
            {
               
                Debug.WriteLine($"[DebugLog] {EnvironmentVariableName}=1，已跳过屏蔽，全部输出");
                return;
            }

            EnsureConfigFile();
            InstallDispatcher();
            Reload();
            StartWatcher();
        }
        catch (Exception ex)
        {
           
            Debug.WriteLine($"[DebugLog] 初始化失败，保持原始输出: {ex.Message}");
        }
    }


    public static void Reload()
    {
        FilterState next;
        try
        {
            next = ReadStateWithRetry();
        }
        catch (Exception ex)
        {
            next = FilterState.PassthroughAll;
            Debug.WriteLine($"[DebugLog] 配置读取失败，已放行全部输出: {ex.Message}");
        }

        Volatile.Write(ref _state, next);
    }



    private static FilterState ReadStateWithRetry()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.Exists(ConfigPath)
                    ? BuildState(File.ReadAllText(ConfigPath))
                    : FilterState.PassthroughAll;
            }
            catch (IOException) when (attempt < ReadRetryCount)
            {
                Thread.Sleep(ReadRetryDelayMs);
            }
            catch (UnauthorizedAccessException) when (attempt < ReadRetryCount)
            {
                Thread.Sleep(ReadRetryDelayMs);
            }
        }
    }


    internal static bool ShouldEmit(string? message)
    {
        var state = _state;
        if (state.Passthrough)
        {
            return true;
        }

        var text = message ?? string.Empty;
        var (tag, bodyStart) = ExtractTag(text);

        if (!IsBlacklisted(state, tag))
        {
            return true;
        }

        if (state.ErrorKeywords.Length == 0)
        {
            return false;
        }


        return ContainsErrorKeyword(text.AsSpan(bodyStart), state.ErrorKeywords);
    }


    private static bool IsBlacklisted(FilterState state, string tag)
    {
        if (state.VerdictCache.TryGetValue(tag, out var cached))
        {
            return cached;
        }

 
        var matched = MatchesAny(state, tag);

        if (state.VerdictCache.Count >= MaxCacheEntries)
        {
            state.VerdictCache.Clear();
        }
        state.VerdictCache[tag] = matched;
        return matched;
    }

    private static bool MatchesAny(FilterState state, string tag)
    {
        if (state.ExactTags.Contains(tag))
        {
            return true;
        }

        foreach (var pattern in state.GlobPatterns)
        {
            if (GlobMatch(pattern, tag))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsErrorKeyword(ReadOnlySpan<char> body, string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            if (body.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static (string Tag, int BodyStart) ExtractTag(string message)
    {
        if (message.Length == 0)
        {
            return (UntaggedKey, 0);
        }

        var i = 0;
        while (i < message.Length && IsDecoration(message[i]))
        {
            i++;
        }

        if (i >= message.Length || message[i] != '[')
        {
            return (UntaggedKey, 0);
        }

        var end = message.IndexOf(']', i + 1);
        if (end < 0)
        {
            return (UntaggedKey, 0);
        }

        var tag = message.Substring(i + 1, end - i - 1).Trim();

        if (tag.Length == 0 || tag.Length > MaxTagLength)
        {
            return (UntaggedKey, 0);
        }


        if (tag.Contains(':') || tag.Contains('\n') || tag.Contains('\r'))
        {
            return (UntaggedKey, 0);
        }

        return (tag, end + 1);
    }

    private static bool IsDecoration(char c) =>
        char.IsWhiteSpace(c) || c is '=' or '-' or '*' or '#' or '>';

    private static bool GlobMatch(string pattern, string text)
    {
        int p = 0, t = 0, starP = -1, starT = 0;

        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || CharsEqual(pattern[p], text[t])))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                starP = p;
                starT = t;
                p++;
            }
            else if (starP >= 0)
            {
                p = starP + 1;
                t = ++starT;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    private static bool CharsEqual(char a, char b) =>
        char.ToLowerInvariant(a) == char.ToLowerInvariant(b);

    private static FilterState BuildState(string json)
    {
        var options = JsonSerializer.Deserialize<DebugLogOptions>(json, JsonOptions);
        if (options == null || options.Enabled == false)
        {
            return FilterState.PassthroughAll;
        }

        var exact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var globs = new List<string>();

        foreach (var raw in options.Tags ?? new List<string>())
        {
            if (raw == null)
            {
                continue;
            }

            // "" 与 "<untagged>" 都指向无标签输出。
            var tag = raw.Trim();
            if (tag.Length == 0 || string.Equals(tag, "<untagged>", StringComparison.OrdinalIgnoreCase))
            {
                exact.Add(UntaggedKey);
                continue;
            }

            if (tag.Contains('*') || tag.Contains('?'))
            {
                globs.Add(tag);
            }
            else
            {
                exact.Add(tag);
            }
        }


        string[] keywords = options.EscapeHatch == false
            ? Array.Empty<string>()
            : (options.ErrorKeywords?.ToArray() ?? DefaultErrorKeywords);

        return new FilterState
        {
            Passthrough = false,
            ExactTags = exact,
            GlobPatterns = globs.ToArray(),
            ErrorKeywords = keywords
        };
    }

    private static bool IsVerboseOverride()
    {
        var raw = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        return string.Equals(raw, "1", StringComparison.Ordinal)
            || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }


    private static void EnsureConfigFile()
    {
#if DEBUG
        if (File.Exists(ConfigPath))
        {
            return;
        }

        Directory.CreateDirectory(AppPaths.SettingsDir);
        File.WriteAllText(ConfigPath, SampleConfig);
#endif
    }


    private static void InstallDispatcher()
    {
        if (Trace.Listeners.Count == 1 && Trace.Listeners[0] is DispatcherListener)
        {
            return;
        }

        var existing = new TraceListener[Trace.Listeners.Count];
        Trace.Listeners.CopyTo(existing, 0);

        Trace.Listeners.Clear();

        if (existing.Length == 0)
        {
            // 与默认行为一致: 写入 OutputDebugString，保留 Debug.Assert 的断言对话框。
            existing = new TraceListener[] { new DefaultTraceListener { Name = "Default" } };
        }

        Trace.Listeners.Add(new DispatcherListener(existing));
    }

    /// <summary>
    /// 监视配置文件，改动后即时生效，免去重启。
    /// </summary>
    private static void StartWatcher()
    {
        if (Interlocked.Exchange(ref _watcherStarted, 1) == 1)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDir);

            var watcher = new FileSystemWatcher(AppPaths.SettingsDir, "debuglog.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            FileSystemEventHandler handler = (_, _) => DebouncedReload();
            watcher.Changed += handler;
            watcher.Created += handler;
            watcher.Deleted += handler;
            watcher.Renamed += (_, _) => DebouncedReload();

            _watcher = watcher;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[DebugLog] 配置热重载不可用: {ex.Message}");
        }
    }

    private static void DebouncedReload()
    {

        lock (DebounceLock)
        {
            _debounceTimer ??= new Timer(_ => Reload(), null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(DebounceDelayMs, Timeout.Infinite);
        }
    }

    /// <summary>不可变的过滤状态，整体替换实现无锁读取。</summary>
    private sealed class FilterState
    {
        public static readonly FilterState PassthroughAll = new()
        {
            Passthrough = true,
            ExactTags = new HashSet<string>(),
            GlobPatterns = Array.Empty<string>(),
            ErrorKeywords = Array.Empty<string>()
        };

        public bool Passthrough { get; init; }

        public HashSet<string> ExactTags { get; init; } = new(StringComparer.OrdinalIgnoreCase);
        public string[] GlobPatterns { get; init; } = Array.Empty<string>();

        /// <summary>逃生通道关键词，为空表示不启用。</summary>
        public string[] ErrorKeywords { get; init; } = Array.Empty<string>();

        public ConcurrentDictionary<string, bool> VerdictCache { get; } = new(StringComparer.Ordinal);
    }

    private sealed class DebugLogOptions
    {
        public bool? Enabled { get; set; }
        public List<string>? Tags { get; set; }
        public bool? EscapeHatch { get; set; }
        public List<string>? ErrorKeywords { get; set; }
    }


    private sealed class DispatcherListener : TraceListener
    {
        private readonly TraceListener[] _inner;

        public DispatcherListener(TraceListener[] inner)
        {
            _inner = inner;
            Name = "FufuLauncher.DebugLog";
        }

        public override void Write(string? message)
        {
            if (!ShouldEmit(message))
            {
                return;
            }

            foreach (var listener in _inner)
            {
                listener.Write(message);
            }
        }

        public override void WriteLine(string? message)
        {
            if (!ShouldEmit(message))
            {
                return;
            }

            foreach (var listener in _inner)
            {
                listener.WriteLine(message);
            }
        }

        public override void WriteLine(string? message, string? category)
        {
            if (!ShouldEmit(message))
            {
                return;
            }

            foreach (var listener in _inner)
            {
                listener.WriteLine(message, category);
            }
        }

        public override void Fail(string? message)
        {
            // 断言失败始终放行。
            foreach (var listener in _inner)
            {
                listener.Fail(message);
            }
        }

        public override void Fail(string? message, string? detailMessage)
        {
            foreach (var listener in _inner)
            {
                listener.Fail(message, detailMessage);
            }
        }
    }


    private const string SampleConfig = """
{
  "enabled": true,
  "tags": ["ResourceExt","UpdateService"],
  "escapeHatch": true,
  "errorKeywords": [
    "失败", "异常", "错误", "无法", "致命", "崩溃", "超时", "拒绝", "不可用", "警告",
    "fail", "error", "exception", "fatal", "crash", "timeout", "refused", "denied", "invalid", "warning"
  ]
}
""";
}
