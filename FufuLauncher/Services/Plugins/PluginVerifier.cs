/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FufuLauncher.Helpers;

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
            DebugLog.Info("PluginVerifier",
                $"[PluginVerifier] No expected hash provided for {description}, skipping verification");
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
            DebugLog.Info("PluginVerifier", $"[PluginVerifier] HASH MISMATCH for {description}:");
            DebugLog.Info("PluginVerifier", $"  Expected: {expectedHash}");
            DebugLog.Info("PluginVerifier", $"  Actual:   {actualHash}");

            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                DebugLog.Info("PluginVerifier", $"[PluginVerifier] Failed to delete bad file: {ex.Message}");
            }

            throw new HashMismatchException(
                "PluginStoreHashMismatch".GetLocalized());
        }

        DebugLog.Info("PluginVerifier", $"[PluginVerifier] Hash verified OK for {description}");
    }

    public static void VerifyLuaHash(string luaScript, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            DebugLog.Info("PluginVerifier", "[PluginVerifier] No expected Lua hash provided, skipping verification");
            return;
        }

        var actualHash = ComputeSha256(luaScript);

        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            DebugLog.Info("PluginVerifier", $"[PluginVerifier] LUA HASH MISMATCH:");
            DebugLog.Info("PluginVerifier", $"  Expected: {expectedHash}");
            DebugLog.Info("PluginVerifier", $"  Actual:   {actualHash}");

            throw new HashMismatchException("PluginStoreLuaHashMismatch".GetLocalized());
        }

        DebugLog.Info("PluginVerifier", "[PluginVerifier] Lua hash verified OK");
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
            ("os.execute", "os.execute() — arbitrary command execution"),
            ("io.popen", "io.popen() — process spawning"),
            ("dofile(", "dofile() — loading external Lua files"),
            ("loadfile(", "loadfile() — loading external Lua files"),
            ("require(", "require() — loading modules"),
            ("os.exit", "os.exit() — terminating the process"),
            ("os.remove", "os.remove() — deleting files outside sandbox"),
            ("os.rename", "os.rename() — moving files outside sandbox"),
            ("os.tmpname", "os.tmpname() — temp file access"),
        };

        foreach (var (pattern, description) in bannedFunctions)
        {
            if (lowerScript.Contains(pattern))
            {
                DebugLog.Info("PluginVerifier", $"[PluginVerifier] SECURITY BLOCK: {description}");
                return SecurityValidationResult.Fail(string.Format("PluginStoreSecurityBannedOp".GetLocalized(),
                    description));
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
                DebugLog.Info("PluginVerifier", "[PluginVerifier] SECURITY BLOCK: Path traversal attempt detected");
                return SecurityValidationResult.Fail("PluginStoreSecurityPathTraversalShort".GetLocalized());
            }
        }

        var httpMatches = System.Text.RegularExpressions.Regex.Matches(
            lowerScript, @"https?://[^\s""']+");
        foreach (System.Text.RegularExpressions.Match match in httpMatches)
        {
            var url = match.Value;
            if (url.StartsWith(ApiEndpoints.HttpLocalhostPrefix) ||
                url.StartsWith(ApiEndpoints.HttpsLocalhostPrefix) ||
                url.Contains(ApiEndpoints.TrustedFufuDomain) ||
                url.Contains(ApiEndpoints.TrustedFufuSkinDomain) ||
                url.Contains(ApiEndpoints.TrustedPluginGithubPrefix))
            {
                continue;
            }

            if (url.StartsWith("http://") && !url.StartsWith(ApiEndpoints.HttpLocalhostPrefix))
            {
                DebugLog.Info("PluginVerifier", $"[PluginVerifier] WARNING: Non-HTTPS URL detected: {url}");
            }
        }

        const int maxScriptLength = 100_000;
        if (luaScript.Length > maxScriptLength)
        {
            DebugLog.Info("PluginVerifier",
                $"[PluginVerifier] SECURITY BLOCK: Script too large ({luaScript.Length} bytes)");
            return SecurityValidationResult.Fail("PluginStoreSecurityScriptTooLarge".GetLocalized());
        }

        var base64Pattern = new System.Text.RegularExpressions.Regex(@"[A-Za-z0-9+/]{200,}={0,2}");
        if (base64Pattern.IsMatch(luaScript))
        {
            DebugLog.Info("PluginVerifier",
                "[PluginVerifier] WARNING: Long base64-like string detected, possible obfuscation");
        }

        DebugLog.Info("PluginVerifier", "[PluginVerifier] Lua security scan PASSED");
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
    public HashMismatchException(string message) : base(message)
    {
    }

    public HashMismatchException(string message, Exception inner) : base(message, inner)
    {
    }
}

public class SecurityViolationException : Exception
{
    public SecurityViolationException(string message) : base(message)
    {
    }
}

public class SecurityValidationResult
{
    public bool IsValid
    {
        get;
    }

    public string? Reason
    {
        get;
    }

    private SecurityValidationResult(bool isValid, string? reason = null)
    {
        IsValid = isValid;
        Reason = reason;
    }

    public static SecurityValidationResult Pass() => new(true);
    public static SecurityValidationResult Fail(string reason) => new(false, reason);
}