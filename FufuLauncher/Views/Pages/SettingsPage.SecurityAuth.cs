/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    #region 开发者认证

    private Task ShowDevAuthMessageAsync(string titleResourceKey, string content)
    {
        return ShowSafeDialogAsync(new ContentDialog
        {
            Title = titleResourceKey.GetLocalized(),
            Content = content,
            CloseButtonText = "OkBtn".GetLocalized(),
            XamlRoot = this.XamlRoot
        });
    }

    private async void OnOpenSecurityAuthClick(object sender, RoutedEventArgs e)
    {
        var authService = App.GetService<DeveloperAuthorizationService>();
        string hwid = await Task.Run(SystemEnvironmentHelper.GetHwid);

        if (!DeveloperAuthorizationService.IsValidHwid(hwid))
        {
            await ShowDevAuthMessageAsync("ErrorTitle", "DevAuth_ErrInvalidHwid".GetLocalized());
            return;
        }

        var status = await authService.GetVerificationStatusAsync(hwid);
        if (status.Ok)
        {
            if (status.Authorized || string.Equals(status.Status, "approved", StringComparison.OrdinalIgnoreCase))
            {
                await ShowDevAuthMessageAsync("Settings_Notice", "DevAuth_NoticeAlreadyApproved".GetLocalized());
                return;
            }

            if (string.Equals(status.Status, "pending", StringComparison.OrdinalIgnoreCase))
            {
                await ShowDevAuthMessageAsync("Settings_Notice", "DevAuth_NoticePending".GetLocalized());
                return;
            }

            if (string.Equals(status.Status, "rejected", StringComparison.OrdinalIgnoreCase))
            {
                await ShowDevAuthMessageAsync("ErrorTitle", "DevAuth_ErrRejected".GetLocalized());
                return;
            }
        }

        var uidBox = new TextBox
        {
            PlaceholderText = "DevAuth_UidPlaceholder".GetLocalized(),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var nameBox = new TextBox
        {
            PlaceholderText = "DevAuth_UsernamePlaceholder".GetLocalized(),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var githubBox = new TextBox
        {
            PlaceholderText = "DevAuth_GithubPlaceholder".GetLocalized(),
            Margin = new Thickness(0, 8, 0, 0)
        };
        var hwidBlock = new TextBlock
        {
            Text = $"HWID: {hwid}",
            Opacity = 0.6,
            Margin = new Thickness(0, 12, 0, 0),
            FontSize = 12
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "DevAuth_DialogHint".GetLocalized(),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(uidBox);
        panel.Children.Add(nameBox);
        panel.Children.Add(githubBox);
        panel.Children.Add(hwidBlock);

        var dialog = new ContentDialog
        {
            Title = "DevAuth_DialogTitle".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "Settings_SubmitApp".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            XamlRoot = this.XamlRoot
        };

        var dialogResult = await ShowSafeDialogAsync(dialog);
        if (dialogResult != ContentDialogResult.Primary)
        {
            return;
        }

        string uid = uidBox.Text?.Trim() ?? string.Empty;
        string username = nameBox.Text?.Trim() ?? string.Empty;
        string github = githubBox.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(username))
        {
            await ShowDevAuthMessageAsync("ErrorTitle", "DevAuth_UidRequired".GetLocalized());
            return;
        }

        if (!DeveloperAuthorizationService.IsValidUid(uid))
        {
            await ShowDevAuthMessageAsync("ErrorTitle", "DevAuth_ErrInvalidUid".GetLocalized());
            return;
        }

        if (!DeveloperAuthorizationService.IsValidUsername(username))
        {
            await ShowDevAuthMessageAsync("ErrorTitle", "DevAuth_ErrInvalidUsername".GetLocalized());
            return;
        }

        if (!DeveloperAuthorizationService.IsValidOptionalGithubUrl(github))
        {
            await ShowDevAuthMessageAsync("ErrorTitle", "DevAuth_ErrInvalidGithub".GetLocalized());
            return;
        }

        var applyResult = await authService.StartApplicationAsync(uid, username, hwid, github);
        if (applyResult.RequiresCaptcha && applyResult.Challenge is { } challenge)
        {
            var captchaUri = authService.TryResolveServiceUri(challenge.CaptchaUrl);
            if (captchaUri == null)
            {
                await ShowDevAuthMessageAsync("Failure", "DevAuth_ErrSecurityRejected".GetLocalized());
                return;
            }

            var captchaResult = await CloudCaptchaWindow.ShowExternalChallengeAsync<CloudApiResult<DevApplySubmissionData>>(
                captchaUri,
                ApiEndpoints.DevApplyResultPath,
                async () =>
                {
                    var pollResult = await authService.PollApplicationChallengeAsync(challenge);
                    if (pollResult.IsPending)
                    {
                        return (false, null);
                    }

                    return (true, pollResult);
                });

            if (captchaResult == null)
            {
                await ShowDevAuthMessageAsync("Settings_Notice", "DevAuth_CaptchaCancelled".GetLocalized());
                return;
            }

            if (captchaResult.Ok)
            {
                await ShowDevAuthMessageAsync("Success", "DevAuth_SubmitSuccess".GetLocalized());
                return;
            }

            string captchaTitleKey = string.Equals(captchaResult.Code, "NETWORK_ERROR", StringComparison.Ordinal)
                ? "Settings_NetworkError"
                : "Failure";
            await ShowDevAuthMessageAsync(
                captchaTitleKey,
                authService.FormatFriendlyError(captchaResult.Code, captchaResult.Message, captchaResult.HttpStatusCode));
            return;
        }

        if (!applyResult.Ok)
        {
            string titleKey = string.Equals(applyResult.Code, "NETWORK_ERROR", StringComparison.Ordinal)
                ? "Settings_NetworkError"
                : "Failure";
            await ShowDevAuthMessageAsync(
                titleKey,
                authService.FormatFriendlyError(applyResult.Code, applyResult.Message, applyResult.HttpStatusCode));
            return;
        }

        await ShowDevAuthMessageAsync("Success", "DevAuth_SubmitSuccess".GetLocalized());
    }

    private bool _isSyncingTemporaryDevAuth;

    private void UpdateTemporaryDeveloperAuthUi()
    {
        if (TemporaryDeveloperAuthToggle == null) return;

        var authService = App.GetService<DeveloperAuthorizationService>();
        if (authService != null)
        {
            _isSyncingTemporaryDevAuth = true;
            try
            {
                TemporaryDeveloperAuthToggle.IsOn = authService.IsTemporaryAuthorized;
            }
            finally
            {
                _isSyncingTemporaryDevAuth = false;
            }
        }
    }

    private async void OnTemporaryDeveloperAuthToggled(object sender, RoutedEventArgs e)
    {
        if (_isSyncingTemporaryDevAuth || sender is not ToggleSwitch toggle) return;

        var authService = App.GetService<DeveloperAuthorizationService>();
        if (authService != null)
        {
            authService.IsTemporaryAuthorized = toggle.IsOn;
        }

        await UpdateDeviceInfoRowVisibilityAsync();
        await UpdatePredownloadDevVisibilityAsync();
    }

    #endregion
}