/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Models;

namespace FufuLauncher.Contracts.Services;

public interface ICloudSyncService
{
    event EventHandler? SessionChanged;

    bool IsLoggedIn { get; }
    bool HasLtoken { get; }
    string CurrentEmail { get; }
    string AccountStatus { get; }
    bool IsLocked { get; }
    string? LockedAt { get; }
    string? LastStartupMessage { get; }

    Task InitializeOnStartupAsync();
    Task<bool> EnsureAuthenticatedAsync();

    Uri ResolveServiceUri(string relativeOrAbsoluteUrl);
    bool IsValidSlotName(string? slot);
    string SuggestValidSlot(string? preferred, string fallbackPrefix = "archive");
    string FormatFriendlyError(string? code, string? message, int statusCode = 0);

    Task<CloudApiResult<CloudChallengeInfo>> StartLoginAsync(string email, string password);
    Task<CloudApiResult<CloudChallengeInfo>> StartRegisterSendCodeAsync(string email);
    Task<CloudApiResult<CloudLoginSessionData>> ConfirmRegisterAsync(string email, string password, string code);
    Task<CloudApiResult<CloudChallengeInfo>> StartResetPasswordSendCodeAsync(string email);
    Task<CloudApiResult<object>> ConfirmResetPasswordAsync(string email, string code, string newPassword);

    Task<CloudApiResult<T>> PollChallengeAsync<T>(string pollUrl, string challengeSecret);
    void ApplyLoginSession(string email, CloudLoginSessionData loginData);

    Task<CloudApiResult<CloudLtokenIssueData>> RotateLtokenAsync();
    Task<CloudApiResult<object>> LogoutAsync(bool revokeStoken = false);
    Task ClearLocalSessionAsync();

    Task<CloudApiResult<CloudAccountStatusData>> GetAccountStatusAsync();
    Task<CloudApiResult<CloudOtpSentData>> SendLockCodeAsync();
    Task<CloudApiResult<CloudAccountStatusData>> ConfirmLockAsync(string code);
    Task<CloudApiResult<CloudOtpSentData>> SendUnlockCodeAsync();
    Task<CloudApiResult<CloudAccountStatusData>> ConfirmUnlockAsync(string code);
    Task<CloudApiResult<object>> DeleteAccountAsync(string password);

    Task<CloudApiResult<CloudQuotaData>> GetQuotaAsync();
    Task<CloudApiResult<CloudActivationResultData>> ActivateCodeAsync(string code);

    Task<CloudApiResult<CloudArchiveListData>> ListArchivesAsync(string? typeFilter = null);
    Task<CloudApiResult<CloudUploadResultData>> UploadArchiveAsync(string dataType, string slot, string jsonContent, bool overwrite = true);
    Task<CloudPullResult> PullArchiveAsync(string dataType, string slot);
    Task<CloudApiResult<CloudRenameResultData>> RenameArchiveAsync(string dataType, string oldSlot, string newSlot, bool overwrite = false);
    Task<CloudApiResult<CloudDeleteResultData>> DeleteArchiveAsync(string dataType, string slot);
    Task<CloudApiResult<CloudDeleteAllResultData>> DeleteAllDataAsync();
}
