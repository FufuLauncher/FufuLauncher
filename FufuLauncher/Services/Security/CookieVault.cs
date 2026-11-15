using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Security;

internal static partial class CookieVault
{
    internal const string FormatName = "FufuLauncher.CookieVault";
    private const int FormatVersion = 1;
    private const string AlgorithmName = "AES-256-GCM";
    private const string KeyProtectionName = "DPAPI";
    private const string IntermediateKeyProtectionName = "DPAPI+HWID";
    private const string LegacyKeyProtectionName = "DPAPI-CurrentUser";
    private const int KeySize = 32;
    private const int KeyIdSize = 8;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int LengthSize = 4;
    private const byte KeyFileVersion = 2;
    private const byte LegacyKeyFileVersion = 1;
    private const byte KeyFlagDpapi = 1;
    private const byte KeyFlagHwid = 2;
    private const int KeyHeaderSize = 4 + 1 + KeyIdSize;
    private const string BinaryFormatName = "FufuLauncher.CookieVault.Binary";
    private const int BinaryFormatVersion = 1;
    private const int MachineMagicSize = 8;
    private const int LegacyBinaryMagicSize = 4;
    private const int BinaryHeaderSize = MachineMagicSize + 1 + KeyIdSize + NonceSize + TagSize;
    private const int LegacyBinaryHeaderSize = LegacyBinaryMagicSize + 1 + KeyIdSize + NonceSize + TagSize;
    private const int HwidIterations = 100_000;
    private static readonly byte[] KeyMagic = Encoding.UTF8.GetBytes("FLVK");
    private static readonly byte[] LegacyBinaryMagic = Encoding.UTF8.GetBytes("FLCK");
    private static readonly Lazy<byte[]> MachineMagic = new(CreateMachineMagic);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FufuLauncher.CookieVault.DPAPI.v1");
    private static readonly byte[] HwidSalt = Encoding.UTF8.GetBytes("FufuLauncher.CookieVault.HWID.v1");
    private static readonly object KeySync = new();

    public static bool IsEncryptedFile(byte[] data) =>
        IsBinaryEnvelope(data) || IsLegacyJsonEnvelope(data);

