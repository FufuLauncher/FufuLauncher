using System.Text.Json.Serialization;

namespace FufuLauncher.Services.Security;

internal enum CookieVaultError
{
    None,
    KeyMissing,
    KeyUnreadable,
    KeyInvalid,
    KeyDecryptFailed,
    KeyMismatch,
    FileFormat,
    FileDecryptFailed,
    HwidUnavailable
}

internal sealed class CookieFileEnvelope
{
    [JsonPropertyName("format")]
    public string Format
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("version")]
    public int Version
    {
        get;
        set;
    }

    [JsonPropertyName("algorithm")]
    public string Algorithm
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("keyProtection")]
    public string KeyProtection
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("keyId")]
    public string KeyId
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("nonce")]
    public string Nonce
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("tag")]
    public string Tag
    {
        get;
        set;
    } = string.Empty;

    [JsonPropertyName("ciphertext")]
    public string Ciphertext
    {
        get;
        set;
    } = string.Empty;
}

internal sealed class CookieVaultException : Exception
{
    public CookieVaultException(CookieVaultError error, string message)
        : base(message)
    {
        Error = error;
    }

    public CookieVaultException(CookieVaultError error, string message, Exception innerException)
        : base(message, innerException)
    {
        Error = error;
    }

    public CookieVaultError Error
    {
        get;
    }
}
