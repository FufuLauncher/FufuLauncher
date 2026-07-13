/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Text.Json;

namespace FufuLauncher.Helpers;

public static class AppPaths
{
    public static string RootDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher");

    public static string SettingsDir => Path.Combine(RootDir, "Settings");

    private static string _dataDir;
    private static string _cacheDir;

    public static string DataDir => _dataDir;
    public static string CacheDir => _cacheDir;

    private static string PathsConfigFile => Path.Combine(SettingsDir, "paths.json");

    public static string LocalSettingsDb => Path.Combine(DataDir, "LocalSettings.db");
    public static string GameAccountsFile => Path.Combine(DataDir, "game_accounts.json");
    public static string GachaDataFile => Path.Combine(DataDir, "gacha_data.json");
    public static string MetadataDb => Path.Combine(DataDir, "metadata.db");
    public static string FufuConfigFile => Path.Combine(DataDir, "FufuConfig.cfg");
    //public static string ConfigFile => Path.Combine(DataDir, "config.json");
    //public static string ConfigLabFile => Path.Combine(DataDir, "config.lab.json");
    public static string InventoryCacheFile => Path.Combine(DataDir, "inventory_cache.json");
    public static string ServerCacheDir => Path.Combine(CacheDir, "ServerCache");
    public static string VerifyCacheDir => Path.Combine(CacheDir, "VerifyCache");
    public static string PluginPresetsDir => Path.Combine(DataDir, "PluginPresets");

    static AppPaths()
    {
        _dataDir = Path.Combine(RootDir, "Data");
        _cacheDir = Path.Combine(RootDir, "Cache");
    }

    public static bool IsFirstRun { get; private set; }

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(SettingsDir);
        LoadCustomPaths();

        IsFirstRun = !File.Exists(PathsConfigFile);

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(CacheDir);

        if (IsFirstRun)
        {
            return;
        }

        var defaultData = Path.Combine(RootDir, "Data");
        var defaultCache = Path.Combine(RootDir, "Cache");
        if (!string.Equals(defaultData, _dataDir, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(defaultData)
            && !ArePathsOverlapping(defaultData, _dataDir))
        {
            if (MoveDirectoryContents(defaultData, _dataDir))
                TryDeleteEmptyDirectory(defaultData);
        }
        if (!string.Equals(defaultCache, _cacheDir, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(defaultCache)
            && !ArePathsOverlapping(defaultCache, _cacheDir))
        {
            if (MoveDirectoryContents(defaultCache, _cacheDir))
                TryDeleteEmptyDirectory(defaultCache);
        }

        MigrateOldFiles();
    }

    public static void FinalizeFirstRun()
    {
        IsFirstRun = false;
    }

    public static void SaveCustomPaths(string newDataDir, string newCacheDir)
    {
        var oldDataDir = _dataDir;
        var oldCacheDir = _cacheDir;

        Directory.CreateDirectory(newDataDir);
        Directory.CreateDirectory(newCacheDir);

        bool dataMovedOk = true;
        bool cacheMovedOk = true;

        if (!string.Equals(oldDataDir, newDataDir, StringComparison.OrdinalIgnoreCase))
        {
            if (!ArePathsOverlapping(oldDataDir, newDataDir))
                dataMovedOk = MoveDirectoryContents(oldDataDir, newDataDir);
            else
                Debug.WriteLine($"[AppPaths] 跳过数据迁移: 源目录与目标目录存在包含关系 {oldDataDir} <-> {newDataDir}");
        }

        if (!string.Equals(oldCacheDir, newCacheDir, StringComparison.OrdinalIgnoreCase))
        {
            if (!ArePathsOverlapping(oldCacheDir, newCacheDir))
                cacheMovedOk = MoveDirectoryContents(oldCacheDir, newCacheDir);
            else
                Debug.WriteLine($"[AppPaths] 跳过缓存迁移: 源目录与目标目录存在包含关系 {oldCacheDir} <-> {newCacheDir}");
        }

        _dataDir = newDataDir;
        _cacheDir = newCacheDir;

        WritePathsConfig(newDataDir, newCacheDir);

        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(newCacheDir, "WebView2Data"));

        if (dataMovedOk && !string.Equals(oldDataDir, newDataDir, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteEmptyDirectory(oldDataDir);
        }
        if (cacheMovedOk && !string.Equals(oldCacheDir, newCacheDir, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteEmptyDirectory(oldCacheDir);
        }
    }

    private static void TryDeleteEmptyDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                Directory.Delete(dir, false);
        }
        catch { }
    }

    private static void WritePathsConfig(string dataDir, string cacheDir)
    {
        Directory.CreateDirectory(SettingsDir);
        var config = new Dictionary<string, string>
        {
            ["DataDir"] = dataDir,
            ["CacheDir"] = cacheDir
        };
        File.WriteAllText(PathsConfigFile, JsonSerializer.Serialize(config));
    }
    
