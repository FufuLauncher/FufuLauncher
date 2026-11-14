using FufuLauncher.Models;

namespace FufuLauncher.Contracts.Services;

public interface IHutaoCloudService
{
    event EventHandler? SessionChanged;

    bool IsLoggedIn
    {
        get;
    }

    string CurrentEmail
    {
        get;
    }

    HutaoUserInfo? CurrentUserInfo
    {
        get;
    }

    DateTimeOffset? VerifyCodeCooldownUntil
    {
        get;
    }

    string CurrentDeviceId
    {
        get;
    }

    Task InitializeAsync();
    Task<bool> EnsureAuthenticatedAsync();

    Task<HutaoOperationResult<object>> SendVerifyCodeAsync(string email, bool isResetPassword);
    Task<HutaoOperationResult<HutaoUserInfo>> LoginAsync(string email, string password);
    Task<HutaoOperationResult<HutaoUserInfo>> RegisterAsync(string email, string password, string verifyCode);
    Task<HutaoOperationResult<HutaoUserInfo>> ResetPasswordAsync(string email, string newPassword, string verifyCode);
    Task<HutaoOperationResult<HutaoUserInfo>> RefreshUserInfoAsync();
    Task<HutaoOperationResult<List<HutaoLoggedInDevice>>> GetLoggedInDevicesAsync();
    Task<HutaoOperationResult<object>> RevokeDeviceAsync(string deviceId);
    Task<HutaoOperationResult<object>> RevokeAllDevicesAsync();
    Task<HutaoOperationResult<string>> RedeemCodeAsync(string code);
    Task LogoutAsync(bool revokeRemote = true);

    Task<HutaoOperationResult<List<HutaoGachaEntry>>> GetGachaEntriesAsync();
    Task<HutaoOperationResult<int>> DeleteGachaUidAsync(string uid);

    Task<HutaoOperationResult<HutaoBridgeTransferSummary>> PullGachaToFufuCloudAsync(
        IReadOnlyList<string> uids,
        string targetSlot,
        bool mergeExistingFufuArchive,
        bool overwriteExisting = true,
        CancellationToken cancellationToken = default);

    Task<HutaoOperationResult<List<FufuCloudUigfUidItem>>> InspectFufuCloudUigfAsync(string slot);

    Task<HutaoOperationResult<HutaoBridgeTransferSummary>> PushFufuCloudGachaToHutaoAsync(
        string slot,
        IReadOnlyList<string> selectedUids,
        CancellationToken cancellationToken = default);

    Task<HutaoOperationResult<List<FufuCloudChallengeItem>>> InspectFufuCloudChallengeAsync(string slot);

    Task<HutaoOperationResult<HutaoChallengeUploadSummary>> PushFufuCloudChallengeToHutaoAsync(
        string slot,
        IReadOnlyList<FufuCloudChallengeItem> selectedItems,
        CancellationToken cancellationToken = default);

    Task<HutaoOperationResult<HutaoStatisticsBundle>> GetHutaoStatisticsBundleAsync(
        bool lastPeriod,
        IReadOnlyList<string>? uids,
        CancellationToken cancellationToken = default);
}
