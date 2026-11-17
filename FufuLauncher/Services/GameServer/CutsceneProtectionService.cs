/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.GameServer;

internal enum CutsceneLocalState
{
    Installed,
    StubbedLocked,
    MissingOrPartial
}

internal sealed record CutscenePersistentStatus(
    bool Exists,
    string PersistentDirectory,
    string? CurrentResRevision,
    string? CurrentSilenceRevision,
    string? CurrentDataRevision,
    string? DetectedLogVersionTag,
    string? DetectedLogRes,
    string? DetectedLogSilence,
    string? DetectedLogData,
    bool HasBaseRevision,
    int CacheVersionsCount,
    bool HasResRemote,
    bool HasResPersist,
    bool HasDataRemote,
    bool HasDataPersist,
    bool HasSilenceRemote,
    bool HasSilencePersist,
    bool HasBackup,
    DateTimeOffset? BackupTimestamp);

internal sealed record CutscenePersistentBypassReport(
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<string> PromotedFiles,
    IReadOnlyList<string> WrittenRevisions,
    IReadOnlyList<string> Notes,
    string BackupDirectory);

internal static partial class CutsceneProtectionService
{
    private static readonly SecurityIdentifier EveryoneSid = new(WellKnownSidType.WorldSid, null);
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly string[] PersistentRevisionFiles =
    [
        "res_revision",
        "silence_revision",
        "data_revision"
    ];

    private static readonly (string Remote, string Persist)[] PersistentManifestPairs =
    [
        ("data_versions_remote", "data_versions_persist"),
        ("res_versions_remote", "res_versions_persist"),
        ("silence_data_versions_remote", "silence_data_versions_persist")
    ];

    public static string GetDataDirectory(string gameDir, bool isOversea) =>
        Path.Combine(gameDir, isOversea ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR);

    public static string GetPersistentDirectory(string gameDir, bool isOversea) =>
        Path.Combine(GetDataDirectory(gameDir, isOversea), "Persistent");

    public static CutsceneLocalState GetLocalState(string streamingPath, string? persistentPath, long expectedSize)
    {
        long streamingBytes = SafeFileLength(streamingPath);
        long persistentBytes = string.IsNullOrEmpty(persistentPath) ? 0 : SafeFileLength(persistentPath);
        if (streamingBytes == expectedSize || persistentBytes == expectedSize)
            return CutsceneLocalState.Installed;

        if (IsStubbedAndLocked(streamingPath)
            || (!string.IsNullOrEmpty(persistentPath) && IsStubbedAndLocked(persistentPath)))
            return CutsceneLocalState.StubbedLocked;

        return CutsceneLocalState.MissingOrPartial;
    }

