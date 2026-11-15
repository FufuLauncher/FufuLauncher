/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.CodeSigning;

public static class CodeSigningPolicy
{
    public const string CodeSigningEku = "1.3.6.1.5.5.7.3.3";
    public const string AnyExtendedKeyUsage = "2.5.29.37.0";
    public const string CaBrowserForumPolicyArc = "2.23.140";
    public const string SignerIdOuPrefixDefault = "SignerId-";
    public const int MaxLeafValidityDaysDefault = 1185;

    public static bool IsCaBrowserForumPolicyOid(string? oid) =>
        !string.IsNullOrWhiteSpace(oid) &&
        (oid.Equals(CaBrowserForumPolicyArc, StringComparison.Ordinal) ||
         oid.StartsWith(CaBrowserForumPolicyArc + ".", StringComparison.Ordinal));

    public static string? TryReadSignerId(X509Certificate2 certificate, string prefix)
    {
        try
        {
            foreach (var rdn in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
            {
                if (rdn.GetSingleElementType().Value != "2.5.4.11") continue;
                var value = rdn.GetSingleElementValue();
                if (value != null && value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return value[prefix.Length..];
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("CodeSigningPolicy", $"[CodeSigningPolicy] failed to read signer id: {ex.Message}");
        }

        return null;
    }

    public static bool IsLeafPolicyCompliant(
        X509Certificate2 leaf, int maxValidityDays, out IReadOnlyList<string> violations)
    {
        var problems = new List<string>();

        var eku = leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        var ekuOids = eku?.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value)
            .Where(v => !string.IsNullOrEmpty(v)).ToArray() ?? Array.Empty<string?>();

        if (ekuOids.Length == 0)
        {
            problems.Add("ModTrust_Policy_NoEku".GetLocalized());
        }
        else
        {
            foreach (var oid in ekuOids)
            {
                if (oid == CodeSigningEku) continue;
                problems.Add(oid == AnyExtendedKeyUsage
                    ? "ModTrust_Policy_AnyEku".GetLocalized()
                    : string.Format("ModTrust_Policy_NonCodeEku_Format".GetLocalized(), oid));
            }
        }

        var basic = leaf.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        if (basic is { CertificateAuthority: true })
        {
            problems.Add("ModTrust_Policy_IsCa".GetLocalized());
        }

        var keyUsage = leaf.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (keyUsage != null)
        {
            if ((keyUsage.KeyUsages & X509KeyUsageFlags.KeyCertSign) != 0)
            {
                problems.Add("ModTrust_Policy_KeyCertSign".GetLocalized());
            }

            if ((keyUsage.KeyUsages & X509KeyUsageFlags.DigitalSignature) == 0)
            {
                problems.Add("ModTrust_Policy_NoDigitalSig".GetLocalized());
            }
        }

        foreach (X509Extension extension in leaf.Extensions)
        {
            if (extension.Oid?.Value is { } oid && IsCaBrowserForumPolicyOid(oid))
            {
                problems.Add(string.Format("ModTrust_Policy_CabForumOid_Format".GetLocalized(), oid));
            }
        }

        var validityDays = (leaf.NotAfter - leaf.NotBefore).TotalDays;
        if (validityDays > maxValidityDays + 1)
        {
            problems.Add(string.Format("ModTrust_Policy_ValidityExceeded_Format".GetLocalized(), validityDays, maxValidityDays));
        }

        violations = problems;
        return problems.Count == 0;
    }
}

public static class WinTrustInterop
{
    public const uint TRUST_E_NOSIGNATURE = 0x800B0100;
    public const uint TRUST_E_BAD_DIGEST = 0x80096010;
    public const uint CERT_E_EXPIRED = 0x800B0101;
    public const uint CERT_E_UNTRUSTEDTESTROOT = 0x800B0102;
    public const uint CERT_E_CHAINING = 0x800B010A;
    public const uint CERT_E_REVOKED = 0x800B010C;
    public const uint CERT_E_UNTRUSTEDROOT = 0x800B0109;

    private const uint WTD_UI_NONE = 2;
    private const uint WTD_REVOKE_NONE = 0;
    private const uint WTD_CHOICE_FILE = 1;
    private const uint WTD_STATEACTION_IGNORE = 0;
    private const uint WTD_REVOCATION_CHECK_NONE = 0x00000010;
    private const uint WTD_CACHE_ONLY_URL_RETRIEVAL = 0x00001000;

    private static readonly Guid WINTRUST_ACTION_GENERIC_VERIFY_V2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        public uint StructSize;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData
    {
        public uint StructSize;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr FileInfo;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid actionId,
        IntPtr data);

    public static bool IsContentDigestVerified(uint trustResult) =>
        trustResult == 0 ||
        trustResult is CERT_E_CHAINING or CERT_E_UNTRUSTEDROOT or CERT_E_UNTRUSTEDTESTROOT
            or CERT_E_EXPIRED or CERT_E_REVOKED;

    public static uint VerifyFileIntegrity(string filePath)
    {
        IntPtr filePathPtr = IntPtr.Zero;
        IntPtr fileInfoPtr = IntPtr.Zero;
        IntPtr trustDataPtr = IntPtr.Zero;

        try
        {
            filePathPtr = Marshal.StringToHGlobalUni(filePath);

            var fileInfo = new WinTrustFileInfo
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePathPtr,
                FileHandle = IntPtr.Zero,
                KnownSubject = IntPtr.Zero
            };

            fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);

            var trustData = new WinTrustData
            {
                StructSize = (uint)Marshal.SizeOf<WinTrustData>(),
                PolicyCallbackData = IntPtr.Zero,
                SipClientData = IntPtr.Zero,
                UiChoice = WTD_UI_NONE,
                RevocationChecks = WTD_REVOKE_NONE,
                UnionChoice = WTD_CHOICE_FILE,
                FileInfo = fileInfoPtr,
                StateAction = WTD_STATEACTION_IGNORE,
                StateData = IntPtr.Zero,
                UrlReference = IntPtr.Zero,
                ProvFlags = WTD_REVOCATION_CHECK_NONE | WTD_CACHE_ONLY_URL_RETRIEVAL,
                UiContext = 0
            };

            trustDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPtr, false);

            return WinVerifyTrust(IntPtr.Zero, WINTRUST_ACTION_GENERIC_VERIFY_V2, trustDataPtr);
        }
        catch (Exception ex)
        {
            DebugLog.Info("CodeSigningPolicy", $"[WinTrustInterop] Verification failed {filePath}: {ex.Message}");
            return TRUST_E_NOSIGNATURE;
        }
        finally
        {
            if (trustDataPtr != IntPtr.Zero) Marshal.FreeHGlobal(trustDataPtr);
            if (fileInfoPtr != IntPtr.Zero) Marshal.FreeHGlobal(fileInfoPtr);
            if (filePathPtr != IntPtr.Zero) Marshal.FreeHGlobal(filePathPtr);
        }
    }
}

