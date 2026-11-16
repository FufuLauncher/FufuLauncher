/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using FufuLauncher.Helpers;
using FufuLauncher.Services.Yae.Proto;

namespace FufuLauncher.Services.Yae;

public static class YaeMetadataService
{
    private static readonly string[] Mirrors =
    [
        ApiEndpoints.YaeMetadataPrimaryUrl,
        ApiEndpoints.YaeMetadataMirrorUrl,
        ApiEndpoints.YaeMetadataFallbackUrl,
    ];

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };


    public static async Task<AchievementInfo> GetMetadataAsync(CancellationToken cancellationToken = default)
    {
        var cacheDir = Path.Combine(AppPaths.CacheDir, "Yae");
        Directory.CreateDirectory(cacheDir);
        var binPath = Path.Combine(cacheDir, "metadata.bin");
        var etagPath = Path.Combine(cacheDir, "metadata.etag");

        string? lastError = null;
        foreach (var mirror in Mirrors)
        {
            try
            {
                var raw = await FetchWithEtagAsync(mirror, binPath, etagPath, cancellationToken).ConfigureAwait(false);
                var data = DecryptIfNeeded(raw);
                return AchievementInfo.Parser.ParseFrom(data);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                           or SocketException or InvalidDataException)
            {
                lastError = ex.Message;
            }
        }


        if (File.Exists(binPath))
        {
            try
            {
                var data = DecryptIfNeeded(File.ReadAllBytes(binPath));
                return AchievementInfo.Parser.ParseFrom(data);
            }
            catch (Exception)
            {
                /* ignore */
            }
        }

        throw new ApplicationException(lastError ?? "Yae_ErrFetchMetadataFailed".GetLocalized());
    }


    public static uint ComputeGameHash(string exePath)
    {
        try
        {
            Span<byte> buffer = stackalloc byte[0x10000];
            using var stream = File.OpenRead(exePath);
            _ = stream.ReadAtLeast(buffer, 0x10000, false);
            return YaeCrc32.Compute(buffer);
        }
        catch (IOException)
        {
            return 0xFFFFFFFF;
        }
    }


    public static YaeNativeConfiguration Resolve(AchievementInfo metadata, uint gameHash)
    {
        if (!metadata.NativeConfig.MethodRva.TryGetValue(gameHash, out var rva))
        {
            throw new ApplicationException(string.Format("Yae_ErrConfigNotFound_Format".GetLocalized(), gameHash));
        }

        return new YaeNativeConfiguration
        {
            StoreCmdId = metadata.NativeConfig.StoreCmdId,
            AchievementCmdId = metadata.NativeConfig.AchievementCmdId,
            DoCmd = rva.DoCmd,
            UpdateNormalProperty = rva.UpdateNormalProp,
            NewString = rva.NewString,
            FindGameObject = rva.FindGameObject,
            EventSystemUpdate = rva.EventSystemUpdate,
            SimulatePointerClick = rva.SimulatePointerClick,
            ToInt32 = rva.ToInt32,
            TcpStatePtr = rva.TcpStatePtr,
            SharedInfoPtr = rva.SharedInfoPtr,
            Decompress = rva.Decompress,
        };
    }

    private static async Task<byte[]> FetchWithEtagAsync(string url, string binPath, string etagPath,
        CancellationToken cancellationToken)
    {
        byte[] cached = File.Exists(binPath)
            ? await File.ReadAllBytesAsync(binPath, cancellationToken).ConfigureAwait(false)
            : [];

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (File.Exists(etagPath))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match",
                await File.ReadAllTextAsync(etagPath, cancellationToken).ConfigureAwait(false));
        }

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotModified && cached.Length > 0)
        {
            return cached;
        }

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (response.Headers.ETag?.Tag is { } etag)
        {
            await File.WriteAllTextAsync(etagPath, etag, cancellationToken).ConfigureAwait(false);
        }

        await File.WriteAllBytesAsync(binPath, bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }


    private static byte[] DecryptIfNeeded(byte[] data)
    {
        if (data.Length < 52 || BitConverter.ToUInt32(data, 0) != 0x38464947)
        {
            return data;
        }

        var seed = BitConverter.ToUInt32(data, 44) ^ 0x01919810;
        var hush = BitConverter.ToUInt32(data, 48) - 0x32123432;
        var span = data.AsSpan()[52..];

        var xorTable = new byte[4096];
        new Random(unchecked((int)seed)).NextBytes(xorTable);
        for (var i = 0; i < span.Length; i++)
        {
            span[i] ^= xorTable[i % 4096];
        }

        using var output = new MemoryStream();
        using (var compressed = new MemoryStream(span.ToArray()))
        using (var decompressor = new BrotliStream(compressed, CompressionMode.Decompress))
        {
            decompressor.CopyTo(output);
        }

        var result = output.ToArray();
        if (YaeCrc32.Compute(result) != hush)
        {
            throw new InvalidDataException("Yae_ErrMetadataDecryptFailed".GetLocalized());
        }

        return result;
    }
}