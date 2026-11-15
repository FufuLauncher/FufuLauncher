/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Passport;
using FufuLauncher.Services.MiHoYo.Passport;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow
{
    #region 国际服登录

    private async void OverseaPasswordButton_Click(object sender, RoutedEventArgs e)
        => await StartOverseaPasswordLoginAsync();

    private async void ThirdPartyLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag }
            && Enum.TryParse<OverseaThirdPartyKind>(tag, out var kind))
        {
            await StartOverseaThirdPartyLoginAsync(kind);
        }
    }

    private async Task StartOverseaPasswordLoginAsync()
    {
        _isLoginCompleting = false;
        _currentSession?.Cancel();
        UpdateStatus("", false, true);

        var dialog = new OverseaPasswordLoginDialog();
        (LoginResult? result, string? error) = await dialog.ShowAndLoginAsync(Content.XamlRoot);
        if (result == null)
        {
            if (!string.IsNullOrEmpty(error))
                UpdateStatus(string.Format("LoginQR_LoginFailedReason_Format".GetLocalized(), error), false);
            return;
        }

        await CompleteOverseaLoginAsync(result);
    }

    private async Task StartOverseaThirdPartyLoginAsync(OverseaThirdPartyKind kind)
    {
        _isLoginCompleting = false;
        _currentSession?.Cancel();
        UpdateStatus("", false, true);

        var oauthWindow = new OverseaOAuthWindow(kind);
        ThirdPartyToken? token = await oauthWindow.ShowAndWaitAsync();
        if (token == null)
        {
            return;
        }

        UpdateStatus("LoginQR_ExchangingLoginCredential".GetLocalized(), true);
        var passportClient = App.GetService<OverseaPassportClient>();
        (string? rawRisk, PassportResponse<LoginResult> response) =
            await passportClient.LoginByThirdPartyAsync(token, verify: null);

        if (!string.IsNullOrEmpty(rawRisk))
        {
            UpdateStatus("", false, true);

            var riskService = App.GetService<OverseaRiskVerificationService>();
            if (await riskService.TryVerifyAsync(token, rawRisk, Content?.XamlRoot))
            {
                (_, response) = await passportClient.LoginByThirdPartyAsync(token, token.Verify);
            }
            else
            {
                UpdateStatus("LoginQR_RiskVerifyIncomplete".GetLocalized(), false);
                return;
            }
        }

        if (!response.IsSuccess || response.Data?.Token?.Token == null || response.Data.UserInfo == null)
        {
            UpdateStatus(string.Format("LoginQR_LoginFailedCode_Format".GetLocalized(), response.RetCode, response.Message), false);
            return;
        }

        await CompleteOverseaLoginAsync(response.Data);
    }

    private async Task CompleteOverseaLoginAsync(LoginResult loginResult)
    {
        string stoken = loginResult.Token!.Token;
        string mid = loginResult.UserInfo!.Mid;
        string aid = loginResult.UserInfo.Aid;

        if (string.IsNullOrEmpty(stoken) || string.IsNullOrEmpty(mid) || string.IsNullOrEmpty(aid))
        {
            UpdateStatus("LoginQR_LoginMissingCredential".GetLocalized(), false);
            return;
        }

        UpdateStatus("LoginQR_FetchingFullCredential".GetLocalized(), true);

        var cookies = new Dictionary<string, string>
        {
            ["stoken"] = stoken,
            ["mid"] = mid,
            ["stuid"] = aid,
            ["ltuid_v2"] = aid,
            ["account_id_v2"] = aid,
        };

        var passportClient = App.GetService<OverseaPassportClient>();

        string ltoken = await passportClient.GetLTokenBySTokenAsync(stoken, mid, aid);
        if (!string.IsNullOrEmpty(ltoken))
        {
            cookies["ltoken_v2"] = ltoken;
        }

        string cookieToken = await passportClient.GetCookieAccountInfoBySTokenAsync(stoken, mid, aid);
        if (!string.IsNullOrEmpty(cookieToken))
        {
            cookies["cookie_token_v2"] = cookieToken;
        }

        OnLoginSuccess(cookies, "os");
    }

    #endregion
}