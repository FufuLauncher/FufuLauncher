/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Models.MiHoYo.Passport;
using FufuLauncher.Services;
using FufuLauncher.Services.MiHoYo.Passport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.Helpers;

namespace FufuLauncher.Views;

public sealed partial class OverseaPasswordLoginDialog : ContentDialog, IPassportPasswordProvider
{
    private const int MaxAttempts = 3;

    private readonly OverseaPassportClient _passportClient = App.GetService<OverseaPassportClient>();
    private readonly GeetestService _geetestService = App.GetService<GeetestService>();

    private readonly OverseaRiskVerificationService _riskVerificationService =
        App.GetService<OverseaRiskVerificationService>();

    public string? Account => AccountTextBox?.Text?.Trim();

    public string? Password => PasswordBox?.Password;

    public string? Aigis
    {
        get;
        set;
    }

    public string? Verify
    {
        get;
        set;
    }

    public OverseaPasswordLoginDialog()
    {
        InitializeComponent();
        IsPrimaryButtonEnabled = false;
    }

    public async Task<(LoginResult? Result, string? Error)> ShowAndLoginAsync(XamlRoot xamlRoot)
    {
        XamlRoot = xamlRoot;
        if (await ShowAsync() != ContentDialogResult.Primary)
        {
            return (null, null);
        }

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            (string? rawAigis, string? rawRisk, PassportResponse<LoginResult> response) =
                await _passportClient.LoginByPasswordAsync(Account!, Password!, Aigis, Verify);

            DebugLog.Info("OverseaPasswordLoginDialog",
                $"[OverseaPasswordLogin] Attempt {attempt + 1}: retcode={response.RetCode}, aigis={(rawAigis is null ? "none" : "present")}, risk={(rawRisk is null ? "none" : "present")}");

            if (await _geetestService.TryVerifyAigisSessionAsync(this, rawAigis, isOversea: true))
            {
                DebugLog.Info("OverseaPasswordLoginDialog",
                    $"[OverseaPasswordLogin] Geetest completed, retrying with aigis");
                continue;
            }

            if (!string.IsNullOrEmpty(rawRisk))
            {
                if (await _riskVerificationService.TryVerifyAsync(this, rawRisk, xamlRoot))
                {
                    continue;
                }

                return (null, "LoginQR_RiskVerifyIncomplete".GetLocalized());
            }

            if (response.IsSuccess && response.Data is not null)
            {
                return (response.Data, null);
            }

            if (!string.IsNullOrEmpty(Aigis))
            {
                DebugLog.Info("OverseaPasswordLoginDialog",
                    $"[OverseaPasswordLogin] aigis rejected (retcode={response.RetCode}), cleared and re-verifying");
                Aigis = null;
                continue;
            }

            return (null, $"[{response.RetCode}] {response.Message}");
        }

        return (null, "LoginQR_TooManyVerifyAttempts".GetLocalized());
    }

    private void AccountTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePrimaryButtonState();

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e) => UpdatePrimaryButtonState();

    private void UpdatePrimaryButtonState()
    {
        IsPrimaryButtonEnabled = !string.IsNullOrEmpty(Account) && !string.IsNullOrEmpty(Password);
    }
}