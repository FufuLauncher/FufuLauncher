/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.Views;
using MihoyoBBS;

namespace FufuLauncher.ViewModels;

public partial class AccountViewModel
{
    public async Task<bool> LoadUserInfoAsync()
    {
        var myVersion = Interlocked.Increment(ref _loadVersion);
        try
        {
            IsLoadingUserInfo = true;
            IsBoundRoleLoading = true;
            RunOnUIThread(() => StatusMessage = "Account_LoadingUserInfo".GetLocalized());

            var activeId = _accountManager.ActiveAccountId;
            if (activeId == null)
            {
                DebugLog.Info("AccountViewModel.UserInfo", "[LoadUserInfo] No active account");
                RunOnUIThread(() => StatusMessage = "Account_PleaseLogin".GetLocalized());
                return false;
            }

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            var entry = _accountManager.GetActiveAccountEntry();
            if (cookies == null || entry == null)
            {
                DebugLog.Info("AccountViewModel.UserInfo", "[LoadUserInfo] Failed to read account data");
                RunOnUIThread(() => StatusMessage = "Account_PleaseLogin".GetLocalized());
                return false;
            }

            RunOnUIThread(() =>
            {
                UserFullInfo = null;
                if (!TryApplyCachedBoundRoles(activeId)) GameRolesInfo = null;
            });

            string cookieString = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));

            DebugLog.Info("AccountViewModel.UserInfo", $"[LoadUserInfo] Calling remote API... (account: {entry.Id})");

            var rolesTask = _userInfoService.GetUserGameRolesAsync(cookieString);
            var userInfoTask = _userInfoService.GetUserFullInfoAsync(cookieString);
            await Task.WhenAll(rolesTask, userInfoTask);

            var newRolesInfo = await rolesTask;
            var newUserFullInfo = await userInfoTask;

            if (myVersion != _loadVersion || _isDisposed || _accountManager.ActiveAccountId != activeId)
            {
                DebugLog.Info("AccountViewModel.UserInfo",
                    $"[LoadUserInfo][{entry.Id}] Result stale (v{myVersion}→{_loadVersion}), discarded");
                return false;
            }

            GameRolesInfo = newRolesInfo;
            UserFullInfo = newUserFullInfo;

            var userInfo = UserFullInfo?.data?.user_info;
            var hasBoundRole = GameRolesInfo?.data?.list?.Count > 0;
            if (newRolesInfo.retcode == 0 && newRolesInfo.data?.list != null)
                await App.GetService<GameRoleService>().UpdateBindingsAsync(entry.Id, newRolesInfo.data.list);
            if (_isDisposed || myVersion != _loadVersion || _accountManager.ActiveAccountId != entry.Id) return false;
            var role = GameRoleSelection.Current(entry);

            var nickname = userInfo?.nickname ??
                           role?.nickname ?? string.Format("Account_User_Format".GetLocalized(), entry.Stuid);
            var avatarUrl = userInfo?.avatar_url ?? entry.AvatarUrl ?? "ms-appx:///Assets/DefaultAvatar.png";
            var gameUid = role?.game_uid ?? "";
            var isOs = entry.Id.StartsWith("os_");
            var pendantUrl = isOs ? null : userInfo != null && newUserFullInfo.retcode == 0
                ? AccountAvatarPendant.NormalizeUrl(userInfo.pendant)
                : entry.AvatarPendantUrl;
            var server = role?.region_name ??
                         (isOs ? "Account_Server_Global".GetLocalized() : "Account_Server_CN".GetLocalized());
            var level = role?.level.ToString() ?? "";
            var sign = string.IsNullOrEmpty(userInfo?.introduce)
                ? "Account_DefaultSignature".GetLocalized()
                : userInfo.introduce;
            var ipRegion = userInfo?.ip_region ?? "Common_Unknown".GetLocalized();
            var gender = userInfo?.gender ?? 0;

