/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Protobuf;
using ProtoBuf;
using ZstdSharp;

namespace FufuLauncher.Services.GameServer;

public readonly record struct SophonBranchInfo(
    string ManifestUrl,
    string ManifestChecksum,
    string ChunkPrefix,
    string ChunkSuffix);

public sealed class SophonBuildClient
{
    private const int MaxAttempts = 3;

    private readonly HttpClient _apiClient;

    public SophonBuildClient(GameServerHttpClientProvider httpClientProvider)
    {
        _apiClient = httpClientProvider.ApiClient;
    }

    public async Task<SophonBranchInfo> GetBranchInfoAsync(GameServerScheme scheme, bool isPreDownload,
        CancellationToken token = default)
    {
        using JsonDocument doc = await GetBuildDocumentAsync(scheme, isPreDownload, token).ConfigureAwait(false);

        var dataProp = doc.RootElement.GetProperty("data");
        if (dataProp.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException("GameServer_BranchDataNull".GetLocalized());
        }

        return ParseBranchInfo(dataProp);
    }

    public async Task<SophonBranchInfo> GetBranchInfoByTagAsync(GameServerScheme scheme, string tag,
        CancellationToken token = default)
    {
        using JsonDocument doc = await GetBranchDocumentAsync(scheme, false, token).ConfigureAwait(false);
        JsonElement targetBranch = GetTargetBranchElement(doc, false);

        string packageId = targetBranch.TryGetProperty("package_id", out var pkgProp) &&
                           pkgProp.ValueKind == JsonValueKind.String
            ? pkgProp.GetString()!
            : throw new InvalidOperationException("GameServer_NoPackageId".GetLocalized());
        string password = targetBranch.TryGetProperty("password", out var pwdProp) &&
                          pwdProp.ValueKind == JsonValueKind.String
            ? pwdProp.GetString()!
            : throw new InvalidOperationException("GameServer_NoPassword".GetLocalized());

        string branchName = targetBranch.TryGetProperty("branch", out var branchProp) &&
                            branchProp.ValueKind == JsonValueKind.String
            ? branchProp.GetString()!
            : "main";
        string buildUrl = ApiEndpoints.BuildSophonBuildUrl(scheme.SophonApi, branchName, packageId, password, tag);
        string json = await GetStringWithRetryAsync(buildUrl, token).ConfigureAwait(false);

        using var buildDoc = JsonDocument.Parse(json);
        var dataProp = buildDoc.RootElement.GetProperty("data");
        if (dataProp.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException("GameServer_BranchDataNull".GetLocalized());
        }

        return ParseBranchInfo(dataProp);
    }

    public async Task<SophonBranchPayload> GetBranchPayloadAsync(GameServerScheme scheme, bool isPreDownload,
        CancellationToken token = default)
    {
        using JsonDocument doc = await GetBranchDocumentAsync(scheme, isPreDownload, token).ConfigureAwait(false);
        JsonElement branchElement = GetTargetBranchElement(doc, isPreDownload);

        var payload = JsonSerializer.Deserialize<SophonBranchPayload>(branchElement.GetRawText(), JsonOptions);
        ArgumentNullException.ThrowIfNull(payload);
        return payload;
    }

