/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Buffers;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Download;

namespace FufuLauncher.Services.Download;

public sealed partial class ParallelDownloadService
{
    private static async Task DownloadSingleThreadAsync(
        HttpClient client, string url, string workPath,
        long totalSize, ParallelDownloadOptions opt,
        IProgress<ParallelDownloadProgress>? progress, CancellationToken ct)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        long lastProgressTicks = Environment.TickCount64;


        PeriodicTimer? stallTimer = null;
        Task? watchdog = null;

        try
        {
            using var req = CreateRequest(HttpMethod.Get, url, opt);

            using var sendCts = CancellationTokenSource.CreateLinkedTokenSource(attemptCts.Token);
            if (opt.RequestTimeout > TimeSpan.Zero)
            {
                sendCts.CancelAfter(opt.RequestTimeout);
            }

            HttpResponseMessage resp;
            try
            {
                resp = await client.SendAsync(
                    req, HttpCompletionOption.ResponseHeadersRead, sendCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new DownloadFailedException(
                    string.Format("Download_ConnectOrQueueTimeout_Format".GetLocalized(), opt.RequestTimeout.TotalSeconds),
                    url, totalSize, 0, DownloadFailureReason.TimedOut);
            }

            using (resp)
            {
                resp.EnsureSuccessStatusCode();

                await using var source = await resp.Content.ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                await using var destination = new FileStream(
                    workPath, FileMode.Create, FileAccess.Write, FileShare.None, opt.BufferSize, useAsync: true);

                if (opt.StallTimeout > TimeSpan.Zero)
                {
                    stallTimer = new PeriodicTimer(StallCheckInterval);
                    watchdog = RunStallWatchdogAsync(
                        stallTimer, attemptCts, () => Volatile.Read(ref lastProgressTicks), opt.StallTimeout);
                }

                var limiter = new TokenBucketRateLimiter(opt.MaxBytesPerSecond);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(opt.BufferSize);
                long downloaded = 0;
                long lastReportTicks = Environment.TickCount64;
                long lastBytes = 0;

                try
                {
                    while (true)
                    {
                        int read = await source.ReadAsync(buffer.AsMemory(0, opt.BufferSize), attemptCts.Token)
                            .ConfigureAwait(false);
                        if (read <= 0)
                        {
                            break;
                        }

                        await limiter.AcquireAsync(read, attemptCts.Token).ConfigureAwait(false);


                        Volatile.Write(ref lastProgressTicks, Environment.TickCount64);
                        await destination.WriteAsync(buffer.AsMemory(0, read), attemptCts.Token).ConfigureAwait(false);

                        downloaded += read;

                        long now = Environment.TickCount64;
                        double elapsed = (now - lastReportTicks) / 1000.0;
                        if (elapsed >= opt.ProgressReportInterval.TotalSeconds)
                        {
                            progress?.Report(new ParallelDownloadProgress
                            {
                                BytesDownloaded = downloaded,
                                TotalBytes = totalSize,
                                BytesPerSecond = (downloaded - lastBytes) / elapsed,
                                TotalChunks = 1,
                                ActiveSegments = 1,
                            });
                            lastBytes = downloaded;
                            lastReportTicks = now;
                        }
                    }

                    await destination.FlushAsync(attemptCts.Token).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DownloadFailedException(
                "Download_SingleThreadStalled".GetLocalized(), url, totalSize, 0, DownloadFailureReason.Stalled);
        }
        finally
        {
            stallTimer?.Dispose();
            if (watchdog != null)
            {
                try
                {
                    await watchdog.ConfigureAwait(false);
                }
                catch
                {
                    /* 看门狗自身异常一律忽略 */
                }
            }
        }
    }
}