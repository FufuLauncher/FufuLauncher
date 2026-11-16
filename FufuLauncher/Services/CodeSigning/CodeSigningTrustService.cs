/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.CodeSigning;

public enum TrustStoreScope
{
    CurrentUser = 0,
    LocalMachine = 1
}

public sealed class TrustServiceStatus
{
    public bool PackageAvailable
    {
        get;
        init;
    }

    public bool PackageVerified
    {
        get;
        init;
    }

    public bool MatchesPinnedRoot
    {
        get;
        init;
    }

    public string? PackageDirectory
    {
        get;
        init;
    }

    public string? PackageError
    {
        get;
        init;
    }

    public string? RootSubject
    {
        get;
        init;
    }

    public string? RootThumbprintSha256
    {
        get;
        init;
    }

    public DateTime? RootNotAfter
    {
        get;
        init;
    }

    public int RevokedCount
    {
        get;
        init;
    }

    public bool InstalledForCurrentUser
    {
        get;
        init;
    }

    public bool InstalledForLocalMachine
    {
        get;
        init;
    }

    public bool UserScopeManaged
    {
        get;
        init;
    }

    public bool MachineScopeManaged
    {
        get;
        init;
    }

    public bool MachineProvidesTrust
    {
        get;
        init;
    }

    public string? PinnedRootThumbprintSha256
    {
        get;
        init;
    }

    public string? SyncEndpoint
    {
        get;
        init;
    }
}

public sealed class TrustOperationResult
{
    public bool Ok
    {
        get;
        init;
    }

    public string Message
    {
        get;
        init;
    } = string.Empty;

    public bool NeedsElevation
    {
        get;
        init;
    }
}

internal sealed class TrustInstallState
{
    public string? RootThumbprintSha256
    {
        get;
        set;
    }

    public DateTimeOffset? UserInstalledUtc
    {
        get;
        set;
    }

    public DateTimeOffset? MachineInstalledUtc
    {
        get;
        set;
    }
}

public sealed class CodeSigningTrustService
{
    public const string DefaultSyncEndpoint = ApiEndpoints.CertificateSyncUrl;
    public const string RootCerFileName = TrustPackageLoader.RootCerFileName;
    public const string PinnedRootFileName = "ModsRootCA.cer";

    private readonly object _sync = new();
    private VerifiedTrustPackage? _package;
    private DateTime _lastLoadUtc = DateTime.MinValue;
    private string? _lastLoadError;

    public string? LastLoadError
    {
        get
        {
            lock (_sync)
            {
                return _lastLoadError;
            }
        }
    }

    public CodeSigningTrustService()
    {
        SyncEndpoint = DefaultSyncEndpoint;
    }

    public string SyncEndpoint
    {
        get;
        set;
    }