    private static bool ArePathsOverlapping(string path1, string path2)
    {
        try
        {
            var full1 = Path.GetFullPath(path1).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
            var full2 = Path.GetFullPath(path2).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        + Path.DirectorySeparatorChar;
            return full1.StartsWith(full2, StringComparison.OrdinalIgnoreCase)
                || full2.StartsWith(full1, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;
        }
    }
    
    private static bool IsSubdirectoryOf(string candidate, string parent)
    {
        var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;
        var fullParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        return fullCandidate.StartsWith(fullParent, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MoveDirectoryContents(string sourceDir, string destDir)
    {
        bool allSuccess = true;
        try
        {
            if (!Directory.Exists(sourceDir)) return true;
            
            if (IsSubdirectoryOf(destDir, sourceDir))
            {
                Debug.WriteLine($"[AppPaths] 中止迁移: 目标目录是源目录的子目录 {sourceDir} -> {destDir}");
                return false;
            }

            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                try
                {
                    if (!File.Exists(destFile))
                    {
                        File.Copy(file, destFile, false);
                    }
                    if (File.Exists(destFile))
                    {
                        File.Delete(file);
                    }
                }
                catch (Exception ex)
                {
                    allSuccess = false;
                    Debug.WriteLine($"[AppPaths] 迁移文件失败 {file}: {ex.Message}");
                }
            }

            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var fullDir = Path.GetFullPath(dir);
                var fullDest = Path.GetFullPath(destDir);
                if (string.Equals(fullDir.TrimEnd(Path.DirectorySeparatorChar),
                                   fullDest.TrimEnd(Path.DirectorySeparatorChar),
                                   StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                if (!MoveDirectoryContents(dir, destSubDir))
                    allSuccess = false;
            }

            try
            {
                if (Directory.GetFileSystemEntries(sourceDir).Length == 0)
                    Directory.Delete(sourceDir, false);
            }
            catch { }
        }
        catch (Exception ex)
        {
            allSuccess = false;
            Debug.WriteLine($"[AppPaths] 迁移目录失败 {sourceDir} -> {destDir}: {ex.Message}");
        }
        return allSuccess;
    }

    private static void LoadCustomPaths()
    {
        try
        {
            if (!File.Exists(PathsConfigFile)) return;
            var json = File.ReadAllText(PathsConfigFile);
            var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (config == null) return;
            if (config.TryGetValue("DataDir", out var d) && !string.IsNullOrWhiteSpace(d))
                _dataDir = d;
            if (config.TryGetValue("CacheDir", out var c) && !string.IsNullOrWhiteSpace(c))
                _cacheDir = c;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppPaths] 读取自定义路径失败: {ex.Message}");
        }
    }

    private static void MigrateOldFiles()
    {
        string oldAppDataDir = Path.Combine(RootDir, "ApplicationData");
        MoveFileIfExists(Path.Combine(oldAppDataDir, "LocalSettings.db"), LocalSettingsDb);

        MoveFileIfExists(Path.Combine(RootDir, "game_accounts.json"), GameAccountsFile);

        string docsFufu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "fufu");
        if (Directory.Exists(docsFufu))
        {
            MoveFileIfExists(Path.Combine(docsFufu, "gacha_data.json"), GachaDataFile);
            MoveFileIfExists(Path.Combine(docsFufu, "metadata.db"), MetadataDb);
            MoveFileIfExists(Path.Combine(docsFufu, "FufuConfig.cfg"), FufuConfigFile);
        }

        string exeDir = AppContext.BaseDirectory;
        //MoveFileIfExists(Path.Combine(exeDir, "config.json"), ConfigFile);
        //MoveFileIfExists(Path.Combine(exeDir, "config.lab.json"), ConfigLabFile);
        MoveFileIfExists(Path.Combine(exeDir, "Data", "inventory_cache.json"), InventoryCacheFile);
        MoveFileIfExists(Path.Combine(exeDir, "browser_config.json"), Path.Combine(DataDir, "browser_config.json"));
        MoveFileIfExists(Path.Combine(exeDir, "user.config.json"), Path.Combine(DataDir, "user.config.json"));

        try
        {
            foreach (var file in Directory.GetFiles(exeDir, "config_*.json"))
            {
                string dest = Path.Combine(DataDir, Path.GetFileName(file));
                MoveFileIfExists(file, dest);
            }
            foreach (var file in Directory.GetFiles(exeDir, "display_*.json"))
            {
                string dest = Path.Combine(DataDir, Path.GetFileName(file));
                MoveFileIfExists(file, dest);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppPaths] 迁移通配文件失败: {ex.Message}");
        }
    }

    private static void MoveFileIfExists(string source, string destination)
    {
        try
        {
            if (File.Exists(source) && !File.Exists(destination))
            {
                string? dir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.Move(source, destination);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[AppPaths] 迁移失败 {source}: {ex.Message}");
        }
    }
}

