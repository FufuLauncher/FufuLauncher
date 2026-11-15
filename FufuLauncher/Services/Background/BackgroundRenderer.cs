/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices.WindowsRuntime;
using FufuLauncher.Models;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Storage.Streams;
using CommunityToolkit.Mvvm.Messaging;

public class BackgroundItem
{
    public string Url
    {
        get;
        set;
    }

    public string PreviewUrl
    {
        get;
        set;
    }

    public bool IsVideo
    {
        get;
        set;
    }

    public string TypeText => IsVideo ? "Background_TypeVideo".GetLocalized() : "Background_TypeImage".GetLocalized();
}

namespace FufuLauncher.Services.Background
{
    public class BackgroundRenderResult
    {
        public bool IsFallback
        {
            get;
            set;
        }

        public ImageSource ImageSource
        {
            get;
            set;
        }

        public MediaSource VideoSource
        {
            get;
            set;
        }

        public InMemoryRandomAccessStream VideoStream
        {
            get;
            set;
        }

        public bool IsVideo
        {
            get;
            set;
        }
    }

    public interface IBackgroundRenderer
    {
        Task<BackgroundRenderResult> GetBackgroundAsync(ServerType server, bool preferVideo);
        Task<BackgroundRenderResult> GetCustomBackgroundAsync(string filePath);
        Task<BackgroundRenderResult> GetSpecificOnlineBackgroundAsync(string url, bool isVideo);
        Task<BackgroundRenderResult> GetFallbackBackgroundAsync();
        Task PreloadImageBackgroundsAsync(IEnumerable<string> imageUrls);
        Task CacheAllBackgroundsAsync(ServerType server);
        void ClearBackground();
        void ClearCustomBackground();
        void ClearImageCaches();
    }

    public class BackgroundRenderer : IBackgroundRenderer
    {
        private static readonly HttpClient _httpClient;

        private readonly IDevBuildDetectionService _devBuildDetectionService;
        private readonly SemaphoreSlim _downloadLock = new(1, 1);

        private string _cacheFolderPath => Path.Combine(Helpers.AppPaths.CacheDir, "BackgroundCache");
        private BackgroundRenderResult _cachedBackground;
        private string _currentBackgroundUrl;
        private byte[] _videoBytes;
        private string _videoBytesUrl;
        private string _videoMimeType;
        private BackgroundRenderResult _cachedCustomBackground;
        private BackgroundRenderResult _cachedFallbackBackground;
        private string _customBackgroundPath;

        private bool AllowVideoBackground => _devBuildDetectionService.IsDevBuild;

        static BackgroundRenderer()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
        }

        public BackgroundRenderer(IDevBuildDetectionService devBuildDetectionService)
        {
            _devBuildDetectionService = devBuildDetectionService;
        }

        private static string GetApiLanguage()
        {
            var culture = ResourceExtensions.CurrentCulture;
            return string.IsNullOrEmpty(culture) ? "zh-cn" : culture.ToLowerInvariant();
        }

        public async Task<BackgroundRenderResult> GetBackgroundAsync(ServerType server, bool preferVideo)
        {
            try
            {
                preferVideo = preferVideo && AllowVideoBackground;

                var apiResponse = await TryRequestBackgroundApiAsync(server);
                var apiRequested = !string.IsNullOrEmpty(apiResponse);
                if (apiRequested)
                {
                    var freshResult = await LoadFromApiResponseAsync(server, apiResponse!, preferVideo);
                    if (freshResult != null)
                        return freshResult;
                }

                var selectedCachedResult = await TryLoadSelectedFromCacheAsync();
                if (selectedCachedResult != null)
                {
                    if (!apiRequested) ScheduleBackgroundRefresh(server, preferVideo);
                    return selectedCachedResult;
                }

                var cachedResult = await TryLoadFromDiskCacheAsync(server, preferVideo);
                if (cachedResult != null)
                {
                    if (!apiRequested) ScheduleBackgroundRefresh(server, preferVideo);
                    return cachedResult;
                }

                return await GetFallbackBackgroundAsync();
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer", $"BackgroundRenderer: GetBackgroundAsync failed - {ex.Message}");
                return await GetFallbackBackgroundAsync();
            }
        }

