using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace FufuLauncher.Services.Security;

internal static partial class CookieVault
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        IntPtr lpVolumeNameBuffer,
        uint nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        IntPtr lpFileSystemNameBuffer,
        uint nFileSystemNameSize);

    private static (byte[] Nonce, byte[] Tag, byte[] Ciphertext) WrapHwid(byte[] keyId, byte[] keyBytes)
    {
        var hwidKey = DeriveHwidKey(keyId);
        var associatedData = BuildHwidAssociatedData(keyId);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var tag = new byte[TagSize];
        var ciphertext = new byte[keyBytes.Length];
        try
        {
            using var aes = new AesGcm(hwidKey, TagSize);
            aes.Encrypt(nonce, keyBytes, ciphertext, tag, associatedData);
            return (nonce, tag, ciphertext);
        }
        catch
        {
            Zero(ciphertext);
            throw;
        }
        finally
        {
            Zero(hwidKey);
            Zero(associatedData);
        }
    }

    private static byte[] UnwrapHwid(ParsedKeyFile parsed)
    {
        var hwidKey = DeriveHwidKey(parsed.KeyId);
        var associatedData = BuildHwidAssociatedData(parsed.KeyId);
        var plaintext = new byte[parsed.HwidCipher!.Length];
        try
        {
            try
            {
                using var aes = new AesGcm(hwidKey, TagSize);
                aes.Decrypt(parsed.HwidNonce!, parsed.HwidCipher, parsed.HwidTag!, plaintext, associatedData);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException)
            {
                Zero(plaintext);
                throw new CookieVaultException(CookieVaultError.FileDecryptFailed, "HWID 密钥完整性校验失败", ex);
            }

            if (plaintext.Length != KeySize)
            {
                Zero(plaintext);
                throw new CookieVaultException(CookieVaultError.KeyInvalid, "HWID 解保护的 Cookie 密钥长度无效");
            }

            return plaintext;
        }
        finally
        {
            Zero(hwidKey);
            Zero(associatedData);
        }
    }

    private static byte[] CreateMachineMagic()
    {
        string identity = TryGetSystemVolumeSerial(out uint serial)
            ? serial.ToString("X8")
            : Environment.MachineName;
        var source = Encoding.UTF8.GetBytes($"FufuLauncher.CookieVault.Magic.v1|{identity}");
        try
        {
            var hash = SHA256.HashData(source);
            try
            {
                return hash.AsSpan(0, MachineMagicSize).ToArray();
            }
            finally
            {
                Zero(hash);
            }
        }
        finally
        {
            Zero(source);
        }
    }

    private static byte[] DeriveHwidKey(byte[] keyId)
    {
        if (!TryGetSystemVolumeSerial(out uint serial))
            throw new CookieVaultException(CookieVaultError.HwidUnavailable, "无法读取 C 盘序列号");

        var password = Encoding.UTF8.GetBytes($"FufuLauncher.HWID.v1|{serial:X8}");
        var salt = new byte[HwidSalt.Length + keyId.Length];
        HwidSalt.CopyTo(salt, 0);
        keyId.CopyTo(salt, HwidSalt.Length);
        try
        {
            using var derive = new Rfc2898DeriveBytes(password, salt, HwidIterations, HashAlgorithmName.SHA256);
            return derive.GetBytes(KeySize);
        }
        finally
        {
            Zero(password);
            Zero(salt);
        }
    }

    private static bool TryGetSystemVolumeSerial(out uint serial)
    {
        if (GetVolumeInformation("C:\\", IntPtr.Zero, 0, out serial, out _, out _, IntPtr.Zero, 0))
            return true;

        string? systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
        if (!string.IsNullOrEmpty(systemRoot)
            && !string.Equals(systemRoot, "C:\\", StringComparison.OrdinalIgnoreCase)
            && GetVolumeInformation(systemRoot, IntPtr.Zero, 0, out serial, out _, out _, IntPtr.Zero, 0))
            return true;

        serial = 0;
        return false;
    }

    private static byte[] BuildHwidAssociatedData(byte[] keyId)
    {
        return Encoding.UTF8.GetBytes(string.Join(",",
            FormatName,
            "HWID.v1",
            Convert.ToBase64String(keyId)));
    }
}