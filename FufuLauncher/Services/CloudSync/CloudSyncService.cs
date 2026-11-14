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

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _authLock = new(1, 1);
    private readonly string _sessionFilePath;

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
    {
        _sessionFilePath = Path.Combine(AppPaths.SettingsDir, SessionFileName);
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false
        };
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(30)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FufuLauncher-CloudSync/1.0");

        LoadPersistedSession();
    }

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
            _ when statusCode == 423 => "CloudSync_ErrLocked".GetLocalized(),
            _ when statusCode == 429 => "CloudSync_ErrRateLimited".GetLocalized(),
            _ when statusCode == 413 => "CloudSync_ErrPayloadTooLarge".GetLocalized(),
            _ => "CloudSync_ErrGeneric".GetLocalized()
        };
    }

    private async Task<CloudApiResult<T>> SendAnonymousJsonAsync<T>(HttpMethod method, string path,
        object? payload = null)
    {
        try
        {
            using var req = new HttpRequestMessage(method, path);
            if (payload != null)
            {
                req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            }

            using var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            return ParseEnvelope<T>((int)resp.StatusCode, body, GetRetryAfter(resp.Headers));
        }
        catch (Exception ex)
        {
            return new CloudApiResult<T>
            {
                Ok = false,
                HttpStatusCode = 0,
                Code = "NETWORK_ERROR",
                Message = ex.Message
            };
        }
    }

    private async Task<CloudApiResult<T>> SendAuthenticatedJsonAsync<T>(HttpMethod method, string path,
        object? payload = null)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return UnauthorizedResult<T>();
        }

        var jsonPayload = payload == null ? null : JsonSerializer.Serialize(payload);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var usedLtoken = _ltoken;
            try
            {
                using var req = new HttpRequestMessage(method, path);
                req.Headers.Add("X-LToken", usedLtoken);
                if (jsonPayload != null)
                {
                    req.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                }

                using var resp = await _httpClient.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                if ((int)resp.StatusCode == 403 && attempt == 0 && IsLoggedIn)
                {
                    var rotate = await RotateLtokenInternalAsync(usedLtoken);
                    if (rotate.Ok)
                    {
                        continue;
                    }
                }

                return ParseEnvelope<T>((int)resp.StatusCode, body);
            }
            catch (Exception ex)
            {
                return new CloudApiResult<T>
                {
                    Ok = false,
                    HttpStatusCode = 0,
                    Code = "NETWORK_ERROR",
                    Message = ex.Message
                };
            }
        }

        return UnauthorizedResult<T>();
    }

    private static TimeSpan? GetRetryAfter(HttpResponseHeaders headers)
        => headers.RetryAfter?.Delta ?? (headers.RetryAfter?.Date - DateTimeOffset.UtcNow);

    private static CloudApiResult<T> ParseEnvelope<T>(int statusCode, string rawBody, TimeSpan? retryAfter = null)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<CloudApiEnvelope>(rawBody, JsonOptions);
            if (envelope == null)
            {
                DebugLog.Info("CloudSyncService", $"[CloudSync] Empty envelope from HTTP {statusCode}");
                return new CloudApiResult<T>
                {
                    Ok = false,
                    HttpStatusCode = statusCode,
                    RetryAfter = retryAfter,
                    Code = "INVALID_RESPONSE",
                    Message = "CloudSync_ErrGeneric".GetLocalized()
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
            DebugLog.Info("CloudSyncService", $"[CloudSync] Envelope parse failed (HTTP {statusCode}): {ex.Message}");
            return new CloudApiResult<T>
            {
                Ok = false,
                HttpStatusCode = statusCode,
                RetryAfter = retryAfter,
                Code = "PARSE_ERROR",
                Message = "CloudSync_ErrGeneric".GetLocalized()
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