    public static string? PinnedRootCertificatePath
    {
        get
        {
            foreach (var directory in TrustPackageLoader.CandidateDirectories())
            {
                var candidate = Path.Combine(directory, PinnedRootFileName);
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }
    }

    public static string? GetPinnedRootThumbprint()
    {
        var path = PinnedRootCertificatePath;
        if (path == null) return null;

        try
        {
            using var certificate = new X509Certificate2(path);
            return ManifestSignatureVerifier.Sha256Thumbprint(certificate);
        }
        catch (Exception ex)
        {
            DebugLog.Info("CodeSigningTrustService",
                $"[TrustService] Failed to read pinned root certificate: {ex.Message}");
            return null;
        }
    }

    public VerifiedTrustPackage? GetPackage(bool forceReload = false)
    {
        lock (_sync)
        {
            if (!forceReload && _package != null && DateTime.UtcNow - _lastLoadUtc < TimeSpan.FromMinutes(5))
            {
                return _package;
            }

            _package?.Dispose();
            _package = null;
            _lastLoadUtc = DateTime.UtcNow;
            _lastLoadError = null;

            var pin = GetPinnedRootThumbprint();

            foreach (var directory in TrustPackageLoader.CandidateDirectories())
            {
                if (!Directory.Exists(directory)) continue;
                if (!File.Exists(Path.Combine(directory, TrustPackageLoader.ManifestFileName))) continue;

                try
                {
                    var package = TrustPackageLoader.Load(directory, pin);
                    if (!package.MatchesPinnedRoot)
                    {
                        _lastLoadError =
                            string.Format("ModTrust_PkgRootMismatch_Format".GetLocalized(), package.RootThumbprintSha256);
                        DebugLog.Info("CodeSigningTrustService", $"[TrustService] {_lastLoadError}");
                        package.Dispose();
                        continue;
                    }

                    _package = package;
                    return _package;
                }
                catch (Exception ex)
                {
                    _lastLoadError = string.Format("ModTrust_PkgVerifyFailed_Format".GetLocalized(), directory, ex.Message);
                    DebugLog.Info("CodeSigningTrustService", $"[TrustService] {_lastLoadError}");
                }
            }

            return null;
        }
    }

    public TrustServiceStatus GetStatus()
    {
        var pin = GetPinnedRootThumbprint();
        var package = GetPackage();

        if (package == null)
        {
            var anyPackageDir = TrustPackageLoader.CandidateDirectories().FirstOrDefault(Directory.Exists);
            return new TrustServiceStatus
            {
                PackageAvailable = anyPackageDir != null,
                PackageVerified = false,
                MatchesPinnedRoot = false,
                PackageDirectory = anyPackageDir,
                PinnedRootThumbprintSha256 = pin,
                SyncEndpoint = SyncEndpoint,
                PackageError = pin == null
                    ? "ModTrust_NoPinnedRoot".GetLocalized()
                    : LastLoadError ?? "ModTrust_NoVerifiedPkg".GetLocalized()
            };
        }

        var machinePresent = IsInstalled(package, TrustStoreScope.LocalMachine);
        var userPresent = IsInstalled(package, TrustStoreScope.CurrentUser);
        var state = ReadInstallState();
        var stateMatchesRoot = string.Equals(state.RootThumbprintSha256, package.RootThumbprintSha256,
            StringComparison.OrdinalIgnoreCase);

        bool machineManaged = machinePresent && stateMatchesRoot && state.MachineInstalledUtc != null;
        if (machinePresent && !machineManaged)
        {
            state.MachineInstalledUtc = DateTimeOffset.UtcNow;
            state.RootThumbprintSha256 = package.RootThumbprintSha256;
            WriteInstallState(state);
            machineManaged = true;
            DebugLog.Info("CodeSigningTrustService", "[TrustService] adopted the machine scope as managed by this app");
        }

        bool userManaged;
        if (machinePresent)
        {
            userManaged = false;
            if (state.UserInstalledUtc != null)
            {
                state.UserInstalledUtc = null;
                WriteInstallState(state);
            }
        }
        else if (userPresent)
        {
            userManaged = stateMatchesRoot && state.UserInstalledUtc != null;
            if (!userManaged)
            {
                state.UserInstalledUtc = DateTimeOffset.UtcNow;
                state.RootThumbprintSha256 = package.RootThumbprintSha256;
                WriteInstallState(state);
                userManaged = true;
                DebugLog.Info("CodeSigningTrustService",
                    "[TrustService] adopted the current-user scope as managed by this app");
            }
        }
        else
        {
            userManaged = false;
        }

        return new TrustServiceStatus
        {
            PackageAvailable = true,
            PackageVerified = true,
            MatchesPinnedRoot = true,
            PackageDirectory = package.Directory,
            PinnedRootThumbprintSha256 = pin,
            RootSubject = package.Root.Subject,
            RootThumbprintSha256 = package.RootThumbprintSha256,
            RootNotAfter = package.Root.NotAfter,
            RevokedCount = package.Manifest.RevokedSerialNumbers.Count,
            InstalledForCurrentUser = userPresent,
            InstalledForLocalMachine = machinePresent,
            UserScopeManaged = userManaged,
            MachineScopeManaged = machineManaged,
            MachineProvidesTrust = machinePresent,
            SyncEndpoint = SyncEndpoint
        };
    }

    internal static string InstallStatePath => Path.Combine(AppPaths.SettingsDir, "trust-install-state.json");

    private static TrustInstallState ReadInstallState()
    {
        try
        {
            if (!File.Exists(InstallStatePath)) return new TrustInstallState();

            return JsonSerializer.Deserialize<TrustInstallState>(File.ReadAllText(InstallStatePath))
                   ?? new TrustInstallState();
        }
        catch (Exception ex)
        {
            DebugLog.Info("CodeSigningTrustService", $"[TrustService] failed to read install state: {ex.Message}");
            return new TrustInstallState();
        }
    }

    private static void WriteInstallState(TrustInstallState state)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDir);
            var temporary = InstallStatePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(state,
                new JsonSerializerOptions { WriteIndented = true }));

            if (File.Exists(InstallStatePath)) File.Replace(temporary, InstallStatePath, null, true);
            else File.Move(temporary, InstallStatePath);
        }
        catch (Exception ex)
        {
            DebugLog.Info("CodeSigningTrustService", $"[TrustService] failed to write install state: {ex.Message}");
        }
    }

    private static void MarkScopeInstalled(VerifiedTrustPackage package, TrustStoreScope scope)
    {
        var state = ReadInstallState();
        state.RootThumbprintSha256 = package.RootThumbprintSha256;

        if (scope == TrustStoreScope.CurrentUser) state.UserInstalledUtc = DateTimeOffset.UtcNow;
        else state.MachineInstalledUtc = DateTimeOffset.UtcNow;

        WriteInstallState(state);
    }

    private static void MarkScopeUninstalled(TrustStoreScope scope)
    {
        var state = ReadInstallState();
        if (scope == TrustStoreScope.CurrentUser) state.UserInstalledUtc = null;
        else state.MachineInstalledUtc = null;

        WriteInstallState(state);
    }

    public static bool IsInstalled(VerifiedTrustPackage package, TrustStoreScope scope)
    {
        return IsCertificateInstalled(package.Root, StoreName.Root, scope, bySha256: true) &&
               IsCertificateInstalled(package.Issuer, StoreName.CertificateAuthority, scope, bySha256: true);
    }

    private static bool IsCertificateInstalled(
        X509Certificate2 certificate, StoreName storeName, TrustStoreScope scope, bool bySha256)
    {
        var location = scope == TrustStoreScope.CurrentUser ? StoreLocation.CurrentUser : StoreLocation.LocalMachine;
        try
        {
            using var store = new X509Store(storeName, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);

            var expected = bySha256
                ? ManifestSignatureVerifier.Sha256Thumbprint(certificate)
                : certificate.Thumbprint.ToUpperInvariant();

            return store.Certificates
                .Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false)
                .Cast<X509Certificate2>()
                .Any(c => string.Equals(ManifestSignatureVerifier.Sha256Thumbprint(c), expected,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            DebugLog.Info("CodeSigningTrustService",
                $"[TrustService] Failed to query certificate store ({storeName}/{scope}): {ex.Message}");
            return false;
        }
    }

    public TrustOperationResult InstallRoot(TrustStoreScope scope)
    {
        var package = GetPackage(true);
        if (package == null)
        {
            return new TrustOperationResult { Ok = false, Message = "ModTrust_InstallPkgUnavailable".GetLocalized() };
        }

        if (scope == TrustStoreScope.CurrentUser && IsInstalled(package, TrustStoreScope.LocalMachine))
        {
            return new TrustOperationResult
            {
                Ok = false,
                Message = "ModTrust_MachineAlreadyCoversUser".GetLocalized()
            };
        }

        var added = 0;
        var denied = false;
        string? lastError = null;

        TryStoreOperation(scope, "install", () =>
        {
            if (AddToStore(package.Root, StoreName.Root, scope)) added++;
            if (AddToStore(package.Issuer, StoreName.CertificateAuthority, scope)) added++;
        }, ref denied, ref lastError);

        var scopeText = scope == TrustStoreScope.CurrentUser ? "ModTrust_ScopeUser".GetLocalized() : "ModTrust_ScopeMachine".GetLocalized();
        DebugLog.Info("CodeSigningTrustService",
            $"[TrustService] install for {scope}: added={added}, denied={denied}, error={lastError}");

        if (denied)
        {
            return new TrustOperationResult
            {
                Ok = false,
                NeedsElevation = true,
                Message = string.IsNullOrWhiteSpace(lastError)
                    ? "ModTrust_AdminRequiredInstall".GetLocalized()
                    : string.Format("ModTrust_AdminRequiredInstallDetail_Format".GetLocalized(), lastError)
            };
        }

        if (lastError != null)
        {
            return new TrustOperationResult { Ok = false, Message = string.Format("ModTrust_InstallFailedMsg_Format".GetLocalized(), lastError) };
        }

        MarkScopeInstalled(package, scope);

        return new TrustOperationResult
        {
            Ok = true,
            Message = added == 0
                ? "ModTrust_CertAlreadyInstalled".GetLocalized()
                : string.Format("ModTrust_CertInstalledScope_Format".GetLocalized(), scopeText)
        };
    }

    private static void TryStoreOperation(
        TrustStoreScope scope, string action, Action operation, ref bool denied, ref string? lastError)
    {
        try
        {
            operation();
        }
        catch (Exception ex) when (IsAccessDenied(ex))
        {
            denied = true;
            lastError = ex.Message;
            DebugLog.Info("CodeSigningTrustService", $"[TrustService] {action} denied for {scope}: {ex.Message}");
        }
        catch (Exception ex)
        {
            lastError = ex.Message;
            DebugLog.Info("CodeSigningTrustService", $"[TrustService] {action} failed for {scope}: {ex}");
        }
    }

    private static bool IsAccessDenied(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException or System.Security.SecurityException) return true;

            var code = current.HResult & 0xFFFF;
            if (code is 5 or 0x20) return true;

            if (current.Message.Contains("拒绝访问", StringComparison.Ordinal) ||
                current.Message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool AddToStore(X509Certificate2 certificate, StoreName storeName, TrustStoreScope scope)
    {
        using var store = new X509Store(storeName,
            scope == TrustStoreScope.CurrentUser ? StoreLocation.CurrentUser : StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        var existing = store.Certificates
            .Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false)
            .Cast<X509Certificate2>()
            .Any(c => string.Equals(ManifestSignatureVerifier.Sha256Thumbprint(c),
                ManifestSignatureVerifier.Sha256Thumbprint(certificate), StringComparison.OrdinalIgnoreCase));

        if (existing) return false;

        store.Add(certificate);
        return true;
    }

    public TrustOperationResult UninstallRoot(TrustStoreScope scope)
    {
        var package = GetPackage(true);
        if (package == null)
        {
            return new TrustOperationResult { Ok = false, Message = "ModTrust_UninstallPkgUnavailable".GetLocalized() };
        }

        if (scope == TrustStoreScope.CurrentUser && IsInstalled(package, TrustStoreScope.LocalMachine))
        {
            return new TrustOperationResult
            {
                Ok = false,
                Message = "ModTrust_UninstallFromMachine".GetLocalized()
            };
        }

        var state = ReadInstallState();
        var stateMatchesRoot = string.Equals(state.RootThumbprintSha256, package.RootThumbprintSha256,
            StringComparison.OrdinalIgnoreCase);
        var managed = stateMatchesRoot &&
                      (scope == TrustStoreScope.CurrentUser
                          ? state.UserInstalledUtc != null
                          : state.MachineInstalledUtc != null);

        if (!managed)
        {
            return new TrustOperationResult
            {
                Ok = false,
                Message = scope == TrustStoreScope.CurrentUser
                    ? "ModTrust_NoUserCertToUninstall".GetLocalized()
                    : "ModTrust_NoMachineCertToUninstall".GetLocalized()
            };
        }

        var removed = 0;
        var denied = false;
        string? lastError = null;

        TryStoreOperation(scope, "uninstall", () =>
        {
            removed += RemoveFromStore(package.Root, StoreName.Root, scope);
            removed += RemoveFromStore(package.Issuer, StoreName.CertificateAuthority, scope);
        }, ref denied, ref lastError);

        var scopeText = scope == TrustStoreScope.CurrentUser ? "ModTrust_ScopeUser".GetLocalized() : "ModTrust_ScopeMachine".GetLocalized();
        DebugLog.Info("CodeSigningTrustService",
            $"[TrustService] uninstall for {scope}: removed={removed}, denied={denied}, error={lastError}");

        if (denied)
        {
            return new TrustOperationResult
            {
                Ok = false,
                NeedsElevation = true,
                Message = string.IsNullOrWhiteSpace(lastError)
                    ? "ModTrust_AdminRequiredUninstall".GetLocalized()
                    : string.Format("ModTrust_AdminRequiredUninstallDetail_Format".GetLocalized(), lastError)
            };
        }

        if (lastError != null)
        {
            return new TrustOperationResult { Ok = false, Message = string.Format("ModTrust_UninstallFailedMsg_Format".GetLocalized(), lastError) };
        }

        MarkScopeUninstalled(scope);

        return new TrustOperationResult
        {
            Ok = true,
            Message = removed == 0
                ? "ModTrust_CertNotInstalled".GetLocalized()
                : string.Format("ModTrust_CertUninstalledScope_Format".GetLocalized(), scopeText)
        };
    }

    private static int RemoveFromStore(X509Certificate2 certificate, StoreName storeName, TrustStoreScope scope)
    {
        using var store = new X509Store(storeName,
            scope == TrustStoreScope.CurrentUser ? StoreLocation.CurrentUser : StoreLocation.LocalMachine);
        store.Open(OpenFlags.ReadWrite);

        var matches = store.Certificates
            .Find(X509FindType.FindByThumbprint, certificate.Thumbprint, validOnly: false)
            .Cast<X509Certificate2>()
            .Where(c => string.Equals(ManifestSignatureVerifier.Sha256Thumbprint(c),
                ManifestSignatureVerifier.Sha256Thumbprint(certificate), StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var match in matches)
        {
            store.Remove(match);
            DebugLog.Info("CodeSigningTrustService",
                $"[TrustService] removed certificate {match.Subject} ({ManifestSignatureVerifier.Sha256Thumbprint(match)})");
        }

        return matches.Count;
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    #region 从同步端点更新信任包

    public async Task<TrustOperationResult> SyncFromEndpointAsync(
        string? endpoint = null, CancellationToken cancellationToken = default)
    {
        var url = (string.IsNullOrWhiteSpace(endpoint) ? SyncEndpoint : endpoint!).TrimEnd('/');
        var pin = GetPinnedRootThumbprint();
        if (pin == null)
        {
            return new TrustOperationResult
            {
                Ok = false,
                Message = "ModTrust_SyncNoPinnedRoot".GetLocalized()
            };
        }

        var temporary = Path.Combine(Path.GetTempPath(), "FufuLauncher-TrustSync-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(temporary);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("FufuLauncher-TrustSync/1.0");

            var indexJson = await client.GetStringAsync(ApiEndpoints.GetCertificateSyncIndexUrl(url), cancellationToken)
                .ConfigureAwait(false);
            var index = JsonSerializer.Deserialize<TrustIndex>(indexJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (index?.Ok != true || index.Files.Count == 0)
            {
                return new TrustOperationResult { Ok = false, Message = "ModTrust_SyncInvalidIndex".GetLocalized() };
            }

            foreach (var file in index.Files)
            {
                if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Contains("..", StringComparison.Ordinal))
                {
                    return new TrustOperationResult { Ok = false, Message = "ModTrust_SyncIllegalFilename".GetLocalized() };
                }

                var bytes = await client.GetByteArrayAsync(ApiEndpoints.GetCertificateSyncFileUrl(url, file.Name),
                        cancellationToken)
                    .ConfigureAwait(false);

                var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
                if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return new TrustOperationResult
                    {
                        Ok = false,
                        Message = string.Format("ModTrust_SyncHashMismatch_Format".GetLocalized(), file.Name, file.Sha256, actualHash)
                    };
                }

                await File.WriteAllBytesAsync(Path.Combine(temporary, file.Name), bytes, cancellationToken)
                    .ConfigureAwait(false);
            }

            using var verified = TrustPackageLoader.Load(temporary, pin);
            if (!verified.MatchesPinnedRoot)
            {
                return new TrustOperationResult
                {
                    Ok = false,
                    Message = "ModTrust_SyncRootMismatch".GetLocalized()
                };
            }

            var targetDirectory = Path.Combine(AppPaths.RootDir, "Trust");
            var staging = targetDirectory + ".new";
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            foreach (var file in Directory.GetFiles(temporary))
            {
                File.Copy(file, Path.Combine(staging, Path.GetFileName(file)), true);
            }

            if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, true);
            Directory.Move(staging, targetDirectory);

            GetPackage(forceReload: true);
            DebugLog.Info("CodeSigningTrustService",
                $"[TrustService] Trust package updated from {url} to {targetDirectory}");

            return new TrustOperationResult
            {
                Ok = true,
                Message = string.Format("ModTrust_SyncUpdated_Format".GetLocalized(), index.ReleaseId, verified.Manifest.RevokedSerialNumbers.Count)
            };
        }
        catch (Exception ex)
        {
            DebugLog.Info("CodeSigningTrustService", $"[TrustService] Trust package sync failed: {ex}");
            return new TrustOperationResult { Ok = false, Message = string.Format("ModTrust_SyncFailed_Format".GetLocalized(), ex.Message) };
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            }
            catch (Exception ex)
            {
                DebugLog.Info("CodeSigningTrustService",
                    $"[TrustService] Failed to clean temporary directory: {ex.Message}");
            }
        }
    }

    private sealed class TrustIndex
    {
        public bool Ok
        {
            get;
            set;
        }

        public string? ReleaseId
        {
            get;
            set;
        }

        public List<TrustIndexFile> Files
        {
            get;
            set;
        } = new();
    }

    private sealed class TrustIndexFile
    {
        public string Name
        {
            get;
            set;
        } = string.Empty;

        public string Sha256
        {
            get;
            set;
        } = string.Empty;

        public long Size
        {
            get;
            set;
        }
    }

    #endregion
}