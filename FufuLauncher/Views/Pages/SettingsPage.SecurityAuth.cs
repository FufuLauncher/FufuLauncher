/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    #region 开发者认证

    private async void OnOpenSecurityAuthClick(object sender, RoutedEventArgs e)
    {
        string hwid = await Task.Run(() => Helpers.SystemEnvironmentHelper.GetHwid());

        try
        {
            using var checkClient = new HttpClient();
            var checkPayload = JsonSerializer.Serialize(new { hwid });
            var checkContent = new StringContent(checkPayload, Encoding.UTF8, "application/json");
            var checkResponse = await checkClient.PostAsync(ApiEndpoints.DevVerifyHwidUrl, checkContent);
            if (checkResponse.IsSuccessStatusCode)
            {
                var checkBody = await checkResponse.Content.ReadAsStringAsync();
                var checkResult = JsonSerializer.Deserialize<JsonElement>(checkBody);
                if (checkResult.TryGetProperty("authorized", out var auth) && auth.GetBoolean())
                {
                    await ShowSafeDialogAsync(new ContentDialog
                    {
                        Title = "Settings_Notice".GetLocalized(),
                        Content = "您的开发者认证已通过，请勿重复提交申请",
                        CloseButtonText = "OkBtn".GetLocalized(),
                        XamlRoot = this.XamlRoot
                    });
                    return;
                }
            }
        }
        catch
        {
        }

        var uidBox = new TextBox { PlaceholderText = "游戏 UID", Margin = new Thickness(0, 8, 0, 0) };
        var nameBox = new TextBox { PlaceholderText = "用户名", Margin = new Thickness(0, 8, 0, 0) };
        var githubBox = new TextBox { PlaceholderText = "GitHub 链接（可选）", Margin = new Thickness(0, 8, 0, 0) };
        var hwidBlock = new TextBlock
        {
            Text = $"HWID: {hwid}",
            Opacity = 0.6,
            Margin = new Thickness(0, 12, 0, 0),
            FontSize = 12
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "填写以下信息提交开发者认证申请" });
        panel.Children.Add(uidBox);
        panel.Children.Add(nameBox);
        panel.Children.Add(githubBox);
        panel.Children.Add(hwidBlock);

        var dialog = new ContentDialog
        {
            Title = "开发者认证申请",
            Content = panel,
            PrimaryButtonText = "Settings_SubmitApp".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            XamlRoot = this.XamlRoot
        };

        var result = await ShowSafeDialogAsync(dialog);
        if (result != ContentDialogResult.Primary) return;

        string uid = uidBox.Text?.Trim();
        string username = nameBox.Text?.Trim();
        string github = githubBox.Text?.Trim();

        if (string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(username))
        {
            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "ErrorTitle".GetLocalized(),
                Content = "UID 和用户名不能为空",
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = this.XamlRoot
            });
            return;
        }

        if (uid.Length < 9 || uid.Length > 10 || !uid.All(char.IsDigit))
        {
            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "ErrorTitle".GetLocalized(),
                Content = "UID 必须为9位或10位数字",
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = this.XamlRoot
            });
            return;
        }

        if (!string.IsNullOrEmpty(github) && !github.Contains("github.com", StringComparison.OrdinalIgnoreCase))
        {
            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "ErrorTitle".GetLocalized(),
                Content = "请输入正确的GitHub地址",
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = this.XamlRoot
            });
            return;
        }

        try
        {
            using var client = new HttpClient();
            object payload = string.IsNullOrEmpty(github)
                ? new { uid, username, hwid }
                : new { uid, username, hwid, github };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(ApiEndpoints.DevApplyUrl, content);
            var body = await response.Content.ReadAsStringAsync();

            string msg = response.IsSuccessStatusCode
                ? "申请已提交，请等待管理员审批"
                : $"提交失败: {body}";

            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = response.IsSuccessStatusCode ? "Success".GetLocalized() : "Failure".GetLocalized(),
                Content = msg,
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = this.XamlRoot
            });
        }
        catch (Exception ex)
        {
            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "Settings_NetworkError".GetLocalized(),
                Content = ex.Message,
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = this.XamlRoot
            });
        }
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