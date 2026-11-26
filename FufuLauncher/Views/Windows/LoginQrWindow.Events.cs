/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class LoginQrWindow
{
    #region UI事件处理

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (LoginMethodComboBox.SelectedIndex == 2)
        {
            await StartMobileCaptchaLoginAsync();
            return;
        }


        _currentSession?.Cancel();
        bool isGameLogin = GameLoginPanel != null && GameLoginPanel.Visibility == Visibility.Visible;
        var newSession = new LoginSession
        {
            Type = isGameLogin ? LoginType.GameQr : LoginType.AppQr,
            GameAppId = _gameAppId,
            GameDevice = Guid.NewGuid().ToString("N")
        };
        _currentSession = newSession;

        if (isGameLogin)
            await StartGameLoginFlowAsync(newSession);
        else
            await StartAppLoginFlowAsync(newSession);
    }

    private async void ManualCookieButton_Click(object sender, RoutedEventArgs e)
    {
        TextBox inputTextBox = new()
        {
            AcceptsReturn = true,
            Height = 150,
            TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "LoginQR_PasteCookiePlaceholder".GetLocalized()
        };

        TextBlock errorTextBlock = new()
        {
            Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Red),
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        StackPanel dialogContent = new();
        dialogContent.Children.Add(inputTextBox);
        dialogContent.Children.Add(errorTextBlock);

        ContentDialog dialog = new()
        {
            Title = "LoginQR_ManualCookie".GetLocalized(),
            Content = dialogContent,
            PrimaryButtonText = "Common_Save".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            XamlRoot = this.Content?.XamlRoot
        };

        dialog.PrimaryButtonClick += async (s, args) =>
        {
            string cookieStr = inputTextBox.Text.Trim();

            if (string.IsNullOrEmpty(cookieStr) || !cookieStr.Contains("="))
            {
                args.Cancel = true;
                errorTextBlock.Text = "LoginQR_InvalidCookie".GetLocalized();
                errorTextBlock.Visibility = Visibility.Visible;
                return;
            }

            ContentDialogButtonClickDeferral deferral = args.GetDeferral();
            try
            {
                var cookies = ParseCookieString(cookieStr);
                if (cookies.Count == 0)
                {
                    args.Cancel = true;
                    errorTextBlock.Text = "LoginQR_InvalidCookieFormat".GetLocalized();
                    errorTextBlock.Visibility = Visibility.Visible;
                    return;
                }

                string serverType = ServerComboBox.SelectedIndex == 1 ? "os" : "cn";
                OnLoginSuccess(cookies, serverType);
            }
            catch (Exception ex)
            {
                args.Cancel = true;
                errorTextBlock.Text = string.Format("Common_SaveFailed_Format".GetLocalized(), ex.Message);
                errorTextBlock.Visibility = Visibility.Visible;
            }
            finally
            {
                deferral.Complete();
            }
        };

        errorTextBlock.Visibility = Visibility.Collapsed;
        inputTextBox.Text = string.Empty;

        await dialog.ShowAsync();
    }

    private async void LoginMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentSession?.Cancel();

        if (GameLoginPanel != null)
            GameLoginPanel.Visibility = Visibility.Collapsed;
        if (PassportWebViewBorder != null)
        {
            PassportWebViewBorder.Visibility = Visibility.Collapsed;
            PassportWebViewBorder.MinWidth = 420;
            PassportWebViewBorder.MinHeight = 480;
        }

        if (QrCodeContainer != null)
            QrCodeContainer.Visibility = Visibility.Visible;
        if (WebLoginWarningTextBlock != null)
            WebLoginWarningTextBlock.Visibility = Visibility.Collapsed;

        if (LoginMethodComboBox.SelectedIndex == 1)
        {
            if (QrCodeContainer != null)
                QrCodeContainer.Visibility = Visibility.Collapsed;
            if (PassportWebViewBorder != null)
                PassportWebViewBorder.Visibility = Visibility.Visible;
            if (WebLoginWarningTextBlock != null)
                WebLoginWarningTextBlock.Visibility = Visibility.Visible;
            await StartWebPassportLoginAsync();
            return;
        }

        if (LoginMethodComboBox.SelectedIndex == 2)
        {
            if (QrCodeContainer != null)
                QrCodeContainer.Visibility = Visibility.Collapsed;
            await StartMobileCaptchaLoginAsync();
            return;
        }


        var session = new LoginSession
        {
            Type = LoginType.AppQr,
            GameAppId = _gameAppId,
            GameDevice = Guid.NewGuid().ToString("N")
        };
        _currentSession = session;
        await StartAppLoginFlowAsync(session);
    }

    private async void ServerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CnLoginPanel == null || OsLoginPanel == null)
            return;

        _currentSession?.Cancel();
        bool isCn = ServerComboBox.SelectedIndex == 0;

        if (GameLoginPanel != null)
        {
            GameLoginPanel.Visibility = Visibility.Collapsed;
            if (LoginMethodComboBox != null)
                LoginMethodComboBox.Visibility = Visibility.Visible;
        }

        CnLoginPanel.Visibility = isCn ? Visibility.Visible : Visibility.Collapsed;
        OsLoginPanel.Visibility = isCn ? Visibility.Collapsed : Visibility.Visible;

        UpdateStatus("", false, true);

        if (PassportWebView != null && PassportWebView.CoreWebView2 != null)
        {
            PassportWebView.CoreWebView2.WebResourceResponseReceived -= CoreWebView2_WebResourceResponseReceived;
            PassportWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
            PassportWebView.CoreWebView2.Stop();
        }

        if (isCn)
        {
            if (LoginMethodComboBox.SelectedIndex == 0)
            {
                var session = new LoginSession
                {
                    Type = LoginType.AppQr,
                    GameAppId = _gameAppId,
                    GameDevice = Guid.NewGuid().ToString("N")
                };
                _currentSession = session;
                await StartAppLoginFlowAsync(session);
            }
            else
            {
                LoginMethodComboBox.SelectedIndex = 0;
            }
        }
    }

    private async void GameSelectionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LoginMethodComboBox != null && LoginMethodComboBox.SelectedIndex == 1)
        {
            UpdateGameAppIdFromSelection();
            await RestartLoginFlowAsync();
        }
    }

    private void RootGrid_PreviewKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (CnLoginPanel == null || CnLoginPanel.Visibility != Visibility.Visible)
            return;

        bool isGameLoginVisible = GameLoginPanel != null && GameLoginPanel.Visibility == Visibility.Visible;

        if (e.Key == Windows.System.VirtualKey.Tab)
        {
            e.Handled = true;
            if (isGameLoginVisible)
            {
                ExitGameLoginMode();
            }
            else
            {
                EnterGameLoginMode();
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            if (isGameLoginVisible)
            {
                e.Handled = true;
                CancelGameLoginPolling();
            }
        }
    }

    private async void ClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "Common_Warning".GetLocalized(),
            Content = "LoginQR_ConfirmClearHistory".GetLocalized(),
            PrimaryButtonText = "OkBtn".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            XamlRoot = Content?.XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            try
            {
                UpdateStatus("LoginQR_ClearingDbCache".GetLocalized(), true);

                var localSettingsService = App.GetService<ILocalSettingsService>();
                await localSettingsService.RemoveSettingAsync("AccountConfig");
                await localSettingsService.RemoveSettingAsync("LabAccountConfig");

                UpdateStatus("LoginQR_ClearDone".GetLocalized(), false, false);
                await Task.Delay(1000);
                UpdateStatus("", false, true);
            }
            catch (Exception ex)
            {
                UpdateStatus(string.Format("LoginQR_ClearFailed_Format".GetLocalized(), ex.Message), false, false);
                await Task.Delay(2000);
                UpdateStatus("", false, true);
            }
        }
    }

    private void ExitGameLoginMode()
    {
        if (GameLoginPanel != null)
            GameLoginPanel.Visibility = Visibility.Collapsed;
        if (LoginMethodComboBox != null)
            LoginMethodComboBox.Visibility = Visibility.Visible;

        LoginMethodComboBox_SelectionChanged(LoginMethodComboBox, null);
    }

    private void CancelGameLoginPolling()
    {
        _currentSession?.Cancel();
        UpdateStatus("LoginQR_ForceStoppedScan".GetLocalized(), false, false);
    }

    private async void EnterGameLoginMode()
    {
        _currentSession?.Cancel();

        if (LoginMethodComboBox != null)
            LoginMethodComboBox.Visibility = Visibility.Collapsed;
        if (GameLoginPanel != null)
            GameLoginPanel.Visibility = Visibility.Visible;
        if (PassportWebViewBorder != null)
            PassportWebViewBorder.Visibility = Visibility.Collapsed;
        if (WebLoginWarningTextBlock != null)
            WebLoginWarningTextBlock.Visibility = Visibility.Collapsed;
        if (QrCodeContainer != null)
            QrCodeContainer.Visibility = Visibility.Visible;

        UpdateGameAppIdFromSelection();

        var session = new LoginSession
        {
            Type = LoginType.GameQr,
            GameAppId = _gameAppId,
            GameDevice = Guid.NewGuid().ToString("N")
        };
        _currentSession = session;
        await StartGameLoginFlowAsync(session);
    }

    private async void GameLoginButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateGameAppIdFromSelection();
        await RestartLoginFlowAsync(true);
    }

    private void UpdateGameAppIdFromSelection()
    {
        if (GameAppIdTextBox != null && !string.IsNullOrWhiteSpace(GameAppIdTextBox.Text))
        {
            _gameAppId = GameAppIdTextBox.Text.Trim();
        }
    }

    #endregion
}
