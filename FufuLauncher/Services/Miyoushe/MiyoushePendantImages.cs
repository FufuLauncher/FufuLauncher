using System.Collections.Concurrent;
using System.Net;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Miyoushe;

internal static class MiyoushePendantImages
{
    private const int Concurrency = 8;
    private const int Attempts = 3;
    private const string ResizableHost = ApiEndpoints.MiyousheStaticHost;

    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim Gate = new(Concurrency, Concurrency);
    private static readonly ConcurrentDictionary<string, Lazy<Task<byte[]?>>> Cache = new(StringComparer.Ordinal);

    internal static string Thumbnail(string url, int width)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Host != ResizableHost) return url;
        return url + (url.Contains('?') ? "&" : "?") + "x-oss-process=image/resize,w_" + width;
    }

    internal static Task<byte[]?> GetAsync(string url, CancellationToken ct) =>
        Cache.GetOrAdd(url, key => new Lazy<Task<byte[]?>>(() => DownloadAsync(key))).Value.WaitAsync(ct);

    private static async Task<byte[]?> DownloadAsync(string url)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            if (attempt > 0) await Task.Delay(250 * attempt).ConfigureAwait(false);
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                    .ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (bytes.Length > 0) return bytes;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
                DebugLog.Info(nameof(MiyoushePendantImages), ex.GetType().Name);
            }
            finally
            {
                Gate.Release();
            }
        }

        Cache.TryRemove(url, out _);
        return null;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");
        return client;
    }
}
