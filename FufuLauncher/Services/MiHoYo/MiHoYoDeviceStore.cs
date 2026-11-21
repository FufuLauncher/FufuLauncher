/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Identity;

namespace FufuLauncher.Services.MiHoYo;

public sealed class MiHoYoDeviceStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private MiHoYoDeviceIdentity? _cached;

    private static int _writeSequence;

    public async Task<MiHoYoDeviceIdentity?> LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await ReadCoreAsync(token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }


    public async Task SaveAsync(MiHoYoDeviceIdentity identity, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await WriteCoreAsync(identity, token).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }


    public async Task<MiHoYoDeviceIdentity> GetOrCreateAsync(
        string? initialDeviceId = null, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = await ReadCoreAsync(token).ConfigureAwait(false);
            if (current is not null && current.IsUsable)
            {
                return current;
            }

            var created = MiHoYoDeviceIdentity.CreateNew(initialDeviceId);
            await WriteCoreAsync(created, token).ConfigureAwait(false);
            DebugLog.Info("MiHoYoDeviceStore", $"[MiHoYoDevice] Generated and persisted device_id={created.DeviceId}");
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }


    public async Task<MiHoYoDeviceIdentity?> WithFingerprintAsync(
        string expectedDeviceId, string deviceFp, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var current = await ReadCoreAsync(token).ConfigureAwait(false);
            if (current is null
                || !current.IsUsable
                || !string.Equals(current.DeviceId, expectedDeviceId, StringComparison.Ordinal))
            {
                DebugLog.Info("MiHoYoDeviceStore",
                    $"[MiHoYoDevice] Identity changed (expected {expectedDeviceId}), discarding stale fingerprint");
                return null;
            }

            var updated = current with
            {
                DeviceFp = deviceFp,
                FingerprintUpdatedAt = DateTimeOffset.UtcNow,
            };
            await WriteCoreAsync(updated, token).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }


    public async Task<MiHoYoDeviceIdentity> ResetAsync(
        string? initialDeviceId = null, CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var created = MiHoYoDeviceIdentity.CreateNew(initialDeviceId);
            await WriteCoreAsync(created, token).ConfigureAwait(false);
            DebugLog.Info("MiHoYoDeviceStore", $"[MiHoYoDevice] Reset and persisted device_id={created.DeviceId}");
            return created;
        }
        finally
        {
            _gate.Release();
        }
    }


    private async Task<MiHoYoDeviceIdentity?> ReadCoreAsync(CancellationToken token)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        string path = AppPaths.MiHoYoDeviceFile;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, token).ConfigureAwait(false);
            var loaded = JsonSerializer.Deserialize<MiHoYoDeviceIdentity>(json, JsonOptions);
            if (loaded is null)
            {
                DebugLog.Info("MiHoYoDeviceStore", "[MiHoYoDevice] File content is empty, treating as unavailable");
                return null;
            }

            _cached = loaded;
            return loaded;
        }
        catch (JsonException ex)
        {
            DebugLog.Info("MiHoYoDeviceStore",
                $"[MiHoYoDevice] Failed to parse content, treating as unavailable: {ex.Message}");
            return null;
        }
    }


    private async Task WriteCoreAsync(MiHoYoDeviceIdentity identity, CancellationToken token)
    {
        string path = AppPaths.MiHoYoDeviceFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        string temporary = $"{path}.{Environment.ProcessId}.{Interlocked.Increment(ref _writeSequence)}.tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporary, JsonSerializer.Serialize(identity, JsonOptions), token).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }


        _cached = identity;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugLog.Info("MiHoYoDeviceStore", $"[MiHoYoDevice] Failed to clean temp file {path}: {ex.Message}");
        }
    }
}