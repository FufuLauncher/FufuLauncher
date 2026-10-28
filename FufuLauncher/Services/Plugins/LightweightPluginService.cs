/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services
{
    public class LightweightPluginService
    {
        public const string SettingKey = "IsLightweightMode";

        public const string LitePluginFolderName = "YuanShen-UnlockerLite";
        public const string LitePluginDllName = "YuanShen-UnlockerLite.dll";
        public const string LitePluginConfigName = "Config.ini";
        public const string MainPluginFolderName = "FuFuPlugin";
        public const string MainPluginDllName = "FufuLauncher.UnlockerIsland.dll";

        private const string ReleaseApiUrl = ApiEndpoints.UnlockerLiteReleaseApiUrl;

        private const string ReleaseZipDirectUrl = ApiEndpoints.UnlockerLiteReleaseZipUrl;

        private const string ZipAssetName = "YuanShen-UnlockerLite.zip";
        private const string ProxyPrefix = ApiEndpoints.GhProxyBase + "/";

        private readonly ILocalSettingsService _localSettings;
        private readonly object _stateLock = new();
        private bool _initialized;
        private bool _isLightweightMode;

        public LightweightPluginService(ILocalSettingsService localSettings)
        {
            _localSettings = localSettings;
        }

        public static string PluginsDir => Path.Combine(AppContext.BaseDirectory, "Plugins");

        public static string LitePluginDir => Path.Combine(PluginsDir, LitePluginFolderName);
        public static string LitePluginDllPath => Path.Combine(LitePluginDir, LitePluginDllName);
        public static string LitePluginDisabledPath => LitePluginDllPath + ".disabled";

        public static string LitePluginShortDisabledPath => Path.Combine(LitePluginDir,
            Path.GetFileNameWithoutExtension(LitePluginDllName) + ".disabled");

        public static string LitePluginConfigPath => Path.Combine(LitePluginDir, LitePluginConfigName);

        public static string MainPluginDir => Path.Combine(PluginsDir, MainPluginFolderName);
        public static string MainPluginDllPath => Path.Combine(MainPluginDir, MainPluginDllName);
        public static string MainPluginDisabledPath => MainPluginDllPath + ".disabled";

        public static string MainPluginShortDisabledPath => Path.Combine(MainPluginDir,
            Path.GetFileNameWithoutExtension(MainPluginDllName) + ".disabled");

        public static string? FindLitePluginDisabledPath()
        {
            if (File.Exists(LitePluginDisabledPath)) return LitePluginDisabledPath;
            if (File.Exists(LitePluginShortDisabledPath)) return LitePluginShortDisabledPath;
            return null;
        }

        public static string? FindMainPluginDisabledPath()
        {
            if (File.Exists(MainPluginDisabledPath)) return MainPluginDisabledPath;
            if (File.Exists(MainPluginShortDisabledPath)) return MainPluginShortDisabledPath;
            return null;
        }

        public static bool IsLitePluginInstalled =>
            File.Exists(LitePluginDllPath) || FindLitePluginDisabledPath() != null;

        public bool IsLightweightMode
        {
            get
            {
                EnsureInitialized();
                lock (_stateLock)
                {
                    return _isLightweightMode;
                }
            }
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;

            bool value = false;
            try
            {
                var stored = await _localSettings.ReadSettingAsync(SettingKey);
                value = stored != null && Convert.ToBoolean(stored);
            }
            catch (Exception ex)
            {
                DebugLog.Info("LightweightPluginService",
                    $"[LightweightMode] Failed to read mode setting: {ex.Message}");
            }

            lock (_stateLock)
            {
                _isLightweightMode = value;
                _initialized = true;
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            try
            {
                InitializeAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                DebugLog.Info("LightweightPluginService", $"[LightweightMode] Failed to initialize: {ex.Message}");
                lock (_stateLock)
                {
                    _initialized = true;
                }
            }
        }

        public async Task SetLightweightModeAsync(bool enabled)
        {
            lock (_stateLock)
            {
                _isLightweightMode = enabled;
                _initialized = true;
            }

            await _localSettings.SaveSettingAsync(SettingKey, enabled);
        }

        public static void DisableMainPluginIfPresent()
        {
            try
            {
                if (File.Exists(MainPluginDllPath))
                {
                    File.Move(MainPluginDllPath, MainPluginDisabledPath, true);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("LightweightPluginService",
                    $"[LightweightMode] Failed to disable main plugin: {ex.Message}");
            }
        }

        public static bool TryEnableMainPlugin()
        {
            try
            {
                if (File.Exists(MainPluginDllPath)) return true;

                var disabledPath = FindMainPluginDisabledPath();
                if (disabledPath != null && PluginGameVersionGuard.IsPluginUsable(MainPluginDllPath))
                {
                    File.Move(disabledPath, MainPluginDllPath, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("LightweightPluginService",
                    $"[LightweightMode] Failed to enable main plugin: {ex.Message}");
            }

            return false;
        }

        public static bool TryEnableLitePlugin()
        {
            try
            {
                if (File.Exists(LitePluginDllPath)) return true;

                var disabledPath = FindLitePluginDisabledPath();
                if (disabledPath != null && PluginGameVersionGuard.IsPluginUsable(LitePluginDllPath))
                {
                    File.Move(disabledPath, LitePluginDllPath, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("LightweightPluginService",
                    $"[LightweightMode] Failed to enable lite plugin: {ex.Message}");
            }

            return false;
        }

        public static bool DisableLitePluginIfPresent()
        {
            try
            {
                if (File.Exists(LitePluginDllPath))
                {
                    File.Move(LitePluginDllPath, LitePluginDisabledPath, true);
                    return true;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("LightweightPluginService",
                    $"[LightweightMode] Failed to disable lite plugin: {ex.Message}");
            }

            return false;
        }

        public static void RemoveOrDisableLitePlugin()
        {
            try
            {
                if (Directory.Exists(LitePluginDir))
                {
                    Directory.Delete(LitePluginDir, true);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("LightweightPluginService",
                    $"[LightweightMode] Failed to delete lite plugin directory: {ex.Message}");
            }

            if (Directory.Exists(LitePluginDir))
            {
                DisableLitePluginIfPresent();
            }
        }

        public string? GetInstallBlockReason(string? folderName, string? dllName)
        {
            if (IsMainPluginPackage(folderName, dllName))
            {
                return IsLightweightMode ? "LightweightMode_BlockMainInstall".GetLocalized() : null;
            }

            if (IsLitePluginPackage(folderName, dllName))
            {
                return IsLightweightMode ? null : "LightweightMode_BlockLiteInstall".GetLocalized();
            }

            return null;
        }

        public static bool IsMainPluginPackage(string? folderName, string? dllName)
        {
            if (!string.IsNullOrEmpty(folderName) &&
                folderName.Contains(MainPluginFolderName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return MatchesDllName(dllName, MainPluginDllName);
        }

        private static bool IsLitePluginPackage(string? folderName, string? dllName)
        {
            if (!string.IsNullOrEmpty(folderName) &&
                folderName.Contains(LitePluginFolderName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return MatchesDllName(dllName, LitePluginDllName);
        }

        private static bool MatchesDllName(string? fileName, string expectedDllName)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            return fileName.Equals(expectedDllName, StringComparison.OrdinalIgnoreCase) ||
                   fileName.Equals(expectedDllName + ".disabled", StringComparison.OrdinalIgnoreCase);
        }

        public bool EnforceModeAtLaunch(bool lightweight)
        {
            if (lightweight)
            {
                bool mainPluginWasEnabled = File.Exists(MainPluginDllPath);
                DisableMainPluginIfPresent();
                return mainPluginWasEnabled;
            }

            if (!IsLitePluginInstalled) return false;

            try
            {
                if (Directory.Exists(LitePluginDir))
                {
                    Directory.Delete(LitePluginDir, true);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("LightweightPluginService",
                    $"[LightweightMode] Failed to clean lite plugin directory on startup: {ex.Message}");
            }

            if (Directory.Exists(LitePluginDir))
            {
                DisableLitePluginIfPresent();
            }

            return true;
        }

        public async Task InstallOrUpdateLitePluginAsync(IProgress<double>? progress = null,
            Action<string>? status = null, CancellationToken cancellationToken = default)
        {
            string tempZip = Path.Combine(Path.GetTempPath(), $"YuanShen-UnlockerLite_{Guid.NewGuid():N}.zip");
            string extractDir = Path.Combine(Path.GetTempPath(), $"YuanShen-UnlockerLite_Extract_{Guid.NewGuid():N}");
            string configBackup =
                Path.Combine(Path.GetTempPath(), $"YuanShen-UnlockerLite_Config_{Guid.NewGuid():N}.ini");

            try
            {
                status?.Invoke("LightweightMode_StatusResolving".GetLocalized());

                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

                Exception? lastError = null;

                async Task<bool> TryDownloadAllAsync(IEnumerable<string> urls)
                {
                    foreach (var url in urls)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            if (File.Exists(tempZip)) File.Delete(tempZip);
                            status?.Invoke("LightweightMode_StatusDownloading".GetLocalized());
                            await DownloadAsync(client, url, tempZip, progress, cancellationToken);
                            return true;
                        }
                        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                        {
                            lastError = ex;
                            DebugLog.Info("LightweightPluginService",
                                $"[LightweightMode] Download failed {url}: {ex.Message}");
                        }
                    }

                    return false;
                }

                bool downloaded = await TryDownloadAllAsync(GetStaticDownloadUrls());

                if (!downloaded)
                {
                    status?.Invoke("LightweightMode_StatusResolving".GetLocalized());
                    downloaded = await TryDownloadAllAsync(await GetReleaseApiDownloadUrlsAsync(client));
                }

                if (!downloaded)
                {
                    throw new IOException(
                        string.Format("LightweightMode_DownloadFailed".GetLocalized(),
                            lastError?.Message ?? string.Empty),
                        lastError);
                }

                cancellationToken.ThrowIfCancellationRequested();

                status?.Invoke("LightweightMode_StatusInstalling".GetLocalized());
                progress?.Report(0);

                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);
                await Task.Run(() => ZipFile.ExtractToDirectory(tempZip, extractDir));

                var packageRoot = FindPackageRoot(extractDir);
                if (packageRoot == null)
                {
                    throw new InvalidDataException("LightweightMode_InvalidPackage".GetLocalized());
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(LitePluginConfigPath))
                {
                    File.Copy(LitePluginConfigPath, configBackup, true);
                }

                if (Directory.Exists(LitePluginDir)) Directory.Delete(LitePluginDir, true);
                await Task.Run(() => CopyDirectory(packageRoot, LitePluginDir));

                if (File.Exists(configBackup))
                {
                    File.Copy(configBackup, LitePluginConfigPath, true);
                }

                try
                {
                    File.Delete(tempZip);
                }
                catch
                {
                }

                if (PluginGameVersionGuard.IsPluginUsable(LitePluginDllPath))
                {
                    TryEnableLitePlugin();
                }
                else
                {
                    DisableLitePluginIfPresent();
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(tempZip)) File.Delete(tempZip);
                    if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                    if (File.Exists(configBackup)) File.Delete(configBackup);
                }
                catch (Exception ex)
                {
                    DebugLog.Info("LightweightPluginService",
                        $"[LightweightMode] Failed to clean temporary files: {ex.Message}");
                }
            }
        }

        private static List<string> GetStaticDownloadUrls() =>
            new()
            {
                ProxyPrefix + ReleaseZipDirectUrl,
                ReleaseZipDirectUrl
            };

        private static async Task<List<string>> GetReleaseApiDownloadUrlsAsync(HttpClient client)
        {
            var urls = new List<string>();

            var assetUrl = await ResolveLatestZipAssetUrlAsync(client);
            if (!string.IsNullOrEmpty(assetUrl))
            {
                urls.Add(ProxyPrefix + assetUrl);
                urls.Add(assetUrl);
            }

            return urls;
        }

        private static async Task<string?> ResolveLatestZipAssetUrlAsync(HttpClient client)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

                using var request = new HttpRequestMessage(HttpMethod.Get, ReleaseApiUrl);
                request.Headers.UserAgent.ParseAdd("FufuLauncher");
                request.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var response = await client.SendAsync(request, timeout.Token);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(timeout.Token);
                using var document = JsonDocument.Parse(json);

                if (!document.RootElement.TryGetProperty("assets", out var assets)) return null;

                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
                    if (!string.Equals(name, ZipAssetName, StringComparison.OrdinalIgnoreCase)) continue;

                    return asset.TryGetProperty("browser_download_url", out var urlElement)
                        ? urlElement.GetString()
                        : null;
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("LightweightPluginService",
                    $"[LightweightMode] Failed to parse latest release: {ex.Message}");
            }

            return null;
        }

        private static async Task DownloadAsync(HttpClient client, string url, string destination,
            IProgress<double>? progress, CancellationToken cancellationToken)
        {
            using var response =
                await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            var totalRead = 0L;
            var buffer = new byte[81920];

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None,
                81920, true);

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                totalRead += read;

                if (totalBytes > 0)
                {
                    progress?.Report(Math.Round((double)totalRead / totalBytes * 100, 1));
                }
            }
        }

        private static string? FindPackageRoot(string extractDir)
        {
            if (File.Exists(Path.Combine(extractDir, LitePluginDllName))) return extractDir;

            foreach (var dir in Directory.GetDirectories(extractDir, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(Path.Combine(dir, LitePluginDllName))) return dir;
            }

            return null;
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }
    }
}