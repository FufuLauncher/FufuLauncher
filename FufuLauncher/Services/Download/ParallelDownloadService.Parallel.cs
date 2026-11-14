/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Download;

namespace FufuLauncher.Services.Download;

public sealed partial class ParallelDownloadService
{
    private sealed class ParallelState
    {
        public long TotalDownloaded;
        public int CompletedChunks;
        public int ActiveSegments;
        public int FailedChunks;
        public int TotalChunks;
        public Exception? LastError;

        public long LastReportTicks = Environment.TickCount64;
        public long LastReportBytes;
        public readonly object ReportLock = new();
        public readonly object ErrorLock = new();
    }

    private async Task DownloadParallelAsync(
        HttpClient client, string url, string workPath, long totalSize,
        ParallelDownloadOptions opt, IProgress<ParallelDownloadProgress>? progress,
        CancellationToken ct, string resumeStatePath)
    {
        var chunks = BuildChunks(totalSize, opt);
        DebugLog.Info("ParallelDownloadService",
            $"[ParallelDownload] Parallel {chunks.Count} chunks × {opt.MaxParallelSegments} threads " +
            $"(total {totalSize / 1024.0 / 1024.0:F2}MB)");


        var completed = opt.EnableResume
            ? LoadResumeState(resumeStatePath, url, totalSize, chunks.Count)
            : new HashSet<int>();


        if (completed.Count > 0 && !IsWorkFileIntact(workPath, totalSize))
        {
            DebugLog.Info("ParallelDownloadService",
                "[ParallelDownload] .part file missing or wrong length, discarding resume state");
            completed.Clear();
            TryDelete(resumeStatePath);
        }

        long alreadyDone = 0;
        foreach (int i in completed)
        {
            alreadyDone += chunks[i].End - chunks[i].Start + 1;
        }


        using var handle = File.OpenHandle(
            workPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite,
            FileOptions.Asynchronous);

        if (RandomAccess.GetLength(handle) != totalSize)
        {
            RandomAccess.SetLength(handle, totalSize);
        }

        var state = new ParallelState
        {
            TotalDownloaded = alreadyDone,
            CompletedChunks = completed.Count,
            TotalChunks = chunks.Count,
        };

        var queue = new ConcurrentQueue<int>();
        for (int i = 0; i < chunks.Count; i++)
        {
            if (!completed.Contains(i))
            {
                queue.Enqueue(i);
            }
        }

        var globalLimiter = new TokenBucketRateLimiter(opt.MaxBytesPerSecond);
        var resumeLock = new object();


        using var parallelAbort = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var workers = new List<Task>(opt.MaxParallelSegments);
        for (int w = 0; w < opt.MaxParallelSegments; w++)
        {
            var segmentLimiter = new TokenBucketRateLimiter(opt.MaxBytesPerSecondPerSegment);
            workers.Add(Task.Run(async () =>
            {
                Interlocked.Increment(ref state.ActiveSegments);
                try
                {
                    while (queue.TryDequeue(out int index))
                    {
                        parallelAbort.Token.ThrowIfCancellationRequested();
                        var (start, end) = chunks[index];
                        try
                        {
                            await DownloadChunkAsync(
                                    client, url, start, end, handle, parallelAbort.Token,
                                    state, totalSize, opt, progress, globalLimiter, segmentLimiter)
                                .ConfigureAwait(false);

                            Interlocked.Increment(ref state.CompletedChunks);
                            if (opt.EnableResume)
                            {
                                lock (resumeLock)
                                {
                                    completed.Add(index);
                                    SaveResumeState(resumeStatePath, url, totalSize, chunks.Count, completed);
                                }
                            }
                        }
                        catch (DownloadFailedException ex)
                            when (ex.Reason == DownloadFailureReason.RangeNotSupported)
                        {
                            lock (state.ErrorLock)
                            {
                                state.LastError = ex;
                            }

                            Interlocked.Increment(ref state.FailedChunks);
                            parallelAbort.Cancel();
                            throw;
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref state.FailedChunks);
                            lock (state.ErrorLock)
                            {
                                state.LastError = ex;
                            }
                        }
                    }
                }
                finally
                {
                    Interlocked.Decrement(ref state.ActiveSegments);
                }
            }, CancellationToken.None));
        }

        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (DownloadFailedException ex) when (ex.Reason == DownloadFailureReason.RangeNotSupported)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Exception? cause;
            lock (state.ErrorLock)
            {
                cause = state.LastError;
            }

