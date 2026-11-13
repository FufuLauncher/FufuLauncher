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
                            $"trust package root certificate does not match the pinned root ({package.RootThumbprintSha256}), rejected";
                        DebugLog.Info("CodeSigningTrustService", $"[TrustService] {_lastLoadError}");
                        package.Dispose();
                        continue;
                    }

                    _package = package;
                    return _package;
                }
                catch (Exception ex)
                {
                    _lastLoadError = $"trust package verification failed ({directory}): {ex.Message}";
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
                    ? "the app does not ship a pinned root certificate (Assets/Trust/ModsRootCA.cer), trust package cannot be verified"
                    : LastLoadError ?? "no trust package passed verification"
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
            return new TrustOperationResult { Ok = false, Message = "信任包不可用，拒绝安装证书" };
        }

        if (scope == TrustStoreScope.CurrentUser && IsInstalled(package, TrustStoreScope.LocalMachine))
        {
            return new TrustOperationResult
            {
                Ok = false,
                Message = "本机信任已安装并覆盖当前用户，无需在当前用户范围重复安装"
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

        var scopeText = scope == TrustStoreScope.CurrentUser ? "current user" : "machine";
        DebugLog.Info("CodeSigningTrustService",
            $"[TrustService] install for {scope}: added={added}, denied={denied}, error={lastError}");

        if (denied)
        {
            return new TrustOperationResult
            {
                Ok = false,
                NeedsElevation = true,
                Message = string.IsNullOrWhiteSpace(lastError)
                    ? "需要管理员权限才能完成证书安装"
                    : $"需要管理员权限才能完成证书安装（{lastError}）"
            };
        }

        if (lastError != null)
        {
            return new TrustOperationResult { Ok = false, Message = $"安装失败：{lastError}" };
        }

        MarkScopeInstalled(package, scope);

        return new TrustOperationResult
        {
            Ok = true,
            Message = added == 0
                ? "平台证书已存在，无需重复安装"
                : $"已安装平台证书（{scopeText}）"
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
            return new TrustOperationResult { Ok = false, Message = "信任包不可用，无法定位需要卸载的证书" };
        }

        if (scope == TrustStoreScope.CurrentUser && IsInstalled(package, TrustStoreScope.LocalMachine))
        {
            return new TrustOperationResult
            {
                Ok = false,
                Message = "当前用户的信任由本机安装提供，请在本机范围卸载"
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
                    ? "当前用户范围没有由本应用安装的证书，无需卸载"
                    : "本机范围没有由本应用安装的证书，无需卸载"
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

        var scopeText = scope == TrustStoreScope.CurrentUser ? "current user" : "machine";
        DebugLog.Info("CodeSigningTrustService",
            $"[TrustService] uninstall for {scope}: removed={removed}, denied={denied}, error={lastError}");

        if (denied)
        {
            return new TrustOperationResult
            {
                Ok = false,
                NeedsElevation = true,
                Message = string.IsNullOrWhiteSpace(lastError)
                    ? "需要管理员权限才能完成证书卸载"
                    : $"需要管理员权限才能完成证书卸载（{lastError}）"
            };
        }

        if (lastError != null)
        {
            return new TrustOperationResult { Ok = false, Message = $"卸载失败：{lastError}" };
        }

        MarkScopeUninstalled(scope);

        return new TrustOperationResult
        {
            Ok = true,
            Message = removed == 0
                ? "平台证书未安装，无需卸载"
                : $"已卸载平台证书（{scopeText}）"
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
                Message = "应用未内置根证书（Assets/Trust/ModsRootCA.cer），拒绝从网络接受任何信任包"
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
                return new TrustOperationResult { Ok = false, Message = "同步端点未返回有效的文件清单" };
            }

            foreach (var file in index.Files)
            {
                if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Contains("..", StringComparison.Ordinal))
                {
                    return new TrustOperationResult { Ok = false, Message = "同步端点返回了非法文件名，已终止" };
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
                        Message = $"文件 {file.Name} 哈希校验失败（期望 {file.Sha256}，实际 {actualHash}），已拒绝本次同步"
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
                    Message = "同步到的信任包根证书与应用内置根证书不一致，已拒绝（可能需要更新客户端）"
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
                Message = $"信任包已更新（release {index.ReleaseId}，吊销 {verified.Manifest.RevokedSerialNumbers.Count} 条）"
            };
        }
        catch (Exception ex)
        {
            DebugLog.Info("CodeSigningTrustService", $"[TrustService] Trust package sync failed: {ex}");
            return new TrustOperationResult { Ok = false, Message = $"同步失败：{ex.Message}" };
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