/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Xml.Linq;

namespace FufuLauncher.Helpers;

public static class ResourceExtensions
{
    private static readonly Dictionary<string, Dictionary<string, string>> _resources = new();
    private static string? _currentCulture;
    private static readonly object _lock = new();
    private static bool _loaded;

    /// <summary>
    /// Sets the language for resource resolution.
    /// Pass null or empty to use the system default (first available).
    /// </summary>
    public static void SetLanguage(string? culture)
    {
        lock (_lock)
        {
            _currentCulture = string.IsNullOrEmpty(culture) ? null : culture;
            Debug.WriteLine($"[ResourceExt] SetLanguage: culture='{_currentCulture ?? "null"}'");
        }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;

            try
            {
                var exeDir = AppContext.BaseDirectory;
                Debug.WriteLine($"[ResourceExt] Loading resw from: {exeDir}");

                LoadResw(Path.Combine(exeDir, "Strings", "zh-CN", "Resources.resw"), "zh-CN");
                LoadResw(Path.Combine(exeDir, "Strings", "en-US", "Resources.resw"), "en-US");
                LoadResw(Path.Combine(exeDir, "Strings", "fr-FR", "Resources.resw"), "fr-FR");
                LoadResw(Path.Combine(exeDir, "Strings", "de-DE", "Resources.resw"), "de-DE");
                LoadResw(Path.Combine(exeDir, "Strings", "ru-RU", "Resources.resw"), "ru-RU");
                LoadResw(Path.Combine(exeDir, "Strings", "ja-JP", "Resources.resw"), "ja-JP");
                LoadResw(Path.Combine(exeDir, "Strings", "es-ES", "Resources.resw"), "es-ES");
                LoadResw(Path.Combine(exeDir, "Strings", "zh-TW", "Resources.resw"), "zh-TW");
                LoadResw(Path.Combine(exeDir, "Strings", "ko-KR", "Resources.resw"), "ko-KR");
                LoadResw(Path.Combine(exeDir, "Strings", "it-IT", "Resources.resw"), "it-IT");
                LoadResw(Path.Combine(exeDir, "Strings", "id-ID", "Resources.resw"), "id-ID");
                LoadResw(Path.Combine(exeDir, "Strings", "pt-BR", "Resources.resw"), "pt-BR");
                LoadResw(Path.Combine(exeDir, "Strings", "es-MX", "Resources.resw"), "es-MX");

                Debug.WriteLine($"[ResourceExt] Loaded {_resources.Count} language(s): {string.Join(", ", _resources.Keys)}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceExt] Failed to load resw files: {ex.Message}");
            }
            finally
            {
                _loaded = true;
            }
        }
    }

    private static void LoadResw(string path, string culture)
    {
        if (!File.Exists(path))
        {
            Debug.WriteLine($"[ResourceExt] Resw not found: {path}");
            return;
        }

        try
        {
            var dict = new Dictionary<string, string>();
            var doc = XDocument.Load(path);
            if (doc.Root == null) return;

            foreach (var data in doc.Root.Elements("data"))
            {
                var key = data.Attribute("name")?.Value;
                var value = data.Element("value")?.Value;
                if (!string.IsNullOrEmpty(key) && value != null)
                    dict[key] = value;
            }

            _resources[culture] = dict;
            Debug.WriteLine($"[ResourceExt] Loaded '{culture}': {dict.Count} strings");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ResourceExt] Error loading '{path}': {ex.Message}");
        }
    }

    public static string GetLocalized(this string resourceKey)
    {
        try
        {
            EnsureLoaded();

            // 1) Use the explicitly-set language
            string? culture;
            lock (_lock) { culture = _currentCulture; }

            if (culture != null &&
                _resources.TryGetValue(culture, out var dict) &&
                dict.TryGetValue(resourceKey, out var result))
                return result;

            // 2) Fallback: return the first match across all loaded languages
            foreach (var kv in _resources)
            {
                if (kv.Value.TryGetValue(resourceKey, out var fb))
                    return fb;
            }

            // 3) Absolute fallback: return the key itself
            return resourceKey;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ResourceExt] GetLocalized error: key='{resourceKey}', err='{ex.Message}'");
            return resourceKey;
        }
    }
}
