/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed partial class CloudSyncService : ICloudSyncService
{
    private const string ServiceBaseUrl = ApiEndpoints.CloudSyncBaseUrl;
    private const string SessionFileName = "fufu_cloud_session.dat";

    private static readonly Uri BaseUri = new(ServiceBaseUrl + "/", UriKind.Absolute);
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("FufuLauncher.CloudSync.Session.v1");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [GeneratedRegex("^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidSlotRegex();

    [GeneratedRegex("[^a-zA-Z0-9_.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeSlotCharsRegex();

    private static readonly TimeSpan[] DefaultTransientRetryDelays =
    [
        TimeSpan.FromMilliseconds(350),
        TimeSpan.FromMilliseconds(900)
    ];

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private readonly string _sessionFilePath;
    private readonly IReadOnlyList<TimeSpan> _transientRetryDelays;

    private string _email = string.Empty;
    private string _stoken = string.Empty;
    private long _stokenExpiresAtUnix;


    private string _ltoken = string.Empty;
    private long _ltokenExpiresAtUnix;
    private string _accountStatus = "unknown";
    private bool _isLocked;
    private string? _lockedAt;
    private string? _lastStartupMessage;

    public event EventHandler? SessionChanged;

    public CloudSyncService()
        : this(CreateDefaultHandler(), Path.Combine(AppPaths.SettingsDir, SessionFileName), null)
    {
    }

    internal CloudSyncService(
        HttpMessageHandler handler,
        string? sessionFilePath = null,
        IReadOnlyList<TimeSpan>? transientRetryDelays = null)
    {
        _sessionFilePath = string.IsNullOrWhiteSpace(sessionFilePath)
            ? Path.Combine(AppPaths.SettingsDir, SessionFileName)
            : sessionFilePath;
        _transientRetryDelays = transientRetryDelays ?? DefaultTransientRetryDelays;
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FufuLauncher-CloudSync/1.0");

        LoadPersistedSession();
    }

    private static HttpClientHandler CreateDefaultHandler()
        => new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false
        };

    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(_stoken) && !IsStokenExpiredLocally();
    public bool HasLtoken => !string.IsNullOrWhiteSpace(_ltoken) && !IsLtokenExpiredLocally();
    public string CurrentEmail => _email;
    public string AccountStatus => _accountStatus;
    public bool IsLocked => _isLocked;
    public string? LockedAt => _lockedAt;
    public string? LastStartupMessage => _lastStartupMessage;

    public async Task InitializeOnStartupAsync()
    {
        if (!IsLoggedIn)
        {
            return;
        }

        try
        {
            var result = await RotateLtokenAsync();
            if (result.Ok)
            {
                _lastStartupMessage = null;
                DebugLog.Info("CloudSyncService",
                    $"[CloudSync] Startup silent ltoken rotation succeeded (locked={_isLocked}).");
            }
            else
            {
                _lastStartupMessage = result.HttpStatusCode == 403 ||
                                      string.Equals(result.Code, "FORBIDDEN", StringComparison.Ordinal)
                    ? "CloudSync_ErrAuthRequired".GetLocalized()
                    : null;
                DebugLog.Info("CloudSyncService",
                    $"[CloudSync] Startup silent ltoken rotation failed: [{result.HttpStatusCode}] {result.Code} - {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _lastStartupMessage = null;
            DebugLog.Info("CloudSyncService", $"[CloudSync] Startup ltoken rotation exception: {ex.Message}");
        }
    }

    public async Task<bool> EnsureAuthenticatedAsync()
    {
        if (HasLtoken)
        {
            return true;
        }

        if (!IsLoggedIn)
        {
            return false;
        }

        var rotate = await RotateLtokenAsync();
        return rotate.Ok && HasLtoken;
    }

    public Uri ResolveServiceUri(string relativeOrAbsoluteUrl)
    {
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        return new Uri(BaseUri, relativeOrAbsoluteUrl.TrimStart('/'));
    }

    public bool IsValidSlotName(string? slot)
    {
        if (string.IsNullOrEmpty(slot))
        {
            return false;
        }

        return ValidSlotRegex().IsMatch(slot) && !slot.Contains("..", StringComparison.Ordinal) && !slot.EndsWith('.');
    }

    public string SuggestValidSlot(string? preferred, string fallbackPrefix = "archive")
    {
        var fallback = IsValidSlotName(fallbackPrefix) ? fallbackPrefix : "archive";
        if (string.IsNullOrWhiteSpace(preferred))
        {
            return fallback;
        }

        var trimmed = preferred.Trim();
        if (IsValidSlotName(trimmed))
        {
            return trimmed;
        }

        var cleaned = UnsafeSlotCharsRegex().Replace(trimmed, "_").Replace("..", "_", StringComparison.Ordinal)
            .Trim('.', '_');
        if (string.IsNullOrEmpty(cleaned) || !char.IsAsciiLetterOrDigit(cleaned[0]))
        {
            cleaned = string.IsNullOrEmpty(cleaned) ? fallback : $"{fallback}_{cleaned}";
        }

        if (cleaned.Length > 64)
        {
            cleaned = cleaned[..64].TrimEnd('.');
        }

        return IsValidSlotName(cleaned) ? cleaned : fallback;
    }

    public string FormatFriendlyError(string? code, string? message, int statusCode = 0)
    {
        if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(message))
        {
            DebugLog.Info("CloudSyncService",
                $"[CloudSync] API error mapped: status={statusCode}, code={code}, detail={message}");
        }

        return code switch
        {
            "ACCOUNT_LOCKED" => "CloudSync_ErrLocked".GetLocalized(),
            "INVALID_CREDENTIALS" => "CloudSync_ErrInvalidCredentials".GetLocalized(),
            "EMAIL_ALREADY_REGISTERED" => "CloudSync_ErrEmailRegistered".GetLocalized(),
            "ACCOUNT_NOT_FOUND" => "CloudSync_ErrAccountNotFound".GetLocalized(),
            "OTP_EXPIRED" => "CloudSync_ErrOtpExpired".GetLocalized(),
            "OTP_INVALID" => "CloudSync_ErrOtpInvalid".GetLocalized(),
            "OTP_TOO_MANY_ATTEMPTS" => "CloudSync_ErrOtpAttempts".GetLocalized(),
            "OTP_COOLDOWN" => "CloudSync_ErrOtpCooldown".GetLocalized(),
            "CHALLENGE_EXPIRED" or "CHALLENGE_INVALID" => "CloudSync_ErrCaptchaFailed".GetLocalized(),
            "FORBIDDEN" => "CloudSync_ErrAuthRequired".GetLocalized(),
            "USER_STORAGE_LIMIT_EXCEEDED" => "CloudSync_ErrStorageLimit".GetLocalized(),
            "USER_FILE_LIMIT_EXCEEDED" => "CloudSync_ErrFileLimit".GetLocalized(),
            "ARCHIVE_ALREADY_EXISTS" => "CloudSync_ErrArchiveExists".GetLocalized(),
            "ARCHIVE_NOT_FOUND" => "CloudSync_ErrArchiveNotFound".GetLocalized(),
            "INVALID_UIAF_SCHEMA" or "UNSUPPORTED_UIAF_VERSION" or "INVALID_UIGF_SCHEMA" or "UNSUPPORTED_UIGF_VERSION"
                or "INVALID_PLAYTIME_SCHEMA" or "UNSUPPORTED_PLAYTIME_VERSION"
                or "INVALID_CHALLENGE_SCHEMA" or "UNSUPPORTED_CHALLENGE_VERSION"
                => "CloudSync_ErrInvalidFormat".GetLocalized(),
            "INVALID_DATA_SLOT" or "INVALID_SLOT" => "CloudSync_ErrInvalidSlot".GetLocalized(),
            "INVALID_ACTIVATION_CODE" or "INVALID_ACTIVATION_CODE_FORMAT" => "CloudSync_ErrActivationInvalid"
                .GetLocalized(),
            "ACTIVATION_ALREADY_REDEEMED" => "CloudSync_ErrActivationRedeemed".GetLocalized(),
            "ACTIVATION_CODE_EXHAUSTED" => "CloudSync_ErrActivationExhausted".GetLocalized(),
            "ACTIVATION_DISABLED" => "CloudSync_ErrActivationDisabled".GetLocalized(),
            "DAILY_OPS_LIMIT_EXCEEDED" or "DAILY_UPLOAD_LIMIT_EXCEEDED" or "DAILY_DELETE_LIMIT_EXCEEDED"
                => "CloudSync_ErrQuotaExceeded".GetLocalized(),
            "RATE_LIMITED" => "CloudSync_ErrRateLimited".GetLocalized(),
            "INVALID_DATA_TYPE" => "CloudSync_ErrUnsupportedDataType".GetLocalized(),
            "BAD_GATEWAY" or "SERVICE_UNAVAILABLE" or "GATEWAY_TIMEOUT" or "UPSTREAM_ERROR"
                => "CloudSync_ErrServiceUnavailable".GetLocalized(),
            _ when statusCode is 502 or 503 or 504 => "CloudSync_ErrServiceUnavailable".GetLocalized(),
            _ when statusCode == 423 => "CloudSync_ErrLocked".GetLocalized(),
            _ when statusCode == 429 => "CloudSync_ErrRateLimited".GetLocalized(),
            _ when statusCode == 413 => "CloudSync_ErrPayloadTooLarge".GetLocalized(),
            _ => "CloudSync_ErrGeneric".GetLocalized()
        };
    }

    private sealed class RawHttpResponse
    {
        public int StatusCode { get; init; }
        public bool IsSuccessStatusCode { get; init; }
        public string Body { get; init; } = string.Empty;
        public TimeSpan? RetryAfter { get; init; }
        public string? DataType { get; init; }
        public string? Slot { get; init; }
        public string? Sha256 { get; init; }
        public long? CompressedBytes { get; init; }
        public long? RawBytes { get; init; }
        public bool IsNetworkError { get; init; }
        public string? NetworkErrorMessage { get; init; }
    }

    private async Task<CloudApiResult<T>> SendAnonymousJsonAsync<T>(HttpMethod method, string path,
        object? payload = null, bool? retryOnTransient = null)
    {
        var jsonPayload = payload == null ? null : JsonSerializer.Serialize(payload);
        var shouldRetry = retryOnTransient ?? (method == HttpMethod.Get);
        var raw = await SendWithTransientRetryAsync(
            () => CreateJsonRequest(method, path, jsonPayload),
            shouldRetry);

        return raw.IsNetworkError
            ? NetworkErrorResult<T>(raw.NetworkErrorMessage)
            : ParseEnvelope<T>(raw.StatusCode, raw.Body, raw.RetryAfter);
    }

    private async Task<CloudApiResult<T>> SendAuthenticatedJsonAsync<T>(HttpMethod method, string path,
        object? payload = null, bool? retryOnTransient = null)
    {
        var jsonPayload = payload == null ? null : JsonSerializer.Serialize(payload);
        var shouldRetry = retryOnTransient ?? (method == HttpMethod.Get);

        var raw = await SendAuthenticatedRawAsync(token =>
        {
            var req = CreateJsonRequest(method, path, jsonPayload);
            req.Headers.Add("X-LToken", token);
            return req;
        }, shouldRetry);

        if (raw == null)
        {
            return UnauthorizedResult<T>();
        }

        return raw.IsNetworkError
            ? NetworkErrorResult<T>(raw.NetworkErrorMessage)
            : ParseEnvelope<T>(raw.StatusCode, raw.Body, raw.RetryAfter);
    }

    private async Task<RawHttpResponse?> SendAuthenticatedRawAsync(
        Func<string, HttpRequestMessage> authenticatedRequestFactory,
        bool retryOnTransientFailure)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return null;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var usedLtoken = _ltoken;
            var raw = await SendWithTransientRetryAsync(
                () => authenticatedRequestFactory(usedLtoken),
                retryOnTransientFailure);

            if (raw.IsNetworkError)
            {
                return raw;
            }

            if (raw.StatusCode == 403 && attempt == 0 && IsLoggedIn)
            {
                var rotate = await RotateLtokenInternalAsync(usedLtoken);
                if (rotate.Ok)
                {
                    continue;
                }
            }

            return raw;
        }

        return null;
    }

    private async Task<RawHttpResponse> SendWithTransientRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        bool retryOnTransientFailure)
    {
        var maxRetryCount = retryOnTransientFailure ? _transientRetryDelays.Count : 0;
        for (var attempt = 0; attempt <= maxRetryCount; attempt++)
        {
            try
            {
                using var req = requestFactory();
                var method = req.Method.Method;
                var targetPath = req.RequestUri?.IsAbsoluteUri == true
                    ? req.RequestUri.AbsolutePath
                    : req.RequestUri?.OriginalString ?? string.Empty;

                using var resp = await _httpClient.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                if (attempt < maxRetryCount && IsTransientGatewayStatus(resp.StatusCode))
                {
                    DebugLog.Info("CloudSyncService",
                        $"[CloudSync] Transient HTTP {(int)resp.StatusCode} on {method} {targetPath}, retrying ({attempt + 1}/{maxRetryCount})...");
                    await Task.Delay(_transientRetryDelays[attempt]);
                    continue;
                }

                return new RawHttpResponse
                {
                    StatusCode = (int)resp.StatusCode,
                    IsSuccessStatusCode = resp.IsSuccessStatusCode,
                    Body = body,
                    RetryAfter = GetRetryAfter(resp.Headers),
                    DataType = TryGetHeader(resp.Headers, "X-Data-Type"),
                    Slot = TryGetHeader(resp.Headers, "X-Data-Slot"),
                    Sha256 = TryGetHeader(resp.Headers, "X-Data-SHA256"),
                    CompressedBytes = long.TryParse(TryGetHeader(resp.Headers, "X-Compressed-Bytes"), out var cb)
                        ? cb
                        : null,
                    RawBytes = long.TryParse(TryGetHeader(resp.Headers, "X-Raw-Bytes"), out var rb)
                        ? rb
                        : null
                };
            }
            catch (HttpRequestException ex) when (attempt < maxRetryCount)
            {
                DebugLog.Info("CloudSyncService",
                    $"[CloudSync] Transient transport error ({ex.Message}), retrying ({attempt + 1}/{maxRetryCount})...");
                await Task.Delay(_transientRetryDelays[attempt]);
            }
            catch (Exception ex)
            {
                return new RawHttpResponse
                {
                    IsNetworkError = true,
                    NetworkErrorMessage = ex.Message
                };
            }
        }

        return new RawHttpResponse
        {
            IsNetworkError = true,
            NetworkErrorMessage = "Transient retry attempts exhausted."
        };
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string path, string? jsonPayload)
    {
        var req = new HttpRequestMessage(method, path);
        if (jsonPayload != null)
        {
            req.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
        }

        return req;
    }

    private static bool IsTransientGatewayStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static CloudApiResult<T> NetworkErrorResult<T>(string? message)
        => new()
        {
            Ok = false,
            HttpStatusCode = 0,
            Code = "NETWORK_ERROR",
            Message = message ?? string.Empty
        };

    private static TimeSpan? GetRetryAfter(HttpResponseHeaders headers)
        => headers.RetryAfter?.Delta ?? (headers.RetryAfter?.Date - DateTimeOffset.UtcNow);

    private static bool LooksLikeJsonValueStart(string? rawBody, bool objectOnly)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return false;
        }

        foreach (var ch in rawBody)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            return objectOnly ? ch == '{' : ch is '{' or '[';
        }

        return false;
    }

    private static string SummarizeNonJsonBody(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return "<empty>";
        }

        var normalized = rawBody.Trim()
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\t", " ", StringComparison.Ordinal);

        return normalized.Length > 120 ? normalized[..120] + "..." : normalized;
    }

    private static (string Code, string ResourceKey) MapUpstreamHttpError(int statusCode)
        => statusCode switch
        {
            502 => ("BAD_GATEWAY", "CloudSync_ErrServiceUnavailable"),
            503 => ("SERVICE_UNAVAILABLE", "CloudSync_ErrServiceUnavailable"),
            504 => ("GATEWAY_TIMEOUT", "CloudSync_ErrServiceUnavailable"),
            >= 500 => ("UPSTREAM_ERROR", "CloudSync_ErrServiceUnavailable"),
            429 => ("RATE_LIMITED", "CloudSync_ErrRateLimited"),
            423 => ("ACCOUNT_LOCKED", "CloudSync_ErrLocked"),
            413 => ("PAYLOAD_TOO_LARGE", "CloudSync_ErrPayloadTooLarge"),
            403 => ("FORBIDDEN", "CloudSync_ErrAuthRequired"),
            _ => ("INVALID_RESPONSE", "CloudSync_ErrGeneric")
        };

    private static CloudApiResult<T> ParseEnvelope<T>(int statusCode, string rawBody, TimeSpan? retryAfter = null)
    {
        if (!LooksLikeJsonValueStart(rawBody, objectOnly: true))
        {
            var (code, resourceKey) = MapUpstreamHttpError(statusCode);
            DebugLog.Info("CloudSyncService",
                $"[CloudSync] Non-JSON upstream response (HTTP {statusCode}, code={code}): {SummarizeNonJsonBody(rawBody)}");
            return new CloudApiResult<T>
            {
                Ok = false,
                HttpStatusCode = statusCode,
                RetryAfter = retryAfter,
                Code = code,
                Message = resourceKey.GetLocalized()
            };
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<CloudApiEnvelope>(rawBody, JsonOptions);
            if (envelope == null)
            {
                var (code, resourceKey) = MapUpstreamHttpError(statusCode);
                DebugLog.Info("CloudSyncService", $"[CloudSync] Empty envelope from HTTP {statusCode}");
                return new CloudApiResult<T>
                {
                    Ok = false,
                    HttpStatusCode = statusCode,
                    RetryAfter = retryAfter,
                    Code = code,
                    Message = resourceKey.GetLocalized()
                };
            }

            T? data = default;
            CloudChallengeInfo? challenge = null;

            if (envelope.Data.HasValue && envelope.Data.Value.ValueKind == JsonValueKind.Object)
            {
                if (statusCode == 202 && string.Equals(envelope.Code, "CAPTCHA_REQUIRED", StringComparison.Ordinal))
                {
                    challenge = envelope.Data.Value.Deserialize<CloudChallengeInfo>(JsonOptions);
                    if (typeof(T) == typeof(CloudChallengeInfo))
                    {
                        data = (T?)(object?)challenge;
                    }
                }
                else
                {
                    data = envelope.Data.Value.Deserialize<T>(JsonOptions);
                }
            }

            return new CloudApiResult<T>
            {
                Ok = envelope.Ok,
                HttpStatusCode = statusCode,
                RetryAfter = retryAfter,
                Code = envelope.Code ?? string.Empty,
                Message = envelope.Message ?? string.Empty,
                Data = data,
                Challenge = challenge
            };
        }
        catch (Exception ex)
        {
            var (code, resourceKey) = statusCode >= 500
                ? MapUpstreamHttpError(statusCode)
                : ("PARSE_ERROR", "CloudSync_ErrGeneric");
            DebugLog.Info("CloudSyncService", $"[CloudSync] Envelope parse failed (HTTP {statusCode}): {ex.Message}");
            return new CloudApiResult<T>
            {
                Ok = false,
                HttpStatusCode = statusCode,
                RetryAfter = retryAfter,
                Code = code,
                Message = resourceKey.GetLocalized()
            };
        }
    }

    private static CloudApiResult<T> UnauthorizedResult<T>()
        => new()
        {
            Ok = false,
            HttpStatusCode = 403,
            Code = "FORBIDDEN",
            Message = "CloudSync_ErrAuthRequired".GetLocalized()
        };

    private static string? TryGetHeader(HttpResponseHeaders headers, string name)
        => headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private bool IsStokenExpiredLocally()
        => _stokenExpiresAtUnix > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= _stokenExpiresAtUnix - 30;

    private bool IsLtokenExpiredLocally()
        => _ltokenExpiresAtUnix > 0 && DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= _ltokenExpiresAtUnix - 30;

    private void UpdateAccountLockState(CloudAccountStatusData status)
    {
        _accountStatus = string.IsNullOrWhiteSpace(status.AccountStatus)
            ? (status.Locked ? "locked" : "active")
            : status.AccountStatus;
        _isLocked = status.Locked || string.Equals(_accountStatus, "locked", StringComparison.OrdinalIgnoreCase);
        _lockedAt = status.LockedAt;
        RaiseSessionChanged();
    }

    private void MarkLockedIf423(int statusCode, string code)
    {
        if (statusCode == 423 || string.Equals(code, "ACCOUNT_LOCKED", StringComparison.Ordinal))
        {
            _accountStatus = "locked";
            _isLocked = true;
            RaiseSessionChanged();
        }
    }

    private void ClearSessionState(bool clearEmail)
    {
        if (clearEmail)
        {
            _email = string.Empty;
        }

        _stoken = string.Empty;
        _stokenExpiresAtUnix = 0;
        _ltoken = string.Empty;
        _ltokenExpiresAtUnix = 0;
        _accountStatus = "unknown";
        _isLocked = false;
        _lockedAt = null;
        _lastStartupMessage = null;

        SavePersistedSession();
        RaiseSessionChanged();
    }

    private void LoadPersistedSession()
    {
        try
        {
            if (!File.Exists(_sessionFilePath))
            {
                return;
            }

            var encrypted = File.ReadAllBytes(_sessionFilePath);
            if (encrypted.Length == 0)
            {
                return;
            }

            var decrypted = ProtectedData.Unprotect(encrypted, DpapiEntropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            var session = JsonSerializer.Deserialize<CloudPersistedSession>(json, JsonOptions);
            if (session == null)
            {
                return;
            }

            _email = session.Email ?? string.Empty;
            _stoken = session.Stoken ?? string.Empty;
            _stokenExpiresAtUnix = session.StokenExpiresAtUnix;

            if (IsStokenExpiredLocally())
            {
                _stoken = string.Empty;
                _stokenExpiresAtUnix = 0;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncService", $"[CloudSync] Failed to load persisted session: {ex.Message}");
        }
    }

    private void SavePersistedSession()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDir);

            if (string.IsNullOrWhiteSpace(_email) && string.IsNullOrWhiteSpace(_stoken))
            {
                if (File.Exists(_sessionFilePath))
                {
                    File.Delete(_sessionFilePath);
                }

                return;
            }

            var session = new CloudPersistedSession
            {
                Email = _email,
                Stoken = _stoken,
                StokenExpiresAtUnix = _stokenExpiresAtUnix
            };

            var plainBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(session));
            var encryptedBytes = ProtectedData.Protect(plainBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_sessionFilePath, encryptedBytes);
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncService", $"[CloudSync] Failed to save persisted session: {ex.Message}");
        }
    }

    private void RaiseSessionChanged()
    {
        SessionChanged?.Invoke(this, EventArgs.Empty);
    }
}
