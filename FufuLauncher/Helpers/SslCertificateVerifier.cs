using System.Collections;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FufuLauncher.Constants;

namespace FufuLauncher.Helpers;

public sealed record SslCertificateDetail(string Label, string Value);

public sealed record SslStackedEndpoint(string FieldName, string Url);

public sealed record SslEndpointTarget(
    string Host,
    int Port,
    string DisplayHost,
    IReadOnlyList<SslStackedEndpoint> StackedEndpoints);

public sealed record SslCertificateInfo(
    string Host,
    int Port,
    string DisplayHost,
    int StackedCount,
    IReadOnlyList<SslStackedEndpoint> StackedEndpoints,
    bool HasCertificate,
    bool IsValid,
    string StatusSummary,
    string? CertificateSummary,
    string? ErrorMessage,
    IReadOnlyList<SslCertificateDetail> Details);

public static partial class SslCertificateVerifier
{
    private const int DefaultHttpsPort = 443;
    private const int MaxConcurrency = 8;
    private const int MaxDisplayIps = 6;
    private const int MaxDisplaySans = 10;

    private static readonly TimeSpan DnsTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(7);
    private static readonly TimeSpan AltSvcProbeTimeout = TimeSpan.FromMilliseconds(2200);
    private static readonly TimeSpan QuicUdpProbeTimeout = TimeSpan.FromMilliseconds(900);

    [GeneratedRegex(
        @"^https://(?:[^/@\s]+@)?(?<host>\[[0-9a-fA-F:]+\]|[^/:?#\s]+)(?::(?<port>\d+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HttpsPrefixRegex();

    public static IReadOnlyList<SslEndpointTarget> DiscoverHttpsEndpoints()
    {
        var grouped = new Dictionary<(string Host, int Port), List<SslStackedEndpoint>>();
        var seenPerHost = new Dictionary<(string Host, int Port), HashSet<(string FieldName, string Url)>>();
        var pendingTypes = new Queue<Type>();
        var visitedTypes = new HashSet<Type>();
        pendingTypes.Enqueue(typeof(ApiEndpoints));

        while (pendingTypes.Count > 0)
        {
            var type = pendingTypes.Dequeue();
            if (!visitedTypes.Add(type)) continue;

            foreach (var nested in type.GetNestedTypes(BindingFlags.Public))
            {
                pendingTypes.Enqueue(nested);
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static |
                                                 BindingFlags.FlattenHierarchy))
            {
                object? rawValue;
                try
                {
                    rawValue = field.GetValue(null);
                }
                catch
                {
                    continue;
                }

                CollectEndpointValues(field.Name, rawValue, grouped, seenPerHost);
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static |
                                                    BindingFlags.FlattenHierarchy))
            {
                if (prop.GetIndexParameters().Length > 0 || prop.GetMethod is not { IsPublic: true })
                {
                    continue;
                }

                object? rawValue;
                try
                {
                    rawValue = prop.GetValue(null);
                }
                catch
                {
                    continue;
                }

                CollectEndpointValues(prop.Name, rawValue, grouped, seenPerHost);
            }
        }

        return grouped
            .OrderBy(kv => kv.Key.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(kv => kv.Key.Port)
            .Select(kv => new SslEndpointTarget(
                kv.Key.Host,
                kv.Key.Port,
                FormatDisplayHost(kv.Key.Host, kv.Key.Port),
                kv.Value
                    .OrderBy(item => item.FieldName, StringComparer.Ordinal)
                    .ThenBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
                    .ToList()))
            .ToList();
    }

    public static async Task<IReadOnlyList<SslCertificateInfo>> VerifyAllAsync(
        Action<int, int, string>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var targets = DiscoverHttpsEndpoints();
        if (targets.Count == 0)
        {
            return Array.Empty<SslCertificateInfo>();
        }

        var inFlightByEndpoint = new ConcurrentDictionary<(string Host, int Port), Lazy<Task<SslCertificateInfo>>>();
        var results = new SslCertificateInfo[targets.Count];
        var completed = 0;
        using var gate = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

        var tasks = targets.Select(async (target, index) =>
        {
            var endpointKey = (target.Host.ToLowerInvariant(), target.Port);
            var lazyProbe = inFlightByEndpoint.GetOrAdd(
                endpointKey,
                _ => new Lazy<Task<SslCertificateInfo>>(
                    async () =>
                    {
                        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return await VerifyEndpointAsync(target, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication));

            results[index] = await lazyProbe.Value.ConfigureAwait(false);
            var done = Interlocked.Increment(ref completed);
            onProgress?.Invoke(done, targets.Count, target.DisplayHost);
        }).ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);

        return results
            .OrderBy(r => r.IsValid)
            .ThenBy(r => !r.HasCertificate)
            .ThenBy(r => r.Host, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Port)
            .ToList();
    }

    private static void CollectEndpointValues(
        string memberName,
        object? rawValue,
        Dictionary<(string Host, int Port), List<SslStackedEndpoint>> grouped,
        Dictionary<(string Host, int Port), HashSet<(string FieldName, string Url)>> seenPerHost)
    {
        if (rawValue is string single)
        {
            TryAddEndpoint(memberName, single, grouped, seenPerHost);
            return;
        }

        if (rawValue is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is string text)
                {
                    TryAddEndpoint(memberName, text, grouped, seenPerHost);
                }
            }
        }
    }