    public async Task<SophonPatchBuildResponse?> GetPatchBuildAsync(GameServerScheme scheme,
        SophonBranchPayload payload, CancellationToken token = default)
    {
        string url = ApiEndpoints.BuildSophonPatchBuildUrl(scheme.SophonApi);
        string json = await ExecuteWithRetryAsync(url, async attempt =>
        {
            using var content = new StringContent(JsonSerializer.Serialize(payload, RequestJsonOptions), Encoding.UTF8,
                "application/json");
            using var response = await _apiClient.PostAsync(url, content, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        }, token).ConfigureAwait(false);

        using var doc = JsonDocument.Parse(json);
        var dataProp = doc.RootElement.GetProperty("data");
        if (dataProp.ValueKind == JsonValueKind.Null || dataProp.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<SophonPatchBuildResponse>(dataProp.GetRawText(), JsonOptions);
        ArgumentNullException.ThrowIfNull(result);
        return result;
    }

    public async Task<PatchManifest> DownloadPatchManifestAsync(string manifestUrl, string expectedMd5Hex,
        CancellationToken token = default)
    {
        byte[] decompressed =
            await DownloadAndDecompressAsync(manifestUrl, expectedMd5Hex, token, throwOnChecksumMismatch: false)
                .ConfigureAwait(false);
        return Serializer.Deserialize<PatchManifest>(decompressed.AsMemory());
    }

    public async Task<SophonManifestProto> DownloadManifestAsync(SophonBranchInfo info,
        CancellationToken token = default)
    {
        byte[] decompressed = await DownloadAndDecompressAsync(info.ManifestUrl, info.ManifestChecksum, token)
            .ConfigureAwait(false);
        return SophonManifestProto.Parser.ParseFrom(decompressed);
    }

    public async Task<JsonDocument> GetBuildDocumentAsync(GameServerScheme scheme, bool isPreDownload,
        CancellationToken token = default)
    {
        string buildUrl = await ResolveBuildUrlAsync(scheme, isPreDownload, token).ConfigureAwait(false);
        string json = await GetStringWithRetryAsync(buildUrl, token).ConfigureAwait(false);
        return JsonDocument.Parse(json);
    }

    public async Task<byte[]> DownloadAndDecompressAsync(string url, string? expectedMd5Hex = null,
        CancellationToken token = default, bool throwOnChecksumMismatch = true)
    {
        byte[] compressed = await GetBytesWithRetryAsync(url, token).ConfigureAwait(false);

        using var compressedStream = new MemoryStream(compressed);
        using var decompressionStream = new DecompressionStream(compressedStream);
        using var outputStream = new MemoryStream();
        await decompressionStream.CopyToAsync(outputStream, token).ConfigureAwait(false);

        byte[] decompressed = outputStream.ToArray();

        if (!string.IsNullOrEmpty(expectedMd5Hex))
        {
            string actualMd5 = HashUtility.Md5Bytes(decompressed);
            if (!actualMd5.Equals(expectedMd5Hex, StringComparison.OrdinalIgnoreCase) && throwOnChecksumMismatch)
            {
                throw new InvalidOperationException(string.Format("GameServer_ManifestChecksumMismatch".GetLocalized(),
                    url));
            }
        }

        return decompressed;
    }

    public Task<string> GetStringWithRetryAsync(string url, CancellationToken token = default)
    {
        return ExecuteWithRetryAsync(url, _ => _apiClient.GetStringAsync(url, token), token);
    }

    private Task<byte[]> GetBytesWithRetryAsync(string url, CancellationToken token)
    {
        return ExecuteWithRetryAsync(url, _ => _apiClient.GetByteArrayAsync(url, token), token);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(string url, Func<int, Task<T>> attempt, CancellationToken token)
    {
        Exception? lastException = null;
        for (int i = 0; i < MaxAttempts; i++)
        {
            try
            {
                return await attempt(i).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (i < MaxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 + i), token).ConfigureAwait(false);
                }
            }
        }

        throw new InvalidOperationException(
            string.Format("GameServer_ApiRequestFailed".GetLocalized(), url, lastException?.Message), lastException);
    }

    private async Task<string> ResolveBuildUrlAsync(GameServerScheme scheme, bool isPreDownload,
        CancellationToken token)
    {
        using JsonDocument doc = await GetBranchDocumentAsync(scheme, isPreDownload, token).ConfigureAwait(false);
        JsonElement targetBranch = GetTargetBranchElement(doc, isPreDownload);

        string? buildUrl = targetBranch.TryGetProperty("build_url", out var buildUrlProp) &&
                           buildUrlProp.ValueKind == JsonValueKind.String
            ? buildUrlProp.GetString()
            : null;
        if (!string.IsNullOrEmpty(buildUrl))
        {
            return buildUrl;
        }

        string packageId = targetBranch.TryGetProperty("package_id", out var pkgProp) &&
                           pkgProp.ValueKind == JsonValueKind.String
            ? pkgProp.GetString()!
            : throw new InvalidOperationException("GameServer_NoPackageId".GetLocalized());
        string password = targetBranch.TryGetProperty("password", out var pwdProp) &&
                          pwdProp.ValueKind == JsonValueKind.String
            ? pwdProp.GetString()!
            : throw new InvalidOperationException("GameServer_NoPassword".GetLocalized());

        string branchName = targetBranch.TryGetProperty("branch", out var branchProp) &&
                            branchProp.ValueKind == JsonValueKind.String
            ? branchProp.GetString()!
            : (isPreDownload ? "predownload" : "main");
        return ApiEndpoints.BuildSophonBuildUrl(scheme.SophonApi, branchName, packageId, password);
    }

    private static SophonBranchInfo ParseBranchInfo(JsonElement dataProp)
    {
        var manifestsProp = dataProp.GetProperty("manifests");
        if (manifestsProp.ValueKind == JsonValueKind.Null || manifestsProp.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("GameServer_NoManifests".GetLocalized());
        }

        JsonElement manifestData = default;
        foreach (var manifest in manifestsProp.EnumerateArray())
        {
            if (manifest.TryGetProperty("matching_field", out var fieldProp)
                && fieldProp.ValueKind == JsonValueKind.String
                && string.Equals(fieldProp.GetString(), "game", StringComparison.OrdinalIgnoreCase))
            {
                manifestData = manifest;
                break;
            }
        }

        if (manifestData.ValueKind == JsonValueKind.Undefined)
        {
            manifestData = manifestsProp[0];
        }

        string manifestId = manifestData.GetProperty("manifest").GetProperty("id").GetString()!;
        string manifestChecksum = manifestData.GetProperty("manifest").TryGetProperty("checksum", out var checksumProp)
                                  && checksumProp.ValueKind == JsonValueKind.String
            ? checksumProp.GetString()!
            : string.Empty;

        string manifestPrefix = manifestData.GetProperty("manifest_download").GetProperty("url_prefix").GetString()!;
        string manifestSuffix = manifestData.GetProperty("manifest_download")
                                    .TryGetProperty("url_suffix", out var manifestSuffixProp)
                                && manifestSuffixProp.ValueKind == JsonValueKind.String
            ? manifestSuffixProp.GetString()!
            : string.Empty;

        string chunkPrefix = manifestData.GetProperty("chunk_download").GetProperty("url_prefix").GetString()!;
        string chunkSuffix =
            manifestData.GetProperty("chunk_download").TryGetProperty("url_suffix", out var chunkSuffixProp)
            && chunkSuffixProp.ValueKind == JsonValueKind.String
                ? chunkSuffixProp.GetString()!
                : string.Empty;

        return new SophonBranchInfo($"{manifestPrefix}/{manifestId}{manifestSuffix}", manifestChecksum, chunkPrefix,
            chunkSuffix);
    }

    private async Task<JsonDocument> GetBranchDocumentAsync(GameServerScheme scheme, bool isPreDownload,
        CancellationToken token)
    {
        string branchUrl = ApiEndpoints.BuildHypGameBranchesUrl(scheme.HypApi, scheme.LauncherId, scheme.GameId);
        string branchJson = await GetStringWithRetryAsync(branchUrl, token).ConfigureAwait(false);
        return JsonDocument.Parse(branchJson);
    }

    private static JsonElement GetTargetBranchElement(JsonDocument doc, bool isPreDownload)
    {
        var dataProp = doc.RootElement.GetProperty("data");
        if (dataProp.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException("GameServer_BranchDataNull".GetLocalized());
        }

        var branches = dataProp.GetProperty("game_branches")[0];

        if (!branches.TryGetProperty(isPreDownload ? "pre_download" : "main", out var targetBranch)
            || targetBranch.ValueKind == JsonValueKind.Null)
        {
            throw new InvalidOperationException(isPreDownload
                ? "GameServer_NoPredownload".GetLocalized()
                : "GameServer_NoMainBranch".GetLocalized());
        }

        return targetBranch;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}