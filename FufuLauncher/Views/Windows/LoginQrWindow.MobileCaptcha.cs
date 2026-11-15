/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Passport;
using FufuLauncher.Services.MiHoYo.Passport;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow
{
    #region 手机验证码登录

    private async Task StartMobileCaptchaLoginAsync()
    {
        _isLoginCompleting = false;
        _currentSession?.Cancel();
        UpdateStatus("", false, true);

        var dialog = new MobileCaptchaDialog { XamlRoot = Content?.XamlRoot };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        if (string.IsNullOrEmpty(dialog.ActionType) || string.IsNullOrEmpty(dialog.Mobile) ||
            string.IsNullOrEmpty(dialog.Captcha))
        {
            UpdateStatus("LoginQR_GetSmsCodeFirst".GetLocalized(), false);
            return;
        }

        UpdateStatus("LoginQR_LoggingIn".GetLocalized(), true);
        var passportClient = App.GetService<PassportClient>();
        PassportResponse<LoginResult> response =
            await passportClient.LoginByMobileCaptchaAsync(dialog.ActionType, dialog.Mobile, dialog.Captcha,
                dialog.Aigis);

        if (!response.IsSuccess || response.Data?.Token?.Token == null || response.Data.UserInfo == null)
        {
            UpdateStatus(string.Format("LoginQR_LoginFailedCode_Format".GetLocalized(), response.RetCode, response.Message), false);
            return;
        }

        string stoken = response.Data.Token.Token;
        string mid = response.Data.UserInfo.Mid;
        string aid = response.Data.UserInfo.Aid;

        if (string.IsNullOrEmpty(stoken) || string.IsNullOrEmpty(mid))
        {
            UpdateStatus("LoginQR_LoginMissingCredential".GetLocalized(), false);
            return;
        }

        var cookies = await ExchangeV2TokensAsync(stoken, mid, aid);
        if (cookies != null)
        {
            OnLoginSuccess(cookies, "cn");
        }
    }

    #endregion
}