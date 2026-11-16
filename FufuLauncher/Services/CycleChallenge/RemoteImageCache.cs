/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Hashing;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.CycleChallenge;

public static class RemoteImageCache
{
    public static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(90);


    public const int MaxCachedImageCount = 500;


    private const int MaxRetryCount = 3;


    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(60);


    private const int CacheKeyByteCount = 24;


    private static readonly ConcurrentDictionary<string, Lazy<Task<string?>>> InFlight = new(StringComparer.Ordinal);


    private static readonly HttpClient HttpClient = CreateHttpClient();


    public static string CacheFolder => Path.Combine(AppPaths.DataDir, "cyclechallenge", "images");

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(15),

            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
        };

        return new HttpClient(handler)
        {
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,

            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
    }


    public static async Task<string?> GetAsync(Uri? uri, CancellationToken cancellationToken = default)
    {
        try
        {
            if (uri is null || !uri.IsAbsoluteUri)
            {
                return null;
            }

            if (!IsHttpUri(uri))
            {
                return null;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            string key = uri.ToString();
            Lazy<Task<string?>> lazy = InFlight.GetOrAdd(
                key,
                _ => new Lazy<Task<string?>>(
                    () => StartDownload(key, uri),
                    System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));
            Task<string?> shared = lazy.Value;

            try
            {
                return await shared.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                DebugLog.Info("RemoteImageCache",
                    $"[RemoteImageCache] Failed to wait for download task {uri}: {ex.Message}");
                return null;
            }
            finally
            {
                RemoveEntry(key, shared);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("RemoteImageCache", $"[RemoteImageCache] GetAsync error: {ex.Message}");
            return null;
        }
    }


    public static void Invalidate(Uri? uri)
    {
        try
        {
            if (uri is null || !uri.IsAbsoluteUri)
            {
                return;
            }

            string path = Path.Combine(CacheFolder, GetCacheFileName(uri));
            DeleteFileIfExists(path);
            DeleteFileIfExists(path + ".tmp");


            string key = uri.ToString();
            if (InFlight.TryGetValue(key, out Lazy<Task<string?>>? lazy)
                && lazy.IsValueCreated
                && lazy.Value.IsCompleted)
            {
                InFlight.TryRemove(key, out _);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("RemoteImageCache", $"[RemoteImageCache] Invalidate error: {ex.Message}");
        }
    }


    public static async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await Task.Run(() => CleanupCore(cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info("RemoteImageCache", $"[RemoteImageCache] Cleanup error: {ex.Message}");
        }
    }

    #region 下载

    private static Task<string?> StartDownload(string key, Uri uri)
    {
        Task<string?> task = DownloadWithRetryAsync(uri);
        _ = task.ContinueWith(
            completed => RemoveEntry(key, completed),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }


    private static void RemoveEntry(string key, Task<string?> task)
    {
        try
        {
            if (InFlight.TryGetValue(key, out Lazy<Task<string?>>? lazy)
                && lazy.IsValueCreated
                && ReferenceEquals(lazy.Value, task))
            {
                InFlight.TryRemove(key, out _);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("RemoteImageCache", $"[RemoteImageCache] Failed to remove single-flight task: {ex.Message}");
        }
    }

    private static async Task<string?> DownloadWithRetryAsync(Uri uri)
    {
        string folder = CacheFolder;
        string filePath = Path.Combine(folder, GetCacheFileName(uri));

        try
        {
            Directory.CreateDirectory(folder);

            if (IsUsableCacheFile(filePath))
            {
                return filePath;
            }


            DeleteFileIfExists(filePath);

            Exception? lastError = null;
            for (int attempt = 0; attempt <= MaxRetryCount; attempt++)
            {
                if (IsUsableCacheFile(filePath))
                {
                    return filePath;
                }

                try
                {
                    await DownloadFileAsync(uri, filePath + ".tmp", filePath).ConfigureAwait(false);

                    if (IsUsableCacheFile(filePath))
                    {
                        return filePath;
                    }

                    throw new IOException("Cache_EmptyDownloadedFile".GetLocalized());
                }
                catch (Exception ex) when (ex is HttpRequestException
                                               or IOException
                                               or TaskCanceledException
                                               or OperationCanceledException
                                               or UnauthorizedAccessException)
                {
                    lastError = ex;
                    DebugLog.Info("RemoteImageCache",
                        $"[RemoteImageCache] Download failed ({attempt + 1}/{MaxRetryCount + 1}) {uri}: {ex.Message}");

                    if (attempt < MaxRetryCount)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1))).ConfigureAwait(false);
                        }
                        catch (Exception delayEx)
                        {
                            DebugLog.Info("RemoteImageCache",
                                $"[RemoteImageCache] Retry wait error: {delayEx.Message}");
                            break;
                        }
                    }
                }
            }

            DebugLog.Info("RemoteImageCache", $"[RemoteImageCache] Giving up download {uri}: {lastError?.Message}");
            return null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("RemoteImageCache", $"[RemoteImageCache] Download pipeline error {uri}: {ex.Message}");
            return null;
        }
    }


    private static async Task DownloadFileAsync(Uri uri, string tmpPath, string finalPath)
    {
        using var timeoutCts = new CancellationTokenSource(DownloadTimeout);
        CancellationToken token = timeoutCts.Token;

        using (FileStream fs = File.Open(tmpPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (fs.Length > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(fs.Length, null);
            }

            using HttpResponseMessage response = await HttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentRange is { From: > 0 } range)
            {
                fs.Position = range.From.Value;
            }
            else
            {
                fs.Position = 0;
                fs.SetLength(0);
            }

            using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await body.CopyToAsync(fs, token).ConfigureAwait(false);
            await fs.FlushAsync(token).ConfigureAwait(false);
        }


        File.Move(tmpPath, finalPath, true);
    }

    #endregion

    #region 缓存文件

    private static bool IsUsableCacheFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists
                   && info.Length > 0
                   && DateTime.UtcNow - info.LastWriteTimeUtc <= CacheMaxAge;
        }
        catch (Exception ex)
        {
            DebugLog.Info("RemoteImageCache", $"[RemoteImageCache] Failed to check cache file {path}: {ex.Message}");
            return false;
        }
    }


    private static string GetCacheFileName(Uri uri)
    {
        string url = uri.ToString();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(url.Length));
        try
        {
            int length = Encoding.UTF8.GetBytes(url, buffer);
            ReadOnlySpan<byte> urlBytes = buffer.AsSpan(0, length);

            Span<byte> hashBytes = stackalloc byte[CacheKeyByteCount];
            XxHash64.Hash(urlBytes, hashBytes[..8]);
            MD5.HashData(urlBytes, hashBytes[8..]);
            return Convert.ToHexString(hashBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("RemoteImageCache", $"[RemoteImageCache] Failed to delete cache file {path}: {ex.Message}");
        }
    }

    private static void CleanupCore(CancellationToken cancellationToken)
    {
        string folder = CacheFolder;
        if (!Directory.Exists(folder))
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        DateTime staleTmpBefore = now - TimeSpan.FromDays(1);
        List<FileInfo> cachedFiles = new();

        foreach (string file in Directory.EnumerateFiles(folder))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var info = new FileInfo(file);
                if (!info.Exists)
                {
                    continue;
                }

                if (string.Equals(info.Extension, ".tmp", StringComparison.OrdinalIgnoreCase))
                {
                    if (info.LastWriteTimeUtc < staleTmpBefore)
                    {
                        DeleteFileIfExists(file);
                    }

                    continue;
                }

                if (now - info.LastWriteTimeUtc > CacheMaxAge)
                {
                    DeleteFileIfExists(file);
                }
                else
                {
                    cachedFiles.Add(info);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("RemoteImageCache", $"[RemoteImageCache] Failed to clean file {file}: {ex.Message}");
            }
        }

        if (cachedFiles.Count <= MaxCachedImageCount)
        {
            return;
        }

        int removeCount = cachedFiles.Count - MaxCachedImageCount;
        foreach (FileInfo info in cachedFiles.OrderBy(static f => f.LastWriteTimeUtc).Take(removeCount))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            DeleteFileIfExists(info.FullName);
        }
    }

    private static bool IsHttpUri(Uri uri)
    {
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}