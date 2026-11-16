/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using FufuLauncher.Services.CodeSigning;
using FufuLauncher.Helpers;

namespace FufuLauncher;

public static class TrustCertCli
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    private const int AttachParentProcess = -1;

    public static bool IsTrustCertCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "--trust-cert", StringComparison.OrdinalIgnoreCase);

    public static bool IsTrustDiagCommand(string[] args) =>
        args.Length > 0 && string.Equals(args[0], "--trust-diag", StringComparison.OrdinalIgnoreCase);

    public static int Run(string[] args)
    {
        var action = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        var scopeText = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : "user";
        var resultFile = GetOption(args, "--result-file");

        var service = new CodeSigningTrustService();

        try
        {
            TrustOperationResult result;

            switch (action)
            {
                case "install":
                case "uninstall":
                {
                    var scope = scopeText.Equals("machine", StringComparison.OrdinalIgnoreCase)
                        ? TrustStoreScope.LocalMachine
                        : TrustStoreScope.CurrentUser;

                    result = action == "install" ? service.InstallRoot(scope) : service.UninstallRoot(scope);
                    break;
                }

                case "status":
                {
                    var status = service.GetStatus();
                    result = new TrustOperationResult
                    {
                        Ok = status.PackageVerified,
                        Message = JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true })
                    };
                    break;
                }

                case "sync":
                {
                    var url = GetOption(args, "--url");
                    result = service.SyncFromEndpointAsync(url).GetAwaiter().GetResult();
                    break;
                }

                default:
                    result = new TrustOperationResult { Ok = false, Message = string.Format("ModTrust_UnknownAction_Format".GetLocalized(), action) };
                    break;
            }

            WriteResult(resultFile, result);
            WriteConsole($"{(result.Ok ? "OK" : "FAIL")} {action} {scopeText}: {result.Message}");
            return result.Ok ? 0 : 2;
        }
        catch (Exception ex)
        {
            var result = new TrustOperationResult { Ok = false, Message = ex.ToString() };
            WriteResult(resultFile, result);
            WriteConsole($"FAIL {action}: {ex.Message}");
            return 1;
        }
    }

    public static int RunDiagnostics(string[] args)
    {
        var builder = new StringBuilder();
        var outputFile = GetOption(args, "--out")
                         ?? Path.Combine(Path.GetTempPath(), "fufu-trust-diag.txt");

        var service = new CodeSigningTrustService();
        var status = service.GetStatus();
        var package = service.GetPackage(forceReload: true);

        builder.AppendLine("FufuLauncher Code Signing Trust Diagnostics");
        builder.AppendLine($"Time                 : {DateTimeOffset.Now:u}");
        builder.AppendLine(
            $"Pinned root file     : {CodeSigningTrustService.PinnedRootCertificatePath ?? "(not shipped with the app)"}");
        builder.AppendLine($"Pinned root SHA-256  : {status.PinnedRootThumbprintSha256 ?? "(none)"}");
        builder.AppendLine($"Trust package dir    : {status.PackageDirectory ?? "(none)"}");
        builder.AppendLine($"Package verified     : {(status.PackageVerified ? "yes" : "no")} {status.PackageError}");
        builder.AppendLine($"Root subject         : {status.RootSubject ?? "(none)"}");
        builder.AppendLine($"Root SHA-256         : {status.RootThumbprintSha256 ?? "(none)"}");
        builder.AppendLine($"Root not after       : {status.RootNotAfter:u}");
        builder.AppendLine($"Revoked entries      : {status.RevokedCount}");
        builder.AppendLine($"Installed (user)     : {status.InstalledForCurrentUser}");
        builder.AppendLine($"Installed (machine)  : {status.InstalledForLocalMachine}");
        builder.AppendLine($"Sync endpoint        : {status.SyncEndpoint}");
        builder.AppendLine($"Trust mode           : {ModTrustGate.ReadMode()}");
        builder.AppendLine($"Trust policy file    : {ModTrustGate.PolicyFilePath}");
        builder.AppendLine();

        var files = GetOptions(args, "--file");
        var allAllowed = true;

        foreach (var file in files)
        {
            var result = CodeSignatureVerifier.VerifyFile(file, package);
            allAllowed &= result.IsAllowed;

            builder.AppendLine($"File                 : {file}");
            builder.AppendLine($"  Status             : {result.Status}");
            builder.AppendLine($"  Signer             : {result.SignerSubject}");
            builder.AppendLine($"  Signer ID          : {result.SignerId ?? "(none)"}");
            builder.AppendLine($"  Serial number      : {result.SerialNumberHex}");
            builder.AppendLine($"  OS chain trusted   : {result.OsChainTrusted}");
            foreach (var detail in result.Details)
            {
                builder.AppendLine($"  - {detail}");
            }

            builder.AppendLine();
        }

        var report = builder.ToString();
        try
        {
            File.WriteAllText(outputFile, report, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.TrustCert", $"[TrustCli] failed to write diagnostics report: {ex.Message}");
        }

        WriteConsole(report);
        WriteConsole($"Report written to: {outputFile}");
        return files.Count == 0 || allAllowed ? 0 : 3;
    }

    private static void WriteResult(string? path, TrustOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                ok = result.Ok,
                message = result.Message,
                needsElevation = result.NeedsElevation,
                timestampUtc = DateTimeOffset.UtcNow.ToString("O")
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.TrustCert", $"[TrustCli] Failed to write result file: {ex.Message}");
        }
    }

    private static void WriteConsole(string message)
    {
        if (!AttachConsole(AttachParentProcess))
        {
            DebugLog.Info("App.TrustCert", "Trust command output written to console");
        }

        try
        {
            Console.Out.WriteLine(message);
            Console.Out.Flush();
        }
        catch (Exception ex)
        {
            DebugLog.Info("App.TrustCert", $"[TrustCli] Console output failed: {ex.Message}");
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        }

        return null;
    }

    private static List<string> GetOptions(string[] args, string name)
    {
        var values = new List<string>();
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) values.Add(args[i + 1]);
        }

        return values;
    }
}