public sealed class ManifestSignatureEnvelope
{
    public int SchemaVersion
    {
        get;
        set;
    }

    public string Algorithm
    {
        get;
        set;
    } = string.Empty;

    public string SignerThumbprintSha256
    {
        get;
        set;
    } = string.Empty;

    public string SignedFile
    {
        get;
        set;
    } = string.Empty;

    public string SignedFileSha256
    {
        get;
        set;
    } = string.Empty;

    public string SignatureBase64
    {
        get;
        set;
    } = string.Empty;

    public string SignedUtc
    {
        get;
        set;
    } = string.Empty;
}

public static class ManifestSignatureVerifier
{
    public const string ExpectedAlgorithm = "RSASSA-PKCS1-v1_5-SHA256";

    public static bool Verify(
        byte[] manifestBytes, string signatureJson, X509Certificate2 signerCertificate, out string reason)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<ManifestSignatureEnvelope>(signatureJson);
            if (envelope == null)
            {
                reason = "ModTrust_Manifest_InvalidEnvelope".GetLocalized();
                return false;
            }

            if (!string.Equals(envelope.Algorithm, ExpectedAlgorithm, StringComparison.Ordinal))
            {
                reason = string.Format("ModTrust_Manifest_UnsupportedAlg_Format".GetLocalized(), envelope.Algorithm);
                return false;
            }

            if (!string.Equals(envelope.SignerThumbprintSha256, Sha256Thumbprint(signerCertificate),
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "ModTrust_Manifest_ThumbprintMismatch".GetLocalized();
                return false;
            }

            if (!string.Equals(envelope.SignedFileSha256, Convert.ToHexString(SHA256.HashData(manifestBytes)),
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "ModTrust_Manifest_HashMismatch".GetLocalized();
                return false;
            }

            using var rsa = signerCertificate.GetRSAPublicKey();
            if (rsa == null)
            {
                reason = "ModTrust_Manifest_NotRsa".GetLocalized();
                return false;
            }

            var signature = Convert.FromBase64String(envelope.SignatureBase64);
            if (!rsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                reason = "ModTrust_Manifest_SigVerifyFailed".GetLocalized();
                return false;
            }

            reason = "ok";
            return true;
        }
        catch (Exception ex)
        {
            reason = string.Format("ModTrust_Manifest_VerifyException_Format".GetLocalized(), ex.Message);
            return false;
        }
    }

    public static string Sha256Thumbprint(X509Certificate2 certificate) =>
        Convert.ToHexString(SHA256.HashData(certificate.RawData));
}