    internal static bool IsLegacyJsonEnvelope(byte[] data)
    {
        try
        {
            using var document = JsonDocument.Parse(data);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                   && root.TryGetProperty("format", out var format)
                   && format.ValueKind == JsonValueKind.String
                   && string.Equals(format.GetString(), FormatName, StringComparison.Ordinal)
                   && root.TryGetProperty("nonce", out var nonce)
                   && nonce.ValueKind == JsonValueKind.String
                   && root.TryGetProperty("tag", out var tag)
                   && tag.ValueKind == JsonValueKind.String
                   && root.TryGetProperty("ciphertext", out var ciphertext)
                   && ciphertext.ValueKind == JsonValueKind.String;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsBinaryEnvelope(byte[] data) =>
        IsMachineBinaryEnvelope(data) || IsLegacyBinaryEnvelope(data);

    private static bool IsMachineBinaryEnvelope(byte[] data) =>
        data.Length > BinaryHeaderSize
        && data.AsSpan(0, MachineMagicSize).SequenceEqual(MachineMagic.Value);

    internal static bool IsLegacyBinaryEnvelope(byte[] data) =>
        !IsMachineBinaryEnvelope(data)
        && data.Length > LegacyBinaryHeaderSize
        && data.AsSpan(0, LegacyBinaryMagicSize).SequenceEqual(LegacyBinaryMagic);

    public static byte[] Encrypt(string dataDir, string fileName, byte[] plaintext)
    {
        var key = GetOrCreateKey(dataDir);
        byte[]? associatedData = null;
        var ciphertext = new byte[plaintext.Length];
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var tag = new byte[TagSize];
            associatedData = BuildBinaryAssociatedData(fileName, key.KeyId);

            using (var aes = new AesGcm(key.KeyBytes, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            }

            var magic = MachineMagic.Value;
            var payload = new byte[BinaryHeaderSize + ciphertext.Length];
            magic.CopyTo(payload, 0);
            payload[MachineMagicSize] = BinaryFormatVersion;
            key.KeyId.CopyTo(payload, MachineMagicSize + 1);

            int offset = MachineMagicSize + 1 + KeyIdSize;
            nonce.CopyTo(payload, offset);
            offset += NonceSize;
            tag.CopyTo(payload, offset);
            offset += TagSize;
            ciphertext.CopyTo(payload, offset);

            return payload;
        }
        finally
        {
            Zero(ciphertext);
            Zero(key.KeyBytes);
            Zero(associatedData);
        }
    }

    public static byte[] Decrypt(string dataDir, string fileName, byte[] fileBytes)
    {
        if (IsBinaryEnvelope(fileBytes))
            return DecryptBinary(dataDir, fileName, fileBytes);

        CookieFileEnvelope envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CookieFileEnvelope>(fileBytes)
                       ?? throw new CookieVaultException(CookieVaultError.FileFormat, "CookieVault_FileFormatInvalid".GetLocalized());
        }
        catch (JsonException ex)
        {
            throw new CookieVaultException(CookieVaultError.FileFormat, "CookieVault_FileFormatInvalid".GetLocalized(), ex);
        }

        if (!string.Equals(envelope.Format, FormatName, StringComparison.Ordinal)
            || envelope.Version != FormatVersion
            || !string.Equals(envelope.Algorithm, AlgorithmName, StringComparison.Ordinal)
            || (!string.Equals(envelope.KeyProtection, KeyProtectionName, StringComparison.Ordinal)
                && !string.Equals(envelope.KeyProtection, IntermediateKeyProtectionName, StringComparison.Ordinal)
                && !string.Equals(envelope.KeyProtection, LegacyKeyProtectionName, StringComparison.Ordinal)))
            throw new CookieVaultException(CookieVaultError.FileFormat, "CookieVault_FormatUnsupported".GetLocalized());

        if (string.IsNullOrEmpty(envelope.KeyId)
            || string.IsNullOrEmpty(envelope.Nonce)
            || string.IsNullOrEmpty(envelope.Tag)
            || string.IsNullOrEmpty(envelope.Ciphertext))
            throw new CookieVaultException(CookieVaultError.FileFormat, "CookieVault_ParamsMissing".GetLocalized());

        byte[] keyId;
        byte[] nonce;
        byte[] tag;
        byte[] ciphertext;
        try
        {
            keyId = Convert.FromBase64String(envelope.KeyId);
            nonce = Convert.FromBase64String(envelope.Nonce);
            tag = Convert.FromBase64String(envelope.Tag);
            ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        }
        catch (FormatException ex)
        {
            throw new CookieVaultException(CookieVaultError.FileFormat, "CookieVault_ParamsEncodingInvalid".GetLocalized(), ex);
        }

        if (keyId.Length != KeyIdSize || nonce.Length != NonceSize || tag.Length != TagSize)
            throw new CookieVaultException(CookieVaultError.FileFormat, "CookieVault_ParamsLengthInvalid".GetLocalized());

        var key = LoadKey(dataDir);
        byte[]? associatedData = null;
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(key.KeyId, keyId))
                throw new CookieVaultException(CookieVaultError.KeyMismatch, "CookieVault_KeyMismatch".GetLocalized());

            associatedData = BuildAssociatedData(fileName, keyId, envelope.Version, envelope.KeyProtection!);
            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key.KeyBytes, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                return plaintext;
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                Zero(plaintext);
                throw new CookieVaultException(CookieVaultError.FileDecryptFailed, "CookieVault_IntegrityFailed".GetLocalized(), ex);
            }
        }
        finally
        {
            Zero(key.KeyBytes);
            Zero(associatedData);
        }
    }

    private static byte[] DecryptBinary(string dataDir, string fileName, byte[] fileBytes)
    {
        int magicSize;
        int headerSize;
        if (IsMachineBinaryEnvelope(fileBytes))
        {
            magicSize = MachineMagicSize;
            headerSize = BinaryHeaderSize;
        }
        else if (IsLegacyBinaryEnvelope(fileBytes))
        {
            magicSize = LegacyBinaryMagicSize;
            headerSize = LegacyBinaryHeaderSize;
        }
        else
        {
            throw new CookieVaultException(CookieVaultError.FileFormat, "CookieVault_BinaryFormatInvalid".GetLocalized());
        }

        if (fileBytes.Length <= headerSize
            || fileBytes[magicSize] != BinaryFormatVersion)
            throw new CookieVaultException(CookieVaultError.FileFormat, "CookieVault_BinaryFormatInvalid".GetLocalized());

        int keyIdOffset = magicSize + 1;
        var keyId = fileBytes.AsSpan(keyIdOffset, KeyIdSize).ToArray();
        int nonceOffset = keyIdOffset + KeyIdSize;
        var nonce = fileBytes.AsSpan(nonceOffset, NonceSize).ToArray();
        int tagOffset = nonceOffset + NonceSize;
        var tag = fileBytes.AsSpan(tagOffset, TagSize).ToArray();
        int cipherOffset = tagOffset + TagSize;
        var ciphertext = fileBytes.AsSpan(cipherOffset).ToArray();

        if (ciphertext.Length == 0)
            throw new CookieVaultException(CookieVaultError.FileFormat, "CookieVault_BinaryFormatInvalid".GetLocalized());

        var key = LoadKey(dataDir);
        byte[]? associatedData = null;
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(key.KeyId, keyId))
                throw new CookieVaultException(CookieVaultError.KeyMismatch, "CookieVault_KeyMismatch".GetLocalized());

            associatedData = BuildBinaryAssociatedData(fileName, keyId);
            var plaintext = new byte[ciphertext.Length];
            try
            {
                using var aes = new AesGcm(key.KeyBytes, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
                return plaintext;
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                Zero(plaintext);
                throw new CookieVaultException(CookieVaultError.FileDecryptFailed, "CookieVault_IntegrityFailed".GetLocalized(), ex);
            }
        }
        finally
        {
            Zero(key.KeyBytes);
            Zero(associatedData);
        }
    }

    private static byte[] BuildBinaryAssociatedData(string fileName, byte[] keyId)
    {
        string value = string.Join(",",
            BinaryFormatName,
            BinaryFormatVersion.ToString(),
            AlgorithmName,
            KeyProtectionName,
            Convert.ToBase64String(keyId),
            fileName);
        return Encoding.UTF8.GetBytes(value);
    }

    private static byte[] BuildAssociatedData(string fileName, byte[] keyId, int version, string keyProtection)
    {
        string value = string.Join(",",
            FormatName,
            version.ToString(),
            AlgorithmName,
            keyProtection,
            Convert.ToBase64String(keyId),
            fileName);
        return Encoding.UTF8.GetBytes(value);
    }

    private static void Zero(byte[]? data)
    {
        if (data != null)
            CryptographicOperations.ZeroMemory(data);
    }
}