        private async Task<BackgroundRenderResult> TryLoadFromDiskCacheAsync(ServerType server, bool preferVideo)
        {
            var apiCachePath = GetApiCachePath(server);
            if (!File.Exists(apiCachePath))
                return null;

            try
            {
                var cachedJson = await File.ReadAllTextAsync(apiCachePath);
                foreach (var bgInfo in ParseBackgroundCandidates(cachedJson, preferVideo))
                {
                    var result = await LoadFromCacheOrNull(bgInfo.Url, bgInfo.IsVideo);
                    if (result != null) return result;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<BackgroundRenderResult> LoadFromCacheOrNull(string url, bool isVideo)
        {
            if (!isVideo && url == _currentBackgroundUrl && _cachedBackground is { IsVideo: false })
                return _cachedBackground;

            if (isVideo)
            {
                var cached = await LoadVideoFromMemoryOrNull(url);
                if (cached != null)
                    return cached;
            }

            string cachedFilePath = null;
            try
            {
                cachedFilePath = FindCachedFilePath(url, isVideo ? GetVideoExtension(url) : ".img");
                if (cachedFilePath == null) return null;

                BackgroundRenderResult result;
                if (isVideo)
                {
                    result = await LoadVideoIntoMemoryAsync(url, cachedFilePath);
                }
                else
                {
                    var bitmap = await BackgroundImageLoader.LoadFileAsync(cachedFilePath);
                    result = new BackgroundRenderResult { ImageSource = bitmap, IsVideo = false };
                }

                _cachedBackground = result;
                _currentBackgroundUrl = url;
                return result;
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Failed to load cached file ({cachedFilePath}): {ex.Message}");
                try
                {
                    File.Delete(cachedFilePath);
                }
                catch
                {
                }

                return null;
            }
        }

        private async Task<BackgroundRenderResult> LoadVideoFromMemoryOrNull(string url)
        {
            if (_videoBytes == null || !string.Equals(_videoBytesUrl, url, StringComparison.Ordinal))
                return null;

            try
            {
                var mediaSource = await CreateMediaSourceFromBytesAsync(_videoBytes, _videoMimeType);
                if (mediaSource == null)
                    return null;

                return new BackgroundRenderResult
                {
                    VideoSource = mediaSource,
                    VideoStream = null,
                    IsVideo = true
                };
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Failed to load in-memory video cache ({url}): {ex.Message}");
                ReleaseVideoCache();
                return null;
            }
        }

        private async Task<BackgroundRenderResult> LoadVideoIntoMemoryAsync(string url, string filePath)
        {
            try
            {
                var mimeType = GetMimeType(filePath);
                var bytes = await File.ReadAllBytesAsync(filePath);
                var mediaSource = await CreateMediaSourceFromBytesAsync(bytes, mimeType);
                if (mediaSource == null)
                    return null;

                _videoBytes = bytes;
                _videoBytesUrl = url;
                _videoMimeType = mimeType;

                return new BackgroundRenderResult { VideoSource = mediaSource, VideoStream = null, IsVideo = true };
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Failed to load video into memory ({filePath}): {ex.Message}");
                ReleaseVideoCache();
                return null;
            }
        }

        private static async Task<MediaSource> CreateMediaSourceFromBytesAsync(byte[] bytes, string contentType)
        {
            try
            {
                var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                return MediaSource.CreateFromStream(stream, contentType);
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Failed to create video source from memory: {ex.Message}");
                return null;
            }
        }

        private static string GetMimeType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext switch
            {
                ".webm" => "video/webm",
                ".mp4" => "video/mp4",
                ".mkv" => "video/x-matroska",
                ".avi" => "video/x-msvideo",
                ".mov" => "video/quicktime",
                _ => "video/mp4"
            };
        }

        private void ReleaseVideoCache()
        {
            _videoBytes = null;
            _videoBytesUrl = null;
            _videoMimeType = null;
        }

        private string? FindCachedFilePath(string url, string defaultExtension)
        {
            var predictedPath = Path.Combine(_cacheFolderPath, GetCacheFileName(url, defaultExtension));
            if (File.Exists(predictedPath))
                return predictedPath;

            try
            {
                if (Directory.Exists(_cacheFolderPath))
                {
                    var hash = ComputeUrlHash(url);
                    foreach (var file in Directory.GetFiles(_cacheFolderPath, hash + ".*"))
                    {
                        if (!file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                            return file;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private async Task<string> ResolveBackgroundApiUrlAsync(ServerType server)
        {
            var localSettings = App.GetService<ILocalSettingsService>();
            var customApiObj = await localSettings.ReadSettingAsync("CustomBackgroundApiUrl");
            var customApi = customApiObj?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(customApi) && Uri.TryCreate(customApi, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return customApi;
            }

            return server switch
            {
                ServerType.CN => ApiEndpoints.BackgroundCnApi,
                ServerType.OS => ApiEndpoints.BackgroundOsApi.Replace("language=zh-cn", $"language={GetApiLanguage()}"),
                _ => ApiEndpoints.BackgroundCnApi
            };
        }

        private async Task<string?> TryRequestBackgroundApiAsync(ServerType server)
        {
            try
            {
                var apiUrl = await ResolveBackgroundApiUrlAsync(server);
                return await _httpClient.GetStringAsync(apiUrl);
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Background API request failed - {ex.Message}");
                return null;
            }
        }

        private async Task<BackgroundRenderResult?> LoadFromApiResponseAsync(ServerType server, string response,
            bool preferVideo)
        {
            BackgroundRenderResult? result = null;
            try
            {
                await SaveApiCacheAsync(server, response);

                var localSettings = App.GetService<ILocalSettingsService>();
                var currentHash = ComputeMD5(response);
                var savedHashObj = await localSettings.ReadSettingAsync("BackgroundJsonHash");
                string savedHash = savedHashObj?.ToString();

                if (!string.IsNullOrEmpty(savedHash) && savedHash != currentHash)
                {
                    await localSettings.SaveSettingAsync("SelectedOnlineBackgroundUrl", "");
                    await localSettings.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
                }

                await localSettings.SaveSettingAsync("BackgroundJsonHash", currentHash);

                var selected = await GetSelectedOnlineBackgroundAsync();
                if (selected != null)
                    result = await GetSpecificOnlineBackgroundAsync(selected.Url, selected.IsVideo);

                if (result == null)
                {
                    var candidates = ParseBackgroundCandidates(response, preferVideo);
                    if (selected != null)
                    {
                        candidates = candidates
                            .Where(c => !string.Equals(c.Url, selected.Url, StringComparison.Ordinal))
                            .ToList();
                    }

                    foreach (var candidate in candidates)
                    {
                        result = await LoadFromCacheOrNull(candidate.Url, candidate.IsVideo);
                        if (result != null) break;
                    }

                    if (result == null)
                    {
                        foreach (var candidate in candidates)
                        {
                            result = await GetSpecificOnlineBackgroundAsync(candidate.Url, candidate.IsVideo);
                            if (result != null) break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Failed to process background API response - {ex.Message}");
            }

            QueueBackgroundPreload(response);
            return result;
        }

        private async Task<BackgroundUrlInfo?> GetSelectedOnlineBackgroundAsync()
        {
            var localSettings = App.GetService<ILocalSettingsService>();
            var specificUrlObj = await localSettings.ReadSettingAsync("SelectedOnlineBackgroundUrl");
            var specificUrl = specificUrlObj?.ToString();
            if (string.IsNullOrEmpty(specificUrl)) return null;

            var isVideoObj = await localSettings.ReadSettingAsync("SelectedOnlineBackgroundIsVideo");
            var isVideo = isVideoObj != null && Convert.ToBoolean(isVideoObj);
            if (isVideo && !AllowVideoBackground)
            {
                DebugLog.Info("BackgroundRenderer",
                    "BackgroundRenderer: Non-dev build, ignoring selected video background");
                return null;
            }

            return new BackgroundUrlInfo { Url = specificUrl, IsVideo = isVideo };
        }

        private async Task<BackgroundRenderResult?> TryLoadSelectedFromCacheAsync()
        {
            var selected = await GetSelectedOnlineBackgroundAsync();
            if (selected == null) return null;
            return await LoadFromCacheOrNull(selected.Url, selected.IsVideo);
        }

        private void QueueBackgroundPreload(string response)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await PreloadAllFromResponse(response);
                    CleanupStaleCacheFiles(response);
                }
                catch
                {
                }
            });
        }

        private void ScheduleBackgroundRefresh(ServerType server, bool preferVideo)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var apiUrl = await ResolveBackgroundApiUrlAsync(server);

                    var response = await _httpClient.GetStringAsync(apiUrl);
                    var newHash = ComputeMD5(response);

                    var apiCachePath = GetApiCachePath(server);
                    string oldHash = null;
                    if (File.Exists(apiCachePath))
                    {
                        try
                        {
                            var oldJson = await File.ReadAllTextAsync(apiCachePath);
                            oldHash = ComputeMD5(oldJson);
                        }
                        catch
                        {
                        }
                    }

                    await SaveApiCacheAsync(server, response);

                    var localSettings = App.GetService<ILocalSettingsService>();
                    var savedHashObj = await localSettings.ReadSettingAsync("BackgroundJsonHash");
                    string savedHash = savedHashObj?.ToString();

                    if (!string.IsNullOrEmpty(savedHash) && savedHash != newHash)
                    {
                        await localSettings.SaveSettingAsync("SelectedOnlineBackgroundUrl", "");
                        await localSettings.SaveSettingAsync("SelectedOnlineBackgroundIsVideo", false);
                    }

                    await localSettings.SaveSettingAsync("BackgroundJsonHash", newHash);

                    bool dataChanged = oldHash != null && oldHash != newHash;

                    var bgInfo = ParseTargetBackground(response, preferVideo);
                    if (bgInfo != null)
                    {
                        await DownloadToCache(bgInfo.Url, bgInfo.IsVideo ? GetVideoExtension(bgInfo.Url) : ".img");
                    }

                    await PreloadAllFromResponse(response);
                    CleanupStaleCacheFiles(response);

                    if (dataChanged)
                    {
                        _cachedBackground = null;
                        _currentBackgroundUrl = null;
                        WeakReferenceMessenger.Default.Send(new BackgroundRefreshMessage());
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Info("BackgroundRenderer",
                        $"BackgroundRenderer: Background refresh failed - {ex.Message}");
                }
            });
        }

        private BackgroundUrlInfo ParseTargetBackground(string apiResponse, bool preferVideo)
            => ParseBackgroundCandidates(apiResponse, preferVideo).FirstOrDefault();

        private List<BackgroundUrlInfo> ParseBackgroundCandidates(string apiResponse, bool preferVideo)
        {
            try
            {
                preferVideo = preferVideo && AllowVideoBackground;

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                var result = JsonSerializer.Deserialize<HoyoverseBackgroundResponse>(apiResponse, options);
                if (result?.Retcode != 0 || result.Data?.GameInfoList == null || result.Data.GameInfoList.Length == 0)
                    return new();

                var backgrounds = result.Data.GameInfoList[0].Backgrounds;
                if (backgrounds == null || backgrounds.Length == 0)
                    return new();

                var candidates = new List<BackgroundUrlInfo>();
                if (preferVideo)
                {
                    candidates.AddRange(backgrounds
                        .Where(b => b != null && b.Type == "BACKGROUND_TYPE_VIDEO" &&
                                    !string.IsNullOrEmpty(b.Video?.Url))
                        .Select(b => new BackgroundUrlInfo { Url = b.Video.Url, IsVideo = true }));
                }

                candidates.AddRange(backgrounds
                    .Where(b => b != null && !string.IsNullOrEmpty(b.Background?.Url))
                    .Select(b => new BackgroundUrlInfo { Url = b.Background.Url, IsVideo = false }));
                return candidates.DistinctBy(b => b.Url).ToList();
            }
            catch
            {
                return new();
            }
        }

        private List<string> ParseAllUrls(string apiResponse, bool includeVideos = true)
        {
            var urls = new List<string>();
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };
                var result = JsonSerializer.Deserialize<HoyoverseBackgroundResponse>(apiResponse, options);
                if (result?.Retcode != 0 || result.Data?.GameInfoList == null || result.Data.GameInfoList.Length == 0)
                    return urls;

                var backgrounds = result.Data.GameInfoList[0].Backgrounds;
                if (backgrounds == null)
                    return urls;

                foreach (var b in backgrounds)
                {
                    if (includeVideos && b.Type == "BACKGROUND_TYPE_VIDEO" && !string.IsNullOrEmpty(b.Video?.Url))
                        urls.Add(b.Video.Url);
                    if (!string.IsNullOrEmpty(b.Background?.Url))
                        urls.Add(b.Background.Url);
                }
            }
            catch
            {
            }

            return urls;
        }

        private async Task PreloadAllFromResponse(string apiResponse)
        {
            var allUrls = ParseAllUrls(apiResponse, includeVideos: AllowVideoBackground);
            foreach (var url in allUrls)
            {
                try
                {
                    var urlExt = GetUrlPathExtension(url);
                    var ext = urlExt != null && VideoExtensions.Contains(urlExt) ? urlExt : ".img";
                    await DownloadToCache(url, ext);
                }
                catch (Exception ex)
                {
                    DebugLog.Info("BackgroundRenderer", $"BackgroundRenderer: Precache failed ({url}): {ex.Message}");
                }
            }
        }

        private void CleanupStaleCacheFiles(string apiResponse)
        {
            try
            {
                if (!Directory.Exists(_cacheFolderPath))
                    return;

                var validHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var allUrls = ParseAllUrls(apiResponse, includeVideos: AllowVideoBackground);
                if (allUrls.Count == 0) return;
                foreach (var url in allUrls)
                {
                    validHashes.Add(ComputeUrlHash(url));
                }

                foreach (var file in Directory.GetFiles(_cacheFolderPath))
                {
                    var name = Path.GetFileName(file);
                    if (name is "api_cn.json" or "api_os.json")
                        continue;
                    if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!validHashes.Contains(Path.GetFileNameWithoutExtension(name)))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public async Task<BackgroundRenderResult> GetSpecificOnlineBackgroundAsync(string url, bool isVideo)
        {
            if (isVideo && !AllowVideoBackground)
            {
                DebugLog.Info("BackgroundRenderer",
                    "BackgroundRenderer: Non-dev build, refusing to load specified video background");
                return null;
            }

            try
            {
                var cached = await LoadFromCacheOrNull(url, isVideo);
                if (cached != null) return cached;

                var ext = isVideo ? GetVideoExtension(url) : ".img";
                await DownloadToCache(url, ext);
                var result = await LoadFromCacheOrNull(url, isVideo);
                return result;
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Failed to load specified background - {ex.Message}");
                return null;
            }
        }

        public async Task<BackgroundRenderResult> GetCustomBackgroundAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            if (_cachedCustomBackground != null && filePath == _customBackgroundPath)
                return _cachedCustomBackground;

            try
            {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                var isVideo = extension is ".mp4" or ".webm" or ".mkv" or ".avi" or ".mov";

                if (isVideo && !AllowVideoBackground)
                {
                    DebugLog.Info("BackgroundRenderer",
                        $"BackgroundRenderer: Non-dev build, custom video background not allowed: {filePath}");
                    return null;
                }

                BackgroundRenderResult result;
                if (isVideo)
                {
                    var videoSource = MediaSource.CreateFromUri(new Uri(filePath));
                    result = new BackgroundRenderResult { VideoSource = videoSource, IsVideo = true };
                }
                else
                {
                    var bitmap = await BackgroundImageLoader.LoadFileAsync(filePath);
                    result = new BackgroundRenderResult { ImageSource = bitmap, IsVideo = false };
                }

                _cachedCustomBackground = result;
                _customBackgroundPath = filePath;
                return result;
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Custom background load failed - {ex.Message}");
                return null;
            }
        }

        public async Task PreloadImageBackgroundsAsync(IEnumerable<string> imageUrls)
        {
            foreach (var url in imageUrls)
            {
                try
                {
                    await DownloadToCache(url, ".img");
                }
                catch (Exception ex)
                {
                    DebugLog.Info("BackgroundRenderer", $"BackgroundRenderer: Preload failed ({url}): {ex.Message}");
                }
            }
        }

        public async Task CacheAllBackgroundsAsync(ServerType server)
        {
            try
            {
                var apiCachePath = GetApiCachePath(server);
                if (!File.Exists(apiCachePath))
                    return;

                var json = await File.ReadAllTextAsync(apiCachePath);
                await PreloadAllFromResponse(json);
                CleanupStaleCacheFiles(json);
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: CacheAllBackgroundsAsync failed - {ex.Message}");
            }
        }

        public void ClearBackground()
        {
            if (Directory.Exists(_cacheFolderPath))
            {
                try
                {
                    foreach (var file in Directory.GetFiles(_cacheFolderPath))
                        File.Delete(file);
                }
                catch
                {
                }
            }

            _cachedBackground = null;
            _currentBackgroundUrl = null;
            ReleaseVideoCache();
        }

        public void ClearCustomBackground()
        {
            _customBackgroundPath = null;
            _cachedCustomBackground = null;
            ReleaseVideoCache();
        }

        public void ClearImageCaches()
        {
            _cachedBackground = null;
            _currentBackgroundUrl = null;
            _cachedCustomBackground = null;
            _customBackgroundPath = null;
            _cachedFallbackBackground = null;
        }

        private async Task DownloadToCache(string url, string defaultExtension)
        {
            await _downloadLock.WaitAsync();
            try
            {
                await DownloadToCacheCoreAsync(url, defaultExtension);
            }
            finally
            {
                _downloadLock.Release();
            }
        }

        private async Task DownloadToCacheCoreAsync(string url, string defaultExtension)
        {
            var urlExt = GetUrlPathExtension(url);
            var isVideoDownload = (urlExt != null && VideoExtensions.Contains(urlExt)) ||
                                  (urlExt == null && VideoExtensions.Contains(defaultExtension));
            if (isVideoDownload && !AllowVideoBackground)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Non-dev build, skipping video background download: {url}");
                return;
            }

            var fileName = GetCacheFileName(url, defaultExtension);
            var cachedFilePath = Path.Combine(_cacheFolderPath, fileName);

            if (File.Exists(cachedFilePath) && new FileInfo(cachedFilePath).Length > 0)
                return;

            Directory.CreateDirectory(_cacheFolderPath);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response =
                await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();

            if (GetUrlPathExtension(url) == null)
            {
                var detected =
                    GetExtensionFromContentType(response.Content.Headers.ContentType?.MediaType, defaultExtension);
                if (!string.Equals(detected, defaultExtension, StringComparison.OrdinalIgnoreCase))
                {
                    fileName = GetCacheFileName(url, detected);
                    cachedFilePath = Path.Combine(_cacheFolderPath, fileName);
                    if (File.Exists(cachedFilePath) && new FileInfo(cachedFilePath).Length > 0)
                        return;
                }
            }

            var tempFile = Path.Combine(_cacheFolderPath, $"{fileName}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using var contentStream = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using (var fileStream = new FileStream(tempFile, FileMode.CreateNew, FileAccess.Write))
                {
                    await contentStream.CopyToAsync(fileStream, timeout.Token);
                    if (fileStream.Length == 0) throw new InvalidDataException("Background_EmptyDownload".GetLocalized());
                }

                File.Move(tempFile, cachedFilePath, true);
            }
            finally
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (Exception ex)
                {
                    DebugLog.Info("BackgroundRenderer",
                        $"BackgroundRenderer: Failed to clean temporary cache: {ex.Message}");
                }
            }
        }

