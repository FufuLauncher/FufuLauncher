using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;

namespace FufuLauncher.Helpers;

public static partial class SslCertificateVerifier
{
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
            details.Add(new SslCertificateDetail("SslCheck_StackedEndpoints".GetLocalized(),
                FormatStackedEndpoints(target.StackedEndpoints)));

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
        details.Add(new SslCertificateDetail("SslCheck_QuicSupport".GetLocalized(),
            FormatQuicSupport(outcome?.QuicOutcome)));

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
        details.Add(new SslCertificateDetail("PluginSignature_SignatureAlgorithm".GetLocalized(),
            capture.SignatureAlgorithm));
        details.Add(new SslCertificateDetail("PluginSignature_PublicKey".GetLocalized(), capture.PublicKeyDescription));
        details.Add(new SslCertificateDetail("SslCheck_StackedEndpoints".GetLocalized(),
            FormatStackedEndpoints(target.StackedEndpoints)));

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
                ? string.Format("SslCheck_MoreItemsFormat".GetLocalized(), string.Join(", ", formatted),
                    addresses.Length)
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
}