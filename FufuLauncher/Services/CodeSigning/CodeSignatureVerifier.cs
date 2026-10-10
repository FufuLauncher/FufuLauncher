/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace FufuLauncher.Services.CodeSigning;

public static class CodeSignatureVerifier
{
    public static ModTrustResult VerifyFile(string filePath, VerifiedTrustPackage? package)
    {
        if (package == null)
        {
            return new ModTrustResult
            {
                FilePath = filePath,
                Status = ModTrustStatus.PackageUnavailable
            };
        }

        var trustResult = WinTrustInterop.VerifyFileIntegrity(filePath);
        var digestVerified = WinTrustInterop.IsContentDigestVerified(trustResult);
        var osTrusted = trustResult == 0;

        X509Certificate2? leaf = TryReadLeaf(filePath);
        if (leaf == null)
        {
            return new ModTrustResult
            {
                FilePath = filePath,
                Status = ModTrustStatus.Unsigned,
                OsChainTrusted = osTrusted
            };
        }

        try
        {
            var thumbprint = ManifestSignatureVerifier.Sha256Thumbprint(leaf);
            var serial = Convert.ToHexString(leaf.GetSerialNumber());
            var result = new ModTrustResult
            {
                FilePath = filePath,
                Status = ModTrustStatus.Untrusted,
                SignerSubject = leaf.Subject,
                SignerId = CodeSigningPolicy.TryReadSignerId(leaf, package.SignerIdPrefix),
                SerialNumberHex = serial,
                ThumbprintSha256 = thumbprint,
                OsChainTrusted = osTrusted
            };

            if (!digestVerified)
            {
                result.Details.Add(trustResult == WinTrustInterop.TRUST_E_BAD_DIGEST
                    ? "signature does not match the file content (file may be tampered)"
                    : $"signature verification failed (WinVerifyTrust=0x{trustResult:X8})");
                return result.Clone(ModTrustStatus.Tampered);
            }

            if (package.Manifest.AdditionalTrustedLeafThumbprintsSha256.Any(t =>
                    t.Equals(thumbprint, StringComparison.OrdinalIgnoreCase)))
            {
                result.Details.Add("thumbprint matches the additional allow list in the trust manifest");
                return result.Clone(ModTrustStatus.TrustedAllowlisted);
            }

            using (var chain = new X509Chain())
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.Add(package.Root);
                chain.ChainPolicy.ExtraStore.Add(package.Issuer);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                chain.ChainPolicy.ApplicationPolicy.Add(new Oid(CodeSigningPolicy.CodeSigningEku));

                if (!chain.Build(leaf))
                {
                    result.Details.Add("certificate chain does not reach the platform root certificate: " +
                                       string.Join("; ", chain.ChainStatus.Select(s => s.StatusInformation.Trim())));
                    return result;
                }
            }

            if (package.IsRevoked(result.SerialNumberHex))
            {
                result.Details.Add("certificate is revoked by the platform");
                return result.Clone(ModTrustStatus.Revoked);
            }

            if (!CodeSigningPolicy.IsLeafPolicyCompliant(leaf, package.Manifest.MaxLeafValidityDays,
                    out var violations))
            {
                foreach (var violation in violations) result.Details.Add(violation);
                return result.Clone(ModTrustStatus.PolicyViolation);
            }

            if (!osTrusted)
            {
                result.Details.Add(
                    "OS trust chain not established (root certificate not installed); verified against the platform root certificate instead");
            }

            result.Details.Add("issued by the platform code signing CA and compliant with policy");
            if (!string.IsNullOrEmpty(result.SignerId))
            {
                result.Details.Add($"signer ID: {result.SignerId}");
            }

            return result.Clone(ModTrustStatus.TrustedPlatform);
        }
        finally
        {
            leaf.Dispose();
        }
    }

    private static X509Certificate2? TryReadLeaf(string filePath)
    {
        try
        {
            var certificate = X509Certificate.CreateFromSignedFile(filePath);
            return new X509Certificate2(certificate);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CodeSignatureVerifier] 读取签名证书失败 {filePath}: {ex.Message}");
            return null;
        }
    }
}