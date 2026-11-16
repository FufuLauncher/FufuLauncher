/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Net;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Download;

namespace FufuLauncher.Services.Download;

public sealed partial class ParallelDownloadService
{
    private const string DefaultUserAgent = "FufuLauncher/1.0";

    private const string ResumeStateSuffix = ".state.json";

    private static readonly TimeSpan StallCheckInterval = TimeSpan.FromSeconds(1);

    public async Task<string> DownloadAsync(
        string url,
        string destinationPath,
        ParallelDownloadOptions? options = null,
        IProgress<ParallelDownloadProgress>? progress = null,
        CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var opt = (options ?? ParallelDownloadOptions.Default).Validate();


        using var handler = CreateHandler(opt);
        using var client = CreateClient(handler);
        return await DownloadCoreAsync(client, url, destinationPath, opt, progress, token)
            .ConfigureAwait(false);
    }

    private async Task<string> DownloadCoreAsync(
        HttpClient client,
        string url,
        string destinationPath,
        ParallelDownloadOptions opt,
        IProgress<ParallelDownloadProgress>? progress,
        CancellationToken token)
    {
        string fullPath = Path.GetFullPath(destinationPath);
        string directory = Path.GetDirectoryName(fullPath)
                           ?? throw new ArgumentException("Download_MissingDirectoryInPath".GetLocalized(), nameof(destinationPath));
        Directory.CreateDirectory(directory);

        if (File.Exists(fullPath) && !opt.OverwriteExisting)
        {
            throw new DownloadFailedException(
                string.Format("Download_TargetExists_Format".GetLocalized(), fullPath), url, 0, 0, DownloadFailureReason.TargetUnwritable);
        }

        string workPath = opt.UseTemporaryFile ? fullPath + ".part" : fullPath;
        string resumeStatePath = workPath + ResumeStateSuffix;


        using var overallCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (opt.OverallTimeout is { } overall && overall > TimeSpan.Zero)
        {
            overallCts.CancelAfter(overall);
        }

        CancellationToken ct = overallCts.Token;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var probe = await ProbeAsync(client, url, opt, ct).ConfigureAwait(false);
            long totalSize = opt.ExpectedSize ?? probe.ContentLength;

            if (opt.ExpectedSize is { } expected && probe.ContentLength > 0 && probe.ContentLength != expected)
            {
                throw new DownloadFailedException(
                    string.Format("Download_SizeMismatchExpected_Format".GetLocalized(), probe.ContentLength, expected),
                    url, probe.ContentLength, 0, DownloadFailureReason.IntegrityMismatch);
            }

            bool canParallel = probe.SupportsRange
                               && totalSize > opt.MinParallelFileSize
                               && opt.MaxParallelSegments > 1;

            if (!canParallel && !probe.SupportsRange && !opt.AllowRangeFallback)
            {
                throw new DownloadFailedException(
                    "Download_RangeNotSupported".GetLocalized(),
                    url, totalSize, 0, DownloadFailureReason.RangeNotSupported);
            }

            if (canParallel)
            {
                try
                {
                    await DownloadParallelAsync(client, url, workPath, totalSize, opt, progress, ct, resumeStatePath)
                        .ConfigureAwait(false);
                }
                catch (DownloadFailedException ex)
                    when (ex.Reason == DownloadFailureReason.RangeNotSupported && opt.AllowRangeFallback)
                {
                    DebugLog.Info("ParallelDownloadService",
                        "[ParallelDownload] Server ignored Range, falling back to single thread");
                    TryDelete(resumeStatePath);
                    await DownloadSingleThreadAsync(client, url, workPath, totalSize, opt, progress, ct)
                        .ConfigureAwait(false);
                }
            }
            else
            {
                DebugLog.Info("ParallelDownloadService",
                    $"[ParallelDownload] Single thread (range supported={probe.SupportsRange}, size={totalSize}B)");
                await DownloadSingleThreadAsync(client, url, workPath, totalSize, opt, progress, ct)
                    .ConfigureAwait(false);
            }

            long actualLength = new FileInfo(workPath).Length;
            if (totalSize > 0 && actualLength != totalSize)
            {
                throw new DownloadFailedException(
                    string.Format("Download_ActualSizeMismatch_Format".GetLocalized(), actualLength, totalSize),
                    url, totalSize, 0, DownloadFailureReason.IntegrityMismatch);
            }

            await VerifyHashAsync(workPath, url, opt, totalSize, ct).ConfigureAwait(false);

            if (opt.UseTemporaryFile)
            {
                File.Move(workPath, fullPath, overwrite: true);
            }

            TryDelete(resumeStatePath);

            stopwatch.Stop();
            DebugLog.Info("ParallelDownloadService",
                $"[ParallelDownload] Completed {Path.GetFileName(fullPath)} " +
                $"({actualLength / 1024.0 / 1024.0:F2} MB, {stopwatch.Elapsed.TotalSeconds:F1}s)");

            progress?.Report(new ParallelDownloadProgress
            {
                BytesDownloaded = actualLength,
                TotalBytes = totalSize > 0 ? totalSize : actualLength,
                BytesPerSecond = 0,
                CompletedChunks = 1,
                TotalChunks = 1,
                IsCompleted = true,
            });

            return fullPath;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw new DownloadFailedException(
                "Download_TimedOut".GetLocalized(), url, 0, 0, DownloadFailureReason.TimedOut);
        }
        catch (OperationCanceledException)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw new DownloadFailedException(
                "Download_Cancelled".GetLocalized(), url, 0, 0, DownloadFailureReason.Cancelled);
        }
        catch (DownloadFailedException)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw new DownloadFailedException(
                string.Format("Download_TargetNotWritable_Format".GetLocalized(), ex.Message), url, 0, 0,
                DownloadFailureReason.TargetUnwritable, ex);
        }
        catch (Exception ex)
        {
            Cleanup(workPath, resumeStatePath, opt);
            throw new DownloadFailedException(
                string.Format("Download_Failed_Format".GetLocalized(), ex.Message), url, 0, 0, DownloadFailureReason.Unknown, ex);
        }
    }

    public async Task<IReadOnlyList<ParallelDownloadResult>> DownloadManyAsync(
        IEnumerable<ParallelDownloadItem> items,
        ParallelDownloadOptions? options = null,
        IProgress<ParallelDownloadProgress>? progress = null,
        int maxConcurrentFiles = 3,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var list = items.ToList();
        var results = new ParallelDownloadResult[list.Count];
        if (list.Count == 0)
        {
            return results;
        }


        var batchOpt = (options ?? ParallelDownloadOptions.Default).Validate();
        int peakSegments = list
            .Select(i => (i.Options ?? batchOpt).Validate().MaxParallelSegments)
            .DefaultIfEmpty(batchOpt.MaxParallelSegments)
            .Max();


        int explicitConnections = new[] { options?.MaxConnectionsPerServer ?? 0 }
            .Concat(list.Select(i => i.Options?.MaxConnectionsPerServer ?? 0))
            .Max();

        var handlerOpt = batchOpt with
        {
            MaxConnectionsPerServer = explicitConnections > 0
                ? explicitConnections
                : Math.Max(peakSegments, maxConcurrentFiles),
        };

        using var handler = CreateHandler(handlerOpt);
        using var client = CreateClient(handler);
        using var gate = new SemaphoreSlim(Math.Max(maxConcurrentFiles, 1));


        var tasks = list.Select((item, index) => Task.Run(async () =>
        {
            try
            {
                await gate.WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                results[index] = new ParallelDownloadResult(item.Url, null, ex);
                return;
            }

            try
            {
                var itemOpt = (item.Options ?? batchOpt).Validate();
                string path = await DownloadCoreAsync(
                        client, item.Url, item.DestinationPath, itemOpt, progress, token)
                    .ConfigureAwait(false);
                results[index] = new ParallelDownloadResult(item.Url, path, null);
            }
            catch (Exception ex)
            {
                results[index] = new ParallelDownloadResult(item.Url, null, ex);
            }
            finally
            {
                gate.Release();
            }
        })).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }


    private static SocketsHttpHandler CreateHandler(ParallelDownloadOptions opt) => new()
    {
        MaxConnectionsPerServer = opt.MaxConnectionsPerServer,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectTimeout = opt.ConnectTimeout,
        EnableMultipleHttp2Connections = true,
    };

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler, disposeHandler: true)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, ParallelDownloadOptions opt)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.UserAgent.ParseAdd(opt.UserAgent ?? DefaultUserAgent);

        req.Headers.AcceptEncoding.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue("identity"));
        return req;
    }


    private readonly record struct ProbeResult(bool SupportsRange, long ContentLength);

    private static async Task<ProbeResult> ProbeAsync(
        HttpClient client, string url, ParallelDownloadOptions opt, CancellationToken ct)
    {
        using (var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            probeCts.CancelAfter(opt.RequestTimeout);

            HttpResponseMessage? headResp = null;
            try
            {
                using var req = CreateRequest(HttpMethod.Head, url, opt);
                headResp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, probeCts.Token)
                    .ConfigureAwait(false);

                if (headResp.IsSuccessStatusCode)
                {
                    return new ProbeResult(SupportsRange(headResp), headResp.Content.Headers.ContentLength ?? 0);
                }

                if (headResp.StatusCode is not (HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented))
                {
                    throw new DownloadFailedException(
                        string.Format("Download_HeadPreflightFailed_Format".GetLocalized(), (int)headResp.StatusCode, headResp.ReasonPhrase),
                        url, 0, 0, DownloadFailureReason.PreFlightFailed);
                }
            }
            catch (HttpRequestException ex)
            {
                throw new DownloadFailedException(
                    string.Format("Download_HeadPreflightNetworkError_Format".GetLocalized(), ex.Message), url, 0, 0,
                    DownloadFailureReason.PreFlightFailed, ex);
            }
            finally
            {
                headResp?.Dispose();
            }


            using var probeReq = CreateRequest(HttpMethod.Get, url, opt);
            probeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var probeResp = await client
                .SendAsync(probeReq, HttpCompletionOption.ResponseHeadersRead, probeCts.Token)
                .ConfigureAwait(false);

            if (!probeResp.IsSuccessStatusCode)
            {
                throw new DownloadFailedException(
                    string.Format("Download_PreflightFailed_Format".GetLocalized(), (int)probeResp.StatusCode, probeResp.ReasonPhrase),
                    url, 0, 0, DownloadFailureReason.PreFlightFailed);
            }

            long length = probeResp.Content.Headers.ContentRange?.Length
                          ?? probeResp.Content.Headers.ContentLength
                          ?? 0;

            return new ProbeResult(SupportsRange(probeResp), length);
        }
    }

    private static bool SupportsRange(HttpResponseMessage resp) =>
        resp.Headers.AcceptRanges?.Any(r => r.Equals("bytes", StringComparison.OrdinalIgnoreCase)) == true;

    private static async Task RunStallWatchdogAsync(
        PeriodicTimer timer, CancellationTokenSource attemptCts,
        Func<long> lastProgressTicks, TimeSpan stallTimeout)
    {
        long thresholdMs = (long)stallTimeout.TotalMilliseconds;
        try
        {
            while (await timer.WaitForNextTickAsync(CancellationToken.None).ConfigureAwait(false))
            {
                if (Environment.TickCount64 - lastProgressTicks() > thresholdMs)
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
    }
}

public sealed record ParallelDownloadItem(
    string Url,
    string DestinationPath,
    ParallelDownloadOptions? Options = null);

public sealed record ParallelDownloadResult(string Url, string? Path, Exception? Error)
{
    public bool IsSuccess => Error == null && Path != null;
}