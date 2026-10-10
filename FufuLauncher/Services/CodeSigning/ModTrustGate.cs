/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.CodeSigning;

public enum ModTrustEnforcement
{
    Off = 0,

    Warn = 1,

    Enforce = 2
}

public sealed class ModTrustDecision
{
    public required bool Allowed
    {
        get;
        init;
    }

    public required ModTrustResult Result
    {
        get;
        init;
    }

    public required ModTrustEnforcement Mode
    {
        get;
        init;
    }

    public string Reason
    {
        get;
        init;
    } = string.Empty;

    public bool ShouldNotify => Mode switch
    {
        ModTrustEnforcement.Off => false,
        ModTrustEnforcement.Warn => !Result.IsAllowed,
        _ => !Allowed
    };
}

public sealed class ModTrustGate
{
    private const string PolicyFileName = "trust-policy.json";
    private const string LegacyPolicyFileName = "mod-trust-policy.json";
    private static readonly object FileLock = new();

    private readonly Func<string, ModTrustResult> _verify;

    public ModTrustGate(CodeSigningTrustService trust)
    {
        ArgumentNullException.ThrowIfNull(trust);
        _verify = filePath => CodeSignatureVerifier.VerifyFile(filePath, trust.GetPackage());
    }

    internal ModTrustGate(Func<string, ModTrustResult> verify)
    {
        ArgumentNullException.ThrowIfNull(verify);
        _verify = verify;
    }

    public static string PolicyFilePath => Path.Combine(AppPaths.SettingsDir, PolicyFileName);

    private static string LegacyPolicyFilePath => Path.Combine(AppPaths.SettingsDir, LegacyPolicyFileName);

    public static ModTrustEnforcement ReadMode()
    {
        try
        {
            var path = File.Exists(PolicyFilePath)
                ? PolicyFilePath
                : File.Exists(LegacyPolicyFilePath)
                    ? LegacyPolicyFilePath
                    : null;

            if (path == null) return ModTrustEnforcement.Off;

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("mode", out var mode))
            {
                if (mode.ValueKind == JsonValueKind.Number && mode.TryGetInt32(out var numeric) &&
                    Enum.IsDefined(typeof(ModTrustEnforcement), numeric))
                {
                    return (ModTrustEnforcement)numeric;
                }

                if (mode.ValueKind == JsonValueKind.String &&
                    Enum.TryParse<ModTrustEnforcement>(mode.GetString(), true, out var parsed) &&
                    Enum.IsDefined(parsed))
                {
                    return parsed;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModTrustGate] 读取策略文件失败: {ex.Message}");
        }

        return ModTrustEnforcement.Off;
    }

    public static void WriteMode(ModTrustEnforcement mode) => TryWriteMode(mode);

    public static bool TryWriteMode(ModTrustEnforcement mode)
    {
        lock (FileLock)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.SettingsDir);
                var json = JsonSerializer.Serialize(new
                {
                    mode = mode.ToString(),
                    updatedUtc = DateTimeOffset.UtcNow.ToString("O"),
                    note = "Off=跳过校验 Warn=仅提示 Enforce=拦截（仅平台签发且符合策略的 DLL 可加载）"
                }, new JsonSerializerOptions { WriteIndented = true });

                var temporary = PolicyFilePath + ".tmp";
                File.WriteAllText(temporary, json);
                if (File.Exists(PolicyFilePath)) File.Replace(temporary, PolicyFilePath, null, true);
                else File.Move(temporary, PolicyFilePath);

                Debug.WriteLine($"[ModTrustGate] 信任策略已设置为 {mode}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ModTrustGate] 写入策略文件失败: {ex.Message}");
                return false;
            }
        }
    }

    public ModTrustResult Evaluate(string filePath) => _verify(filePath);

    public ModTrustDecision EvaluateForLoading(string filePath, ModTrustEnforcement? modeOverride = null)
    {
        var mode = modeOverride ?? ReadMode();
        if (mode == ModTrustEnforcement.Off)
        {
            return new ModTrustDecision
            {
                Allowed = true,
                Result = new ModTrustResult { FilePath = filePath, Status = ModTrustStatus.VerificationSkipped },
                Mode = mode,
                Reason = "trust check disabled"
            };
        }

        var result = Evaluate(filePath);
        var allowed = mode switch
        {
            ModTrustEnforcement.Warn => true,
            _ => result.Status is ModTrustStatus.TrustedPlatform or ModTrustStatus.TrustedAllowlisted
        };

        var reason = Describe(result, mode);

        if (!allowed)
        {
            Debug.WriteLine($"[ModTrustGate] 拒绝加载 {filePath}（{result.Status}）：{reason}");
        }

        return new ModTrustDecision { Allowed = allowed, Result = result, Mode = mode, Reason = reason };
    }

    public static string Describe(ModTrustResult result, ModTrustEnforcement mode)
    {
        var signer = string.IsNullOrWhiteSpace(result.SignerSubject)
            ? "(unsigned)"
            : $"{result.SignerSubject}{(string.IsNullOrWhiteSpace(result.SignerId) ? string.Empty : $" (signer id {result.SignerId})")}";

        return result.Status switch
        {
            ModTrustStatus.VerificationSkipped => "trust check disabled",
            ModTrustStatus.TrustedPlatform => $"signed by the FufuLauncher code signing CA: {signer}",
            ModTrustStatus.TrustedAllowlisted => $"listed in the platform trust allow list: {signer}",
            ModTrustStatus.Unsigned => "unsigned: publisher cannot be verified",
            ModTrustStatus.Tampered => "signature does not match the file content, the file may be tampered",
            ModTrustStatus.Revoked => $"signing certificate is revoked by the platform: {signer}",
            ModTrustStatus.PolicyViolation =>
                $"signing certificate violates the platform policy (basic code signing certificates only): {signer}",
            ModTrustStatus.PackageUnavailable =>
                mode == ModTrustEnforcement.Enforce
                    ? "trust package unavailable, strict mode refuses to load unverified files"
                    : "trust package unavailable, reported without blocking",
            _ => $"signature failed platform trust verification: {signer}"
        };
    }
}