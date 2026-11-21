/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FufuLauncher.Models.MiHoYo.Fingerprint;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Services.Device;
using FufuLauncher.Services.MiHoYo.Networking;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.MiHoYo.Fingerprint;

public sealed class DeviceFpService
{
    private const string GetFpUrl = ApiEndpoints.DeviceFpUrl;


    public const string AppName = "bbs_cn";


    public const string Platform = "2";

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly MiHoYoDeviceStore _store;
    private readonly MobileDeviceService _device;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _startupSyncLock = new();
    private Task<FingerprintSyncResult>? _startupSync;

    public DeviceFpService(MiHoYoDeviceStore store, MobileDeviceService device)
    {
        _store = store;
        _device = device;
    }


    public Task<MiHoYoDeviceIdentity> GetIdentityAsync(CancellationToken token = default) =>
        _store.GetOrCreateAsync(ResolveInitialDeviceId(), token);


    public async Task<string> GetBbsDeviceIdAsync(CancellationToken token = default) =>
        (await GetIdentityAsync(token).ConfigureAwait(false)).BbsDeviceId;


    public async Task<string?> GetFingerprintAsync(CancellationToken token = default)
    {
        var identity = await GetIdentityAsync(token).ConfigureAwait(false);
        return string.IsNullOrEmpty(identity.DeviceFp) ? null : identity.DeviceFp;
    }


    public enum FingerprintSyncOutcome
    {
        NotNeeded,
        Registered,
        Updated,
        Failed,
    }

    public readonly record struct FingerprintSyncResult(FingerprintSyncOutcome Outcome, string? Error)
    {
        public bool HadFingerprint { get; init; }
    }

    public async Task<MiHoYoDeviceIdentity> GetOrRegisterAsync(CancellationToken token = default)
    {
        return await GetIdentityAsync(token).ConfigureAwait(false);
    }

    public async Task<FingerprintSyncResult> SyncOnStartupAsync(CancellationToken token = default)
    {
        var identity = await GetIdentityAsync(token).ConfigureAwait(false);
        if (HasFreshFingerprint(identity))
        {
            return new FingerprintSyncResult(FingerprintSyncOutcome.NotNeeded, null);
        }

        bool hadFingerprint = !string.IsNullOrEmpty(identity.DeviceFp);
        var result = await SyncOnStartupCoreAsync(token).ConfigureAwait(false);
        return result.Outcome == FingerprintSyncOutcome.Failed
            ? result with { HadFingerprint = hadFingerprint }
            : result;
    }


    public Task<FingerprintSyncResult> BeginStartupSync()
    {
        if (Volatile.Read(ref _startupSync) is { } existing)
        {
            return existing;
        }

        lock (_startupSyncLock)
        {
            _startupSync ??= Task.Run(() => SyncOnStartupAsync(CancellationToken.None));
            return _startupSync;
        }
    }


    public Task<FingerprintSyncResult> WaitStartupSyncAsync() => BeginStartupSync();

    private async Task<FingerprintSyncResult> SyncOnStartupCoreAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var identity = await GetIdentityAsync(token).ConfigureAwait(false);
            if (HasFreshFingerprint(identity))
            {
                return new FingerprintSyncResult(FingerprintSyncOutcome.NotNeeded, null);
            }

            bool isUpdate = !string.IsNullOrEmpty(identity.DeviceFp);
            string? issued = await RegisterAsync(identity, token).ConfigureAwait(false);
            if (string.IsNullOrEmpty(issued))
            {
                DebugLog.Info("DeviceFpService",
                    $"[DeviceFp] {(isUpdate ? "Update" : "Registration")} returned no fingerprint");
                return new FingerprintSyncResult(FingerprintSyncOutcome.Failed,
                    "DeviceFp_RequestFailed".GetLocalized());
            }

            var updated = await _store.WithFingerprintAsync(identity.DeviceId, issued, token)
                .ConfigureAwait(false);
            if (updated is null)
            {
                DebugLog.Info("DeviceFpService", "[DeviceFp] Identity reset, discarding stale fingerprint");
                return new FingerprintSyncResult(FingerprintSyncOutcome.Failed,
                    "DeviceFp_RequestFailed".GetLocalized());
            }

