/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using File = System.IO.File;

namespace FufuLauncher.Views;

public sealed partial class GameSettingsPage
{
    #region 服务器切换

    private async void SwitchServer_Click(object sender, RoutedEventArgs e)
    {
        if (_currentConfig == null || string.IsNullOrEmpty(_currentConfig.GamePath))
        {
            await ShowError("Err_GamePathNotFound".GetLocalized());
            return;
        }

        string gameDir = _currentConfig.GamePath;
        gameDir = gameDir.Trim().Trim('"');
        if (File.Exists(gameDir))
        {
            gameDir = Path.GetDirectoryName(gameDir) ?? gameDir;
        }

        string configPath = Path.Combine(gameDir, "config.ini");

        if (!File.Exists(configPath))
        {
            await ShowError(string.Format("Err_ConfigIniNotFound_Format".GetLocalized(), configPath));
            return;
        }

        bool isGlobalExe = File.Exists(Path.Combine(gameDir, "GenshinImpact.exe"));

        var stackPanel = new StackPanel { Spacing = 10 };

        var dialog = new ContentDialog
        {
            Title = "SwitchServerTitle".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            XamlRoot = XamlRoot
        };

        if (isGlobalExe)
        {
            stackPanel.Children.Add(new TextBlock
                { Text = "Msg_GlobalClientNoSwitchToBili".GetLocalized(), TextWrapping = TextWrapping.Wrap });
            dialog.PrimaryButtonText = "Btn_SwitchToOfficialServer".GetLocalized();
        }
        else
        {
            stackPanel.Children.Add(new TextBlock
                { Text = "Label_ChooseTargetServer".GetLocalized(), TextWrapping = TextWrapping.Wrap });
            dialog.PrimaryButtonText = "Btn_SwitchToBiliServer".GetLocalized();
            dialog.SecondaryButtonText = "Btn_SwitchToOfficialServer".GetLocalized();
        }

        var advancedBtn = new Button
        {
            Content = "Btn_ConvertBetweenGlobalAndCN".GetLocalized(),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        advancedBtn.Click += (s, args) =>
        {
            dialog.Hide();
            OpenAdvancedServerSwitchWindow(gameDir);
        };
        stackPanel.Children.Add(advancedBtn);
        dialog.Content = stackPanel;

        var result = await dialog.ShowAsync();

        if (isGlobalExe)
        {
            if (result == ContentDialogResult.Primary)
            {
                OpenAdvancedServerSwitchWindow(gameDir, "CN");
            }
        }
        else
        {
            if (result == ContentDialogResult.Primary)
            {
                OpenAdvancedServerSwitchWindow(gameDir, "Bili");
            }
            else if (result == ContentDialogResult.Secondary)
            {
                OpenAdvancedServerSwitchWindow(gameDir, "CN");
            }
        }
    }

    public class SwitchPageParams
    {
        public string GameDir
        {
            get;
            set;
        }

        public Window ParentWindow
        {
            get;
            set;
        }

        public string TargetServer
        {
            get;
            set;
        }
    }

    internal static void OpenAdvancedServerSwitchWindow(string gameDir, string targetServer = "")
    {
        var newWindow = new Window();

        newWindow.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        newWindow.ExtendsContentIntoTitleBar = true;

        newWindow.Title = "SwitchServerTitle".GetLocalized();

        var hWnd = WindowNative.GetWindowHandle(newWindow);
        var winId = Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(winId);
        appWindow.Resize(new Windows.Graphics.SizeInt32(1280, 720));

        var rootFrame = new Frame();
        rootFrame.Navigate(typeof(AdvancedServerSwitchPage), new SwitchPageParams
        {
            GameDir = gameDir,
            ParentWindow = newWindow,
            TargetServer = targetServer
        });

        newWindow.Content = rootFrame;
        newWindow.Activate();
    }

    #endregion
}