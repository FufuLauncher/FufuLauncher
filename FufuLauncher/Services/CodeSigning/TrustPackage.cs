/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.CodeSigning;

public sealed class TrustManifest
{
    public int SchemaVersion
    {
        get;
        set;
    }

    public string? Product
    {
        get;
        set;
    }

    public string? Purpose
    {
        get;
        set;
    }

    public string? GeneratedUtc
    {
        get;
        set;
    }

    public string? RootCertificateFile
    {
        get;
        set;
    }

    public string? IssuerCertificateFile
    {
        get;
        set;
    }

    public string? ManifestSignerCertificateFile
    {
        get;
        set;
    }

    public string? RootThumbprintSha256
    {
        get;
        set;
    }

    public string? RootSubject
    {
        get;
        set;
    }

    public string? RootNotAfterUtc
    {
        get;
        set;
    }

    public string? IssuerThumbprintSha256
    {
        get;
        set;
    }

    public string? IssuerSubject
    {
        get;
        set;
    }

    public string? ManifestSignerThumbprintSha256
    {
        get;
        set;
    }

    public List<string> AllowedEkuOids
    {
        get;
        set;
    } = new();

    public string? CustomPolicyOid
    {
        get;
        set;
    }

    public string? SignerIdOuPrefix
    {
        get;
        set;
    }

    public int MaxLeafValidityDays
    {
        get;
        set;
    } = CodeSigningPolicy.MaxLeafValidityDaysDefault;

    public string? RevocationListFile
    {
        get;
        set;
    }

    public string? RevocationListUrl
    {
        get;
        set;
    }

    public List<string> RevokedSerialNumbers
    {
        get;
        set;
    } = new();

    public List<string> AdditionalTrustedLeafThumbprintsSha256
    {
        get;
        set;
    } = new();
}

public sealed class VerifiedTrustPackage : IDisposable
{
    public required string Directory
    {
        get;
        init;
    }

    public required TrustManifest Manifest
    {
        get;
        init;
    }

    public required X509Certificate2 Root
    {
        get;
        init;
    }

    public required X509Certificate2 Issuer
    {
        get;
        init;
    }

    public X509Certificate2? ManifestSigner
    {
        get;
        init;
    }

    public string RootThumbprintSha256
    {
        get;
        init;
    } = string.Empty;

    public string ManifestSignatureReason
    {
        get;
        init;
    } = string.Empty;

    public bool MatchesPinnedRoot
    {
        get;
        init;
    }

    public string SignerIdPrefix =>
        string.IsNullOrWhiteSpace(Manifest.SignerIdOuPrefix)
            ? CodeSigningPolicy.SignerIdOuPrefixDefault
            : Manifest.SignerIdOuPrefix!;

    public bool IsRevoked(string serialNumberHex) =>
        Manifest.RevokedSerialNumbers.Any(s => s.Equals(serialNumberHex, StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        Root.Dispose();
        Issuer.Dispose();
        ManifestSigner?.Dispose();
    }
}

public static class TrustPackageLoader
{
    public const string ManifestFileName = "trust-manifest.json";
    public const string ManifestSignatureFileName = "trust-manifest.json.sig";
    public const string RootCerFileName = "ModsRootCA.cer";
    public const string IssuerCerFileName = "ModsIntermediateCA.cer";
    public const string SignerCerFileName = "ModsManifestSigner.cer";
    public const string CrlFileName = "mods.crl";

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static VerifiedTrustPackage Load(string directory, string? pinnedRootThumbprintSha256 = null)
    {
        var manifestPath = Path.Combine(directory, ManifestFileName);
        var signaturePath = Path.Combine(directory, ManifestSignatureFileName);
        var rootPath = Path.Combine(directory, RootCerFileName);
        var issuerPath = Path.Combine(directory, IssuerCerFileName);
        var signerPath = Path.Combine(directory, SignerCerFileName);

        if (!File.Exists(manifestPath)) throw new TrustPackageException($"missing {ManifestFileName}");
        if (!File.Exists(signaturePath))
            throw new TrustPackageException($"missing {ManifestSignatureFileName} (unsigned manifests are rejected)");
        if (!File.Exists(rootPath)) throw new TrustPackageException($"missing {RootCerFileName}");
        if (!File.Exists(issuerPath)) throw new TrustPackageException($"missing {IssuerCerFileName}");

        var manifestBytes = File.ReadAllBytes(manifestPath);
        TrustManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<TrustManifest>(manifestBytes, ManifestJsonOptions)
                       ?? throw new TrustPackageException("ModTrust_Pkg_ManifestEmpty".GetLocalized());
        }
        catch (JsonException ex)
        {
            throw new TrustPackageException(string.Format("ModTrust_Pkg_ManifestInvalidJson_Format".GetLocalized(), ex.Message));
        }

