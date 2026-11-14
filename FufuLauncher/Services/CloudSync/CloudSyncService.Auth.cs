/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed partial class CloudSyncService
{
    public Task<CloudApiResult<CloudChallengeInfo>> StartLoginAsync(string email, string password)
        => SendAnonymousJsonAsync<CloudChallengeInfo>(HttpMethod.Post, ApiEndpoints.CloudSyncAuthLoginPath, new
        {
            email = email.Trim(),
            password
        }, retryOnTransient: true);

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
        var raw = await SendWithTransientRetryAsync(() =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, ResolveServiceUri(pollUrl));
            req.Headers.Add("X-Challenge-Secret", challengeSecret);
            return req;
        }, retryOnTransientFailure: true);

        return raw.IsNetworkError
            ? NetworkErrorResult<T>(raw.NetworkErrorMessage)
            : ParseEnvelope<T>(raw.StatusCode, raw.Body, raw.RetryAfter);
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

            var raw = await SendWithTransientRetryAsync(() =>
            {
                var req = new HttpRequestMessage(HttpMethod.Post, ApiEndpoints.CloudSyncAuthLtokenPath);
                req.Headers.Add("X-SToken", _stoken);
                req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
                return req;
            }, retryOnTransientFailure: true);

            if (raw.IsNetworkError)
            {
                return NetworkErrorResult<CloudLtokenIssueData>(raw.NetworkErrorMessage);
            }

            var parsed = ParseEnvelope<CloudLtokenIssueData>(raw.StatusCode, raw.Body, raw.RetryAfter);

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
            else if (raw.StatusCode == 403)
            {
                ClearSessionState(clearEmail: false);
            }

            return parsed;
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
}