    public static bool IsStubbedAndLocked(string path)
    {
        try
        {
            if (!File.Exists(path))
                return false;

            FileAttributes attrs = File.GetAttributes(path);
            if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                return false;

            var info = new FileInfo(path);
            if (info.Length != 0)
                return false;

            return attrs.HasFlag(FileAttributes.ReadOnly) || HasEveryoneDenyWriteRule(info);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public static void LockCleanedAsset(string streamingPath, string? persistentPath, string? persistentRoot)
    {
        StubAndLockFile(streamingPath);

        if (!string.IsNullOrEmpty(persistentPath)
            && !string.IsNullOrEmpty(persistentRoot)
            && Directory.Exists(persistentRoot))
        {
            string? persistentDir = Path.GetDirectoryName(persistentPath);
            if (!string.IsNullOrEmpty(persistentDir))
                Directory.CreateDirectory(persistentDir);
            StubAndLockFile(persistentPath);
        }
    }

    public static void UnlockCleanedAsset(string streamingPath, string? persistentPath, bool deleteStub)
    {
        UnlockFile(streamingPath, deleteStub);
        if (!string.IsNullOrEmpty(persistentPath))
            UnlockFile(persistentPath, deleteStub);
    }

    public static void StubAndLockFile(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            throw new IOException("Invalid asset file path.");
        Directory.CreateDirectory(directory);
        ValidateSafePath(directory);

        UnlockFile(path, deleteFile: false);

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(0);
        }

        // Apply ReadOnly BEFORE adding Deny Write/Delete so SetAttributes does not fail on WriteAttributes denial,
        // and both File.Delete and overwrite attempts are blocked cleanly.
        File.SetAttributes(path, FileAttributes.ReadOnly);
        var info = new FileInfo(path);
        FileSecurity security = info.GetAccessControl();
        security.AddAccessRule(CreateDenyRule());
        info.SetAccessControl(security);
    }

    public static void UnlockFile(string path, bool deleteFile = false)
    {
        if (!File.Exists(path))
            return;

        FileAttributes attrs = File.GetAttributes(path);
        if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new IOException("Cutscene_UnsafePath".GetLocalized());

        var info = new FileInfo(path);
        try
        {
            FileSecurity security = info.GetAccessControl();
            if (RemoveEveryoneDenyRules(security))
                info.SetAccessControl(security);
        }
        catch (UnauthorizedAccessException)
        {
            TryClearReadOnly(path);
            FileSecurity security = info.GetAccessControl();
            if (RemoveEveryoneDenyRules(security))
                info.SetAccessControl(security);
        }

        TryClearReadOnly(path);

        if (deleteFile && File.Exists(path))
            File.Delete(path);
    }

    public static bool TryParseVersionString(string? input, out string res, out string silence, out string data)
    {
        res = string.Empty;
        silence = string.Empty;
        data = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        Match match = VersionTagRegex().Match(input.Trim());
        if (!match.Success)
            return false;

        res = match.Groups[1].Value;
        silence = match.Groups[2].Value;
        data = match.Groups[3].Value;
        return true;
    }

    public static CutscenePersistentStatus InspectPersistent(string gameDir, bool isOversea)
    {
        string persistentDir = GetPersistentDirectory(gameDir, isOversea);
        string backupDir = GetBackupDirectory(gameDir, isOversea);
        bool hasBackup = Directory.Exists(backupDir) && Directory.EnumerateFiles(backupDir).Any();
        DateTimeOffset? backupTime = hasBackup ? Directory.GetLastWriteTimeUtc(backupDir) : null;

        var (logTag, logR, logS, logD) = TryDetectRevisionsFromPlayerLog(isOversea);

        if (!Directory.Exists(persistentDir))
        {
            return new CutscenePersistentStatus(
                Exists: false,
                PersistentDirectory: persistentDir,
                CurrentResRevision: null,
                CurrentSilenceRevision: null,
                CurrentDataRevision: null,
                DetectedLogVersionTag: logTag,
                DetectedLogRes: logR,
                DetectedLogSilence: logS,
                DetectedLogData: logD,
                HasBaseRevision: false,
                CacheVersionsCount: 0,
                HasResRemote: false,
                HasResPersist: false,
                HasDataRemote: false,
                HasDataPersist: false,
                HasSilenceRemote: false,
                HasSilencePersist: false,
                HasBackup: hasBackup,
                BackupTimestamp: backupTime);
        }

        int cacheCount = Directory.EnumerateFiles(persistentDir, "cache_versions_*", SearchOption.TopDirectoryOnly).Count();
        return new CutscenePersistentStatus(
            Exists: true,
            PersistentDirectory: persistentDir,
            CurrentResRevision: ReadRevisionValue(Path.Combine(persistentDir, "res_revision")),
            CurrentSilenceRevision: ReadRevisionValue(Path.Combine(persistentDir, "silence_revision")),
            CurrentDataRevision: ReadRevisionValue(Path.Combine(persistentDir, "data_revision")),
            DetectedLogVersionTag: logTag,
            DetectedLogRes: logR,
            DetectedLogSilence: logS,
            DetectedLogData: logD,
            HasBaseRevision: File.Exists(Path.Combine(persistentDir, "base_revision")),
            CacheVersionsCount: cacheCount,
            HasResRemote: File.Exists(Path.Combine(persistentDir, "res_versions_remote")),
            HasResPersist: File.Exists(Path.Combine(persistentDir, "res_versions_persist")),
            HasDataRemote: File.Exists(Path.Combine(persistentDir, "data_versions_remote")),
            HasDataPersist: File.Exists(Path.Combine(persistentDir, "data_versions_persist")),
            HasSilenceRemote: File.Exists(Path.Combine(persistentDir, "silence_data_versions_remote")),
            HasSilencePersist: File.Exists(Path.Combine(persistentDir, "silence_data_versions_persist")),
            HasBackup: hasBackup,
            BackupTimestamp: backupTime);
    }

    public static async Task<CutscenePersistentBypassReport> ApplyPersistentBypassAsync(
        string gameDir,
        bool isOversea,
        string resRevision,
        string silenceRevision,
        string dataRevision,
        CancellationToken token)
    {
        resRevision = resRevision.Trim();
        silenceRevision = silenceRevision.Trim();
        dataRevision = dataRevision.Trim();
        if (!RevisionDigitsRegex().IsMatch(resRevision)
            || !RevisionDigitsRegex().IsMatch(silenceRevision)
            || !RevisionDigitsRegex().IsMatch(dataRevision))
        {
            throw new InvalidOperationException("Cutscene_BypassInvalidNumbers".GetLocalized());
        }

        await GameServerOperation.EnsureGameClosedAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        string persistentDir = GetPersistentDirectory(gameDir, isOversea);
        if (!Directory.Exists(persistentDir))
            throw new InvalidOperationException("Cutscene_BypassNoPersistent".GetLocalized());
        ValidateSafePath(persistentDir);

        string backupDir = GetBackupDirectory(gameDir, isOversea);
        CreatePersistentBackup(persistentDir, backupDir, token);

        RemoveReadOnlyRecursive(persistentDir, token);

        var deleted = new List<string>();
        var promoted = new List<string>();
        var written = new List<string>();
        var notes = new List<string>();

        string baseRevisionPath = Path.Combine(persistentDir, "base_revision");
        if (File.Exists(baseRevisionPath))
        {
            File.Delete(baseRevisionPath);
            deleted.Add("base_revision");
        }

        foreach (string cacheFile in Directory.EnumerateFiles(persistentDir, "cache_versions_*", SearchOption.TopDirectoryOnly))
        {
            token.ThrowIfCancellationRequested();
            File.Delete(cacheFile);
            deleted.Add(Path.GetFileName(cacheFile));
        }

        foreach (var (remoteName, persistName) in PersistentManifestPairs)
        {
            token.ThrowIfCancellationRequested();
            string remotePath = Path.Combine(persistentDir, remoteName);
            string persistPath = Path.Combine(persistentDir, persistName);
            if (File.Exists(remotePath))
            {
                if (File.Exists(persistPath))
                    File.Delete(persistPath);
                File.Move(remotePath, persistPath);
                promoted.Add($"{remoteName} -> {persistName}");
            }
            else if (File.Exists(persistPath))
            {
                notes.Add(string.Format("Cutscene_BypassKeptPersist".GetLocalized(), remoteName, persistName));
            }
            else
            {
                notes.Add(string.Format("Cutscene_BypassMissingPair".GetLocalized(), remoteName));
            }
        }

        WriteRevisionFile(Path.Combine(persistentDir, "res_revision"), resRevision);
        written.Add($"res_revision = {resRevision}");
        WriteRevisionFile(Path.Combine(persistentDir, "silence_revision"), silenceRevision);
        written.Add($"silence_revision = {silenceRevision}");
        WriteRevisionFile(Path.Combine(persistentDir, "data_revision"), dataRevision);
        written.Add($"data_revision = {dataRevision}");

        return new CutscenePersistentBypassReport(deleted, promoted, written, notes, backupDir);
    }

    public static async Task<int> RestorePersistentBackupAsync(string gameDir, bool isOversea, CancellationToken token)
    {
        await GameServerOperation.EnsureGameClosedAsync().ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        string persistentDir = GetPersistentDirectory(gameDir, isOversea);
        string backupDir = GetBackupDirectory(gameDir, isOversea);
        if (!Directory.Exists(backupDir))
            throw new InvalidOperationException("Cutscene_BypassNoBackup".GetLocalized());

        string[] backupFiles = Directory.GetFiles(backupDir, "*", SearchOption.TopDirectoryOnly);
        if (backupFiles.Length == 0)
            throw new InvalidOperationException("Cutscene_BypassNoBackup".GetLocalized());

        Directory.CreateDirectory(persistentDir);
        ValidateSafePath(persistentDir);
        RemoveReadOnlyRecursive(persistentDir, token);

        int restored = 0;
        foreach (string backupFile in backupFiles)
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileName(backupFile);
            string target = Path.Combine(persistentDir, name);
            File.Copy(backupFile, target, overwrite: true);
            File.SetAttributes(target, FileAttributes.Normal);
            restored++;
        }

        return restored;
    }