    private static void TryAddEndpoint(
        string memberName,
        string? rawUrl,
        Dictionary<(string Host, int Port), List<SslStackedEndpoint>> grouped,
        Dictionary<(string Host, int Port), HashSet<(string FieldName, string Url)>> seenPerHost)
    {
        if (string.IsNullOrWhiteSpace(rawUrl)) return;

        var trimmed = rawUrl.Trim();
        if (!trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string rawHost;
        int port;

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            rawHost = uri.IdnHost;
            port = uri.Port > 0 ? uri.Port : DefaultHttpsPort;
        }
        else
        {
            var match = HttpsPrefixRegex().Match(trimmed);
            if (!match.Success) return;

            rawHost = match.Groups["host"].Value;
            port = DefaultHttpsPort;
            if (match.Groups["port"].Success &&
                (!int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out port) ||
                 port is < 1 or > 65535))
            {
                return;
            }
        }

        if (!TryNormalizePublicHost(rawHost, out var normalizedHost))
        {
            return;
        }

        var key = (normalizedHost, port);
        if (!grouped.TryGetValue(key, out var endpoints))
        {
            endpoints = new List<SslStackedEndpoint>();
            grouped[key] = endpoints;
            seenPerHost[key] = new HashSet<(string FieldName, string Url)>();
        }

        if (seenPerHost[key].Add((memberName, trimmed)))
        {
            endpoints.Add(new SslStackedEndpoint(memberName, trimmed));
        }
    }

    private static bool TryNormalizePublicHost(string rawHost, out string normalizedHost)
    {
        normalizedHost = string.Empty;
        if (string.IsNullOrWhiteSpace(rawHost)) return false;

        var candidate = rawHost.Trim();
        if (candidate.StartsWith('[') && candidate.EndsWith(']'))
        {
            candidate = candidate[1..^1];
        }

        candidate = candidate.TrimEnd('.');
        if (string.IsNullOrWhiteSpace(candidate) ||
            candidate.Contains('*') ||
            candidate.Contains('{') ||
            candidate.Contains('}') ||
            candidate.Contains('$'))
        {
            return false;
        }

        if (IPAddress.TryParse(candidate, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return false;
            normalizedHost = ip.ToString();
            return true;
        }

        string asciiHost;
        try
        {
            asciiHost = new IdnMapping().GetAscii(candidate).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (!asciiHost.Contains('.')) return false;

        if (asciiHost.Equals(ApiEndpoints.LocalhostHost, StringComparison.OrdinalIgnoreCase) ||
            asciiHost.Equals(ApiEndpoints.LoopbackHost, StringComparison.OrdinalIgnoreCase) ||
            asciiHost.Equals(ApiEndpoints.HelpFontVirtualHost, StringComparison.OrdinalIgnoreCase) ||
            asciiHost.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
            asciiHost.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            asciiHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedHost = asciiHost;
        return true;
    }

    private static string FormatDisplayHost(string host, int port)
    {
        var formattedHost = host.Contains(':') ? $"[{host}]" : host;
        return port == DefaultHttpsPort ? formattedHost : $"{formattedHost}:{port}";
    }

    private static void AddDistinct(List<string> list, string message)
    {
        if (!string.IsNullOrWhiteSpace(message) && !list.Contains(message, StringComparer.Ordinal))
        {
            list.Add(message);
        }
    }

    private sealed record CapturedCertificateData(
        string Subject,
        string SubjectCommonName,
        string Issuer,
        string IssuerShortName,
        string ChainSummary,
        string? SanSummary,
        string SerialNumber,
        string Sha256Thumbprint,
        DateTime NotBeforeUtc,
        DateTime NotAfterUtc,
        string SignatureAlgorithm,
        string PublicKeyDescription,
        string CertificateTypeCode,
        string CertificateType,
        IReadOnlyList<string> ValidationErrors);

    private sealed record QuicProbeOutcome(
        bool IsSupported,
        bool TimedOut,
        string? Summary);

    private sealed record TlsProbeOutcome(
        CapturedCertificateData? Capture,
        string? ProtocolDisplay,
        string? CipherSuiteDisplay,
        QuicProbeOutcome? QuicOutcome,
        string? Error);
}
