/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed class DevVerifyStatusResult
{
    public bool Ok { get; init; }
    public int HttpStatusCode { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public bool Authorized { get; init; }
    public string Status { get; init; } = "unknown";
}

public sealed class DevApplySubmissionData
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;
}

public sealed partial class DeveloperAuthorizationService
{
    private const string VerifyHwidUrl = ApiEndpoints.DevVerifyHwidUrl;
    private const string ApplyUrl = ApiEndpoints.DevApplyUrl;

    private static readonly Uri BaseUri = new(ApiEndpoints.DevServiceBase.TrimEnd('/') + "/", UriKind.Absolute);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly TimeSpan[] DefaultTransientRetryDelays =
    [
        TimeSpan.FromMilliseconds(350),
        TimeSpan.FromMilliseconds(900)
    ];

    [GeneratedRegex("^[0-9A-Fa-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex HwidHexRegex();

    [GeneratedRegex("^[1-9]\\d{8,9}$", RegexOptions.CultureInvariant)]
    private static partial Regex UidDigitsRegex();

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<TimeSpan> _transientRetryDelays;
    private bool _hasChecked;
    private bool _isAuthorized;

    public DeveloperAuthorizationService()
        : this(CreateDefaultHandler(), null)
    {
    }

    internal DeveloperAuthorizationService(
        HttpMessageHandler handler,
        IReadOnlyList<TimeSpan>? transientRetryDelays = null)
    {
        _transientRetryDelays = transientRetryDelays ?? DefaultTransientRetryDelays;
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(15)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FufuLauncher-DevAuth/1.0");
    }

    private static HttpClientHandler CreateDefaultHandler()
        => new()
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = false
        };

    public bool IsTemporaryAuthorized
    {
        get;
        set;
    }

    public static bool IsValidHwid(string? hwid)
    {
        if (string.IsNullOrWhiteSpace(hwid) || string.Equals(hwid.Trim(), "Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return HwidHexRegex().IsMatch(hwid.Trim());
    }

    public static bool IsValidUid(string? uid)
        => !string.IsNullOrWhiteSpace(uid) && UidDigitsRegex().IsMatch(uid.Trim());

    public static bool IsValidUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        var trimmed = username.Trim();
        if (trimmed.Length is < 1 or > 32 || trimmed.Contains('<') || trimmed.Contains('>'))
        {
            return false;
        }

        foreach (var ch in trimmed)
        {
            if (char.IsControl(ch))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsValidOptionalGithubUrl(string? github)
    {
        if (string.IsNullOrWhiteSpace(github))
        {
            return true;
        }

        var trimmed = github.Trim();
        if (trimmed.Length > 128 || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Host, "www.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length is 1 or 2;
    }

    public Uri ResolveServiceUri(string relativeOrAbsoluteUrl)
    {
        var resolved = TryResolveServiceUri(relativeOrAbsoluteUrl);
        if (resolved == null)
        {
            throw new InvalidOperationException("Target URL must use HTTPS on the trusted developer service host.");
        }

        return resolved;
    }

    public Uri? TryResolveServiceUri(string? relativeOrAbsoluteUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsoluteUrl))
        {
            return null;
        }

        Uri resolved;
        if (Uri.TryCreate(relativeOrAbsoluteUrl, UriKind.Absolute, out var absolute))
        {
            resolved = absolute;
        }
        else if (!Uri.TryCreate(BaseUri, relativeOrAbsoluteUrl.TrimStart('/'), out resolved!))
        {
            return null;
        }

        if (!string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(resolved.Host, BaseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            resolved.Port != BaseUri.Port)
        {
            return null;
        }

        return resolved;
    }

    public async Task<bool> IsAuthorizedAsync()
    {
        if (IsTemporaryAuthorized)
        {
            return true;
        }

        if (_hasChecked && _isAuthorized)
        {
            return true;
        }

        string hwid = await Task.Run(SystemEnvironmentHelper.GetHwid).ConfigureAwait(false);
        if (!IsValidHwid(hwid))
        {
            _hasChecked = true;
            _isAuthorized = false;
            return false;
        }

        var status = await GetVerificationStatusAsync(hwid).ConfigureAwait(false);
        _hasChecked = true;
        _isAuthorized = status.Ok && status.Authorized;
        return _isAuthorized;
    }

    public async Task<DevVerifyStatusResult> GetVerificationStatusAsync(string hwid)
    {
        if (!IsValidHwid(hwid))
        {
            return new DevVerifyStatusResult
            {
                Ok = false,
                HttpStatusCode = 0,
                Code = "INVALID_HWID",
                Message = "DevAuth_ErrInvalidHwid".GetLocalized(),
                Authorized = false,
                Status = "unknown"
            };
        }

        var jsonPayload = JsonSerializer.Serialize(new { hwid = hwid.Trim().ToUpperInvariant() });
        var raw = await SendWithTransientRetryAsync(
            () => CreateJsonRequest(HttpMethod.Post, VerifyHwidUrl, jsonPayload),
            retryOnTransientFailure: true).ConfigureAwait(false);

        if (raw.IsNetworkError)
        {
            return new DevVerifyStatusResult
            {
                Ok = false,
                HttpStatusCode = 0,
                Code = "NETWORK_ERROR",
                Message = "Settings_NetworkError".GetLocalized(),
                Authorized = false,
                Status = "unknown"
            };
        }

        if (!LooksLikeJsonObjectStart(raw.Body))
        {
            var (code, resourceKey) = MapUpstreamHttpError(raw.StatusCode);
            DebugLog.Info("DeveloperAuthorizationService",
                $"[DevAuth] Non-JSON upstream response on verify-hwid (HTTP {raw.StatusCode}, code={code})");
            return new DevVerifyStatusResult
            {
                Ok = false,
                HttpStatusCode = raw.StatusCode,
                Code = code,
                Message = resourceKey.GetLocalized(),
                Authorized = false,
                Status = "unknown"
            };
        }

        try
        {
            using var doc = JsonDocument.Parse(raw.Body);
            var root = doc.RootElement;
            bool ok = root.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.True;
            string code = TryGetStringProperty(root, "code") ?? string.Empty;
            string message = TryGetStringProperty(root, "message") ?? string.Empty;
            bool authorized = root.TryGetProperty("authorized", out var authProp) &&
                              authProp.ValueKind == JsonValueKind.True;
            string status = TryGetStringProperty(root, "status") ?? (authorized ? "approved" : "unknown");

            if (ok && authorized)
            {
                _hasChecked = true;
                _isAuthorized = true;
            }

            return new DevVerifyStatusResult
            {
                Ok = ok,
                HttpStatusCode = raw.StatusCode,
                Code = code,
                Message = message,
                Authorized = authorized,
                Status = status
            };
        }
        catch (Exception ex)
        {
            DebugLog.Info("DeveloperAuthorizationService",
                $"[DevAuth] verify-hwid JSON parse failed (HTTP {raw.StatusCode}): {ex.Message}");
            var (code, resourceKey) = raw.StatusCode >= 500
                ? MapUpstreamHttpError(raw.StatusCode)
                : ("PARSE_ERROR", "CloudSync_ErrGeneric");
            return new DevVerifyStatusResult
            {
                Ok = false,
                HttpStatusCode = raw.StatusCode,
                Code = code,
                Message = resourceKey.GetLocalized(),
                Authorized = false,
                Status = "unknown"
            };
        }
    }

    public async Task<CloudApiResult<CloudChallengeInfo>> StartApplicationAsync(
        string uid,
        string username,
        string hwid,
        string? github = null)
    {
        var jsonPayload = JsonSerializer.Serialize(new
        {
            uid = (uid ?? string.Empty).Trim(),
            username = (username ?? string.Empty).Trim(),
            hwid = (hwid ?? string.Empty).Trim().ToUpperInvariant(),
            github = (github ?? string.Empty).Trim()
        });

        var raw = await SendWithTransientRetryAsync(
            () => CreateJsonRequest(HttpMethod.Post, ApplyUrl, jsonPayload),
            retryOnTransientFailure: false).ConfigureAwait(false);

        return raw.IsNetworkError
            ? NetworkErrorResult<CloudChallengeInfo>(raw.NetworkErrorMessage)
            : ParseEnvelope<CloudChallengeInfo>(raw.StatusCode, raw.Body);
    }

    public async Task<CloudApiResult<DevApplySubmissionData>> PollApplicationChallengeAsync(
        CloudChallengeInfo challenge)
    {
        var pollUri = TryResolveServiceUri(challenge?.PollUrl);
        if (pollUri == null || string.IsNullOrWhiteSpace(challenge?.ChallengeSecret))
        {
            return new CloudApiResult<DevApplySubmissionData>
            {
                Ok = false,
                HttpStatusCode = 400,
                Code = "SECURITY_REJECTED",
                Message = "DevAuth_ErrSecurityRejected".GetLocalized()
            };
        }

        var secret = challenge.ChallengeSecret;
        var raw = await SendWithTransientRetryAsync(
            () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, pollUri);
                req.Headers.TryAddWithoutValidation("X-Challenge-Secret", secret);
                return req;
            },
            retryOnTransientFailure: true).ConfigureAwait(false);

        return raw.IsNetworkError
            ? NetworkErrorResult<DevApplySubmissionData>(raw.NetworkErrorMessage)
            : ParseEnvelope<DevApplySubmissionData>(raw.StatusCode, raw.Body);
    }

    public string FormatFriendlyError(string? code, string? message, int statusCode = 0)
    {
        if (!string.IsNullOrWhiteSpace(code) || !string.IsNullOrWhiteSpace(message))
        {
            DebugLog.Info("DeveloperAuthorizationService",
                $"[DevAuth] API error mapped: status={statusCode}, code={code}, detail={message}");
        }

        return code switch
        {
            "ALREADY_APPROVED" => "DevAuth_ErrAlreadyApproved".GetLocalized(),
            "ALREADY_PENDING" => "DevAuth_ErrAlreadyPending".GetLocalized(),
            "APPLICATION_REJECTED" => "DevAuth_ErrRejected".GetLocalized(),
            "UID_DEVICE_LIMIT" => "DevAuth_ErrUidDeviceLimit".GetLocalized(),
            "CAPTCHA_NOT_CONFIGURED" => "DevAuth_ErrCaptchaNotConfigured".GetLocalized(),
            "INVALID_UID" => "DevAuth_ErrInvalidUid".GetLocalized(),
            "INVALID_USERNAME" => "DevAuth_ErrInvalidUsername".GetLocalized(),
            "INVALID_GITHUB" => "DevAuth_ErrInvalidGithub".GetLocalized(),
            "INVALID_HWID" => "DevAuth_ErrInvalidHwid".GetLocalized(),
            "CAPTCHA_FAILED"
                or "CAPTCHA_EXPIRED"
                or "CAPTCHA_INVALID_PARAMS"
                or "CAPTCHA_INVALID"
                or "CHALLENGE_EXPIRED"
                or "CHALLENGE_USED"
                or "CHALLENGE_ALREADY_USED"
                or "CHALLENGE_NOT_FOUND"
                or "CHALLENGE_INVALID" => "CloudSync_ErrCaptchaFailed".GetLocalized(),
            "RATE_LIMITED" => "CloudSync_ErrRateLimited".GetLocalized(),
            "QUOTA_EXCEEDED" => "CloudSync_ErrQuotaExceeded".GetLocalized(),
            "FORBIDDEN"
                or "BAD_REQUEST"
                or "REQUEST_REJECTED"
                or "SECURITY_REJECTED"
                or "IP_BANNED"
                or "IP_TEMP_BANNED"
                or "HONEYPOT_TRIGGERED"
                or "ORIGIN_FORBIDDEN"
                or "CSRF_MISSING"
                or "CSRF_INVALID"
                or "INVALID_FIELDS"
                or "INVALID_ID"
                or "INVALID_JSON"
                or "INVALID_FORM"
                or "UNSUPPORTED_MEDIA_TYPE" => "DevAuth_ErrSecurityRejected".GetLocalized(),
            "BAD_GATEWAY"
                or "SERVICE_UNAVAILABLE"
                or "GATEWAY_TIMEOUT"
                or "UPSTREAM_ERROR"
                or "CAPTCHA_UPSTREAM_ERROR"
                or "INTERNAL_ERROR"
                or "SERVER_MISCONFIGURED" => "CloudSync_ErrServiceUnavailable".GetLocalized(),
            "NETWORK_ERROR" => "Settings_NetworkError".GetLocalized(),
            _ when statusCode >= 500 => "CloudSync_ErrServiceUnavailable".GetLocalized(),
            _ when statusCode == 429 => "CloudSync_ErrRateLimited".GetLocalized(),
            _ => "CloudSync_ErrGeneric".GetLocalized()
        };
    }

    private sealed class RawHttpResponse
    {
        public int StatusCode { get; init; }
        public string Body { get; init; } = string.Empty;
        public bool IsNetworkError { get; init; }
        public string? NetworkErrorMessage { get; init; }
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
                using var resp = await _httpClient.SendAsync(req).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (attempt < maxRetryCount && IsTransientGatewayStatus(resp.StatusCode))
                {
                    DebugLog.Info("DeveloperAuthorizationService",
                        $"[DevAuth] Transient HTTP {(int)resp.StatusCode}, retrying ({attempt + 1}/{maxRetryCount})...");
                    await Task.Delay(_transientRetryDelays[attempt]).ConfigureAwait(false);
                    continue;
                }

                return new RawHttpResponse
                {
                    StatusCode = (int)resp.StatusCode,
                    Body = body
                };
            }
            catch (HttpRequestException ex) when (attempt < maxRetryCount)
            {
                DebugLog.Info("DeveloperAuthorizationService",
                    $"[DevAuth] Transient transport error ({ex.Message}), retrying ({attempt + 1}/{maxRetryCount})...");
                await Task.Delay(_transientRetryDelays[attempt]).ConfigureAwait(false);
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

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string absoluteOrRelativeUrl, string jsonPayload)
    {
        var req = new HttpRequestMessage(method, absoluteOrRelativeUrl)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };
        return req;
    }

    private static bool IsTransientGatewayStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static bool LooksLikeJsonObjectStart(string? rawBody)
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

            return ch == '{';
        }

