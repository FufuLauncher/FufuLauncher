/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.PluginMirror;

public class MirrorSiteConfig
{
    [JsonPropertyName("ProbeUrl")]
    public string ProbeUrl
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("ProbeMd5")]
    public string ProbeMd5
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("Mirrors")]
    public List<string> Mirrors
    {
        get;
        set;
    } = new();
}

public class MirrorTestResult
{
    public string Domain
    {
        get;
        set;
    } = string.Empty;

    public bool IsSuccess
    {
        get;
        set;
    }

    public long ResponseTimeMs
    {
        get;
        set;
    } = long.MaxValue;

    public string StatusDesc
    {
        get;
        set;
    } = string.Empty;
}

public readonly record struct MirrorTestProgress(int Tested, int Total);

public class MirrorSiteProvider
{
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly HttpClient SpeedTestClient = CreateClient(autoRedirect: true);

    private static readonly HttpClient RedirectProbeClient = CreateClient(autoRedirect: false);

    private static HttpClient CreateClient(bool autoRedirect)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = autoRedirect,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUserAgent);
        return client;
    }

    private static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "mirrors.json");

    public MirrorSiteConfig LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                DebugLog.Info("MirrorSiteProvider", $"[MirrorSiteProvider] mirrors.json not found: {ConfigPath}");
                return new MirrorSiteConfig();
            }

            var config = JsonSerializer.Deserialize<MirrorSiteConfig>(File.ReadAllText(ConfigPath), JsonOptions);
            config ??= new MirrorSiteConfig();
            DebugLog.Info("MirrorSiteProvider",
                $"[MirrorSiteProvider] Loaded {config.Mirrors.Count} mirror(s), probe: {config.ProbeUrl}");
            return config;
        }
        catch (Exception ex)
        {
            DebugLog.Info("MirrorSiteProvider", $"[MirrorSiteProvider] Failed to load mirrors.json: {ex.Message}");
            return new MirrorSiteConfig();
        }
    }

    public static bool IsGitHubUrl(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return ApiEndpoints.IsGitHubHost(host);
        }
        catch
        {
            return false;
        }
    }

    public static string BuildMirrorUrl(string domain, string originalUrl)
    {
        return ApiEndpoints.BuildMirrorUrl(domain, originalUrl);
    }

    public static async Task<string?> ResolveRedirectUrlAsync(string url)
    {
        try
        {
            var current = url;
            for (int hop = 0; hop < 5; hop++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, current);
                using var response =
                    await RedirectProbeClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect
                    or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (response.Headers.Location is not { } location) return null;

                    var next = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(new Uri(current), location).ToString();

                    if (IsGitHubUrl(next)) return next;

                    current = next;
                    continue;
                }

                return current;
            }

            return null;
        }
        catch (Exception ex)
        {
            DebugLog.Info("MirrorSiteProvider", $"[MirrorSiteProvider] Redirect probe failed for {url}: {ex.Message}");
            return null;
        }
    }

    public async Task<List<MirrorTestResult>> TestMirrorsAsync(MirrorSiteConfig config,
        IProgress<MirrorTestProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (config.Mirrors.Count == 0)
            return new List<MirrorTestResult>();

        int completed = 0;
        var tasks = config.Mirrors.Select(async domain =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await TestSingleMirrorAsync(domain, config);
                int current = Interlocked.Increment(ref completed);
                progress?.Report(new MirrorTestProgress(current, config.Mirrors.Count));
                return result;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                int current = Interlocked.Increment(ref completed);
                progress?.Report(new MirrorTestProgress(current, config.Mirrors.Count));
                return new MirrorTestResult { Domain = domain, IsSuccess = false, ResponseTimeMs = long.MaxValue };
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r.IsSuccess)
            .OrderBy(r => r.ResponseTimeMs)
            .ToList();
    }

    private static async Task<MirrorTestResult> TestSingleMirrorAsync(string domain, MirrorSiteConfig config)
    {
        var result = new MirrorTestResult { Domain = domain, IsSuccess = false, ResponseTimeMs = long.MaxValue };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var testUrl = BuildMirrorUrl(domain, config.ProbeUrl);
            using var response = await SpeedTestClient.GetAsync(testUrl, HttpCompletionOption.ResponseContentRead);
            response.EnsureSuccessStatusCode();

            var fileData = await response.Content.ReadAsByteArrayAsync();
            stopwatch.Stop();

            var hash = CalculateMd5(fileData);
            if (hash.Equals(config.ProbeMd5, StringComparison.OrdinalIgnoreCase))
            {
                result.IsSuccess = true;
                result.ResponseTimeMs = stopwatch.ElapsedMilliseconds;
                double speedKbps = (fileData.Length / 1024.0) / (result.ResponseTimeMs / 1000.0);
                string quality = speedKbps > 500
                    ? "PluginMirrorStatusExcellent".GetLocalized()
                    : "PluginMirrorStatusNormal".GetLocalized();
                result.StatusDesc = $"{result.ResponseTimeMs} ms | {quality}";
            }
        }
        catch
        {
            stopwatch.Stop();
            result.IsSuccess = false;
            result.ResponseTimeMs = long.MaxValue;
        }

        return result;
    }

    private static string CalculateMd5(byte[] data)
    {
        var hashBytes = MD5.HashData(data);
        var sb = new System.Text.StringBuilder(hashBytes.Length * 2);
        foreach (var b in hashBytes)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}