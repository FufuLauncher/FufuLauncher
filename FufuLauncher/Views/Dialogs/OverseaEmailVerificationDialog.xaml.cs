/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Passport;
using FufuLauncher.Services;
using FufuLauncher.Services.MiHoYo.Passport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class OverseaEmailVerificationDialog : ContentDialog, IAigisProvider
{
    private const int MaxAttempts = 3;

    private readonly OverseaPassportClient _passportClient = App.GetService<OverseaPassportClient>();
    private readonly GeetestService _geetestService = App.GetService<GeetestService>();
    private readonly DispatcherTimer _countdownTimer = new();
    private int _countdownSeconds;
    private string? _ticket;

    public string? Aigis
    {
        get;
        set;
    }

    public OverseaEmailVerificationDialog()
    {
        InitializeComponent();
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += CountdownTimer_Tick;
        Closed += (s, e) => _countdownTimer.Stop();
    }

    public async Task<bool> TryValidateAsync(string ticket, CancellationToken token = default)
    {
        _ticket = ticket;

        PassportResponse<ActionTicketInfo> info = await _passportClient.GetActionTicketInfoAsync(ticket, token);
        if (!info.IsSuccess || info.Data is null)
        {
            return false;
        }

        EmailTextBlock.Text = string.IsNullOrEmpty(info.Data.UserInfo.Email)
            ? "LoginQR_UnknownEmail".GetLocalized()
            : info.Data.UserInfo.Email;
        if (info.Data.CaptchaSent)
        {
            InfoTextBlock.Text = "LoginQR_EmailCaptchaSent".GetLocalized();
            InfoTextBlock.Visibility = Visibility.Visible;
        }

        IsPrimaryButtonEnabled = false;

        for (int attempt = 0; attempt < MaxAttempts; attempt++)
        {
            if (await ShowAsync() != ContentDialogResult.Primary)
            {
                return false;
            }

            string captcha = CaptchaTextBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(captcha))
            {
                return false;
            }

            PassportResponse verify = await _passportClient.VerifyActionTicketPartlyAsync(ticket, captcha, token);
            if (!verify.IsSuccess)
            {
                ShowError($"[{verify.RetCode}] {verify.Message}");
                continue;
            }

            PassportResponse<ActionTicketInfo>
                finalInfo = await _passportClient.GetActionTicketInfoAsync(ticket, token);
            if (finalInfo.IsSuccess && finalInfo.Data is not null)
            {
                return finalInfo.Data.VerifyInfo.Status == VerifyStatus.StatusVerified;
            }

            ShowError($"[{finalInfo.RetCode}] {finalInfo.Message}");
        }

        return false;
    }

    private async void SendEmailCaptchaButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_ticket))
        {
            return;
        }

        SendEmailCaptchaButton.IsEnabled = false;
        HideError();
        try
        {
            const int MaxAttempts = 3;
            bool aigisWasUsed = false;
            PassportResponse response = new() { Message = "Checkin_RequestSignStatusException".GetLocalized() };

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                (string? rawAigis, PassportResponse current) =
                    await _passportClient.CreateEmailCaptchaByActionTicketAsync(_ticket, Aigis);
                response = current;

                DebugLog.Info("OverseaEmailVerificationDialog",
                    $"[OverseaEmailVerification] Sending email captcha, attempt {attempt + 1}: retcode={current.RetCode}, aigis={(rawAigis is null ? "none" : "present")}");

                if (current.IsSuccess)
                {
                    InfoTextBlock.Text = "LoginQR_EmailCaptchaSent".GetLocalized();
                    InfoTextBlock.Visibility = Visibility.Visible;
                    StartCountdown(60);
                    return;
                }

                if (rawAigis is not null)
                {
                    if (await _geetestService.TryVerifyAigisSessionAsync(this, rawAigis, isOversea: true))
                    {
                        aigisWasUsed = true;
                        continue;
                    }

                    ShowError("LoginQR_CaptchaNotCompleted".GetLocalized());
                    return;
                }

                if (aigisWasUsed)
                {
                    DebugLog.Info("OverseaEmailVerificationDialog",
                        $"[OverseaEmailVerification] aigis rejected (retcode={current.RetCode}), cleared and retrying");
                    aigisWasUsed = false;
                    Aigis = null;
                    continue;
                }

                break;
            }

            ShowError($"[{response.RetCode}] {response.Message}");
        }
        catch (Exception ex)
        {
            ShowError(string.Format("LoginQR_SendCaptchaException_Format".GetLocalized(), ex.Message));
        }
        finally
        {
            if (!_countdownTimer.IsEnabled)
            {
                SendEmailCaptchaButton.IsEnabled = true;
            }
        }
    }

    private void StartCountdown(int seconds)
    {
        _countdownSeconds = seconds;
        SendEmailCaptchaButton.IsEnabled = false;
        SendEmailCaptchaButton.Content = $"{seconds}s";
        _countdownTimer.Start();
    }

    private void CountdownTimer_Tick(object? sender, object e)
    {
        _countdownSeconds--;
        if (_countdownSeconds <= 0)
        {
            _countdownTimer.Stop();
            SendEmailCaptchaButton.IsEnabled = true;
            SendEmailCaptchaButton.Content = "LoginQR_SendEmailCaptcha".GetLocalized();
        }
        else
        {
            SendEmailCaptchaButton.Content = $"{_countdownSeconds}s";
        }
    }

    private void CaptchaTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = !string.IsNullOrEmpty(CaptchaTextBox?.Text?.Trim());
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorTextBlock.Text = string.Empty;
        ErrorTextBlock.Visibility = Visibility.Collapsed;
    }
}