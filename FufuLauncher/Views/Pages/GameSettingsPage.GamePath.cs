/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Services;
using FufuLauncher.Services.GameServer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using File = System.IO.File;

namespace FufuLauncher.Views;

public sealed partial class GameSettingsPage
{
    #region 游戏路径设置与信息加载

    private void PathTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ApplyPathButton != null)
        {
            ApplyPathButton.IsEnabled = !string.IsNullOrWhiteSpace(PathTextBox.Text);
        }
    }

    private string? GetGameDirectory()
    {
        if (_currentConfig == null || string.IsNullOrEmpty(_currentConfig.GamePath))
        {
            return null;
        }

        string gameDir = _currentConfig.GamePath;
        if (File.Exists(gameDir))
        {
            gameDir = Path.GetDirectoryName(gameDir) ?? gameDir;
        }

        return gameDir;
    }

    private async void ApplyPath_Click(object sender, RoutedEventArgs e)
    {
        await ProcessPathInput(PathTextBox.Text.Trim());
    }

    private async Task<bool> ValidateGameExecutableAsync(string path)
    {
        var exeNames = await GameExeManager.GetExeNamesAsync();
        bool found = false;
        bool isGlobal = false;

        foreach (var name in exeNames)
        {
            if (File.Exists(Path.Combine(path, name)))
            {
                found = true;
                if (name.Equals("GenshinImpact.exe", StringComparison.OrdinalIgnoreCase))
                {
                    isGlobal = true;
                }

                break;
            }
        }

        if (isGlobal)
        {
            var dialog = new ContentDialog
            {
                Title = "Title_GlobalClient".GetLocalized(),
                Content = "Msg_GlobalClientInjectionWarning".GetLocalized(),
                PrimaryButtonText = "Btn_ContinueUsing".GetLocalized(),
                CloseButtonText = "Btn_DiscardAndClear".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            return result == ContentDialogResult.Primary;
        }
        else if (found)
        {
            return true;
        }
        else
        {
            var dialog = new ContentDialog
            {
                Title = "Title_InvalidGamePath".GetLocalized(),
                Content = string.Format("Err_ExeNotFoundAtPath_Format".GetLocalized(), string.Join(" / ", exeNames)),
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = XamlRoot
            };
            await dialog.ShowAsync();
            return false;
        }
    }

    private async Task ProcessPathInput(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ShowEmptyState();
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                bool isValid = await ValidateGameExecutableAsync(path);

                if (isValid)
                {
                    await LoadGameInfoAsync(path);
                    await _localSettingsService.SaveSettingAsync("GameInstallationPath", path);
                    WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(path));

                    DebugLog.Info("GameSettingsPage.GamePath", $"[ProcessPathInput] Path set: {path}");
                }
                else
                {
                    PathTextBox.Text = string.Empty;
                    ShowEmptyState();
                }
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = "Title_InvalidPath".GetLocalized(),
                    Content = "Msg_PathDoesNotExist".GetLocalized(),
                    PrimaryButtonText = "OkBtn".GetLocalized(),
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();

                if (await _localSettingsService.ReadSettingAsync("GameInstallationPath") is string savedPath)
                {
                    PathTextBox.Text = savedPath.Trim('"').Trim();
                }
                else
                {
                    PathTextBox.Text = string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameSettingsPage.GamePath", $"[ProcessPathInput] Processing failed: {ex.Message}");
            await ShowError(string.Format("Err_PathProcessingFailed_Format".GetLocalized(), ex.Message));

            PathTextBox.Text = string.Empty;
            ShowEmptyState();
        }
    }

    private async void PathTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && ApplyPathButton.IsEnabled)
        {
            e.Handled = true;
            await ProcessPathInput(PathTextBox.Text.Trim());
        }
    }

    private async Task ShowAutoPathDialog(string foundPath)
    {
        DebugLog.Info("GameSettingsPage.GamePath", $"[Debug] ShowAutoPathDialog started");
        DebugLog.Info("GameSettingsPage.GamePath", $"[Debug] Received foundPath: {foundPath}");

        if (string.IsNullOrEmpty(foundPath))
        {
            DebugLog.Info("GameSettingsPage.GamePath", "[Debug] foundPath is empty, returning.");
            return;
        }

        if (XamlRoot == null)
        {
            DebugLog.Info("GameSettingsPage.GamePath",
                "[Debug] Critical: XamlRoot is null! Dialog cannot be shown, returning.");
            return;
        }

        try
        {
            DebugLog.Info("GameSettingsPage.GamePath", "[Debug] Creating ContentDialog...");
            var dialog = new ContentDialog
            {
                Title = "Title_AutoFoundGamePath".GetLocalized(),
                Content = string.Format("Msg_DetectedPossiblePath_Format".GetLocalized(), foundPath),
                PrimaryButtonText = "ApplyBtn".GetLocalized(),
                CloseButtonText = "Btn_SelectManually".GetLocalized(),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            DebugLog.Info("GameSettingsPage.GamePath", "[Debug] About to call dialog.ShowAsync()...");
            var result = await dialog.ShowAsync();
            DebugLog.Info("GameSettingsPage.GamePath", $"[Debug] Dialog closed, user choice: {result}");

            if (result == ContentDialogResult.Primary)
            {
                DebugLog.Info("GameSettingsPage.GamePath", "[Debug] User clicked Apply, saving...");
                PathTextBox.Text = foundPath;
                await LoadGameInfoAsync(foundPath);
                await _localSettingsService.SaveSettingAsync("GameInstallationPath", foundPath);
                WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(foundPath));
            }
            else
            {
                DebugLog.Info("GameSettingsPage.GamePath",
                    "[Debug] User clicked manual select, calling PickGameFolderAsync()");
                await PickGameFolderAsync();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameSettingsPage.GamePath",
                $"[Debug] ShowAutoPathDialog failed (possibly conflicting dialogs): {ex.Message}\n{ex.StackTrace}");
        }
    }

    private async void SelectPath_Click(object sender, RoutedEventArgs e)
    {
        await PickGameFolderAsync();
    }

    private async void ClearPath_Click(object sender, RoutedEventArgs e)
    {
        PathTextBox.Text = string.Empty;
        _currentConfig = null;
        await _localSettingsService.SaveSettingAsync("GameInstallationPath", string.Empty);
        WeakReferenceMessenger.Default.Send(new GamePathChangedMessage(string.Empty));
        ShowEmptyState();
    }

    private async Task PickGameFolderAsync()
    {
        var path = await FilePickerService.PickOpenFileAsync(
            null,
            new[] { ("Common_ExecutableFiles".GetLocalized(), new[] { ".exe" }) },
            PickerLocationId.ComputerFolder,
            msg => WeakReferenceMessenger.Default.Send(new NotificationMessage("ErrorTitle".GetLocalized(), msg,
                NotificationType.Error)));
        if (string.IsNullOrEmpty(path)) return;

        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            PathTextBox.Text = folder;
            await ProcessPathInput(folder);
        }
    }

    private async Task LoadGameInfoAsync(string gamePath)
    {
        gamePath = gamePath?.Trim('"').Trim();

        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
        {
            ShowEmptyState();
            return;
        }

        LoadingRing.IsActive = true;

        try
        {
            var config = new GameConfigData { GamePath = gamePath };

            _currentConfig = config;

            ShowInfo();

            await Task.Run(async () =>
            {
                var configPath = Path.Combine(gamePath, "config.ini");
                if (!File.Exists(configPath))
                {
                    configPath = Directory.GetFiles(gamePath, "config.ini", SearchOption.AllDirectories)
                        .FirstOrDefault();
                }

                if (configPath != null && File.Exists(configPath))
                {
                    var content = await File.ReadAllTextAsync(configPath);
                    var versionLine = content.Split('\n')
                        .FirstOrDefault(line => line.StartsWith("game_version=", StringComparison.OrdinalIgnoreCase));
                    if (versionLine != null)
                    {
                        var parts = versionLine.Split('=', 2);
                        if (parts.Length > 1)
                            config.Version = parts[1].Trim();
                    }
                }
                else
                {
                    config.Version = "Msg_VersionInfoNotFound".GetLocalized();
                }

                var serverScheme = App.GetService<GameServerConfigurationService>().TryDetectCurrentScheme(gamePath);
                config.ServerType = serverScheme?.DisplayName ?? "UnknownGeneric".GetLocalized();

                config.DirectorySize = CalculateDirectorySize(gamePath);

                DispatcherQueue.TryEnqueue(() => ShowInfo());
            });

            _ = GetGameBranchesInfoAsync();
            _ = RefreshUpdateStateAsync();
        }
        catch (Exception ex)
        {
            DebugLog.Info("GameSettingsPage.GamePath", $"[LoadGameInfoAsync] Error: {ex.Message}");
            ShowEmptyState();
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private void ShowInfo()
    {
        if (_currentConfig == null) return;

        VersionText.Text = _currentConfig.Version;
        ServerText.Text = _currentConfig.ServerType;
        SizeText.Text = _currentConfig.DirectorySize;

        InfoPanel.Visibility = Visibility.Visible;
        EmptyPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowEmptyState()
    {
        InfoPanel.Visibility = Visibility.Collapsed;
        EmptyPanel.Visibility = Visibility.Visible;
    }

    private string CalculateDirectorySize(string path)
    {
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };

            long sizeInBytes = 0;
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
            {
                sizeInBytes += file.Length;
            }

            return sizeInBytes switch
            {
                >= 1073741824 => $"{sizeInBytes / 1073741824.0:F2} GB",
                >= 1048576 => $"{sizeInBytes / 1048576.0:F2} MB",
                >= 1024 => $"{sizeInBytes / 1024.0:F2} KB",
                _ => $"{sizeInBytes} Bytes"
            };
        }
        catch
        {
            return "Msg_CannotCalculate".GetLocalized();
        }
    }

    #endregion
}