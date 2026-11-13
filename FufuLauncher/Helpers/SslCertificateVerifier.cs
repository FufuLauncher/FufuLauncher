using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Formats.Asn1;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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

    private static readonly HashSet<string> EvPolicyOids = new(StringComparer.Ordinal)
    {
        "2.23.140.1.1",
        "2.16.840.1.114412.2.1",
        "2.16.840.1.113733.1.7.23.6",
        "2.16.840.1.113733.1.7.48.1",
        "1.3.6.1.4.1.14370.1.6",
        "1.3.6.1.4.1.6449.1.2.1.5.1",
        "1.3.6.1.4.1.4146.1.1",
        "2.16.840.1.114028.10.1.2",
        "2.16.840.1.114413.1.7.23.3",
        "2.16.840.1.114414.1.7.23.3",
        "1.3.6.1.4.1.8024.0.2.100.1.2",
        "2.16.756.1.89.1.2.1.1",
        "1.2.616.1.113527.2.5.1.1",
        "2.16.578.1.26.1.3.3",
        "1.3.159.1.17.1",
        "2.16.840.1.114404.1.1.2.4.1",
        "1.2.392.200091.100.721.1",
        "1.3.6.1.4.1.34697.2.1",
        "1.3.6.1.4.1.34697.2.2",
        "1.3.6.1.4.1.34697.2.3",
        "1.3.6.1.4.1.34697.2.4",
        "1.3.6.1.4.1.4788.2.202.1",
        "1.3.6.1.4.1.14777.6.1.1",
        "1.3.6.1.4.1.14777.6.1.2",
        "2.16.156.112554.3"
    };

    [GeneratedRegex(
        @"(?:^|,)\s*(?<token>h3(?:-[a-z0-9]+)?\s*=\s*""[^""]+"")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AltSvcH3Regex();

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

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
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

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
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

    private static async Task<SslCertificateInfo> VerifyEndpointAsync(
        SslEndpointTarget target,
        CancellationToken cancellationToken)
    {
        IPAddress[] resolvedAddresses = Array.Empty<IPAddress>();
        string? dnsError = null;

        if (IPAddress.TryParse(target.Host, out var literalIp))
        {
            resolvedAddresses = [literalIp];
        }
        else
        {
            using var dnsCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            dnsCts.CancelAfter(DnsTimeout);
            try
            {
                resolvedAddresses = (await Dns.GetHostAddressesAsync(target.Host, dnsCts.Token).ConfigureAwait(false))
                    .Distinct()
                    .ToArray();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                dnsError = "SslCheck_ErrTimeout".GetLocalized();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                dnsError = string.Format("SslCheck_ErrDnsFailed".GetLocalized(), ex.Message);
            }
        }

        TlsProbeOutcome? outcome = null;
        string? connectError = null;
        string? viaProxyDisplay = null;

        if (resolvedAddresses.Length > 0)
        {
            var direct = await ProbeDirectAsync(target, resolvedAddresses, cancellationToken).ConfigureAwait(false);
            if (direct.Capture != null || direct.Error == null)
            {
                outcome = direct;
            }
            else
            {
                connectError = direct.Error;
            }
        }
        else
        {
            connectError = dnsError ?? string.Format("SslCheck_ErrDnsFailed".GetLocalized(), "SslCheck_NoDnsIp".GetLocalized());
        }

        if ((outcome == null || outcome.Capture == null) &&
            !cancellationToken.IsCancellationRequested &&
            TryGetSystemHttpProxy(target, out var proxyUri))
        {
            var proxied = await ProbeViaHttpProxyAsync(target, proxyUri, cancellationToken).ConfigureAwait(false);
            if (proxied.Capture != null || outcome == null)
            {
                outcome = proxied;
                if (proxied.Error != null && proxied.Capture == null)
                {
                    connectError = proxied.Error;
                }
            }

            if (proxied.Capture != null)
            {
                viaProxyDisplay = FormatDisplayHost(proxyUri.IdnHost.Trim('[', ']'), proxyUri.Port);
            }
        }

        return BuildResult(target, resolvedAddresses, dnsError, viaProxyDisplay, outcome, connectError);
    }

    private static async Task<TlsProbeOutcome> ProbeDirectAsync(
        SslEndpointTarget target,
        IPAddress[] addresses,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectTimeout);

        CapturedCertificateData? capture = null;
        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(addresses, target.Port, timeoutCts.Token).ConfigureAwait(false);
            using var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);

            var options = CreateSslOptions(target.Host, captured => capture = captured);
            await sslStream.AuthenticateAsClientAsync(options, timeoutCts.Token).ConfigureAwait(false);
            var remoteIp = (tcpClient.Client.RemoteEndPoint as IPEndPoint)?.Address;
            return await CompleteTlsProbeAsync(sslStream, target, capture, remoteIp, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new TlsProbeOutcome(capture, null, null, null, "SslCheck_ErrTimeout".GetLocalized());
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return new TlsProbeOutcome(capture, null, null, null, message);
        }
    }

    private static async Task<TlsProbeOutcome> ProbeViaHttpProxyAsync(
        SslEndpointTarget target,
        Uri proxyUri,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ConnectTimeout);

        CapturedCertificateData? capture = null;
        try
        {
            using var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(proxyUri.IdnHost.Trim('[', ']'), proxyUri.Port, timeoutCts.Token).ConfigureAwait(false);
            var networkStream = tcpClient.GetStream();

            var authority = target.Host.Contains(':')
                ? $"[{target.Host}]:{target.Port}"
                : $"{target.Host}:{target.Port}";

            var requestBuilder = new StringBuilder();
            requestBuilder.Append($"CONNECT {authority} HTTP/1.1\r\n");
            requestBuilder.Append($"Host: {authority}\r\n");

            var cred = HttpClient.DefaultProxy.Credentials?.GetCredential(proxyUri, "Basic");
            if (cred != null && !string.IsNullOrEmpty(cred.UserName))
            {
                var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{cred.UserName}:{cred.Password}"));
                requestBuilder.Append($"Proxy-Authorization: Basic {token}\r\n");
            }

            requestBuilder.Append("\r\n");
            var requestBytes = Encoding.ASCII.GetBytes(requestBuilder.ToString());
            await networkStream.WriteAsync(requestBytes, timeoutCts.Token).ConfigureAwait(false);
            await networkStream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);

            var statusLine = await ReadHttpHeaderLineAndConsumeHeadersAsync(networkStream, timeoutCts.Token).ConfigureAwait(false);
            if (!IsHttpSuccessStatusLine(statusLine))
            {
                return new TlsProbeOutcome(null, null, null, null, statusLine);
            }

            using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
            var options = CreateSslOptions(target.Host, captured => capture = captured);
            await sslStream.AuthenticateAsClientAsync(options, timeoutCts.Token).ConfigureAwait(false);
            return await CompleteTlsProbeAsync(sslStream, target, capture, remoteAddress: null, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return new TlsProbeOutcome(capture, null, null, null, "SslCheck_ErrTimeout".GetLocalized());
        }
        catch (Exception ex)
        {
            var message = ex.InnerException?.Message ?? ex.Message;
            return new TlsProbeOutcome(capture, null, null, null, message);
        }
    }

    private static async Task<TlsProbeOutcome> CompleteTlsProbeAsync(
        SslStream sslStream,
        SslEndpointTarget target,
        CapturedCertificateData? capture,
        IPAddress? remoteAddress,
        CancellationToken cancellationToken)
    {
        var protocolDisplay = FormatProtocol(sslStream.SslProtocol);
        var cipherSuiteDisplay = TryGetCipherSuite(sslStream);
        var quicOutcome = await ProbeQuicSupportAsync(sslStream, target, remoteAddress, cancellationToken).ConfigureAwait(false);

        return new TlsProbeOutcome(capture, protocolDisplay, cipherSuiteDisplay, quicOutcome, null);
    }

    private static async Task<QuicProbeOutcome> ProbeQuicSupportAsync(
        SslStream sslStream,
        SslEndpointTarget target,
        IPAddress? remoteAddress,
        CancellationToken cancellationToken)
    {
        var altSvc = await ProbeHttpAltSvcAsync(sslStream, target, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(altSvc.AdvertisedProtocols))
        {
            return new QuicProbeOutcome(IsSupported: true, TimedOut: false, Summary: $"Alt-Svc: {altSvc.AdvertisedProtocols}");
        }

        if (remoteAddress != null)
        {
            var udpResult = await ProbeUdpQuicAsync(remoteAddress, target.Port, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(udpResult.Versions))
            {
                return new QuicProbeOutcome(IsSupported: true, TimedOut: false, Summary: udpResult.Versions);
            }

            if (!altSvc.GotResponse && (altSvc.TimedOut || udpResult.TimedOut))
            {
                return new QuicProbeOutcome(IsSupported: false, TimedOut: true, Summary: null);
            }

            return new QuicProbeOutcome(IsSupported: false, TimedOut: false, Summary: null);
        }

        return new QuicProbeOutcome(IsSupported: false, TimedOut: altSvc.TimedOut && !altSvc.GotResponse, Summary: null);
    }

    private static async Task<(bool GotResponse, bool TimedOut, string? AdvertisedProtocols)> ProbeHttpAltSvcAsync(
        SslStream sslStream,
        SslEndpointTarget target,
        CancellationToken cancellationToken)
    {
        using var httpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        httpCts.CancelAfter(AltSvcProbeTimeout);

        try
        {
            var hostHeader = FormatDisplayHost(target.Host, target.Port);
            var requestText =
                $"HEAD / HTTP/1.1\r\nHost: {hostHeader}\r\nUser-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64)\r\nAccept: */*\r\nConnection: close\r\n\r\n";
            var requestBytes = Encoding.ASCII.GetBytes(requestText);
            await sslStream.WriteAsync(requestBytes, httpCts.Token).ConfigureAwait(false);
            await sslStream.FlushAsync(httpCts.Token).ConfigureAwait(false);

            const int maxHeaderBytes = 16 * 1024;
            var buffer = new byte[maxHeaderBytes];
            var totalRead = 0;

            while (totalRead < maxHeaderBytes)
            {
                var read = await sslStream.ReadAsync(buffer.AsMemory(totalRead, maxHeaderBytes - totalRead), httpCts.Token).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
                if (buffer.AsSpan(0, totalRead).IndexOf("\r\n\r\n"u8) >= 0)
                {
                    break;
                }
            }

            if (totalRead == 0)
            {
                return (false, false, null);
            }

            var headerSection = Encoding.Latin1.GetString(buffer, 0, totalRead);
            var headerEnd = headerSection.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0)
            {
                headerSection = headerSection[..headerEnd];
            }

            var h3Tokens = ParseAltSvcH3Tokens(headerSection);
            return (true, false, h3Tokens);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return (false, true, null);
        }
        catch
        {
            return (false, false, null);
        }
    }

    private static string? ParseAltSvcH3Tokens(string headerSection)
    {
        var tokens = new List<string>();
        var lines = headerSection.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (!line.StartsWith("alt-svc:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[8..].Trim();
            if (string.IsNullOrWhiteSpace(value) || value.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (Match match in AltSvcH3Regex().Matches(value))
            {
                AddDistinct(tokens, match.Groups["token"].Value.Trim());
            }
        }

        return tokens.Count > 0 ? string.Join(", ", tokens) : null;
    }

    private static async Task<(bool TimedOut, string? Versions)> ProbeUdpQuicAsync(
        IPAddress remoteAddress,
        int port,
        CancellationToken cancellationToken)
    {
        if (IPAddress.IsLoopback(remoteAddress) || port is < 1 or > 65535)
        {
            return (false, null);
        }

        using var udpCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        udpCts.CancelAfter(QuicUdpProbeTimeout);

        try
        {
            using var udpClient = new UdpClient(remoteAddress.AddressFamily);
            udpClient.Connect(remoteAddress, port);
            
            var packet = new byte[1200];
            packet[0] = 0xC0;
            packet[1] = 0x0A;
            packet[2] = 0x0A;
            packet[3] = 0x0A;
            packet[4] = 0x0A;
            packet[5] = 8;
            RandomNumberGenerator.Fill(packet.AsSpan(6, 8));
            packet[14] = 8;
            var clientScid = new byte[8];
            RandomNumberGenerator.Fill(clientScid);
            clientScid.CopyTo(packet.AsSpan(15, 8));

            await udpClient.SendAsync(packet, udpCts.Token).ConfigureAwait(false);
            var received = await udpClient.ReceiveAsync(udpCts.Token).ConfigureAwait(false);
            return (false, ParseQuicVersionNegotiation(received.Buffer, clientScid));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return (true, null);
        }
        catch
        {
            return (false, null);
        }
    }

    private static string? ParseQuicVersionNegotiation(byte[] data, ReadOnlySpan<byte> expectedDcid)
    {
        if (data.Length < 11 ||
            (data[0] & 0x80) == 0 ||
            data[1] != 0 ||
            data[2] != 0 ||
            data[3] != 0 ||
            data[4] != 0)
        {
            return null;
        }

        int dcidLen = data[5];
        if (6 + dcidLen >= data.Length)
        {
            return null;
        }

        if (dcidLen != expectedDcid.Length || !data.AsSpan(6, dcidLen).SequenceEqual(expectedDcid))
        {
            return null;
        }

        int scidLen = data[6 + dcidLen];
        var versionsOffset = 7 + dcidLen + scidLen;
        if (versionsOffset + 4 > data.Length)
        {
            return null;
        }

        var versions = new List<string>();
        for (var offset = versionsOffset; offset + 4 <= data.Length; offset += 4)
        {
            var version = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset, 4));
            if (version == 0 || (version & 0x0F0F0F0Fu) == 0x0A0A0A0Au)
            {
                continue;
            }

            var label = version switch
            {
                0x00000001u => "QUIC v1",
                0x6B3343CFu => "QUIC v2",
                0xFF00001Du => "QUIC draft-29",
                _ => $"QUIC 0x{version:x8}"
            };

            AddDistinct(versions, label);
        }

        return versions.Count > 0 ? string.Join(", ", versions) : "QUIC";
    }

    private static bool TryGetSystemHttpProxy(SslEndpointTarget target, out Uri proxyUri)
    {
        proxyUri = null!;
        try
        {
            var proxy = HttpClient.DefaultProxy;
            var destination = new UriBuilder(Uri.UriSchemeHttps, target.Host, target.Port).Uri;
            if (proxy.IsBypassed(destination)) return false;

            var resolved = proxy.GetProxy(destination);
            if (resolved == null || resolved == destination) return false;
            if (!string.Equals(resolved.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            proxyUri = resolved;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> ReadHttpHeaderLineAndConsumeHeadersAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new List<byte>(256);
        var singleByte = new byte[1];
        const int maxHeaderBytes = 16 * 1024;

        while (buffer.Count < maxHeaderBytes)
        {
            var read = await stream.ReadAsync(singleByte.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("Proxy closed connection before completing CONNECT handshake.");
            }

            buffer.Add(singleByte[0]);
            var count = buffer.Count;
            if (count >= 4 &&
                buffer[count - 4] == '\r' &&
                buffer[count - 3] == '\n' &&
                buffer[count - 2] == '\r' &&
                buffer[count - 1] == '\n')
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(buffer.ToArray());
        var lineEnd = headerText.IndexOf("\r\n", StringComparison.Ordinal);
        return lineEnd >= 0 ? headerText[..lineEnd] : headerText.Trim();
    }

    private static bool IsHttpSuccessStatusLine(string statusLine)
    {
        if (string.IsNullOrWhiteSpace(statusLine)) return false;
        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 &&
               parts[0].StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var code) &&
               code is >= 200 and < 300;
    }

    private static SslClientAuthenticationOptions CreateSslOptions(
        string targetHost,
        Action<CapturedCertificateData> onCaptured)
    {
        return new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
            RemoteCertificateValidationCallback = (_, certificate, chain, sslPolicyErrors) =>
            {
                if (certificate != null)
                {
                    onCaptured(ExtractCertificateData(certificate, chain, sslPolicyErrors));
                }
                
                return true;
            }
        };
    }

    private static CapturedCertificateData ExtractCertificateData(
        X509Certificate certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        using var cert2 = certificate as X509Certificate2 ?? new X509Certificate2(certificate);

        X509Chain? ownedFallbackChain = null;
        var effectiveChain = chain;
        if (effectiveChain == null)
        {
            ownedFallbackChain = new X509Chain();
            ownedFallbackChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            ownedFallbackChain.Build(cert2);
            effectiveChain = ownedFallbackChain;
        }

        try
        {
            var validationErrors = CollectValidationErrors(cert2, effectiveChain, sslPolicyErrors);
            var chainDisplay = BuildChainSummary(cert2, effectiveChain);
            var sanDisplay = ExtractSanDisplay(cert2);

            var subjectCommonName = cert2.GetNameInfo(X509NameType.SimpleName, false);
            var issuerShortName = cert2.GetNameInfo(X509NameType.SimpleName, true);
            if (string.IsNullOrWhiteSpace(issuerShortName))
            {
                issuerShortName = cert2.Issuer;
            }

            var (certTypeCode, certTypeDisplay) = DetectCertificateType(cert2);

            return new CapturedCertificateData(
                Subject: cert2.Subject,
                SubjectCommonName: subjectCommonName,
                Issuer: cert2.Issuer,
                IssuerShortName: issuerShortName,
                ChainSummary: chainDisplay,
                SanSummary: sanDisplay,
                SerialNumber: cert2.SerialNumber,
                Sha256Thumbprint: cert2.GetCertHashString(HashAlgorithmName.SHA256),
                NotBeforeUtc: cert2.NotBefore.ToUniversalTime(),
                NotAfterUtc: cert2.NotAfter.ToUniversalTime(),
                SignatureAlgorithm: cert2.SignatureAlgorithm.FriendlyName ?? cert2.SignatureAlgorithm.Value ?? "-",
                PublicKeyDescription: FormatPublicKey(cert2),
                CertificateTypeCode: certTypeCode,
                CertificateType: certTypeDisplay,
                ValidationErrors: validationErrors);
        }
        finally
        {
            ownedFallbackChain?.Dispose();
        }
    }

    private static IReadOnlyList<string> CollectValidationErrors(
        X509Certificate2 cert2,
        X509Chain effectiveChain,
        SslPolicyErrors sslPolicyErrors)
    {
        var errors = new List<string>();
        var nowUtc = DateTime.UtcNow;

        if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
        {
            AddDistinct(errors, "SslCheck_ErrNoCertificate".GetLocalized());
        }

        if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch))
        {
            AddDistinct(errors, "SslCheck_ErrNameMismatch".GetLocalized());
        }

        if (nowUtc < cert2.NotBefore.ToUniversalTime() || nowUtc > cert2.NotAfter.ToUniversalTime())
        {
            AddDistinct(errors, "SslCheck_ErrExpiredOrNotYetValid".GetLocalized());
        }

        var chainStatuses = new List<X509ChainStatus>();
        chainStatuses.AddRange(effectiveChain.ChainStatus);
        foreach (var element in effectiveChain.ChainElements)
        {
            chainStatuses.AddRange(element.ChainElementStatus);
        }

        foreach (var status in chainStatuses)
        {
            if (status.Status is X509ChainStatusFlags.NoError
                or X509ChainStatusFlags.RevocationStatusUnknown
                or X509ChainStatusFlags.OfflineRevocation)
            {
                continue;
            }

            AddDistinct(errors, MapChainStatusFlag(status));
        }

        if (sslPolicyErrors.HasFlag(SslPolicyErrors.RemoteCertificateChainErrors) && errors.Count == 0)
        {
            var hadOnlyRevocationUnknown = effectiveChain.ChainStatus.Length > 0 &&
                effectiveChain.ChainStatus.All(s => s.Status is X509ChainStatusFlags.NoError
                    or X509ChainStatusFlags.RevocationStatusUnknown
                    or X509ChainStatusFlags.OfflineRevocation);

            if (!hadOnlyRevocationUnknown)
            {
                AddDistinct(errors, "SslCheck_ErrChainError".GetLocalized());
            }
        }

        return errors;
    }

    private static string MapChainStatusFlag(X509ChainStatus status)
    {
        var flags = status.Status;
        if (flags.HasFlag(X509ChainStatusFlags.NotTimeValid) || flags.HasFlag(X509ChainStatusFlags.NotTimeNested))
        {
            return "SslCheck_ErrExpiredOrNotYetValid".GetLocalized();
        }

        if (flags.HasFlag(X509ChainStatusFlags.UntrustedRoot))
        {
            return "SslCheck_ErrUntrustedRoot".GetLocalized();
        }

        if (flags.HasFlag(X509ChainStatusFlags.PartialChain))
        {
            return "SslCheck_ErrPartialChain".GetLocalized();
        }

        if (flags.HasFlag(X509ChainStatusFlags.Revoked))
        {
            return "SslCheck_ErrRevoked".GetLocalized();
        }

        var detail = status.StatusInformation?.Trim();
        return string.IsNullOrEmpty(detail) ? flags.ToString() : detail;
    }

    private static string BuildChainSummary(X509Certificate2 leafCert, X509Chain? chain)
    {
        if (chain != null && chain.ChainElements.Count > 0)
        {
            var names = new List<string>(chain.ChainElements.Count);
            foreach (var element in chain.ChainElements)
            {
                var cn = element.Certificate.GetNameInfo(X509NameType.SimpleName, false);
                names.Add(string.IsNullOrWhiteSpace(cn) ? element.Certificate.Subject : cn);
            }

            return string.Join(" → ", names);
        }

        var leafName = leafCert.GetNameInfo(X509NameType.SimpleName, false);
        var issuerName = leafCert.GetNameInfo(X509NameType.SimpleName, true);
        return string.IsNullOrWhiteSpace(issuerName)
            ? (string.IsNullOrWhiteSpace(leafName) ? leafCert.Subject : leafName)
            : $"{leafName} → {issuerName}";
    }

    private static string? ExtractSanDisplay(X509Certificate2 cert2)
    {
        var entries = new List<string>();
        foreach (var ext in cert2.Extensions)
        {
            X509SubjectAlternativeNameExtension? sanExt = ext as X509SubjectAlternativeNameExtension;
            if (sanExt == null && string.Equals(ext.Oid?.Value, "2.5.29.17", StringComparison.Ordinal))
            {
                sanExt = new X509SubjectAlternativeNameExtension(ext.RawData, ext.Critical);
            }

            if (sanExt == null) continue;

            foreach (var dns in sanExt.EnumerateDnsNames())
            {
                entries.Add(dns);
            }

            foreach (var ip in sanExt.EnumerateIPAddresses())
            {
                entries.Add(ip.ToString());
            }
        }

        if (entries.Count == 0) return null;

        if (entries.Count <= MaxDisplaySans)
        {
            return string.Join(", ", entries);
        }

        var head = string.Join(", ", entries.Take(MaxDisplaySans));
        return string.Format("SslCheck_MoreItemsFormat".GetLocalized(), head, entries.Count);
    }

    private static string FormatPublicKey(X509Certificate2 cert2)
    {
        var alg = cert2.PublicKey.Oid.FriendlyName ?? cert2.PublicKey.Oid.Value ?? "Unknown";

        using var rsa = cert2.GetRSAPublicKey();
        if (rsa != null) return $"{alg} ({rsa.KeySize} bit)";

        using var ecdsa = cert2.GetECDsaPublicKey();
        if (ecdsa != null) return $"{alg} ({ecdsa.KeySize} bit)";

        using var dsa = cert2.GetDSAPublicKey();
        if (dsa != null) return $"{alg} ({dsa.KeySize} bit)";

        return alg;
    }

    private static (string Code, string Display) DetectCertificateType(X509Certificate2 cert2)
    {
        var policyOids = ExtractCertificatePolicyOids(cert2);

        var hasOrganization = false;
        var hasEvJurisdiction = false;
        var hasBusinessCategory = false;
        var hasSerialNumber = false;

        foreach (var rdn in cert2.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            if (rdn.HasMultipleElements)
            {
                continue;
            }

            var oid = rdn.GetSingleElementType().Value;
            var value = rdn.GetSingleElementValue();
            if (string.IsNullOrWhiteSpace(oid) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            switch (oid)
            {
                case "2.5.4.10":
                    hasOrganization = true;
                    break;
                case "2.5.4.15":
                    hasBusinessCategory = true;
                    break;
                case "2.5.4.5":
                    hasSerialNumber = true;
                    break;
                case "1.3.6.1.4.1.311.60.2.1.1":
                case "1.3.6.1.4.1.311.60.2.1.2":
                case "1.3.6.1.4.1.311.60.2.1.3":
                    hasEvJurisdiction = true;
                    break;
            }
        }

        if (policyOids.Any(EvPolicyOids.Contains) ||
            hasEvJurisdiction ||
            (hasOrganization && hasBusinessCategory && hasSerialNumber))
        {
            return ("EV", "SslCheck_CertType_EV".GetLocalized());
        }

        if (policyOids.Contains("2.23.140.1.2.2") || hasOrganization)
        {
            return ("OV", "SslCheck_CertType_OV".GetLocalized());
        }

        if (policyOids.Contains("2.23.140.1.2.3"))
        {
            return ("IV", "SslCheck_CertType_IV".GetLocalized());
        }

        if (policyOids.Contains("2.23.140.1.2.1"))
        {
            return ("DV", "SslCheck_CertType_DV".GetLocalized());
        }

        if (string.Equals(cert2.Subject, cert2.Issuer, StringComparison.Ordinal))
        {
            return ("SslCheck_CertTypeShort_SelfSigned".GetLocalized(), "SslCheck_CertType_SelfSigned".GetLocalized());
        }

        return ("SslCheck_CertTypeShort_Unknown".GetLocalized(), "SslCheck_CertType_Unknown".GetLocalized());
    }

    private static HashSet<string> ExtractCertificatePolicyOids(X509Certificate2 cert2)
    {
        var oids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ext in cert2.Extensions)
        {
            if (!string.Equals(ext.Oid?.Value, "2.5.29.32", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var reader = new AsnReader(ext.RawData, AsnEncodingRules.DER);
                var sequence = reader.ReadSequence();
                while (sequence.HasData)
                {
                    var policyInfo = sequence.ReadSequence();
                    var oid = policyInfo.ReadObjectIdentifier();
                    if (!string.IsNullOrWhiteSpace(oid))
                    {
                        oids.Add(oid);
                    }
                }
            }
            catch (AsnContentException)
            {
                // Ignore malformed Certificate Policies extension payloads
            }
        }

        return oids;
    }

    private static SslCertificateInfo BuildResult(
        SslEndpointTarget target,
        IPAddress[] resolvedAddresses,
        string? dnsError,
        string? viaProxyDisplay,
        TlsProbeOutcome? outcome,
        string? connectError)
    {
        var details = new List<SslCertificateDetail>();

        var ipDisplay = FormatResolvedIpDisplay(resolvedAddresses, dnsError, viaProxyDisplay);
        var capture = outcome?.Capture;

        if (capture == null)
        {
            var reason = outcome?.Error ?? connectError ?? "SslCheck_ErrNoCertificate".GetLocalized();
            var statusSummary = "SslCheck_ConnectFailed".GetLocalized();

            details.Add(new SslCertificateDetail("SslCheck_ValidationStatus".GetLocalized(), statusSummary));
            details.Add(new SslCertificateDetail("SslCheck_ResolvedIp".GetLocalized(), ipDisplay));
            details.Add(new SslCertificateDetail("SslCheck_FailureReason".GetLocalized(), reason));
            details.Add(new SslCertificateDetail("SslCheck_StackedEndpoints".GetLocalized(), FormatStackedEndpoints(target.StackedEndpoints)));

            return new SslCertificateInfo(
                Host: target.Host,
                Port: target.Port,
                DisplayHost: target.DisplayHost,
                StackedCount: target.StackedEndpoints.Count,
                StackedEndpoints: target.StackedEndpoints,
                HasCertificate: false,
                IsValid: false,
                StatusSummary: statusSummary,
                CertificateSummary: null,
                ErrorMessage: reason,
                Details: details);
        }

        var isValid = capture.ValidationErrors.Count == 0 && outcome?.Error == null;
        var allErrors = new List<string>(capture.ValidationErrors);
        if (outcome?.Error != null)
        {
            AddDistinct(allErrors, outcome.Error);
        }

        var validationText = isValid
            ? "SslCheck_Valid".GetLocalized()
            : string.Format("SslCheck_Invalid".GetLocalized(), string.Join("; ", allErrors));

        details.Add(new SslCertificateDetail("SslCheck_ValidationStatus".GetLocalized(), validationText));
        details.Add(new SslCertificateDetail("SslCheck_ResolvedIp".GetLocalized(), ipDisplay));

        if (!string.IsNullOrWhiteSpace(outcome?.ProtocolDisplay))
        {
            var protocolText = string.IsNullOrWhiteSpace(outcome.CipherSuiteDisplay)
                ? outcome.ProtocolDisplay
                : $"{outcome.ProtocolDisplay} ({outcome.CipherSuiteDisplay})";
            details.Add(new SslCertificateDetail("SslCheck_TlsProtocol".GetLocalized(), protocolText));
        }

        details.Add(new SslCertificateDetail("SslCheck_CertType".GetLocalized(), capture.CertificateType));
        details.Add(new SslCertificateDetail("SslCheck_QuicSupport".GetLocalized(), FormatQuicSupport(outcome?.QuicOutcome)));

        details.Add(new SslCertificateDetail("SslCheck_Subject".GetLocalized(), capture.Subject));
        details.Add(new SslCertificateDetail("SslCheck_Issuer".GetLocalized(), capture.Issuer));
        details.Add(new SslCertificateDetail("SslCheck_CertChain".GetLocalized(), capture.ChainSummary));

        if (!string.IsNullOrWhiteSpace(capture.SanSummary))
        {
            details.Add(new SslCertificateDetail("SslCheck_San".GetLocalized(), capture.SanSummary));
        }

        details.Add(new SslCertificateDetail("PluginSignature_SerialNumber".GetLocalized(), capture.SerialNumber));
        details.Add(new SslCertificateDetail("SslCheck_Sha256Thumbprint".GetLocalized(), capture.Sha256Thumbprint));
        details.Add(new SslCertificateDetail(
            "PluginSignature_ValidFrom".GetLocalized(),
            capture.NotBeforeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")));
        details.Add(new SslCertificateDetail(
            "PluginSignature_ValidTo".GetLocalized(),
            FormatValidTo(capture.NotBeforeUtc, capture.NotAfterUtc)));
        details.Add(new SslCertificateDetail("PluginSignature_SignatureAlgorithm".GetLocalized(), capture.SignatureAlgorithm));
        details.Add(new SslCertificateDetail("PluginSignature_PublicKey".GetLocalized(), capture.PublicKeyDescription));
        details.Add(new SslCertificateDetail("SslCheck_StackedEndpoints".GetLocalized(), FormatStackedEndpoints(target.StackedEndpoints)));

        return new SslCertificateInfo(
            Host: target.Host,
            Port: target.Port,
            DisplayHost: target.DisplayHost,
            StackedCount: target.StackedEndpoints.Count,
            StackedEndpoints: target.StackedEndpoints,
            HasCertificate: true,
            IsValid: isValid,
            StatusSummary: validationText,
            CertificateSummary: FormatCertificateSummary(capture, outcome?.QuicOutcome),
            ErrorMessage: allErrors.Count > 0 ? string.Join("; ", allErrors) : null,
            Details: details);
    }

    private static string FormatResolvedIpDisplay(
        IPAddress[] addresses,
        string? dnsError,
        string? viaProxyDisplay)
    {
        string baseIp;
        if (addresses.Length > 0)
        {
            var formatted = addresses
                .OrderBy(ip => ip.AddressFamily != AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .Take(MaxDisplayIps)
                .ToList();

            baseIp = addresses.Length > MaxDisplayIps
                ? string.Format("SslCheck_MoreItemsFormat".GetLocalized(), string.Join(", ", formatted), addresses.Length)
                : string.Join(", ", formatted);
        }
        else
        {
            baseIp = dnsError ?? "SslCheck_NoDnsIp".GetLocalized();
        }

        if (string.IsNullOrWhiteSpace(viaProxyDisplay))
        {
            return baseIp;
        }

        return addresses.Length > 0
            ? string.Format("SslCheck_ViaProxyFormat".GetLocalized(), baseIp, viaProxyDisplay)
            : string.Format("SslCheck_ViaProxyOnlyFormat".GetLocalized(), viaProxyDisplay);
    }

    private static string FormatStackedEndpoints(IReadOnlyList<SslStackedEndpoint> endpoints)
    {
        return string.Join(
            Environment.NewLine,
            endpoints.Select(endpoint => $"{endpoint.FieldName}: {endpoint.Url}"));
    }

    private static string FormatValidTo(DateTime notBeforeUtc, DateTime notAfterUtc)
    {
        var dateText = notAfterUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        var nowUtc = DateTime.UtcNow;

        if (nowUtc < notBeforeUtc)
        {
            return string.Format("SslCheck_NotYetValidFormat".GetLocalized(), dateText);
        }

        if (nowUtc > notAfterUtc)
        {
            var expiredDays = Math.Max(0, (int)(nowUtc - notAfterUtc).TotalDays);
            return string.Format("SslCheck_ExpiredFormat".GetLocalized(), dateText, expiredDays);
        }

        var remainingDays = Math.Max(0, (int)Math.Ceiling((notAfterUtc - nowUtc).TotalDays));
        return string.Format("SslCheck_DaysRemainingFormat".GetLocalized(), dateText, remainingDays);
    }

    private static string FormatDisplayHost(string host, int port)
    {
        var formattedHost = host.Contains(':') ? $"[{host}]" : host;
        return port == DefaultHttpsPort ? formattedHost : $"{formattedHost}:{port}";
    }

    private static string? FormatProtocol(SslProtocols protocol) => protocol switch
    {
        SslProtocols.Tls13 => "TLS 1.3",
        SslProtocols.Tls12 => "TLS 1.2",
        SslProtocols.Tls11 => "TLS 1.1",
#pragma warning disable SYSLIB0039
        SslProtocols.Tls => "TLS 1.0",
#pragma warning restore SYSLIB0039
        SslProtocols.None => null,
        _ => protocol.ToString()
    };

    private static string? TryGetCipherSuite(SslStream sslStream)
    {
        try
        {
            return sslStream.NegotiatedCipherSuite.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static void AddDistinct(List<string> list, string message)
    {
        if (!string.IsNullOrWhiteSpace(message) && !list.Contains(message, StringComparer.Ordinal))
        {
            list.Add(message);
        }
    }

    private static string FormatCertificateSummary(CapturedCertificateData capture, QuicProbeOutcome? quic)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(capture.IssuerShortName))
        {
            parts.Add(capture.IssuerShortName);
        }

        if (!string.IsNullOrWhiteSpace(capture.CertificateTypeCode))
        {
            parts.Add(capture.CertificateTypeCode);
        }

        var quicSummary = quic switch
        {
            null or { TimedOut: true } => "SslCheck_QuicSummary_Timeout".GetLocalized(),
            { IsSupported: true } => "SslCheck_QuicSummary_Supported".GetLocalized(),
            _ => "SslCheck_QuicSummary_Unsupported".GetLocalized()
        };
        parts.Add(quicSummary);

        return string.Join("  ·  ", parts);
    }

    private static string FormatQuicSupport(QuicProbeOutcome? quic)
    {
        if (quic == null || quic.TimedOut)
        {
            return "SslCheck_QuicTimeout".GetLocalized();
        }

        if (quic.IsSupported && !string.IsNullOrWhiteSpace(quic.Summary))
        {
            return string.Format("SslCheck_QuicSupportedFormat".GetLocalized(), quic.Summary);
        }

        return "SslCheck_QuicUnsupported".GetLocalized();
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
