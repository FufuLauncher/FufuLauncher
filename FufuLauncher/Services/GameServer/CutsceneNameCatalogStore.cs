/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;

namespace FufuLauncher.Services.GameServer;

internal sealed record CutsceneCatalogUpdateResult(CutsceneNameCatalog Catalog, bool Updated);

internal static class CutsceneNameCatalogStore
{
    internal const int MaxCatalogBytes = 16 * 1024 * 1024;
    private static readonly HttpClient Client = CreateClient();

    private static string DirectoryPath => Path.Combine(AppPaths.CacheDir, "CutsceneCatalog");
    private static string CatalogPath => Path.Combine(DirectoryPath, "CutsceneNames.json");

    public static CutsceneNameCatalog? LoadCurrent() => ReadCache()?.Catalog;

    public static async Task<CutsceneCatalogUpdateResult> UpdateAsync(
        SophonBuildClient sophonClient,
        GameServerScheme? preferredScheme,
        IProgress<string>? status,
        CancellationToken token)
    {
        var cache = ReadCache();
        var (bytes, catalog) = await CutsceneCatalogUpdater
            .PullAndMergeAsync(Client, sophonClient, preferredScheme, cache?.Catalog, cache?.Bytes, status, token)
            .ConfigureAwait(false);

        return bytes is null
            ? new CutsceneCatalogUpdateResult(catalog, false)
            : await SaveAsync(bytes, catalog, replaceSameVersion: false, token).ConfigureAwait(false);
    }

    public static async Task<CutsceneCatalogUpdateResult> ImportFromFileAsync(string filePath, CancellationToken token)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists || info.Length is <= 0 or > MaxCatalogBytes)
            throw new InvalidDataException("Invalid cutscene catalog file.");

        byte[] bytes = await File.ReadAllBytesAsync(filePath, token).ConfigureAwait(false);
        using var source = new MemoryStream(bytes, writable: false);
        return await SaveAsync(bytes, CutsceneNames.Read(source), replaceSameVersion: true, token).ConfigureAwait(false);
    }

    private static (CutsceneNameCatalog Catalog, byte[] Bytes)? ReadCache()
    {
        try
        {
            using var source = new FileStream(CatalogPath, FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            if (source.Length is <= 0 or > MaxCatalogBytes)
                throw new InvalidDataException("Invalid cached cutscene catalog size.");

            byte[] bytes = new byte[(int)source.Length];
            source.ReadExactly(bytes);
            using var content = new MemoryStream(bytes, writable: false);
            return (CutsceneNames.Read(content), bytes);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            DebugLog.Info(nameof(CutsceneNameCatalogStore), $"Cached catalog ignored ({ex.GetType().Name}).");
            return null;
        }
    }

    private static async Task<CutsceneCatalogUpdateResult> SaveAsync(
        byte[] bytes, CutsceneNameCatalog candidate, bool replaceSameVersion, CancellationToken token)
    {
        if (bytes.Length > MaxCatalogBytes)
            throw new InvalidDataException("Cutscene catalog exceeds the size limit.");

        Directory.CreateDirectory(DirectoryPath);
        using var updateLock = new FileStream(Path.Combine(DirectoryPath, ".update.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        if (LoadCurrent() is { } current)
        {
            if (!replaceSameVersion && !candidate.IsNewerThan(current))
                return new CutsceneCatalogUpdateResult(current, false);
            if (replaceSameVersion && current.IsNewerThan(candidate))
                throw new InvalidOperationException("Cutscene_CatalogImportOutdated".GetLocalized());
            if (!current.Entries.Keys.All(candidate.Entries.ContainsKey))
                throw new InvalidDataException("Catalog is missing previously published entries.");
        }

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
                File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                DebugLog.Info(nameof(CutsceneNameCatalogStore), $"Temporary catalog cleanup failed ({ex.GetType().Name}).");
            }
        }
        return new CutsceneCatalogUpdateResult(candidate, true);
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All
        });
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FufuLauncher-CutsceneCatalog/1.0");
        return client;
    }
}