            DebugLog.Info("DeviceFpService",
                $"[DeviceFp] {(isUpdate ? "Update" : "Registration")} succeeded at {updated.FingerprintUpdatedAt:O}");
            return new FingerprintSyncResult(
                isUpdate ? FingerprintSyncOutcome.Updated : FingerprintSyncOutcome.Registered, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool HasFreshFingerprint(MiHoYoDeviceIdentity identity) =>
        !string.IsNullOrEmpty(identity.DeviceFp) &&
        identity.FingerprintUpdatedAt?.LocalDateTime.Date == DateTime.Today;

    public async Task<MiHoYoDeviceIdentity> ResetAndRegisterAsync(CancellationToken token = default)
    {
        await _store.ResetAsync(ResolveInitialDeviceId(), token).ConfigureAwait(false);
        var result = await SyncOnStartupCoreAsync(token).ConfigureAwait(false);
        if (result.Outcome == FingerprintSyncOutcome.Failed)
        {
            throw new InvalidOperationException(result.Error ?? "DeviceFp_RequestFailed".GetLocalized());
        }

        return await GetIdentityAsync(token).ConfigureAwait(false);
    }

    public Task SaveIdentityAsync(MiHoYoDeviceIdentity identity, CancellationToken token = default) =>
        _store.SaveAsync(identity, token);


    public string BuildExtFieldsJson() => FpExtFieldsBuilder.BuildJson(_device.CaptureSnapshot());


    private async Task<string?> RegisterAsync(MiHoYoDeviceIdentity identity, CancellationToken token)
    {
        try
        {
            var request = CreateRequest(identity);
            string bodyJson = JsonSerializer.Serialize(request, _jsonOptions);
            using var req = new HttpRequestMessage(HttpMethod.Post, GetFpUrl);
            req.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
            MiHoYoHeaderFactory.ApplyDeviceFpHeaders(req);

            using var resp = await _httpClient.SendAsync(req, token).ConfigureAwait(false);
            string json = await resp.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            DebugLog.Info("DeviceFpService",
                $"[DeviceFp] getFp status code: {(int)resp.StatusCode}, response: {Truncate(json, 300)}");

            return ParseFingerprint(json);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLog.Info("DeviceFpService", $"[DeviceFp] getFp exception: {ex.Message}");
            return null;
        }
    }


    private DeviceFpRequest CreateRequest(MiHoYoDeviceIdentity identity) => new()
    {
        DeviceId = identity.DeviceId,
        SeedId = identity.SeedId,
        SeedTime = identity.SeedTime,
        Platform = Platform,
        DeviceFp = string.IsNullOrEmpty(identity.DeviceFp)
            ? CreatePlaceholderFingerprint()
            : identity.DeviceFp,
        AppName = AppName,
        ExtFields = FpExtFieldsBuilder.BuildJson(_device.CaptureSnapshot()),
        BbsDeviceId = identity.BbsDeviceId,
    };


    private static string? ParseFingerprint(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("retcode", out var rc) && rc.GetInt32() != 0)
            {
                DebugLog.Info("DeviceFpService", $"[DeviceFp] getFp retcode={rc.GetInt32()}");
                return null;
            }

            if (root.TryGetProperty("data", out var data)
                && data.TryGetProperty("device_fp", out var fpProp))
            {
                string? fp = fpProp.GetString();
                return string.IsNullOrEmpty(fp) ? null : fp;
            }

            DebugLog.Info("DeviceFpService", "[DeviceFp] getFp response missing device_fp");
            return null;
        }
        catch (JsonException ex)
        {
            DebugLog.Info("DeviceFpService", $"[DeviceFp] Failed to parse getFp response: {ex.Message}");
            return null;
        }
    }


    private string ResolveInitialDeviceId() => _device.Device.AndroidId;

    private static string CreatePlaceholderFingerprint()
    {
        Span<char> buffer = stackalloc char[10];
        buffer[0] = (char)('1' + Random.Shared.Next(9));
        for (int i = 1; i < buffer.Length; i++)
        {
            buffer[i] = (char)('0' + Random.Shared.Next(10));
        }

        return new string(buffer);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
