/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.RegularExpressions;
using FufuLauncher.Helpers;
using FufuLauncher.Models.MiHoYo.Passport;
using FufuLauncher.Services;
using FufuLauncher.Services.MiHoYo.Passport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class MobileCaptchaDialog : ContentDialog, IPassportMobileCaptchaProvider
{
    private static readonly Regex _mobileRegex = new(@"^\d{11}$", RegexOptions.Compiled);

    private readonly PassportClient _passportClient = App.GetService<PassportClient>();
    private readonly GeetestService _geetestService = App.GetService<GeetestService>();
    private readonly DispatcherTimer _countdownTimer = new();
    private int _countdownSeconds;
    private bool _isSending;

    public string? Mobile => MobileTextBox?.Text?.Trim();

    public string? Captcha => CaptchaTextBox?.Text?.Trim();

    public string? ActionType
    {
        get;
        private set;
    }

    public string? Aigis
    {
        get;
        set;
    }

    public MobileCaptchaDialog()
    {
        InitializeComponent();
        _countdownTimer.Interval = TimeSpan.FromSeconds(1);
        _countdownTimer.Tick += CountdownTimer_Tick;
        Closed += (s, e) => _countdownTimer.Stop();
        IsPrimaryButtonEnabled = false;
    }

    private async void SendCaptchaButton_Click(object sender, RoutedEventArgs e)
    {
        string mobile = Mobile ?? string.Empty;
        if (!_mobileRegex.IsMatch(mobile))
        {
            ShowError("LoginQR_InvalidMobileLength".GetLocalized());
            return;
        }

        if (_isSending)
        {
            return;
        }

        _isSending = true;
        HideError();
        try
        {
            (string? rawAigis, PassportResponse<MobileCaptcha> response) =
                await _passportClient.CreateLoginCaptchaAsync(mobile, aigis: null);

            DebugLog.Info("MobileCaptchaDialog",
                $"[MobileCaptcha] Sending captcha: retcode={response.RetCode}, aigis={(rawAigis is null ? "none" : "present")}");

            if (await _geetestService.TryVerifyAigisSessionAsync(this, rawAigis, isOversea: false))
            {
                (_, response) = await _passportClient.CreateLoginCaptchaAsync(mobile, Aigis);
            }

            if (response.IsSuccess && response.Data is not null)
            {
                ActionType = response.Data.ActionType;
                StartCountdown(response.Data.Countdown > 0 ? response.Data.Countdown : 60);
            }
            else
            {
                ShowError(string.Format("LoginQR_SendCaptchaFailed_Format".GetLocalized(), response.RetCode, response.Message));
            }
        }
        catch (Exception ex)
        {
            ShowError(string.Format("LoginQR_SendCaptchaException_Format".GetLocalized(), ex.Message));
        }
        finally
        {
            _isSending = false;
        }
    }

    private void StartCountdown(int seconds)
    {
        _countdownSeconds = seconds;
        SendCaptchaButton.IsEnabled = false;
        SendCaptchaButton.Content = $"{seconds}s";
        _countdownTimer.Start();
    }

    private void CountdownTimer_Tick(object? sender, object e)
    {
        _countdownSeconds--;
        if (_countdownSeconds <= 0)
        {
            _countdownTimer.Stop();
            SendCaptchaButton.IsEnabled = true;
            SendCaptchaButton.Content = "LoginQR_SendCaptcha".GetLocalized();
        }
        else
        {
            SendCaptchaButton.Content = $"{_countdownSeconds}s";
        }
    }

    private void MobileTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePrimaryButtonState();

    private void CaptchaTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdatePrimaryButtonState();

    private void UpdatePrimaryButtonState()
    {
        IsPrimaryButtonEnabled = _mobileRegex.IsMatch(Mobile ?? string.Empty)
                                 && !string.IsNullOrEmpty(Captcha);
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