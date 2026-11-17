/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;

namespace FufuLauncher.Services.GameServer;

internal sealed record CutsceneCatalogStatus(
    CutsceneNameCatalog Catalog,
    bool UsingCachedCatalog,
    DateTimeOffset? CachedUpdatedAtUtc);

internal sealed record CutsceneCatalogUpdateResult(
    CutsceneNameCatalog Catalog,
    bool Updated,
    string SourceName);

/// <summary>
/// Manages local cutscene catalog state and coordinates direct client-side
/// upstream pulling and autonomous merging into the user cache directory.
/// </summary>
internal static class CutsceneNameCatalogStore
{
    private const int MaxCatalogBytes = 16 * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5
        };
        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FufuLauncher-CutsceneCatalog/1.0");
        return client;
    }

    private static string DirectoryPath => Path.Combine(AppPaths.CacheDir, "CutsceneCatalog");
    private static string CatalogPath => Path.Combine(DirectoryPath, "CutsceneNames.json");

    public static CutsceneNameCatalog LoadCurrent() => GetStatus().Catalog;

    public static CutsceneCatalogStatus GetStatus()
    {
        CutsceneNameCatalog embedded = CutsceneNames.BuiltIn;
        try
        {
            if (File.Exists(CatalogPath))
            {
                var info = new FileInfo(CatalogPath);
                if (info.Length > MaxCatalogBytes)
                    throw new InvalidDataException("Cached cutscene catalog exceeds the size limit.");

                using var source = new FileStream(CatalogPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read | FileShare.Delete);
                CutsceneNameCatalog cached = CutsceneNames.Read(source);
                if (cached.IsNewerThan(embedded) && KeepsEntries(cached, embedded))
                {
                    if (cached.SelfMerged && cached.BuiltInBaseGameVersion is { } baseVer
                        && (embedded.GameVersion > baseVer
                            || (embedded.GameVersion == baseVer && embedded.Revision > cached.BuiltInBaseRevision)))
                    {
                        if (embedded.GameVersion >= cached.GameVersion)
                            return new CutsceneCatalogStatus(embedded, false, null);

                        return new CutsceneCatalogStatus(
                            CutsceneCatalogUpdater.OverlayBuiltInCurated(cached, embedded),
                            true,
                            info.LastWriteTimeUtc);
                    }

                    return new CutsceneCatalogStatus(cached, true, info.LastWriteTimeUtc);
                }
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or System.Text.Json.JsonException)
        {
            DebugLog.Info(nameof(CutsceneNameCatalogStore), $"Cached catalog ignored ({ex.GetType().Name}).");
        }
        return new CutsceneCatalogStatus(embedded, false, null);
    }

    public static async Task<CutsceneCatalogUpdateResult> UpdateAsync(
        SophonBuildClient? sophonClient,
        GameServerScheme? preferredScheme,
        IProgress<string>? status,
        CancellationToken token)
    {
        byte[]? existingCachedBytes = null;
        try
        {
            if (File.Exists(CatalogPath))
            {
                var info = new FileInfo(CatalogPath);
                if (info.Length is > 0 and <= MaxCatalogBytes)
                    existingCachedBytes = await File.ReadAllBytesAsync(CatalogPath, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(CutsceneNameCatalogStore), $"Could not read existing cache before merge: {ex.Message}");
        }

        CutsceneNameCatalog current = LoadCurrent();
        CutsceneCatalogUpdater.MergeOutcome outcome = await CutsceneCatalogUpdater
            .PullAndMergeAsync(Client, sophonClient, preferredScheme, current, existingCachedBytes, status, token)
            .ConfigureAwait(false);

        if (!outcome.Changed)
            return new CutsceneCatalogUpdateResult(outcome.Catalog, false, outcome.SourceSummary);

        var (savedCatalog, updated) = await SaveIfNewerAsync(
                outcome.CatalogJsonBytes, outcome.Catalog, forceReplaceSameVersion: false, token)
            .ConfigureAwait(false);
        return new CutsceneCatalogUpdateResult(savedCatalog, updated, outcome.SourceSummary);
    }

    public static async Task<CutsceneCatalogUpdateResult> ImportFromFileAsync(string filePath, CancellationToken token)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length == 0 || info.Length > MaxCatalogBytes)
            throw new InvalidDataException("Invalid cutscene catalog file.");

        byte[] bytes = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
        using var stream = new MemoryStream(bytes, writable: false);
        CutsceneNameCatalog imported = CutsceneNames.Read(stream);
        var (savedCatalog, updated) = await SaveIfNewerAsync(bytes, imported, forceReplaceSameVersion: true, token)
            .ConfigureAwait(false);
        return new CutsceneCatalogUpdateResult(savedCatalog, updated, Path.GetFileName(filePath));
    }

    public static void ResetToBuiltIn()
    {
        try
        {
            if (File.Exists(CatalogPath))
                File.Delete(CatalogPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Info(nameof(CutsceneNameCatalogStore), $"Reset cached catalog failed ({ex.GetType().Name}).");
        }
    }

    private static async Task<(CutsceneNameCatalog Catalog, bool Updated)> SaveIfNewerAsync(
        byte[] bytes, CutsceneNameCatalog candidate, bool forceReplaceSameVersion, CancellationToken token)
    {
        Directory.CreateDirectory(DirectoryPath);
        // Separate launcher instances may share the same cache directory.
        using var updateLock = new FileStream(Path.Combine(DirectoryPath, ".update.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);

        CutsceneNameCatalog current = LoadCurrent();
        if (!forceReplaceSameVersion && !candidate.IsNewerThan(current))
            return (current, false);
        if (forceReplaceSameVersion && candidate.GameVersion < current.GameVersion)
            throw new InvalidDataException("Imported catalog version is older than current catalog.");
        if (!KeepsEntries(candidate, CutsceneNames.BuiltIn) || !KeepsEntries(candidate, current))
            throw new InvalidDataException("Catalog is missing previously published entries.");

        string temporary = Path.Combine(DirectoryPath, $"{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, CatalogPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
            catch
            {
            }
        }
        return (candidate, true);
    }

    private static bool KeepsEntries(CutsceneNameCatalog candidate, CutsceneNameCatalog baseline) =>
        baseline.Entries.Keys.All(candidate.Entries.ContainsKey);
}
