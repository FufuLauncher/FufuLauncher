namespace FufuLauncher.Services.CodeSigning;

public enum ModTrustStatus
{
    PackageUnavailable = 0,

    Unsigned = 1,

    Tampered = 2,

    TrustedPlatform = 3,

    PolicyViolation = 4,

    Revoked = 5,

    Untrusted = 6,

    TrustedAllowlisted = 7,

    VerificationSkipped = 8
}

public sealed class ModTrustResult
{
    public required string FilePath
    {
        get;
        init;
    }

    public required ModTrustStatus Status
    {
        get;
        init;
    }

    public string SignerSubject
    {
        get;
        init;
    } = string.Empty;

    public string? SignerId
    {
        get;
        init;
    }

    public string SerialNumberHex
    {
        get;
        init;
    } = string.Empty;

    public string ThumbprintSha256
    {
        get;
        init;
    } = string.Empty;

    public bool OsChainTrusted
    {
        get;
        init;
    }

    public List<string> Details
    {
        get;
    } = new();

    public bool IsAllowed => Status is ModTrustStatus.TrustedPlatform or ModTrustStatus.TrustedAllowlisted;

    public bool IsPlatformSigned => Status is ModTrustStatus.TrustedPlatform
        or ModTrustStatus.PolicyViolation
        or ModTrustStatus.Revoked;

    internal ModTrustResult Clone(ModTrustStatus status)
    {
        var clone = new ModTrustResult
        {
            FilePath = FilePath,
            Status = status,
            SignerSubject = SignerSubject,
            SignerId = SignerId,
            SerialNumberHex = SerialNumberHex,
            ThumbprintSha256 = ThumbprintSha256,
            OsChainTrusted = OsChainTrusted
        };
        clone.Details.AddRange(Details);
        return clone;
    }
}