        var root = new X509Certificate2(rootPath);
        var issuer = new X509Certificate2(issuerPath);
        X509Certificate2? signer = File.Exists(signerPath) ? new X509Certificate2(signerPath) : null;

        try
        {
            var rootThumbprint = ManifestSignatureVerifier.Sha256Thumbprint(root);
            var issuerThumbprint = ManifestSignatureVerifier.Sha256Thumbprint(issuer);

            if (!string.Equals(manifest.RootThumbprintSha256, rootThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustPackageException("ModTrust_Pkg_RootThumbprintMismatch".GetLocalized());
            }

            if (!string.Equals(manifest.IssuerThumbprintSha256, issuerThumbprint, StringComparison.OrdinalIgnoreCase))
            {
                throw new TrustPackageException("ModTrust_Pkg_IssuingThumbprintMismatch".GetLocalized());
            }

            if (root.Subject != root.Issuer)
            {
                throw new TrustPackageException("ModTrust_Pkg_RootNotSelfSigned".GetLocalized());
            }

            if (root.HasPrivateKey)
            {
                throw new TrustPackageException(
                    "ModTrust_Pkg_RootHasPrivateKey".GetLocalized());
            }

            var signatureReason = "ModTrust_Pkg_NoSignerCert".GetLocalized();
            var signatureOk = false;
            if (signer != null)
            {
                signatureOk = ManifestSignatureVerifier.Verify(
                    manifestBytes, File.ReadAllText(signaturePath), signer, out signatureReason);

                if (signatureOk)
                {
                    using var chain = new X509Chain();
                    chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                    chain.ChainPolicy.CustomTrustStore.Add(root);
                    chain.ChainPolicy.ExtraStore.Add(issuer);
                    chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                    chain.ChainPolicy.ApplicationPolicy.Add(new Oid(CodeSigningPolicy.CodeSigningEku));

                    if (!chain.Build(signer))
                    {
                        signatureOk = false;
                        signatureReason =
                            "ModTrust_Pkg_SignerNotChained".GetLocalized();
                    }
                    else if (!string.IsNullOrWhiteSpace(manifest.ManifestSignerThumbprintSha256) &&
                             !string.Equals(manifest.ManifestSignerThumbprintSha256,
                                 ManifestSignatureVerifier.Sha256Thumbprint(signer),
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        signatureOk = false;
                        signatureReason = "ModTrust_Pkg_SignerThumbprintMismatch".GetLocalized();
                    }
                }
            }

            if (!signatureOk)
            {
                throw new TrustPackageException(string.Format("ModTrust_Pkg_ManifestSigInvalid_Format".GetLocalized(), signatureReason));
            }

            var matchesPin = string.IsNullOrWhiteSpace(pinnedRootThumbprintSha256) ||
                             string.Equals(pinnedRootThumbprintSha256, rootThumbprint,
                                 StringComparison.OrdinalIgnoreCase);

            DebugLog.Info("TrustPackage",
                $"[TrustPackage] verified {directory}, root {rootThumbprint}, pinned={matchesPin}");

            return new VerifiedTrustPackage
            {
                Directory = directory,
                Manifest = manifest,
                Root = root,
                Issuer = issuer,
                ManifestSigner = signer,
                RootThumbprintSha256 = rootThumbprint,
                ManifestSignatureReason = signatureReason,
                MatchesPinnedRoot = matchesPin
            };
        }
        catch
        {
            root.Dispose();
            issuer.Dispose();
            signer?.Dispose();
            throw;
        }
    }

    public static IReadOnlyList<string> ReadRevokedSerials(string directory)
    {
        try
        {
            var manifestPath = Path.Combine(directory, ManifestFileName);
            if (!File.Exists(manifestPath)) return Array.Empty<string>();

            var manifest =
                JsonSerializer.Deserialize<TrustManifest>(File.ReadAllBytes(manifestPath), ManifestJsonOptions);
            return manifest?.RevokedSerialNumbers ?? (IReadOnlyList<string>)Array.Empty<string>();
        }
        catch (Exception ex)
        {
            DebugLog.Info("TrustPackage", $"[TrustPackage] failed to read revocation list: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    public static IReadOnlyList<string> CandidateDirectories() => new[]
    {
        Path.Combine(AppPaths.RootDir, "Trust"),
        Path.Combine(AppContext.BaseDirectory, "Assets", "Trust")
    };
}

public sealed class TrustPackageException : Exception
{
    public TrustPackageException(string message) : base(message)
    {
    }
}