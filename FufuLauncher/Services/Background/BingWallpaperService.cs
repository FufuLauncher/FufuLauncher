/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Background;

public class BingWallpaperItem
{
    public string Url
    {
        get;
        init;
    } = string.Empty;

    public string ThumbnailUrl
    {
        get;
        init;
    } = string.Empty;

    public string Copyright
    {
        get;
        init;
    } = string.Empty;

    public string Title
    {
        get;
        init;
    } = string.Empty;

    public string StartDate
    {
        get;
        init;
    } = string.Empty;

    public string EndDate
    {
        get;
        init;
    } = string.Empty;

    public string Hash
    {
        get;
        init;
    } = string.Empty;

    public string Market
    {
        get;
        init;
    } = string.Empty;
}

public sealed class BingWallpaperLoadResult
{
    public BingWallpaperItem Wallpaper
    {
        get;
        init;
    } = new();

    public BackgroundRenderResult Result
    {
        get;
        init;
    } = new();
}

public class BingWallpaperService
{
    private const string ApiHost = ApiEndpoints.BingBase;
    private const string HistoricalApiUrl = ApiEndpoints.BingWallpaperHistoricalRandomUrl;
    private const int ArchiveCount = 8;
    private const int MaxDownloadBytes = 30 * 1024 * 1024;

    private static string CacheDirectory => Path.Combine(AppPaths.CacheDir, "BingBackground");
    private static string CacheImagePath => Path.Combine(CacheDirectory, "next.img");
    private static string CacheMetadataPath => Path.Combine(CacheDirectory, "next.json");

    private static readonly HttpClient HttpClient;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private static readonly HashSet<string> SupportedMarkets = new(StringComparer.OrdinalIgnoreCase)
    {
        "zh-CN", "zh-TW", "en-US", "en-GB", "ja-JP", "ko-KR", "de-DE", "fr-FR",
        "es-ES", "es-MX", "pt-BR", "ru-RU", "it-IT", "id-ID", "hi-IN"
    };

    private readonly object _cacheLock = new();
    private IReadOnlyList<BingWallpaperItem> _cachedWallpapers = Array.Empty<BingWallpaperItem>();
    private DateTime _cachedAtUtc = DateTime.MinValue;
    private string _cachedMarket = string.Empty;

    static BingWallpaperService()
    {
        HttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/145.0.0.0 Safari/537.36");
    }

