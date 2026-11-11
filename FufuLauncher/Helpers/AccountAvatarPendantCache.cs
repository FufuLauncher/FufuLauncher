/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace FufuLauncher.Helpers;

public static class AccountAvatarPendantCache
{
    private const int MinImageBytes = 32;
    private const int DownloadAttempts = 3;
    private const int MaxCacheAgeDays = 30;
    private const string CacheFolderName = "AccountPendants";
    private const string TempSuffix = ".tmp";

    private static readonly TimeSpan FailureCooldown = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan TempFileMaxAge = TimeSpan.FromHours(1);

    private static readonly byte[] PngSignature =
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    private static readonly byte[] PngEndMark =
        new byte[] { 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82 };

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(4, 4);
    private static readonly ConcurrentDictionary<string, Task<string?>> Pending = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, DateTime> Failures = new(StringComparer.Ordinal);

    public static string CacheDirectory => Path.Combine(AppPaths.CacheDir, CacheFolderName);

    public static bool TryGetCachedFile(string? url, out string path)
    {
        path = string.Empty;
        if (!TryResolveUri(url, out var uri)) return false;

        var candidate = ResolveCachePath(uri);
        if (!File.Exists(candidate)) return false;

        path = candidate;
        return true;
    }

    public static Task<string?> EnsureCachedAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (!TryResolveUri(url, out var uri)) return Task.FromResult<string?>(null);

