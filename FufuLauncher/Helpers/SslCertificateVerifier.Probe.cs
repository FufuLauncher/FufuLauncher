using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FufuLauncher.Helpers;

public static partial class SslCertificateVerifier
{
    [GeneratedRegex(
        @"(?:^|,)\s*(?<token>h3(?:-[a-z0-9]+)?\s*=\s*""[^""]+"")",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AltSvcH3Regex();

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
            connectError = dnsError ??
                           string.Format("SslCheck_ErrDnsFailed".GetLocalized(), "SslCheck_NoDnsIp".GetLocalized());
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
            return await CompleteTlsProbeAsync(sslStream, target, capture, remoteIp, cancellationToken)
                .ConfigureAwait(false);
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
            await tcpClient.ConnectAsync(proxyUri.IdnHost.Trim('[', ']'), proxyUri.Port, timeoutCts.Token)
                .ConfigureAwait(false);
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

            var statusLine = await ReadHttpHeaderLineAndConsumeHeadersAsync(networkStream, timeoutCts.Token)
                .ConfigureAwait(false);
            if (!IsHttpSuccessStatusLine(statusLine))
            {
                return new TlsProbeOutcome(null, null, null, null, statusLine);
            }

            using var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false);
            var options = CreateSslOptions(target.Host, captured => capture = captured);
            await sslStream.AuthenticateAsClientAsync(options, timeoutCts.Token).ConfigureAwait(false);
            return await CompleteTlsProbeAsync(sslStream, target, capture, remoteAddress: null, cancellationToken)
                .ConfigureAwait(false);
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
        var quicOutcome = await ProbeQuicSupportAsync(sslStream, target, remoteAddress, cancellationToken)
            .ConfigureAwait(false);

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
            return new QuicProbeOutcome(IsSupported: true, TimedOut: false,
                Summary: $"Alt-Svc: {altSvc.AdvertisedProtocols}");
        }

        if (remoteAddress != null)
        {
            var udpResult = await ProbeUdpQuicAsync(remoteAddress, target.Port, cancellationToken)
                .ConfigureAwait(false);
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

        return new QuicProbeOutcome(IsSupported: false, TimedOut: altSvc.TimedOut && !altSvc.GotResponse,
            Summary: null);
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
                var read = await sslStream
                    .ReadAsync(buffer.AsMemory(totalRead, maxHeaderBytes - totalRead), httpCts.Token)
                    .ConfigureAwait(false);
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
}
