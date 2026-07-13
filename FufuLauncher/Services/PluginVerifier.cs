/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace FufuLauncher.Services;

public static class PluginVerifier
{
    public static string ComputeSha256(Stream stream)
    {
        var originalPos = stream.CanSeek ? stream.Position : 0;
        if (stream.CanSeek) stream.Position = 0;

        var hashBytes = SHA256.HashData(stream);

        if (stream.CanSeek) stream.Position = originalPos;

        return BytesToHex(hashBytes);
    }
    
    public static string ComputeSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hashBytes = SHA256.HashData(bytes);
        return BytesToHex(hashBytes);
    }
    
    public static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return ComputeSha256(stream);
    }
    
    public static void VerifyFileHash(string filePath, string expectedHash, string description = "file")
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            Debug.WriteLine($"[PluginVerifier] No expected hash provided for {description}, skipping verification");
            return;
        }

        string actualHash;
        try
        {
            actualHash = ComputeFileSha256(filePath);
        }
        catch (Exception ex)
        {
            throw new HashMismatchException(
                $"Unable to read {description} for hash verification: {ex.Message}", ex);
        }

        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[PluginVerifier] HASH MISMATCH for {description}:");
            Debug.WriteLine($"  Expected: {expectedHash}");
            Debug.WriteLine($"  Actual:   {actualHash}");
            
            try { File.Delete(filePath); }
            catch (Exception ex) { Debug.WriteLine($"[PluginVerifier] Failed to delete bad file: {ex.Message}"); }

            throw new HashMismatchException(
                $"文件完整性校验失败，下载可能已损坏或被篡改\nExpected: {expectedHash[..16]}...\nActual: {actualHash[..16]}...");
        }

        Debug.WriteLine($"[PluginVerifier] Hash verified OK for {description}");
    }
    
    public static void VerifyLuaHash(string luaScript, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            Debug.WriteLine("[PluginVerifier] No expected Lua hash provided, skipping verification");
            return;
        }

        var actualHash = ComputeSha256(luaScript);

        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[PluginVerifier] LUA HASH MISMATCH:");
            Debug.WriteLine($"  Expected: {expectedHash}");
            Debug.WriteLine($"  Actual:   {actualHash}");

            throw new HashMismatchException("安装脚本完整性校验失败，脚本可能已被篡改");
        }

        Debug.WriteLine("[PluginVerifier] Lua hash verified OK");
    }
    
    public static SecurityValidationResult ValidateLuaSecurity(string luaScript)
    {
        if (string.IsNullOrWhiteSpace(luaScript))
        {
            return SecurityValidationResult.Fail("Lua script is empty.");
        }

        var lowerScript = luaScript.ToLowerInvariant();
        
        var bannedFunctions = new (string Pattern, string Description)[]
        {
            ("os.execute",    "os.execute() — arbitrary command execution"),
            ("io.popen",      "io.popen() — process spawning"),
            ("dofile(",       "dofile() — loading external Lua files"),
            ("loadfile(",     "loadfile() — loading external Lua files"),
            ("require(",      "require() — loading modules"),
            ("os.exit",       "os.exit() — terminating the process"),
            ("os.remove",     "os.remove() — deleting files outside sandbox"),
            ("os.rename",     "os.rename() — moving files outside sandbox"),
            ("os.tmpname",    "os.tmpname() — temp file access"),
        };

        foreach (var (pattern, description) in bannedFunctions)
        {
            if (lowerScript.Contains(pattern))
            {
                Debug.WriteLine($"[PluginVerifier] SECURITY BLOCK: {description}");
                return SecurityValidationResult.Fail($"脚本包含禁止的操作 ({description})。");
            }
        }
        
        if (lowerScript.Contains("\"..\"") ||
            lowerScript.Contains("'..'") ||
            lowerScript.Contains("..\\\\") ||
            lowerScript.Contains("../"))
        {
            var hasPathTraversal = false;
            
            if (lowerScript.Contains("..\\\\") || lowerScript.Contains("../"))
            {
                hasPathTraversal = true;
            }

            if (hasPathTraversal)
            {
                Debug.WriteLine("[PluginVerifier] SECURITY BLOCK: Path traversal attempt detected");
                return SecurityValidationResult.Fail("路径穿越尝试");
            }
        }
        
        var httpMatches = System.Text.RegularExpressions.Regex.Matches(
            lowerScript, @"https?://[^\s""']+");
        foreach (System.Text.RegularExpressions.Match match in httpMatches)
        {
            var url = match.Value;
            if (url.StartsWith("http://localhost") ||
                url.StartsWith("https://localhost") ||
                url.Contains("fu1.fun") ||
                url.Contains("philia093.cyou") ||
                url.Contains("github.com/codecubist"))
            {
                continue;
            }
            
            if (url.StartsWith("http://") && !url.StartsWith("http://localhost"))
            {
                Debug.WriteLine($"[PluginVerifier] WARNING: Non-HTTPS URL detected: {url}");
            }
        }
        
        const int maxScriptLength = 100_000;
        if (luaScript.Length > maxScriptLength)
        {
            Debug.WriteLine($"[PluginVerifier] SECURITY BLOCK: Script too large ({luaScript.Length} bytes)");
            return SecurityValidationResult.Fail("脚本过大");
        }
        
        var base64Pattern = new System.Text.RegularExpressions.Regex(@"[A-Za-z0-9+/]{200,}={0,2}");
        if (base64Pattern.IsMatch(luaScript))
        {
            Debug.WriteLine("[PluginVerifier] WARNING: Long base64-like string detected, possible obfuscation");
        }

        Debug.WriteLine("[PluginVerifier] Lua security scan PASSED");
        return SecurityValidationResult.Pass();
    }
    
    public static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}

public class HashMismatchException : Exception
{
    public HashMismatchException(string message) : base(message) { }
    public HashMismatchException(string message, Exception inner) : base(message, inner) { }
}

public class SecurityViolationException : Exception
{
    public SecurityViolationException(string message) : base(message) { }
}

public class SecurityValidationResult
{
    public bool IsValid { get; }
    public string? Reason { get; }

    private SecurityValidationResult(bool isValid, string? reason = null)
    {
        IsValid = isValid;
        Reason = reason;
    }

    public static SecurityValidationResult Pass() => new(true);
    public static SecurityValidationResult Fail(string reason) => new(false, reason);
}