        var task = Start(uri.AbsoluteUri, forceRefresh: false);
        return cancellationToken.CanBeCanceled ? task.WaitAsync(cancellationToken) : task;
    }

    public static Task<bool> RefreshAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (!TryResolveUri(url, out var uri)) return Task.FromResult(false);

        var result = HasFileAsync(Start(uri.AbsoluteUri, forceRefresh: true));
        return cancellationToken.CanBeCanceled ? result.WaitAsync(cancellationToken) : result;
    }

    public static bool Invalidate(string? url)
    {
        if (!TryResolveUri(url, out var uri)) return false;
        return TryDelete(ResolveCachePath(uri));
    }

    public static async Task PruneAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(CacheDirectory)) return;

            var expireBefore = DateTime.UtcNow.AddDays(-MaxCacheAgeDays);
            foreach (var file in Directory.EnumerateFiles(CacheDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (cancellationToken.IsCancellationRequested) return;

                try
                {
                    var info = new FileInfo(file);
                    if (!info.Exists) continue;

                    if (info.LastWriteTimeUtc < expireBefore || info.Length < MinImageBytes)
                    {
                        info.Delete();
                        continue;
                    }

                    if (info.Name.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase) &&
                        info.LastWriteTimeUtc < DateTime.UtcNow - TempFileMaxAge)
                    {
                        info.Delete();
                        continue;
                    }

                    var bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
                    if (IsValidPayload(bytes)) continue;

                    info.Delete();
                    DebugLog.Warn("AccountAvatarPendantCache",
                        $"[PendantCache] Removed corrupted cache file: {info.Name}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    DebugLog.Info("AccountAvatarPendantCache", $"[PendantCache] Prune skipped {file}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountAvatarPendantCache", $"[PendantCache] Prune failed: {ex.Message}");
        }
    }

    private static Task<string?> Start(string url, bool forceRefresh)
    {
        var key = (forceRefresh ? "refresh:" : "ensure:") + url;
        if (Pending.TryGetValue(key, out var running)) return running;

        var task = RunAsync(url, forceRefresh);
        Pending[key] = task;
        _ = task.ContinueWith(t => Pending.TryRemove(key, out _), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return task;
    }

    private static async Task<bool> HasFileAsync(Task<string?> task) =>
        !string.IsNullOrEmpty(await task.ConfigureAwait(false));

    private static async Task<string?> RunAsync(string url, bool forceRefresh)
    {
        try
        {
            var uri = new Uri(url, UriKind.Absolute);
            var path = ResolveCachePath(uri);

            var exists = File.Exists(path);
            var existingValid = exists && IsValidPayload(await File.ReadAllBytesAsync(path).ConfigureAwait(false));

            if (exists && !existingValid)
            {
                TryDelete(path);
                DebugLog.Warn("AccountAvatarPendantCache",
                    $"[PendantCache] Removed corrupted cache file: {Path.GetFileName(path)}");
            }

            if (existingValid && !forceRefresh) return path;
            if (!forceRefresh && IsInFailureCooldown(url)) return null;

            var bytes = await DownloadAsync(url).ConfigureAwait(false);
            if (bytes != null && IsValidPayload(bytes))
            {
                Failures.TryRemove(url, out _);
                await WriteAtomicallyAsync(path, bytes).ConfigureAwait(false);
                return path;
            }

            Failures[url] = DateTime.UtcNow + FailureCooldown;
            if (bytes != null)
                DebugLog.Warn("AccountAvatarPendantCache", "[PendantCache] Downloaded payload is not a valid image");

            return existingValid ? path : null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountAvatarPendantCache", $"[PendantCache] {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static async Task<byte[]?> DownloadAsync(string url)
    {
        for (var attempt = 0; attempt < DownloadAttempts; attempt++)
        {
            if (attempt > 0) await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt)).ConfigureAwait(false);

            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await Http
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    if (status is >= 400 and < 500 and not 408 and not 429) return null;
                    continue;
                }

                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length == 0) continue;

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType != null &&
                    !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                    !IsKnownImageFormat(bytes)) continue;

                return bytes;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                DebugLog.Info("AccountAvatarPendantCache",
                    $"[PendantCache] Download attempt {attempt + 1} failed: {ex.GetType().Name}");
            }
            finally
            {
                Gate.Release();
            }
        }

        return null;
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes)
    {
        var temp = path + "." + Path.GetRandomFileName() + TempSuffix;
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    private static bool IsValidPayload(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < MinImageBytes) return false;

        if (bytes.StartsWith(PngSignature))
            return bytes.Length > PngEndMark.Length && bytes[^PngEndMark.Length..].SequenceEqual(PngEndMark);

        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return bytes[^2] == 0xFF && bytes[^1] == 0xD9;

        if (bytes.StartsWith("GIF8"u8))
            return bytes[^1] == 0x3B;

        if (bytes.Length > 12 && bytes.StartsWith("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
            return riffSize == 0 || riffSize + 8u <= (uint)bytes.Length;
        }

        if (bytes.Length > 14 && bytes.StartsWith("BM"u8))
        {
            var bmpSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(2, 4));
            return bmpSize == 0 || bmpSize <= (uint)bytes.Length;
        }

        return true;
    }

    private static bool IsInFailureCooldown(string url) =>
        Failures.TryGetValue(url, out var retryAt) && DateTime.UtcNow < retryAt;

    private static bool IsKnownImageFormat(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12
        && (bytes.StartsWith(PngSignature)
            || (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            || bytes.StartsWith("GIF8"u8)
            || bytes.StartsWith("RIFF"u8)
            || bytes.StartsWith("BM"u8));

    private static bool TryResolveUri(string? url, out Uri uri)
    {
        var normalized = AccountAvatarPendant.NormalizeUrl(url);
        if (normalized != null && Uri.TryCreate(normalized, UriKind.Absolute, out var parsed))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }

    private static string ResolveCachePath(Uri uri)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri), hash);
        return Path.Combine(CacheDirectory, Convert.ToHexString(hash[..16]).ToLowerInvariant() + ResolveExtension(uri));
    }

    private static string ResolveExtension(Uri uri) =>
        Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() switch
        {
            ".png" => ".png",
            ".jpg" or ".jpeg" => ".jpg",
            ".gif" => ".gif",
            ".webp" => ".webp",
            ".bmp" => ".bmp",
            _ => ".img"
        };

    private static bool TryDelete(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("AccountAvatarPendantCache", $"[PendantCache] Unable to delete {path}: {ex.Message}");
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Referrer = new Uri("https://www.miyoushe.com/");
        return client;
    }
}