            await _accountManager.UpdateAccountMetaAsync(entry.Id, nickname, avatarUrl, avatarPendantUrl: pendantUrl);
            if (_isDisposed || myVersion != _loadVersion || _accountManager.ActiveAccountId != entry.Id) return false;

            RunOnUIThread(() =>
            {
                if (CurrentAccount == null)
                {
                    CurrentAccount = new AccountInfo { AccountId = entry.Id, Stuid = entry.Stuid };
                }

                CurrentAccount.Nickname = nickname;
                CurrentAccount.AvatarUrl = avatarUrl;
                CurrentAccount.AvatarPendantUrl = IsAvatarPendantEnabled ? pendantUrl ?? "" : "";
                CurrentAccount.GameUid = gameUid;
                CurrentAccount.Server = server;
                CurrentAccount.Level = level;
                CurrentAccount.Sign = sign;
                CurrentAccount.IpRegion = ipRegion;
                CurrentAccount.Gender = gender;
                CurrentAccount.HasBoundRole = hasBoundRole;

                StatusMessage = hasBoundRole
                    ? "Account_LoggedIn".GetLocalized()
                    : "Account_LoggedInNoRole".GetLocalized();
            });
            WeakReferenceMessenger.Default.Send(new AccountAvatarChangedMessage());
            DebugLog.Info("AccountViewModel.UserInfo",
                $"[LoadUserInfo] User info updated, HasBoundRole={hasBoundRole}");
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountViewModel.UserInfo", $"[LoadUserInfo] Exception: {ex.Message}");
            RunOnUIThread(() =>
            {
                StatusMessage = string.Format("Common_LoadFailed_Format".GetLocalized(), ex.Message);
                var activeId = _accountManager.ActiveAccountId;
                if (activeId == null || !TryApplyCachedBoundRoles(activeId)) GameRolesInfo = null;
                UserFullInfo = null;
            });
            return false;
        }
        finally
        {
            IsLoadingUserInfo = false;
            IsBoundRoleLoading = false;
            DebugLog.Info("AccountViewModel.UserInfo", "[LoadUserInfo] Load finished");
        }
    }

    partial void OnUserFullInfoChanged(UserFullInfoResponse? value)
    {
        OnPropertyChanged(nameof(HasCommunityData));
        OnPropertyChanged(nameof(CommunityLikeCount));
        OnPropertyChanged(nameof(CommunityPostCount));
        OnPropertyChanged(nameof(CommunityReplyCount));
    }

    partial void OnGameRolesInfoChanged(GameRolesResponse? value)
    {
        OnPropertyChanged(nameof(BoundRoles));
        NotifyBoundRoleState();
    }

    partial void OnIsBoundRoleLoadingChanged(bool value) => NotifyBoundRoleState();

    private bool TryApplyCachedBoundRoles(string accountId)
    {
        var cached = _accountManager.GetSessionGameRoles(accountId);
        if (cached is not { Count: > 0 }) return false;
        GameRolesInfo = new GameRolesResponse(0, string.Empty, new GameRolesData(cached.ToList()));
        if (CurrentAccount != null) CurrentAccount.HasBoundRole = true;
        return true;
    }

    private void NotifyBoundRoleState()
    {
        OnPropertyChanged(nameof(HasBoundRoles));
        OnPropertyChanged(nameof(ShowBoundRoleLoadingHint));
        OnPropertyChanged(nameof(ShowBoundRoleEmptyState));
    }

    private async Task OpenGenshinDataAsync()
    {
        try
        {
            RunOnUIThread(() =>
            {
                StatusMessage = "Account_OpeningGenshinData".GetLocalized();
                var window = App.GetService<GenshinDataWindow>();
                window.Activate();
                StatusMessage = "Common_Opened".GetLocalized();
            });
        }
        catch (Exception ex)
        {
            DebugLog.Info("AccountViewModel.UserInfo", $"ERROR: Failed to open Genshin data window: {ex.Message}");
            RunOnUIThread(() => StatusMessage = string.Format("Common_OpenFailed_Format".GetLocalized(), ex.Message));
        }
    }
}
