/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;

namespace FufuLauncher.Helpers;

public static class AppPaths
{
    public static string RootDir
    {
        get;
    } = Path.Combine(
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

    public static string GamePlayTimeFile => Path.Combine(DataDir, "game_play_time.json");

    public static string LegacyFufuConfigFile => Path.Combine(DataDir, "FufuConfig.cfg");


    public static string InventoryCacheFile => Path.Combine(DataDir, "inventory_cache.json");
    public static string MiHoYoDeviceFile => Path.Combine(DataDir, "mihoyo_device.json");
    public static string ServerCacheDir => Path.Combine(CacheDir, "ServerCache");
    public static string VerifyCacheDir => Path.Combine(CacheDir, "VerifyCache");
    public static string PluginPresetsDir => Path.Combine(DataDir, "PluginPresets");
    public static string PluginConfigBaselineDir => Path.Combine(DataDir, "PluginConfigBaseline");
    public static string MainPluginConfigBaselineFile =>
        Path.Combine(PluginConfigBaselineDir, "FuFuPlugin.config.bin");

    static AppPaths()
    {
        _dataDir = Path.Combine(RootDir, "Data");
        _cacheDir = Path.Combine(RootDir, "Cache");
    }

    public static bool IsFirstRun
    {
        get;
        private set;
    }

    public static string? MissingDataDir
    {
        get;
        private set;
    }

    public static string? MissingCacheDir
    {
        get;
        private set;
    }

    public static bool HasMissingPaths => MissingDataDir != null || MissingCacheDir != null;

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(SettingsDir);
        LoadCustomPaths();

        var defaultData = Path.Combine(RootDir, "Data");
        var defaultCache = Path.Combine(RootDir, "Cache");

        if (!Directory.Exists(_dataDir) && !string.Equals(_dataDir, defaultData, StringComparison.OrdinalIgnoreCase))
        {
            MissingDataDir = _dataDir;
            DebugLog.Info("AppPaths", $"[AppPaths] Data directory missing: {_dataDir}");
        }

        if (!Directory.Exists(_cacheDir) && !string.Equals(_cacheDir, defaultCache, StringComparison.OrdinalIgnoreCase))
        {
            MissingCacheDir = _cacheDir;
            DebugLog.Info("AppPaths", $"[AppPaths] Cache directory missing: {_cacheDir}");
        }

        ValidateOrFallbackDataDir();
        ValidateOrFallbackCacheDir();

        IsFirstRun = !File.Exists(PathsConfigFile) && !File.Exists(LocalSettingsDb);

        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(CacheDir);

        if (IsFirstRun)
        {
            try
            {
                WritePathsConfig(_dataDir, _cacheDir);
            }
            catch (Exception ex)
            {
                DebugLog.Info("AppPaths", $"[AppPaths] Failed to create paths.json: {ex.Message}");
            }

            return;
        }

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
                DebugLog.Info("AppPaths",
                    $"[AppPaths] Skipping data migration: source and target directories overlap {oldDataDir} <-> {newDataDir}");
        }

