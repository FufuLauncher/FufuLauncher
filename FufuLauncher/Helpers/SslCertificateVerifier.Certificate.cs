using System.Formats.Asn1;
using System.Net;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FufuLauncher.Helpers;

public static partial class SslCertificateVerifier
{
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
}