        return false;
    }

    private static (string Code, string ResourceKey) MapUpstreamHttpError(int statusCode)
        => statusCode switch
        {
            502 => ("BAD_GATEWAY", "CloudSync_ErrServiceUnavailable"),
            503 => ("SERVICE_UNAVAILABLE", "CloudSync_ErrServiceUnavailable"),
            504 => ("GATEWAY_TIMEOUT", "CloudSync_ErrServiceUnavailable"),
            >= 500 => ("UPSTREAM_ERROR", "CloudSync_ErrServiceUnavailable"),
            429 => ("RATE_LIMITED", "CloudSync_ErrRateLimited"),
            403 => ("SECURITY_REJECTED", "DevAuth_ErrSecurityRejected"),
            _ => ("INVALID_RESPONSE", "CloudSync_ErrGeneric")
        };

    private static CloudApiResult<T> NetworkErrorResult<T>(string? message)
        => new()
        {
            Ok = false,
            HttpStatusCode = 0,
            Code = "NETWORK_ERROR",
            Message = message ?? "Settings_NetworkError".GetLocalized()
        };

    private static CloudApiResult<T> ParseEnvelope<T>(int statusCode, string rawBody)
    {
        if (!LooksLikeJsonObjectStart(rawBody))
        {
            var (code, resourceKey) = MapUpstreamHttpError(statusCode);
            DebugLog.Info("DeveloperAuthorizationService",
                $"[DevAuth] Non-JSON upstream response (HTTP {statusCode}, code={code})");
            return new CloudApiResult<T>
            {
                Ok = false,
                HttpStatusCode = statusCode,
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
                return new CloudApiResult<T>
                {
                    Ok = false,
                    HttpStatusCode = statusCode,
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
            DebugLog.Info("DeveloperAuthorizationService",
                $"[DevAuth] Envelope parse failed (HTTP {statusCode}): {ex.Message}");
            return new CloudApiResult<T>
            {
                Ok = false,
                HttpStatusCode = statusCode,
                Code = code,
                Message = resourceKey.GetLocalized()
            };
        }
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            return prop.GetString();
        }

        return null;
    }
}