    private static FileSystemAccessRule CreateDenyRule() =>
        new(EveryoneSid, FileSystemRights.Write | FileSystemRights.Delete, AccessControlType.Deny);

    private static bool HasEveryoneDenyWriteRule(FileInfo info)
    {
        FileSecurity security = info.GetAccessControl();
        AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: false,
            typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
        {
            if (rule.AccessControlType == AccessControlType.Deny
                && rule.IdentityReference.Equals(EveryoneSid)
                && (rule.FileSystemRights & (FileSystemRights.WriteData | FileSystemRights.Write)) != 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool RemoveEveryoneDenyRules(FileSecurity security)
    {
        bool removed = false;
        AuthorizationRuleCollection rules = security.GetAccessRules(includeExplicit: true, includeInherited: false,
            typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules.OfType<FileSystemAccessRule>())
        {
            if (rule.AccessControlType == AccessControlType.Deny
                && rule.IdentityReference.Equals(EveryoneSid)
                && (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.Delete)) != 0)
            {
                security.RemoveAccessRuleSpecific(rule);
                removed = true;
            }
        }
        return removed;
    }

    private static void TryClearReadOnly(string path)
    {
        try
        {
            FileAttributes attrs = File.GetAttributes(path);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
        }
        catch
        {
        }
    }

    private static long SafeFileLength(string path)
    {
        try
        {
            if (!File.Exists(path))
                return 0;
            FileAttributes attrs = File.GetAttributes(path);
            if ((attrs & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
                return 0;
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void ValidateSafePath(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
        {
            if (!current.Exists || current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new IOException("Cutscene_UnsafePath".GetLocalized());
        }
    }

    private static string GetBackupDirectory(string gameDir, bool isOversea)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDir)).ToLowerInvariant();
        string hash = Convert.ToHexString(SHA256.HashData(Utf8NoBom.GetBytes(normalized)))[..12];
        string side = isOversea ? "os" : "cn";
        return Path.Combine(AppPaths.DataDir, "PersistentBackups", $"{side}_{hash}");
    }

    private static void CreatePersistentBackup(string persistentDir, string backupDir, CancellationToken token)
    {
        string tempDir = backupDir + $".{Guid.NewGuid():N}.tmp";
        Directory.CreateDirectory(tempDir);
        try
        {
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "base_revision" };
            foreach (string name in PersistentRevisionFiles)
                targets.Add(name);
            foreach (var (remote, persist) in PersistentManifestPairs)
            {
                targets.Add(remote);
                targets.Add(persist);
            }

            foreach (string file in Directory.EnumerateFiles(persistentDir, "*", SearchOption.TopDirectoryOnly))
            {
                token.ThrowIfCancellationRequested();
                string name = Path.GetFileName(file);
                if (targets.Contains(name) || name.StartsWith("cache_versions_", StringComparison.OrdinalIgnoreCase))
                    File.Copy(file, Path.Combine(tempDir, name), overwrite: true);
            }

            if (Directory.Exists(backupDir))
                Directory.Delete(backupDir, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(backupDir)!);
            Directory.Move(tempDir, backupDir);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    private static void RemoveReadOnlyRecursive(string directory, CancellationToken token)
    {
        var dirInfo = new DirectoryInfo(directory);
        if (dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly))
            dirInfo.Attributes &= ~FileAttributes.ReadOnly;

        foreach (string entry in Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                FileAttributes attrs = File.GetAttributes(entry);
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(entry, attrs & ~FileAttributes.ReadOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string? ReadRevisionValue(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            string text = File.ReadAllText(path).Trim();
            return text.Length > 0 ? text : null;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteRevisionFile(string path, string value)
    {
        TryClearReadOnly(path);
        File.WriteAllText(path, value, Utf8NoBom);
    }

    private static (string? Tag, string? Res, string? Silence, string? Data) TryDetectRevisionsFromPlayerLog(bool isOversea)
    {
        try
        {
            string localLow = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow");
            string[] candidates = isOversea
                ? [Path.Combine(localLow, "HoYoverse", "Genshin Impact", "Player.log"),
                   Path.Combine(localLow, "miHoYo", "Genshin Impact", "Player.log")]
                : [Path.Combine(localLow, "miHoYo", "原神", "Player.log"),
                   Path.Combine(localLow, "miHoYo", "YuanShen", "Player.log")];

            foreach (string logPath in candidates)
            {
                if (!File.Exists(logPath))
                    continue;

                using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string? lastTag = null;
                string? lastR = null;
                string? lastS = null;
                string? lastD = null;
                int linesRead = 0;
                while (reader.ReadLine() is { } line && linesRead++ < 4000)
                {
                    Match match = FullVersionTagRegex().Match(line);
                    if (match.Success)
                    {
                        lastTag = match.Value;
                        lastR = match.Groups[1].Value;
                        lastS = match.Groups[2].Value;
                        lastD = match.Groups[3].Value;
                    }
                }
                if (lastTag is not null)
                    return (lastTag, lastR, lastS, lastD);
            }
        }
        catch
        {
        }
        return (null, null, null, null);
    }

    [GeneratedRegex(@"R(\d+).*?S(\d+).*?D(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VersionTagRegex();

    [GeneratedRegex(@"[A-Za-z0-9.]+_R(\d+)_S(\d+)_D(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex FullVersionTagRegex();

    [GeneratedRegex(@"^\d{1,12}$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionDigitsRegex();
}
