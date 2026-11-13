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

    public Task<CloudApiResult<CloudChallengeInfo>> StartLoginAsync(string email, string password)
        => SendAnonymousJsonAsync<CloudChallengeInfo>(HttpMethod.Post, ApiEndpoints.CloudSyncAuthLoginPath, new
        {
            email = email.Trim(),
            password
        });

    public Task<CloudApiResult<CloudChallengeInfo>> StartRegisterSendCodeAsync(string email)
        => SendAnonymousJsonAsync<CloudChallengeInfo>(HttpMethod.Post, ApiEndpoints.CloudSyncAuthRegisterSendCodePath,
            new
            {
                email = email.Trim()
            });

    public async Task<CloudApiResult<CloudLoginSessionData>> ConfirmRegisterAsync(string email, string password,
        string code)
    {
        var trimmedEmail = email.Trim();
        var result = await SendAnonymousJsonAsync<CloudLoginSessionData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncAuthRegisterConfirmPath, new
            {
                email = trimmedEmail,
                password,
                code = code.Trim()
            });

        if (result.Ok && result.Data != null)
        {
            ApplyLoginSession(trimmedEmail, result.Data);
        }

        return result;
    }

    public Task<CloudApiResult<CloudChallengeInfo>> StartResetPasswordSendCodeAsync(string email)
        => SendAnonymousJsonAsync<CloudChallengeInfo>(HttpMethod.Post,
            ApiEndpoints.CloudSyncAuthResetPasswordSendCodePath, new
            {
                email = email.Trim()
            });

    public Task<CloudApiResult<object>> ConfirmResetPasswordAsync(string email, string code, string newPassword)
        => SendAnonymousJsonAsync<object>(HttpMethod.Post, ApiEndpoints.CloudSyncAuthResetPasswordConfirmPath, new
        {
            email = email.Trim(),
            code = code.Trim(),
            new_password = newPassword
        });

    public async Task<CloudApiResult<T>> PollChallengeAsync<T>(string pollUrl, string challengeSecret)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ResolveServiceUri(pollUrl));
            req.Headers.Add("X-Challenge-Secret", challengeSecret);
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

    public void ApplyLoginSession(string email, CloudLoginSessionData loginData)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _email = string.IsNullOrWhiteSpace(email) ? _email : email.Trim();
        _stoken = loginData.Stoken;
        _stokenExpiresAtUnix = loginData.StokenExpiresIn > 0 ? now + loginData.StokenExpiresIn : 0;
        _ltoken = loginData.Ltoken;
        _ltokenExpiresAtUnix = loginData.LtokenExpiresIn > 0 ? now + loginData.LtokenExpiresIn : 0;
        _accountStatus = string.IsNullOrWhiteSpace(loginData.AccountStatus)
            ? (loginData.Locked ? "locked" : "active")
            : loginData.AccountStatus;
        _isLocked = loginData.Locked || string.Equals(_accountStatus, "locked", StringComparison.OrdinalIgnoreCase);
        _lockedAt = loginData.LockedAt;
        _lastStartupMessage = null;

        SavePersistedSession();
        RaiseSessionChanged();
    }

    public async Task<CloudApiResult<CloudLtokenIssueData>> RotateLtokenAsync()
    {
        return await RotateLtokenInternalAsync(staleLtoken: null);
    }

    private async Task<CloudApiResult<CloudLtokenIssueData>> RotateLtokenInternalAsync(string? staleLtoken)
    {
        await _authLock.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(staleLtoken) &&
                !string.IsNullOrEmpty(_ltoken) &&
                !string.Equals(_ltoken, staleLtoken, StringComparison.Ordinal) &&
                !IsLtokenExpiredLocally())
            {
                return new CloudApiResult<CloudLtokenIssueData>
                {
                    Ok = true,
                    HttpStatusCode = 200,
                    Code = "LTOKEN_REUSED",
                    Data = new CloudLtokenIssueData
                    {
                        Ltoken = _ltoken,
                        AccountStatus = _accountStatus,
                        Locked = _isLocked,
                        LockedAt = _lockedAt
                    }
                };
            }

            if (string.IsNullOrWhiteSpace(_stoken) || IsStokenExpiredLocally())
            {
                ClearSessionState(clearEmail: false);
                return new CloudApiResult<CloudLtokenIssueData>
                {
                    Ok = false,
                    HttpStatusCode = 403,
                    Code = "FORBIDDEN",
                    Message = "CloudSync_ErrAuthRequired".GetLocalized()
                };
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.CloudSyncAuthLtokenPath);
            req.Headers.Add("X-SToken", _stoken);
            req.Content = new StringContent("{}", Encoding.UTF8, "application/json");

            using var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            var parsed = ParseEnvelope<CloudLtokenIssueData>((int)resp.StatusCode, body);

            if (parsed.Ok && parsed.Data != null && !string.IsNullOrWhiteSpace(parsed.Data.Ltoken))
            {
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                _ltoken = parsed.Data.Ltoken;
                _ltokenExpiresAtUnix = parsed.Data.ExpiresIn > 0 ? now + parsed.Data.ExpiresIn : 0;
                _accountStatus = string.IsNullOrWhiteSpace(parsed.Data.AccountStatus)
                    ? (parsed.Data.Locked ? "locked" : "active")
                    : parsed.Data.AccountStatus;
                _isLocked = parsed.Data.Locked ||
                            string.Equals(_accountStatus, "locked", StringComparison.OrdinalIgnoreCase);
                _lockedAt = parsed.Data.LockedAt;
                RaiseSessionChanged();
            }
            else if ((int)resp.StatusCode == 403)
            {
                ClearSessionState(clearEmail: false);
            }

            return parsed;
        }
        catch (Exception ex)
        {
            return new CloudApiResult<CloudLtokenIssueData>
            {
                Ok = false,
                HttpStatusCode = 0,
                Code = "NETWORK_ERROR",
                Message = ex.Message
            };
        }
        finally
        {
            _authLock.Release();
        }
    }

    public async Task<CloudApiResult<object>> LogoutAsync(bool revokeStoken = false)
    {
        CloudApiResult<object> result;
        if (HasLtoken)
        {
            result = await SendAuthenticatedJsonAsync<object>(HttpMethod.Post, ApiEndpoints.CloudSyncAuthLogoutPath, new
            {
                revoke_stoken = revokeStoken
            });
        }
        else
        {
            result = new CloudApiResult<object> { Ok = true, HttpStatusCode = 200, Code = "LOGOUT_LOCAL" };
        }

        _ltoken = string.Empty;
        _ltokenExpiresAtUnix = 0;
        if (revokeStoken)
        {
            ClearSessionState(clearEmail: false);
        }
        else
        {
            ClearSessionState(clearEmail: false);
        }

        return result;
    }

    public Task ClearLocalSessionAsync()
    {
        ClearSessionState(clearEmail: false);
        return Task.CompletedTask;
    }

    public async Task<CloudApiResult<CloudAccountStatusData>> GetAccountStatusAsync()
    {
        var result =
            await SendAuthenticatedJsonAsync<CloudAccountStatusData>(HttpMethod.Get,
                ApiEndpoints.CloudSyncAccountStatusPath);
        if (result.Ok && result.Data != null)
        {
            UpdateAccountLockState(result.Data);
        }

        return result;
    }

    public Task<CloudApiResult<CloudOtpSentData>> SendLockCodeAsync()
        => SendAuthenticatedJsonAsync<CloudOtpSentData>(HttpMethod.Post, ApiEndpoints.CloudSyncAccountLockSendCodePath,
            new { });

    public async Task<CloudApiResult<CloudAccountStatusData>> ConfirmLockAsync(string code)
    {
        var result = await SendAuthenticatedJsonAsync<CloudAccountStatusData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncAccountLockConfirmPath, new
            {
                code = code.Trim()
            });
        if (result.Ok && result.Data != null)
        {
            UpdateAccountLockState(result.Data);
        }

        return result;
    }

    public Task<CloudApiResult<CloudOtpSentData>> SendUnlockCodeAsync()
        => SendAuthenticatedJsonAsync<CloudOtpSentData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncAccountUnlockSendCodePath, new { });

    public async Task<CloudApiResult<CloudAccountStatusData>> ConfirmUnlockAsync(string code)
    {
        var result = await SendAuthenticatedJsonAsync<CloudAccountStatusData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncAccountUnlockConfirmPath, new
            {
                code = code.Trim()
            });
        if (result.Ok && result.Data != null)
        {
            UpdateAccountLockState(result.Data);
        }

        return result;
    }

    public async Task<CloudApiResult<object>> DeleteAccountAsync(string password)
    {
        var result = await SendAuthenticatedJsonAsync<object>(HttpMethod.Post, ApiEndpoints.CloudSyncAccountDeletePath,
            new
            {
                password,
                confirm = true
            });
        if (result.Ok)
        {
            ClearSessionState(clearEmail: true);
        }

        return result;
    }

    public async Task<CloudApiResult<CloudQuotaData>> GetQuotaAsync()
    {
        var result =
            await SendAuthenticatedJsonAsync<CloudQuotaData>(HttpMethod.Get, ApiEndpoints.CloudSyncDataQuotaPath);
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudActivationResultData>> ActivateCodeAsync(string code)
    {
        var result = await SendAuthenticatedJsonAsync<CloudActivationResultData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncDataActivatePath, new
            {
                code = code.Trim()
            });
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudArchiveListData>> ListArchivesAsync(string? typeFilter = null)
    {
        var path = string.IsNullOrWhiteSpace(typeFilter) ||
                   string.Equals(typeFilter, "all", StringComparison.OrdinalIgnoreCase)
            ? ApiEndpoints.CloudSyncDataListPath
            : ApiEndpoints.GetCloudSyncDataListPath(typeFilter);

        var result = await SendAuthenticatedJsonAsync<CloudArchiveListData>(HttpMethod.Get, path);
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudUploadResultData>> UploadArchiveAsync(string dataType, string slot,
        string jsonContent, bool overwrite = true)
    {
        var normalizedType = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedType is not ("uiaf" or "uigf" or "playtime"))
        {
            return new CloudApiResult<CloudUploadResultData>
            {
                Ok = false,
                HttpStatusCode = 400,
                Code = "INVALID_DATA_TYPE",
                Message = "CloudSync_ErrUnsupportedDataType".GetLocalized()
            };
        }

        var trimmedSlot = (slot ?? string.Empty).Trim();
        if (!IsValidSlotName(trimmedSlot))
        {
            return new CloudApiResult<CloudUploadResultData>
            {
                Ok = false,
                HttpStatusCode = 400,
                Code = "INVALID_DATA_SLOT",
                Message = FormatFriendlyError("INVALID_DATA_SLOT", null, 400)
            };
        }

        if (!await EnsureAuthenticatedAsync())
        {
            return UnauthorizedResult<CloudUploadResultData>();
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var usedLtoken = _ltoken;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.CloudSyncDataUploadPath);
                req.Headers.Add("X-LToken", usedLtoken);
                req.Headers.Add("X-Data-Type", normalizedType);
                req.Headers.Add("X-Data-Slot", trimmedSlot);
                req.Headers.Add("X-Overwrite", overwrite ? "true" : "false");
                req.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

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

                var parsed = ParseEnvelope<CloudUploadResultData>((int)resp.StatusCode, body);
                MarkLockedIf423(parsed.HttpStatusCode, parsed.Code);
                return parsed;
            }
            catch (Exception ex)
            {
                return new CloudApiResult<CloudUploadResultData>
                {
                    Ok = false,
                    HttpStatusCode = 0,
                    Code = "NETWORK_ERROR",
                    Message = ex.Message
                };
            }
        }

        return UnauthorizedResult<CloudUploadResultData>();
    }

    public async Task<CloudPullResult> PullArchiveAsync(string dataType, string slot)
    {
        if (!await EnsureAuthenticatedAsync())
        {
            return new CloudPullResult
            {
                Ok = false,
                HttpStatusCode = 403,
                Code = "FORBIDDEN",
                Message = "CloudSync_ErrAuthRequired".GetLocalized()
            };
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = dataType.Trim().ToLowerInvariant(),
            slot = slot.Trim()
        });

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var usedLtoken = _ltoken;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.CloudSyncDataPullPath);
                req.Headers.Add("X-LToken", usedLtoken);
                req.Content = new StringContent(payload, Encoding.UTF8, "application/json");

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

                if (resp.IsSuccessStatusCode)
                {
                    return new CloudPullResult
                    {
                        Ok = true,
                        HttpStatusCode = (int)resp.StatusCode,
                        Code = "PULL_SUCCESS",
                        DataType = TryGetHeader(resp.Headers, "X-Data-Type") ?? dataType,
                        Slot = TryGetHeader(resp.Headers, "X-Data-Slot") ?? slot,
                        Sha256 = TryGetHeader(resp.Headers, "X-Data-SHA256") ?? string.Empty,
                        CompressedBytes = long.TryParse(TryGetHeader(resp.Headers, "X-Compressed-Bytes"), out var cb)
                            ? cb
                            : 0,
                        RawBytes = long.TryParse(TryGetHeader(resp.Headers, "X-Raw-Bytes"), out var rb)
                            ? rb
                            : Encoding.UTF8.GetByteCount(body),
                        JsonContent = body
                    };
                }

                var errorEnvelope = ParseEnvelope<object>((int)resp.StatusCode, body);
                MarkLockedIf423(errorEnvelope.HttpStatusCode, errorEnvelope.Code);
                return new CloudPullResult
                {
                    Ok = false,
                    HttpStatusCode = errorEnvelope.HttpStatusCode,
                    Code = errorEnvelope.Code,
                    Message = errorEnvelope.Message
                };
            }
            catch (Exception ex)
            {
                return new CloudPullResult
                {
                    Ok = false,
                    HttpStatusCode = 0,
                    Code = "NETWORK_ERROR",
                    Message = ex.Message
                };
            }
        }

        return new CloudPullResult
        {
            Ok = false,
            HttpStatusCode = 403,
            Code = "FORBIDDEN",
            Message = "CloudSync_ErrAuthRequired".GetLocalized()
        };
    }

    public async Task<CloudApiResult<CloudRenameResultData>> RenameArchiveAsync(string dataType, string oldSlot,
        string newSlot, bool overwrite = false)
    {
        var trimmedNew = (newSlot ?? string.Empty).Trim();
        if (!IsValidSlotName(trimmedNew))
        {
            return new CloudApiResult<CloudRenameResultData>
            {
                Ok = false,
                HttpStatusCode = 400,
                Code = "INVALID_DATA_SLOT",
                Message = FormatFriendlyError("INVALID_DATA_SLOT", null, 400)
            };
        }

        var result = await SendAuthenticatedJsonAsync<CloudRenameResultData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncDataRenamePath, new
            {
                type = dataType.Trim().ToLowerInvariant(),
                old_slot = oldSlot.Trim(),
                new_slot = trimmedNew,
                overwrite
            });
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudDeleteResultData>> DeleteArchiveAsync(string dataType, string slot)
    {
        var result = await SendAuthenticatedJsonAsync<CloudDeleteResultData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncDataDeletePath, new
            {
                type = dataType.Trim().ToLowerInvariant(),
                slot = slot.Trim()
            });
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
    }

    public async Task<CloudApiResult<CloudDeleteAllResultData>> DeleteAllDataAsync()
    {
        var result = await SendAuthenticatedJsonAsync<CloudDeleteAllResultData>(HttpMethod.Post,
            ApiEndpoints.CloudSyncDataDeleteAllPath, new
            {
                confirm = true
            });
        MarkLockedIf423(result.HttpStatusCode, result.Code);
        return result;
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