    public async Task<IReadOnlyList<BingWallpaperItem>> GetWallpapersAsync(bool forceRefresh = false)
    {
        var market = ResolveMarket();

        if (!forceRefresh)
        {
            lock (_cacheLock)
            {
                if (_cachedWallpapers.Count > 0 &&
                    string.Equals(_cachedMarket, market, StringComparison.OrdinalIgnoreCase) &&
                    DateTime.UtcNow - _cachedAtUtc < CacheDuration)
                {
                    return _cachedWallpapers;
                }
            }
        }

        try
        {
            var items = await DownloadWallpapersAsync(market);
            if (items.Count > 0)
            {
                lock (_cacheLock)
                {
                    _cachedWallpapers = items;
                    _cachedMarket = market;
                    _cachedAtUtc = DateTime.UtcNow;
                }

                return items;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("BingWallpaperService", $"[BingWallpaper] Failed to load Bing wallpaper: {ex.Message}");
        }

        lock (_cacheLock)
        {
            return _cachedWallpapers;
        }
    }

    public async Task<BingWallpaperItem?> GetRandomWallpaperAsync(
        string? excludedUrl = null,
        int year = 0,
        bool officialOnly = false)
    {
        if (!officialOnly)
        {
            var historical = await GetHistoricalRandomWallpaperAsync(year, excludedUrl);
            if (historical != null) return historical;
        }

        var wallpapers = await GetWallpapersAsync();
        if (wallpapers.Count == 0) return null;

        IReadOnlyList<BingWallpaperItem> candidates = wallpapers;
        if (!string.IsNullOrWhiteSpace(excludedUrl))
        {
            var filtered = wallpapers
                .Where(item => !string.Equals(item.Url, excludedUrl, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (filtered.Count > 0)
                candidates = filtered;
        }

        return candidates[Random.Shared.Next(candidates.Count)];
    }

    public async Task<BingWallpaperLoadResult?> LoadCachedAsync()
    {
        try
        {
            if (!File.Exists(CacheImagePath) || !File.Exists(CacheMetadataPath))
                return null;

            var json = await File.ReadAllTextAsync(CacheMetadataPath);
            var wallpaper = JsonSerializer.Deserialize<BingWallpaperItem>(json);
            if (wallpaper == null || string.IsNullOrWhiteSpace(wallpaper.Url))
                return null;

            var image = await BackgroundImageLoader.LoadFileAsync(CacheImagePath);
            return new BingWallpaperLoadResult
            {
                Wallpaper = wallpaper,
                Result = new BackgroundRenderResult { ImageSource = image, IsVideo = false }
            };
        }
        catch (Exception ex)
        {
            DebugLog.Info("BingWallpaperService", $"[BingWallpaper] Failed to load cache: {ex.Message}");
            TryDelete(CacheImagePath);
            TryDelete(CacheMetadataPath);
            return null;
        }
    }

    public async Task<BingWallpaperLoadResult?> FetchAndCacheAsync(
        string? excludedUrl = null,
        int year = 0,
        bool officialOnly = false)
    {
        try
        {
            var wallpapers = await GetWallpapersAsync();
            var historicalItems = officialOnly
                ? Array.Empty<BingWallpaperItem?>()
                : await Task.WhenAll(Enumerable.Range(0, 2)
                    .Select(_ => GetHistoricalRandomWallpaperAsync(year, excludedUrl)));
            var historicalCandidates = historicalItems
                .Where(item => item != null)
                .Select(item => item!)
                .ToList();

            var allCandidates = wallpapers
                .Concat(historicalCandidates)
                .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (allCandidates.Count == 0) return null;

            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(excludedUrl)) excluded.Add(excludedUrl);
            TryAddCachedUrl(excluded);

            var selected = new List<BingWallpaperItem>();
            foreach (var item in historicalCandidates)
            {
                if (selected.Count >= 2) break;
                if (excluded.Contains(item.Url)) continue;
                if (selected.Any(existing =>
                        string.Equals(existing.Url, item.Url, StringComparison.OrdinalIgnoreCase)))
                    continue;
                selected.Add(item);
            }

            foreach (var item in allCandidates.OrderBy(_ => Random.Shared.Next()))
            {
                if (selected.Count >= 2) break;
                if (excluded.Contains(item.Url)) continue;
                if (selected.Any(existing =>
                        string.Equals(existing.Url, item.Url, StringComparison.OrdinalIgnoreCase)))
                    continue;
                selected.Add(item);
            }

            if (selected.Count == 0)
                selected = allCandidates.OrderBy(_ => Random.Shared.Next()).Take(2).ToList();
            var downloads = await Task.WhenAll(selected.Select(async item =>
                (Item: item, Bytes: await DownloadImageAsync(item.Url))));

            var successful = downloads.Where(x => x.Bytes is { Length: > 0 }).ToList();
            if (successful.Count == 0) return null;

            var displayIndex = 0;
            var result = await CreateResultAsync(successful[0].Bytes!);
            if (result == null && successful.Count > 1)
            {
                displayIndex = 1;
                result = await CreateResultAsync(successful[1].Bytes!);
            }

            if (result == null) return null;

            if (displayIndex == 0 && successful.Count > 1)
                await SaveCacheAsync(successful[1].Bytes!, successful[1].Item);

            return new BingWallpaperLoadResult { Wallpaper = successful[displayIndex].Item, Result = result };
        }
        catch (Exception ex)
        {
            DebugLog.Info("BingWallpaperService", $"[BingWallpaper] Failed to fetch and cache wallpaper: {ex.Message}");
            return null;
        }
    }

    private static void TryAddCachedUrl(HashSet<string> excluded)
    {
        try
        {
            if (!File.Exists(CacheMetadataPath)) return;
            var wallpaper = JsonSerializer.Deserialize<BingWallpaperItem>(File.ReadAllText(CacheMetadataPath));
            if (!string.IsNullOrWhiteSpace(wallpaper?.Url)) excluded.Add(wallpaper.Url);
        }
        catch
        {
        }
    }

    private static async Task<byte[]?> DownloadImageAsync(string url)
    {
        try
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrEmpty(mediaType) ||
                !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length is > 0 and <= MaxDownloadBytes ? bytes : null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("BingWallpaperService", $"[BingWallpaper] Image download failed: {ex.Message}");
            return null;
        }
    }

    private static async Task<BackgroundRenderResult?> CreateResultAsync(byte[] bytes)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"bing_display_{Guid.NewGuid():N}.img");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes);
            var image = await BackgroundImageLoader.LoadFileAsync(temp);
            return new BackgroundRenderResult { ImageSource = image, IsVideo = false };
        }
        catch (Exception ex)
        {
            DebugLog.Info("BingWallpaperService", $"[BingWallpaper] Image decode failed: {ex.Message}");
            return null;
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task SaveCacheAsync(byte[] bytes, BingWallpaperItem wallpaper)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var imageTemp = Path.Combine(CacheDirectory, $"next_{Guid.NewGuid():N}.tmp");
            await File.WriteAllBytesAsync(imageTemp, bytes);
            File.Move(imageTemp, CacheImagePath, true);

            var metadataTemp = Path.Combine(CacheDirectory, $"next_{Guid.NewGuid():N}.json.tmp");
            await File.WriteAllTextAsync(metadataTemp, JsonSerializer.Serialize(wallpaper));
            File.Move(metadataTemp, CacheMetadataPath, true);
        }
        catch (Exception ex)
        {
            DebugLog.Info("BingWallpaperService", $"[BingWallpaper] Failed to write cache: {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
        }
    }

    private static async Task<BingWallpaperItem?> GetHistoricalRandomWallpaperAsync(
        int year,
        string? excludedUrl)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var requestUrl = BuildHistoricalApiUrl(year);
            if (string.IsNullOrEmpty(requestUrl))
                return null;

            try
            {
                var json = await HttpClient.GetStringAsync(requestUrl);
                var image = JsonSerializer.Deserialize<BingHistoricalImage>(json, JsonOptions);
                if (image == null)
                    return null;

                var historicalUrl = image.Url;
                if (string.IsNullOrWhiteSpace(historicalUrl))
                    return null;

                var fullUrl = historicalUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? historicalUrl
                    : historicalUrl.StartsWith("//", StringComparison.Ordinal)
                        ? "https:" + historicalUrl
                        : ApiHost + historicalUrl;

                if (!string.IsNullOrWhiteSpace(excludedUrl) &&
                    string.Equals(fullUrl, excludedUrl, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (year > 0 &&
                    !string.IsNullOrWhiteSpace(image.StartDate) &&
                    !image.StartDate.StartsWith(year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        StringComparison.Ordinal))
                    continue;

                var thumbnailUrl = fullUrl;
                if (!string.IsNullOrWhiteSpace(image.UrlBase))
                {
                    var thumbnailBase = image.UrlBase.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? image.UrlBase
                        : ApiHost + image.UrlBase;
                    thumbnailUrl = thumbnailBase + "_400x240.jpg";
                }

                return new BingWallpaperItem
                {
                    Url = fullUrl,
                    ThumbnailUrl = thumbnailUrl,
                    Copyright = image.Copyright?.Trim() ?? string.Empty,
                    Title = image.Title?.Trim() ?? string.Empty,
                    StartDate = image.StartDate ?? string.Empty,
                    EndDate = string.Empty,
                    Hash = image.UrlBase ?? string.Empty,
                    Market = "en-US"
                };
            }
            catch (Exception ex)
            {
                DebugLog.Info("BingWallpaperService",
                    $"[BingWallpaper] Failed to load historical wallpaper: {ex.Message}");
                return null;
            }
        }

        return null;
    }

    private static string? BuildHistoricalApiUrl(int year)
    {
        if (year <= 0) return HistoricalApiUrl;
        if (year < 2019 || year > DateTime.Today.Year) return null;

        var today = DateTime.Today;
        var start = new DateTime(year, 1, 1);
        var earliest = new DateTime(2019, 3, 9);
        if (start < earliest) start = earliest;

        var end = year == today.Year ? today : new DateTime(year, 12, 31);
        if (start > end) return null;

        var date = start.AddDays(Random.Shared.Next((end - start).Days + 1));
        var dateValue = date.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        return ApiEndpoints.GetBingWallpaperHistoricalDateUrl(dateValue);
    }

    private static async Task<IReadOnlyList<BingWallpaperItem>> DownloadWallpapersAsync(string market)
    {
        var url = ApiEndpoints.GetBingWallpaperArchiveUrl(ArchiveCount, market);
        var json = await HttpClient.GetStringAsync(url);
        var response = JsonSerializer.Deserialize<BingArchiveResponse>(json, JsonOptions);

        if (response?.Images is not { Count: > 0 })
            return Array.Empty<BingWallpaperItem>();

        var items = new List<BingWallpaperItem>(response.Images.Count);
        foreach (var image in response.Images)
        {
            if (string.IsNullOrWhiteSpace(image.Url)) continue;

            var fullUrl = image.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? image.Url
                : ApiHost + image.Url;

            var thumbnailUrl = !string.IsNullOrWhiteSpace(image.UrlBase)
                ? ApiHost + image.UrlBase + "_400x240.jpg"
                : fullUrl;

            items.Add(new BingWallpaperItem
            {
                Url = fullUrl,
                ThumbnailUrl = thumbnailUrl,
                Copyright = image.Copyright?.Trim() ?? string.Empty,
                Title = image.Title?.Trim() ?? string.Empty,
                StartDate = image.StartDate ?? string.Empty,
                EndDate = image.EndDate ?? string.Empty,
                Hash = image.Hsh ?? string.Empty,
                Market = market
            });
        }

        return items;
    }

    private static string ResolveMarket()
    {
        var culture = ResourceExtensions.CurrentCulture;
        if (string.IsNullOrWhiteSpace(culture))
            culture = Thread.CurrentThread.CurrentUICulture.Name;

        if (SupportedMarkets.Contains(culture))
            return culture;

        foreach (var supported in SupportedMarkets)
        {
            if (culture.StartsWith(supported[..2], StringComparison.OrdinalIgnoreCase))
                return supported;
        }

        return "en-US";
    }

    private sealed class BingArchiveResponse
    {
        public List<BingImage>? Images
        {
            get;
            set;
        }
    }

    private sealed class BingHistoricalImage
    {
        public string? StartDate
        {
            get;
            set;
        }

        public string? Copyright
        {
            get;
            set;
        }

        public string? Title
        {
            get;
            set;
        }

        public string? UrlBase
        {
            get;
            set;
        }

        public string? Url
        {
            get;
            set;
        }
    }

    private sealed class BingImage
    {
        public string? Url
        {
            get;
            set;
        }

        public string? UrlBase
        {
            get;
            set;
        }

        public string? Copyright
        {
            get;
            set;
        }

        public string? Title
        {
            get;
            set;
        }

        public string? StartDate
        {
            get;
            set;
        }

        public string? EndDate
        {
            get;
            set;
        }

        public string? Hsh
        {
            get;
            set;
        }
    }
}