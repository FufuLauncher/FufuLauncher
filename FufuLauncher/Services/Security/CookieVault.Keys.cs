using System.Buffers.Binary;
using System.Security.Cryptography;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Security;

internal static partial class CookieVault
{
    public static void DeleteKey(string dataDir)
    {
        string keyPath = GetKeyPath(dataDir);
        lock (KeySync)
        {
            try
            {
                if (!File.Exists(keyPath))
                    return;

                try
                {
                    long length = new FileInfo(keyPath).Length;
                    if (length > 0 && length <= int.MaxValue)
                    {
                        var scrub = RandomNumberGenerator.GetBytes((int)length);
                        try
                        {
                            using var stream = new FileStream(keyPath, FileMode.Open, FileAccess.Write, FileShare.None);
                            stream.Write(scrub, 0, scrub.Length);
                            stream.Flush(true);
                        }
                        finally
                        {
                            Zero(scrub);
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Info("CookieVault",
                        $"[CookieVault] Failed to overwrite Cookie encryption key: {ex.Message}");
                }

                File.Delete(keyPath);

                string? directory = Path.GetDirectoryName(keyPath);
                if (!string.IsNullOrEmpty(directory)
                    && Directory.Exists(directory)
                    && !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception ex)
            {
                DebugLog.Info("CookieVault", $"[CookieVault] Failed to delete Cookie encryption key: {ex.Message}");
            }
        }
    }

    public static void EnsureKeyProtectionUpgrade(string dataDir)
    {
        lock (KeySync)
        {
            try
            {
                string keyPath = GetKeyPath(dataDir);
                if (!File.Exists(keyPath))
                    return;

                var fileBytes = File.ReadAllBytes(keyPath);
                try
                {
                    var parsed = ParseKeyFile(fileBytes);
                    try
                    {
                        if (parsed.Version == KeyFileVersion
                            && parsed.HwidCipher != null
                            && parsed.DpapiBlob == null)
                        {
                            Zero(UnwrapKeyData(parsed));
                            return;
                        }

                        if (parsed.Version == KeyFileVersion
                            && parsed.DpapiBlob != null
                            && TryUnprotectDpapi(parsed.DpapiBlob, out var existingKey))
                        {
                            Zero(existingKey);
                            return;
                        }

                        var keyBytes = UnwrapKeyData(parsed);
                        if (keyBytes == null)
                            return;

                        try
                        {
                            var payload = BuildKeyFileV2(parsed.KeyId, keyBytes,
                                parsed.Version == LegacyKeyFileVersion);
                            try
                            {
                                WriteKeyFile(keyPath, payload, overwrite: true);
                                DebugLog.Info("CookieVault", "[CookieVault] Cookie encryption key protection upgraded");
                            }
                            finally
                            {
                                Zero(payload);
                            }
                        }
                        finally
                        {
                            Zero(keyBytes);
                        }
                    }
                    finally
                    {
                        parsed.Clear();
                    }
                }
                finally
                {
                    Zero(fileBytes);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Info("CookieVault",
                    $"[CookieVault] Failed to upgrade Cookie encryption key protection: {ex.Message}");
            }
        }
    }

    private static CookieKey GetOrCreateKey(string dataDir)
    {
        string keyPath = GetKeyPath(dataDir);
        lock (KeySync)
        {
            if (File.Exists(keyPath))
            {
                try
                {
                    return LoadKey(dataDir);
                }
                catch (CookieVaultException ex) when (ex.Error is CookieVaultError.KeyMissing
                                                          or CookieVaultError.KeyInvalid
                                                          or CookieVaultError.KeyDecryptFailed)
                {
                    QuarantineKeyFile(keyPath);
                }
            }

            return CreateKey(keyPath);
        }
    }

    private static CookieKey CreateKey(string keyPath)
    {
        string? directory = Path.GetDirectoryName(keyPath);
        if (string.IsNullOrEmpty(directory))
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥路径无效");

        byte[]? keyBytes = null;
        byte[]? payload = null;
        try
        {
            Directory.CreateDirectory(directory);
            keyBytes = RandomNumberGenerator.GetBytes(KeySize);
            var keyId = RandomNumberGenerator.GetBytes(KeyIdSize);
            payload = BuildKeyFileV2(keyId, keyBytes, allowDpapi: true);
            WriteKeyFile(keyPath, payload, overwrite: false);

            var key = new CookieKey(keyBytes, keyId);
            keyBytes = null;
            return key;
        }
        catch (IOException) when (File.Exists(keyPath))
        {
            return LoadKeyFile(keyPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥无法创建", ex);
        }
        finally
        {
            Zero(keyBytes);
            Zero(payload);
        }
    }

    private static CookieKey LoadKey(string dataDir) => LoadKeyFile(GetKeyPath(dataDir));

    private static CookieKey LoadKeyFile(string keyPath)
    {
        byte[] fileBytes;
        try
        {
            fileBytes = File.ReadAllBytes(keyPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CookieVaultException(CookieVaultError.KeyMissing, "Cookie 加密密钥不存在", ex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CookieVaultException(CookieVaultError.KeyUnreadable, "Cookie 加密密钥无法读取", ex);
        }

        try
        {
            var parsed = ParseKeyFile(fileBytes);
            try
            {
                var keyBytes = UnwrapKeyData(parsed);
                if (keyBytes == null)
                    throw new CookieVaultException(CookieVaultError.KeyDecryptFailed,
                        "Cookie 加密密钥无法由 DPAPI 或 HWID 解密");
                return new CookieKey(keyBytes, parsed.KeyId);
            }
            finally
            {
                parsed.Clear();
            }
        }
        finally
        {
            Zero(fileBytes);
        }
    }

    private static ParsedKeyFile ParseKeyFile(byte[] fileBytes)
    {
        if (fileBytes.Length < KeyHeaderSize
            || !fileBytes.AsSpan(0, KeyMagic.Length).SequenceEqual(KeyMagic))
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");

        byte version = fileBytes[KeyMagic.Length];
        var keyId = fileBytes.AsSpan(KeyMagic.Length + 1, KeyIdSize).ToArray();

        if (version == LegacyKeyFileVersion)
        {
            if (fileBytes.Length <= KeyHeaderSize)
                throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
            return new ParsedKeyFile
            {
                Version = version,
                KeyId = keyId,
                DpapiBlob = fileBytes.AsSpan(KeyHeaderSize).ToArray()
            };
        }

        if (version != KeyFileVersion || fileBytes.Length < KeyHeaderSize + 1)
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");

        int offset = KeyHeaderSize;
        byte flags = fileBytes[offset++];
        if ((flags & ~(KeyFlagDpapi | KeyFlagHwid)) != 0)
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥保护标志无效");

        var parsed = new ParsedKeyFile { Version = version, KeyId = keyId };
        try
        {
            if ((flags & KeyFlagDpapi) != 0)
            {
                if (fileBytes.Length < offset + LengthSize)
                    throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
                int length = BinaryPrimitives.ReadInt32LittleEndian(fileBytes.AsSpan(offset, LengthSize));
                offset += LengthSize;
                if (length <= 0 || length > fileBytes.Length - offset)
                    throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
                parsed.DpapiBlob = fileBytes.AsSpan(offset, length).ToArray();
                offset += length;
            }

            if ((flags & KeyFlagHwid) != 0)
            {
                if (fileBytes.Length < offset + NonceSize + TagSize + LengthSize)
                    throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
                parsed.HwidNonce = fileBytes.AsSpan(offset, NonceSize).ToArray();
                offset += NonceSize;
                parsed.HwidTag = fileBytes.AsSpan(offset, TagSize).ToArray();
                offset += TagSize;
                int length = BinaryPrimitives.ReadInt32LittleEndian(fileBytes.AsSpan(offset, LengthSize));
                offset += LengthSize;
                if (length != KeySize || fileBytes.Length < offset + length)
                    throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");
                parsed.HwidCipher = fileBytes.AsSpan(offset, length).ToArray();
                offset += length;
            }

            if (offset != fileBytes.Length
                || (parsed.DpapiBlob == null && parsed.HwidCipher == null))
                throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥文件格式无效");

            return parsed;
        }
        catch
        {
            parsed.Clear();
            throw;
        }
    }

    private static byte[]? UnwrapKeyData(ParsedKeyFile parsed)
    {
        if (parsed.DpapiBlob != null && TryUnprotectDpapi(parsed.DpapiBlob, out var dpapiKey))
            return dpapiKey;

        if (parsed.HwidNonce != null && parsed.HwidTag != null && parsed.HwidCipher != null)
        {
            try
            {
                return UnwrapHwid(parsed);
            }
            catch (Exception ex) when (ex is CryptographicException or CookieVaultException)
            {
                DebugLog.Info("CookieVault", $"[CookieVault] Failed to unprotect HWID key: {ex.Message}");
            }
        }

        return null;
    }

    private static bool TryUnprotectDpapi(byte[] blob, out byte[] keyBytes)
    {
        try
        {
            var key = ProtectedData.Unprotect(blob, Entropy, DataProtectionScope.CurrentUser);
            if (key.Length == KeySize)
            {
                keyBytes = key;
                return true;
            }

            Zero(key);
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException or ArgumentException)
        {
            DebugLog.Info("CookieVault", $"[CookieVault] DPAPI key unprotect failed, trying HWID: {ex.Message}");
        }

        keyBytes = Array.Empty<byte>();
        return false;
    }

    private static byte[] BuildKeyFileV2(byte[] keyId, byte[] keyBytes, bool allowDpapi)
    {
        byte[]? dpapiBlob = null;
        byte[]? hwidNonce = null;
        byte[]? hwidTag = null;
        byte[]? hwidCipher = null;
        try
        {
            if (allowDpapi)
            {
                try
                {
                    dpapiBlob = ProtectedData.Protect(keyBytes, Entropy, DataProtectionScope.CurrentUser);
                }
                catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException)
                {
                    DebugLog.Info("CookieVault",
                        $"[CookieVault] DPAPI protection failed, enabling HWID protection: {ex.Message}");
                }
            }

            try
            {
                var wrapped = WrapHwid(keyId, keyBytes);
                hwidNonce = wrapped.Nonce;
                hwidTag = wrapped.Tag;
                hwidCipher = wrapped.Ciphertext;
            }
            catch (Exception ex) when (ex is CookieVaultException or CryptographicException)
            {
                DebugLog.Info("CookieVault", $"[CookieVault] HWID protection unavailable: {ex.Message}");
            }

            byte flags = 0;
            if (dpapiBlob != null)
                flags |= KeyFlagDpapi;
            if (hwidCipher != null)
                flags |= KeyFlagHwid;
            if (flags == 0)
                throw new CookieVaultException(CookieVaultError.KeyInvalid,
                    "DPAPI 与 HWID 均不可用，无法保护 Cookie 密钥");

            int size = KeyHeaderSize + 1;
            if (dpapiBlob != null)
                size += LengthSize + dpapiBlob.Length;
            if (hwidCipher != null)
                size += NonceSize + TagSize + LengthSize + hwidCipher.Length;

            var payload = new byte[size];
            KeyMagic.CopyTo(payload, 0);
            payload[KeyMagic.Length] = KeyFileVersion;
            keyId.CopyTo(payload, KeyMagic.Length + 1);

            int offset = KeyHeaderSize;
            payload[offset++] = flags;

            if (dpapiBlob != null)
            {
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, LengthSize), dpapiBlob.Length);
                offset += LengthSize;
                dpapiBlob.CopyTo(payload, offset);
                offset += dpapiBlob.Length;
            }

            if (hwidCipher != null)
            {
                hwidNonce!.CopyTo(payload, offset);
                offset += NonceSize;
                hwidTag!.CopyTo(payload, offset);
                offset += TagSize;
                BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(offset, LengthSize), hwidCipher.Length);
                offset += LengthSize;
                hwidCipher.CopyTo(payload, offset);
            }

            return payload;
        }
        finally
        {
            Zero(dpapiBlob);
            Zero(hwidNonce);
            Zero(hwidTag);
            Zero(hwidCipher);
        }
    }

    private static void QuarantineKeyFile(string keyPath)
    {
        try
        {
            if (!File.Exists(keyPath))
                return;

            string quarantinePath = keyPath + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
            File.Move(keyPath, quarantinePath);
            DebugLog.Info("CookieVault",
                $"[CookieVault] Quarantined undecryptable Cookie encryption key: {Path.GetFileName(quarantinePath)}");
        }
        catch (Exception ex)
        {
            DebugLog.Info("CookieVault",
                $"[CookieVault] Failed to quarantine undecryptable Cookie encryption key: {ex.Message}");
            try
            {
                if (File.Exists(keyPath))
                    File.Delete(keyPath);
            }
            catch
            {
            }
        }
    }

    private static void WriteKeyFile(string keyPath, byte[] payload, bool overwrite)
    {
        string tempPath = keyPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       tempPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(true);
            }

            File.Move(tempPath, keyPath, overwrite);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    private static string GetKeyPath(string dataDir)
    {
        if (string.IsNullOrWhiteSpace(dataDir))
            throw new CookieVaultException(CookieVaultError.KeyInvalid, "Cookie 加密密钥目录无效");
        return Path.Combine(dataDir, "secrets", "cookie.key");
    }

    private readonly record struct CookieKey(byte[] KeyBytes, byte[] KeyId);

    private sealed class ParsedKeyFile
    {
        public byte Version
        {
            get;
            init;
        }

        public byte[] KeyId
        {
            get;
            init;
        } = Array.Empty<byte>();

        public byte[]? DpapiBlob
        {
            get;
            set;
        }

        public byte[]? HwidNonce
        {
            get;
            set;
        }

        public byte[]? HwidTag
        {
            get;
            set;
        }

        public byte[]? HwidCipher
        {
            get;
            set;
        }

        public void Clear()
        {
            if (DpapiBlob != null)
                CryptographicOperations.ZeroMemory(DpapiBlob);
            if (HwidNonce != null)
                CryptographicOperations.ZeroMemory(HwidNonce);
            if (HwidTag != null)
                CryptographicOperations.ZeroMemory(HwidTag);
            if (HwidCipher != null)
                CryptographicOperations.ZeroMemory(HwidCipher);
        }
    }
}