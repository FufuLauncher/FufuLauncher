/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;

namespace FufuLauncher.Services.GameServer;

public sealed class ChunkDownloader
{
    public const int BufferSize = 81920;
    private static readonly TimeSpan AttemptAbsoluteTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan StallCheckInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan StallThreshold = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 3;
    private readonly HttpClient _httpClient;

    public ChunkDownloader(GameServerHttpClientProvider httpClientProvider)
    {
        _httpClient = httpClientProvider.ChunkClient;
    }

    public Task<string> DownloadChunkAsync(SophonChunk chunk, string chunksDir, KeyedSemaphoreSlim chunkLocks,
        CancellationToken token = default, Action<long>? onBytesTransferred = null)
    {
        return DownloadBlobAsync(chunk.AssetChunk.ChunkName, chunk.AssetChunk.ChunkSize, chunk.DownloadUrl, chunksDir,
            chunkLocks, token, onBytesTransferred);
    }

    public async Task<string> DownloadBlobAsync(string name, long expectedSize, string url, string chunksDir,
        KeyedSemaphoreSlim chunkLocks,
        CancellationToken token = default, Action<long>? onBytesTransferred = null)
    {
        Directory.CreateDirectory(chunksDir);
        string chunkPath = Path.Combine(chunksDir, name);
        string expectedHashPrefix = GetHashPrefix(name);

        using (await chunkLocks.LockAsync(name, token).ConfigureAwait(false))
        {
            if (await IsCachedBlobValidAsync(chunksDir, name, expectedSize, token).ConfigureAwait(false))
            {
                return chunkPath;
            }

            TryDeleteFile(chunkPath);

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                token.ThrowIfCancellationRequested();

                string tempPath = chunkPath + ".tmp";
                try
                {
                    await DownloadFileCoreAsync(url, tempPath, expectedSize, token, onBytesTransferred)
                        .ConfigureAwait(false);

                    if (await IsValidChunkAsync(tempPath, expectedHashPrefix, token).ConfigureAwait(false))
                    {
                        File.Move(tempPath, chunkPath, overwrite: true);
                        return chunkPath;
                    }

                    TryDeleteFile(tempPath);
                }
                catch (OperationCanceledException) when (!token.IsCancellationRequested)
                {
                    TryDeleteFile(tempPath);
                }
                catch
                {
                    TryDeleteFile(tempPath);
                    if (attempt == MaxAttempts)
                    {
                        throw;
                    }
                }

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), token).ConfigureAwait(false);
                }
            }

            throw new InvalidOperationException(string.Format("GameServer_ChunkDownloadFailed".GetLocalized(), name));
        }
    }

    public async Task DownloadFileAsync(string url, string destPath, long? expectedSize, string? expectedMd5Hex,
        CancellationToken token = default, Action<long>? onBytesTransferred = null)
    {
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            token.ThrowIfCancellationRequested();

            try
            {
                await DownloadFileCoreAsync(url, destPath, expectedSize, token, onBytesTransferred)
                    .ConfigureAwait(false);

                if (!string.IsNullOrEmpty(expectedMd5Hex))
                {
                    string actualMd5 = await HashUtility.Md5FileAsync(destPath, token).ConfigureAwait(false);
                    if (actualMd5.Equals(expectedMd5Hex, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }

                    TryDeleteFile(destPath);
                    throw new InvalidOperationException(string.Format("GameServer_FileChecksumMismatch".GetLocalized(),
                        url));
                }

                return;
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                TryDeleteFile(destPath);
            }
            catch
            {
                TryDeleteFile(destPath);
                if (attempt == MaxAttempts)
                {
                    throw;
                }
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), token).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(string.Format("GameServer_ChunkDownloadFailed".GetLocalized(), url));
    }

    private async Task DownloadFileCoreAsync(string url, string destPath, long? expectedSize, CancellationToken token,
        Action<long>? onBytesTransferred)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        attemptCts.CancelAfter(AttemptAbsoluteTimeout);

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var contentStream =
            await response.Content.ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None,
            BufferSize, useAsync: true);

        await CopyWithStallWatchdogAsync(contentStream, fileStream, attemptCts, onBytesTransferred)
            .ConfigureAwait(false);
        await fileStream.FlushAsync(attemptCts.Token).ConfigureAwait(false);

        if (expectedSize.HasValue && fileStream.Length != expectedSize.Value)
        {
            throw new InvalidOperationException(string.Format("GameServer_FileSizeMismatch".GetLocalized(), url,
                fileStream.Length, expectedSize.Value));
        }
    }

    private static async Task CopyWithStallWatchdogAsync(Stream source, Stream destination,
        CancellationTokenSource attemptCts, Action<long>? onBytesTransferred)
    {
        byte[] buffer = new byte[BufferSize];
        long lastProgressTicks = Environment.TickCount64;

        using var timer = new PeriodicTimer(StallCheckInterval);
        Task watchdog = Task.Run(async () =>
        {
            try
            {
                while (await timer.WaitForNextTickAsync(CancellationToken.None).ConfigureAwait(false))
                {
                    if (Environment.TickCount64 - Volatile.Read(ref lastProgressTicks) >=
                        (long)StallThreshold.TotalMilliseconds)
                    {
                        attemptCts.Cancel();
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        try
        {
            while (true)
            {
                int read = await source.ReadAsync(buffer, attemptCts.Token).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), attemptCts.Token).ConfigureAwait(false);
                Volatile.Write(ref lastProgressTicks, Environment.TickCount64);
                onBytesTransferred?.Invoke(read);
            }
        }
        finally
        {
            timer.Dispose();
            try
            {
                await watchdog.ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    public async Task<bool> IsCachedBlobValidAsync(string chunksDir, string name, long expectedSize,
        CancellationToken token = default)
    {
        string chunkPath = Path.Combine(chunksDir, name);
        try
        {
            var info = new FileInfo(chunkPath);
            if (!info.Exists || info.Length != expectedSize)
            {
                return false;
            }

            return await IsValidChunkAsync(chunkPath, GetHashPrefix(name), token).ConfigureAwait(false);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string GetHashPrefix(string name)
    {
        int separatorIndex = name.IndexOf('_');
        return separatorIndex >= 0 ? name[..separatorIndex] : name;
    }

    private static async Task<bool> IsValidChunkAsync(string chunkPath, string expectedHashPrefix,
        CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            string actualHash = await HashUtility.XxHash64HexAsync(stream, token).ConfigureAwait(false);
            return actualHash.Equals(expectedHashPrefix, StringComparison.OrdinalIgnoreCase);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}