            if (cause is DownloadFailedException { Reason: DownloadFailureReason.RangeNotSupported } rns)
            {
                throw rns;
            }

            throw;
        }

        if (Volatile.Read(ref state.FailedChunks) > 0)
        {
            ct.ThrowIfCancellationRequested();

            Exception? last;
            lock (state.ErrorLock)
            {
                last = state.LastError;
            }

            int failed = Volatile.Read(ref state.FailedChunks);
            throw new DownloadFailedException(
                $"下载失败: {failed} 个小段出错" +
                (last != null ? $"（{last.GetType().Name}: {last.Message}）" : ""),
                url, totalSize, failed, DownloadFailureReason.ChunksFailed, last)
            {
                BytesDownloaded = Interlocked.Read(ref state.TotalDownloaded),
            };
        }

        if (opt.EnableResume)
        {
            TryDelete(resumeStatePath);
        }
    }

    private static List<(long Start, long End)> BuildChunks(long totalSize, ParallelDownloadOptions opt)
    {
        int totalChunks = Math.Max(opt.MaxParallelSegments * opt.ChunkMultiplier, 1);
        long chunkSize = totalSize / totalChunks;


        if (chunkSize < opt.MinChunkSize)
        {
            totalChunks = (int)Math.Max(totalSize / opt.MinChunkSize, 1);
            chunkSize = totalSize / totalChunks;
        }

        totalChunks = Math.Max(totalChunks, 1);
        var chunks = new List<(long, long)>(totalChunks);
        for (int i = 0; i < totalChunks; i++)
        {
            long start = i * chunkSize;
            long end = i == totalChunks - 1 ? totalSize - 1 : start + chunkSize - 1;
            chunks.Add((start, end));
        }

        return chunks;
    }

    private static async Task DownloadChunkAsync(
        HttpClient client, string url, long start, long end,
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        CancellationToken ct, ParallelState state, long totalSize,
        ParallelDownloadOptions opt, IProgress<ParallelDownloadProgress>? progress,
        TokenBucketRateLimiter globalLimiter, TokenBucketRateLimiter segmentLimiter)
    {
        long chunkSize = end - start + 1;
        Exception? lastError = null;


        long writtenThisAttempt = 0;

        for (int attempt = 0; attempt <= opt.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            long lastProgressTicks = Environment.TickCount64;
            writtenThisAttempt = 0;


            PeriodicTimer? stallTimer = null;
            Task? watchdog = null;

            byte[] buffer = ArrayPool<byte>.Shared.Rent(opt.BufferSize);
            try
            {
                using var req = CreateRequest(HttpMethod.Get, url, opt);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(start, end);


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
                    throw new IOException($"建立连接或排队超时（>{opt.RequestTimeout.TotalSeconds:F0}s）。");
                }

                using (resp)
                {
                    if (resp.StatusCode == HttpStatusCode.OK)
                    {
                        throw new DownloadFailedException(
                            "服务端忽略了 Range 请求，返回完整内容。",
                            url, totalSize, 1, DownloadFailureReason.RangeNotSupported);
                    }

                    resp.EnsureSuccessStatusCode();


                    var contentRange = resp.Content.Headers.ContentRange;
                    if (contentRange?.From != start || contentRange.To != end)
                    {
                        throw new DownloadFailedException(
                            $"分段响应范围不符: 请求 [{start}-{end}]，返回 " +
                            (contentRange == null
                                ? "无 Content-Range 头"
                                : $"[{contentRange.From}-{contentRange.To}]"),
                            url, totalSize, 1, DownloadFailureReason.RangeNotSupported);
                    }

                    await using var stream = await resp.Content.ReadAsStreamAsync(attemptCts.Token)
                        .ConfigureAwait(false);


                    if (opt.StallTimeout > TimeSpan.Zero)
                    {
                        stallTimer = new PeriodicTimer(StallCheckInterval);
                        watchdog = RunStallWatchdogAsync(
                            stallTimer, attemptCts, () => Volatile.Read(ref lastProgressTicks), opt.StallTimeout);
                    }

                    long writePos = start;
                    long written = 0;
                    while (true)
                    {
                        int read = await stream.ReadAsync(buffer.AsMemory(0, opt.BufferSize), attemptCts.Token)
                            .ConfigureAwait(false);
                        if (read <= 0)
                        {
                            break;
                        }

                        await globalLimiter.AcquireAsync(read, attemptCts.Token).ConfigureAwait(false);
                        await segmentLimiter.AcquireAsync(read, attemptCts.Token).ConfigureAwait(false);


                        Volatile.Write(ref lastProgressTicks, Environment.TickCount64);

                        await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), writePos, attemptCts.Token)
                            .ConfigureAwait(false);

                        writePos += read;
                        written += read;
                        writtenThisAttempt += read;
                        Interlocked.Add(ref state.TotalDownloaded, read);
                        ReportProgress(state, totalSize, opt, progress);
                    }

                    if (written < chunkSize)
                    {
                        throw new IOException($"小段不完整: 预期 {chunkSize}B，实际 {written}B");
                    }

                    return;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                lastError = new DownloadFailedException(
                    "小段下载停滞超时。", url, totalSize, 1, DownloadFailureReason.Stalled);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (DownloadFailedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
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


            if (writtenThisAttempt > 0)
            {
                Interlocked.Add(ref state.TotalDownloaded, -writtenThisAttempt);
                writtenThisAttempt = 0;
            }

            if (attempt < opt.MaxRetries)
            {
                double backoff = opt.RetryBaseDelay.TotalMilliseconds * Math.Pow(2, attempt);
                var delay = TimeSpan.FromMilliseconds(
                    Math.Min(backoff, opt.RetryMaxDelay.TotalMilliseconds));
                DebugLog.Info("ParallelDownloadService",
                    $"[ParallelDownload] Chunk [{start}-{end}] attempt {attempt + 1} failed, " +
                    $"retrying in {delay.TotalSeconds:F1}s" +
                    (lastError != null ? $"（{lastError.Message}）" : ""));
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        throw new IOException(
            $"小段 [{start}-{end}] 重试 {opt.MaxRetries} 次后仍失败", lastError);
    }

    private static void ReportProgress(
        ParallelState state, long totalSize,
        ParallelDownloadOptions opt, IProgress<ParallelDownloadProgress>? progress)
    {
        if (progress == null)
        {
            return;
        }

        long now = Environment.TickCount64;
        bool report = false;
        double speed = 0;
        long downloaded = 0;

        lock (state.ReportLock)
        {
            double elapsed = (now - state.LastReportTicks) / 1000.0;
            if (elapsed >= opt.ProgressReportInterval.TotalSeconds)
            {
                downloaded = Interlocked.Read(ref state.TotalDownloaded);
                speed = (downloaded - state.LastReportBytes) / elapsed;
                state.LastReportBytes = downloaded;
                state.LastReportTicks = now;
                report = true;
            }
        }

        if (report)
        {
            progress.Report(new ParallelDownloadProgress
            {
                BytesDownloaded = downloaded,
                TotalBytes = totalSize,
                BytesPerSecond = speed,
                CompletedChunks = Volatile.Read(ref state.CompletedChunks),
                TotalChunks = state.TotalChunks,
                ActiveSegments = Volatile.Read(ref state.ActiveSegments),
            });
        }
    }
}