        private static string GetExtensionFromContentType(string? mediaType, string defaultExtension)
        {
            if (string.IsNullOrEmpty(mediaType))
                return defaultExtension;

            var mime = mediaType.ToLowerInvariant();
            if (mime.Contains("webm")) return ".webm";
            if (mime.Contains("mp4")) return ".mp4";
            if (mime.Contains("matroska")) return ".mkv";
            if (mime.Contains("quicktime")) return ".mov";
            if (mime.StartsWith("image/")) return ".img";
            return defaultExtension;
        }

        private string GetApiCachePath(ServerType server)
        {
            var name = server == ServerType.OS ? "api_os.json" : "api_cn.json";
            return Path.Combine(_cacheFolderPath, name);
        }

        private async Task SaveApiCacheAsync(ServerType server, string json)
        {
            try
            {
                Directory.CreateDirectory(_cacheFolderPath);
                await File.WriteAllTextAsync(GetApiCachePath(server), json);
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer",
                    $"BackgroundRenderer: Failed to write background API cache: {ex.Message}");
            }
        }

        private static readonly string[] VideoExtensions = { ".webm", ".mp4", ".mkv", ".avi", ".mov" };

        private static string ComputeUrlHash(string url)
        {
            var bytes = Encoding.UTF8.GetBytes(url);
            var hash = MD5.HashData(bytes);
            return Convert.ToHexString(hash).ToLower();
        }

        private static string? GetUrlPathExtension(string url)
        {
            try
            {
                var ext = Path.GetExtension(new Uri(url).AbsolutePath)?.ToLowerInvariant();
                return string.IsNullOrEmpty(ext) ? null : ext;
            }
            catch
            {
                return null;
            }
        }

        private static string GetVideoExtension(string url)
        {
            var ext = GetUrlPathExtension(url);
            return ext != null && VideoExtensions.Contains(ext) ? ext : ".mp4";
        }

        private string GetCacheFileName(string url, string defaultExtension = ".mp4")
        {
            var extension = GetUrlPathExtension(url) ?? defaultExtension;
            return ComputeUrlHash(url) + extension;
        }

        private static string ComputeMD5(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = MD5.HashData(bytes);
            return Convert.ToHexString(hash).ToLower();
        }

        public async Task<BackgroundRenderResult> GetFallbackBackgroundAsync()
        {
            if (_cachedFallbackBackground != null) return _cachedFallbackBackground;
            try
            {
                var bitmap = await BackgroundImageLoader.LoadFallbackAsync();
                return _cachedFallbackBackground = new BackgroundRenderResult
                    { ImageSource = bitmap, IsVideo = false, IsFallback = true };
            }
            catch (Exception ex)
            {
                DebugLog.Info("BackgroundRenderer", $"BackgroundRenderer: Default background load failed - {ex}");
                return null;
            }
        }
    }
}