        if (!string.Equals(oldCacheDir, newCacheDir, StringComparison.OrdinalIgnoreCase))
        {
            if (!ArePathsOverlapping(oldCacheDir, newCacheDir))
                cacheMovedOk = MoveDirectoryContents(oldCacheDir, newCacheDir);
            else
                DebugLog.Info("AppPaths",
                    $"[AppPaths] Skipping cache migration: source and target directories overlap {oldCacheDir} <-> {newCacheDir}");
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
        catch
        {
        }
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

    public static string? ValidateCustomPaths(string dataPath, string cachePath)
    {
        var dataLabel = "AgreementPage_UserDataDir".GetLocalized();
        var cacheLabel = "AgreementPage_CacheDir".GetLocalized();

        if (string.IsNullOrWhiteSpace(dataPath))
            return string.Format("StoragePath_Error_Empty".GetLocalized(), dataLabel);

        if (string.IsNullOrWhiteSpace(cachePath))
            return string.Format("StoragePath_Error_Empty".GetLocalized(), cacheLabel);

        var dataError = TryNormalizeDirectory(dataPath, dataLabel, out var fullData);
        if (dataError != null)
            return dataError;

        var cacheError = TryNormalizeDirectory(cachePath, cacheLabel, out var fullCache);
        if (cacheError != null)
            return cacheError;

        if (string.Equals(fullData, fullCache, StringComparison.OrdinalIgnoreCase))
            return string.Format("StoragePath_Error_Same".GetLocalized(), dataLabel, cacheLabel, dataPath);

        if (fullData.StartsWith(fullCache, StringComparison.OrdinalIgnoreCase)
            || fullCache.StartsWith(fullData, StringComparison.OrdinalIgnoreCase))
            return string.Format("StoragePath_Error_Nested".GetLocalized(), dataLabel, dataPath, cacheLabel, cachePath);

        var appDir = NormalizeDirectory(AppContext.BaseDirectory);
        var appDirDisplay =
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (fullData.StartsWith(appDir, StringComparison.OrdinalIgnoreCase))
            return string.Format("StoragePath_Error_UnderAppDir".GetLocalized(), dataLabel, dataPath, appDirDisplay);

        if (fullCache.StartsWith(appDir, StringComparison.OrdinalIgnoreCase))
            return string.Format("StoragePath_Error_UnderAppDir".GetLocalized(), cacheLabel, cachePath, appDirDisplay);

        if (appDir.StartsWith(fullData, StringComparison.OrdinalIgnoreCase))
            return string.Format("StoragePath_Error_ParentOfAppDir".GetLocalized(), dataLabel, dataPath, appDirDisplay);

        if (appDir.StartsWith(fullCache, StringComparison.OrdinalIgnoreCase))
            return string.Format("StoragePath_Error_ParentOfAppDir".GetLocalized(), cacheLabel, cachePath,
                appDirDisplay);

        if (string.Equals(Path.GetPathRoot(dataPath), fullData, StringComparison.OrdinalIgnoreCase))
            return string.Format("StoragePath_Error_DriveRoot".GetLocalized(), dataLabel, dataPath);

        if (string.Equals(Path.GetPathRoot(cachePath), fullCache, StringComparison.OrdinalIgnoreCase))
            return string.Format("StoragePath_Error_DriveRoot".GetLocalized(), cacheLabel, cachePath);

        return null;
    }

    private static string? TryNormalizeDirectory(string path, string label, out string normalized)
    {
        try
        {
            normalized = NormalizeDirectory(path);
            return null;
        }
        catch (Exception ex)
        {
            normalized = string.Empty;
            return string.Format("StoragePath_Error_Invalid".GetLocalized(), label, path, ex.Message);
        }
    }

    private static string NormalizeDirectory(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               + Path.DirectorySeparatorChar;
    }

    private static bool ArePathsOverlapping(string path1, string path2)
    {
        try
        {
            var full1 = NormalizeDirectory(path1);
            var full2 = NormalizeDirectory(path2);
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
        var fullCandidate = NormalizeDirectory(candidate);
        var fullParent = NormalizeDirectory(parent);
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
                DebugLog.Info("AppPaths",
                    $"[AppPaths] Aborting migration: target is a subdirectory of source {sourceDir} -> {destDir}");
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
                    DebugLog.Info("AppPaths", $"[AppPaths] Failed to migrate file {file}: {ex.Message}");
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
            catch
            {
            }
        }
        catch (Exception ex)
        {
            allSuccess = false;
            DebugLog.Info("AppPaths", $"[AppPaths] Failed to migrate directory {sourceDir} -> {destDir}: {ex.Message}");
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
            DebugLog.Info("AppPaths", $"[AppPaths] Failed to read custom paths: {ex.Message}");
        }
    }


    private static void ValidateOrFallbackDataDir()
    {
        if (string.IsNullOrWhiteSpace(_dataDir))
        {
            _dataDir = Path.Combine(RootDir, "Data");
        }

        try
        {
            Directory.CreateDirectory(_dataDir);

            var probe = Path.Combine(_dataDir, $".write_test_{Environment.ProcessId}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            var defaultData = Path.Combine(RootDir, "Data");
            DebugLog.Info("AppPaths",
                $"[AppPaths] Data directory unavailable, fell back to default: {_dataDir} -> {defaultData} ({ex.Message})");
            MissingDataDir = _dataDir;
            _dataDir = defaultData;

            try
            {
                Directory.CreateDirectory(_dataDir);
            }
            catch (Exception fallbackEx)
            {
                DebugLog.Info("AppPaths", $"[AppPaths] Failed to create default data directory: {fallbackEx.Message}");
            }
        }
    }

    private static void ValidateOrFallbackCacheDir()
    {
        if (string.IsNullOrWhiteSpace(_cacheDir))
        {
            _cacheDir = Path.Combine(RootDir, "Cache");
        }

        try
        {
            Directory.CreateDirectory(_cacheDir);

            var probe = Path.Combine(_cacheDir, $".write_test_{Environment.ProcessId}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            var defaultCache = Path.Combine(RootDir, "Cache");
            DebugLog.Info("AppPaths",
                $"[AppPaths] Cache directory unavailable, fell back to default: {_cacheDir} -> {defaultCache} ({ex.Message})");
            MissingCacheDir = _cacheDir;
            _cacheDir = defaultCache;

            try
            {
                Directory.CreateDirectory(_cacheDir);
            }
            catch (Exception fallbackEx)
            {
                DebugLog.Info("AppPaths", $"[AppPaths] Failed to create default cache directory: {fallbackEx.Message}");
            }
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
            if (!File.Exists(GamePlayTimeFile))
            {
                MoveFileIfExists(Path.Combine(docsFufu, "FufuConfig.cfg"), LegacyFufuConfigFile);
            }
        }

        string exeDir = AppContext.BaseDirectory;


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
            DebugLog.Info("AppPaths", $"[AppPaths] Failed to migrate wildcard files: {ex.Message}");
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
            DebugLog.Info("AppPaths", $"[AppPaths] Migration failed {source}: {ex.Message}");
        }
    }
}