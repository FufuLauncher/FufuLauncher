/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using FufuLauncher.Views;
using Windows.ApplicationModel.DataTransfer;

namespace FufuLauncher.ViewModels;

public partial class AccountViewModel
{
    [RelayCommand]
    private void NavigateToGacha() => _navigationService.NavigateTo(typeof(GachaViewModel).FullName!);

    private async Task LockAccountAsync() =>
        await OpenSecurityWindowInternalAsync(ApiEndpoints.AccountLockUrl, "Account_OpeningFreezePage".GetLocalized());

    private async Task CopyCookieAsync()
    {
        try
        {
            var activeId = _accountManager.ActiveAccountId;
            if (activeId == null)
            {
                RunOnUIThread(() =>
                {
                    StatusMessage = "Account_NoLoginInfo".GetLocalized();
                    WeakReferenceMessenger.Default.Send(new NotificationMessage("Common_CopyFailed".GetLocalized(),
                        "Account_NoLoginInfo".GetLocalized(),
                        NotificationType.Error));
                });
                return;
            }

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0)
            {
                RunOnUIThread(() =>
                {
                    StatusMessage = "Account_NoValidCookie".GetLocalized();
                    WeakReferenceMessenger.Default.Send(new NotificationMessage("Common_CopyFailed".GetLocalized(),
                        "Account_NoValidCookie".GetLocalized(),
                        NotificationType.Error));
                });
                return;
            }

            string cookieString = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));

            RunOnUIThread(() =>
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(cookieString);
                Clipboard.SetContent(dataPackage);
                StatusMessage = "Account_CookieCopied".GetLocalized();
                WeakReferenceMessenger.Default.Send(new NotificationMessage("Common_CopySuccess".GetLocalized(),
                    "Account_CookieCopySuccessDetail".GetLocalized(),
                    NotificationType.Success));
            });
        }
        catch (Exception ex)
        {
            RunOnUIThread(() =>
            {
                StatusMessage = string.Format("Common_CopyFailed_Format".GetLocalized(), ex.Message);
                WeakReferenceMessenger.Default.Send(new NotificationMessage("Common_CopyFailed".GetLocalized(),
                    ex.Message,
                    NotificationType.Error));
            });
        }
    }

    private async Task RefreshCookieAsync()
    {
        try
        {
            var activeId = _accountManager.ActiveAccountId;
            if (activeId == null)
            {
                RunOnUIThread(() =>
                {
                    StatusMessage = "Account_NoLoginInfo".GetLocalized();
                    WeakReferenceMessenger.Default.Send(new NotificationMessage("Home_RefreshFailed".GetLocalized(),
                        "Home_NoActiveAccount".GetLocalized(), NotificationType.Error));
                });
                return;
            }

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0)
            {
                RunOnUIThread(() =>
                {
                    StatusMessage = "Account_NoValidCookie".GetLocalized();
                    WeakReferenceMessenger.Default.Send(new NotificationMessage("Home_RefreshFailed".GetLocalized(),
                        "Home_CannotLoadCredentials".GetLocalized(), NotificationType.Error));
                });
                return;
            }

            RunOnUIThread(() => StatusMessage = "Account_RefreshingCookie".GetLocalized());

            using var tokenService = new TokenRefreshService();
            var newCookies = await tokenService.RefreshCookieAsync(cookies, true);

            if (newCookies == null || newCookies.Count == 0)
            {
                RunOnUIThread(() => StatusMessage = "Account_CookieRefreshFailed".GetLocalized());
                return;
            }

            bool saved = await _accountManager.TryUpdateCookiesAsync(activeId, cookies, newCookies);
            RunOnUIThread(() => StatusMessage = (saved ? "Account_CookieRefreshSuccess" :
                "Account_CookieRefreshFailed").GetLocalized());
        }
        catch (Exception ex)
        {
            RunOnUIThread(() =>
            {
                StatusMessage = string.Format("Account_CookieRefreshFailed_Format".GetLocalized(), ex.Message);
                WeakReferenceMessenger.Default.Send(new NotificationMessage("Home_RefreshFailed".GetLocalized(),
                    ex.Message, NotificationType.Error));
            });
        }
    }

    private async Task OpenSecurityCenterAsync() =>
        await OpenSecurityWindowInternalAsync(ApiEndpoints.AccountSecurityUrl,
            "Account_OpeningSecurityCenter".GetLocalized());

    private async Task OpenSecurityWindowInternalAsync(string url, string loadingMsg)
    {
        try
        {
            RunOnUIThread(() => StatusMessage = loadingMsg);
            var activeId = _accountManager.ActiveAccountId;
            if (activeId == null) return;

            var cookies = await _accountManager.LoadCookiesAsync(activeId);
            if (cookies == null || cookies.Count == 0) return;

            string cookieString = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));

            RunOnUIThread(() =>
            {
                var window = new SecurityWebWindow(cookieString, url);
                window.Activate();
                StatusMessage = "Common_Opened".GetLocalized();
            });
        }
        catch (Exception ex)
        {
            RunOnUIThread(() =>
                StatusMessage = string.Format("Common_OperationFailed_Format".GetLocalized(), ex.Message));
        }
    }
}
