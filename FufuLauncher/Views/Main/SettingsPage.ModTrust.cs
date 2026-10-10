/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using FufuLauncher.Helpers;
using FufuLauncher.Services.CodeSigning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage
{
    private CodeSigningTrustService TrustService => App.GetService<CodeSigningTrustService>();

    private bool _isSyncingModTrustMode;
    private bool _isModTrustPackageVerified;
    private ModTrustEnforcement _lastEnabledModTrustMode = ModTrustEnforcement.Warn;

    private async Task RefreshModTrustUiAsync()
    {
        SelectModTrustMode(ModTrustGate.ReadMode());

        try
        {
            var status = await Task.Run(() => TrustService.GetStatus());

            if (ModTrustStatusText != null)
            {
                ModTrustStatusText.Text = BuildStatusText(status);
            }

            if (ModTrustRootInfoText != null)
            {
                var info = status.PackageVerified
                    ? string.Empty
                    : status.PackageError ?? "ModTrust_PackageMissing".GetLocalized();

                ModTrustRootInfoText.Text = info;
                ModTrustRootInfoText.Visibility =
                    string.IsNullOrEmpty(info) ? Visibility.Collapsed : Visibility.Visible;
            }

            if (ModTrustModeComboBox != null)
            {
                _isModTrustPackageVerified = status.PackageVerified;
                ModTrustModeComboBox.IsEnabled = status.PackageVerified && LaunchSignatureVerificationToggle.IsOn;
            }

            if (ModTrustInstallUserButton != null)
            {
                ModTrustInstallUserButton.IsEnabled = status.PackageVerified &&
                                                      !status.MachineProvidesTrust &&
                                                      !status.UserScopeManaged;
            }

            if (ModTrustUninstallUserButton != null)
            {
                ModTrustUninstallUserButton.IsEnabled = status.PackageVerified && status.UserScopeManaged;
            }

            if (ModTrustInstallMachineButton != null)
            {
                ModTrustInstallMachineButton.IsEnabled = status.PackageVerified && !status.MachineScopeManaged;
            }

            if (ModTrustUninstallMachineButton != null)
            {
                ModTrustUninstallMachineButton.IsEnabled = status.PackageVerified && status.MachineScopeManaged;
            }

            if (ModTrustSyncButton != null)
            {
                ModTrustSyncButton.IsEnabled = CodeSigningTrustService.GetPinnedRootThumbprint() != null;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModTrust] 刷新信任状态失败: {ex}");
        }
    }

    private static string BuildStatusText(TrustServiceStatus status)
    {
        if (!status.PackageVerified)
        {
            return status.PackageError ?? "ModTrust_PackageMissing".GetLocalized();
        }

        string installed;

        if (status.UserScopeManaged && status.MachineProvidesTrust)
        {
            installed = "ModTrust_InstalledBoth".GetLocalized();
        }
        else if (status.MachineProvidesTrust)
        {
            installed = "ModTrust_InstalledMachineEffective".GetLocalized();
        }
        else if (status.UserScopeManaged)
        {
            installed = "ModTrust_InstalledUser".GetLocalized();
        }
        else
        {
            installed = "ModTrust_NotInstalled".GetLocalized();
        }

        var text = string.Format("ModTrust_StatusFormat".GetLocalized(), installed,
            status.SyncEndpoint ?? CodeSigningTrustService.DefaultSyncEndpoint);

        if (status.MachineProvidesTrust && !status.UserScopeManaged)
        {
            text += Environment.NewLine + "ModTrust_MachineHint".GetLocalized();
        }

        return text;
    }

    private async void OnModTrustRefreshClick(object sender, RoutedEventArgs e)
    {
        await RefreshModTrustUiAsync();
    }

    private async void OnModTrustInstallUserClick(object sender, RoutedEventArgs e)
    {
        await InstallOrUninstallAsync(TrustStoreScope.CurrentUser, install: true);
    }

    private async void OnModTrustUninstallUserClick(object sender, RoutedEventArgs e)
    {
        await InstallOrUninstallAsync(TrustStoreScope.CurrentUser, install: false);
    }

    private async void OnModTrustInstallMachineClick(object sender, RoutedEventArgs e)
    {
        await InstallOrUninstallAsync(TrustStoreScope.LocalMachine, install: true);
    }

    private async void OnModTrustUninstallMachineClick(object sender, RoutedEventArgs e)
    {
        await InstallOrUninstallAsync(TrustStoreScope.LocalMachine, install: false);
    }

    private async void OnModTrustModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingModTrustMode) return;
        if (ModTrustModeComboBox?.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        if (!Enum.TryParse<ModTrustEnforcement>(tag, true, out var mode)) return;

        var current = ModTrustGate.ReadMode();
        if (mode == current) return;

        if (mode == ModTrustEnforcement.Enforce)
        {
            var confirmed = await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "ModTrust_StrictTitle".GetLocalized(),
                Content = "ModTrust_StrictConfirm".GetLocalized(),
                PrimaryButtonText = "OkBtn".GetLocalized(),
                CloseButtonText = "CancelBtn".GetLocalized(),
                XamlRoot = XamlRoot
            });

            if (confirmed != ContentDialogResult.Primary)
            {
                SelectModTrustMode(current);
                return;
            }
        }

        SaveModTrustMode(mode);
    }

    private void OnLaunchSignatureVerificationToggled(object sender, RoutedEventArgs e)
    {
        if (_isSyncingModTrustMode || sender is not ToggleSwitch toggle) return;

        var current = ModTrustGate.ReadMode();
        var mode = toggle.IsOn ? _lastEnabledModTrustMode : ModTrustEnforcement.Off;
        if (mode == current) return;

        SaveModTrustMode(mode);
    }

    private void SaveModTrustMode(ModTrustEnforcement mode)
    {
        var saved = ModTrustGate.TryWriteMode(mode);
        var current = ModTrustGate.ReadMode();
        SelectModTrustMode(current);
        Debug.WriteLine(saved
            ? $"[ModTrust] 信任策略模式已切换为 {current}"
            : "[ModTrust] 保存信任策略失败，已恢复当前设置");
    }

    private void SelectModTrustMode(ModTrustEnforcement mode)
    {
        if (ModTrustModeComboBox == null) return;

        _isSyncingModTrustMode = true;
        try
        {
            if (mode != ModTrustEnforcement.Off)
            {
                _lastEnabledModTrustMode = mode;
            }

            LaunchSignatureVerificationToggle.IsOn = mode != ModTrustEnforcement.Off;
            ModTrustModeComboBox.IsEnabled = _isModTrustPackageVerified && LaunchSignatureVerificationToggle.IsOn;
            ModTrustModeComboBox.SelectedIndex = mode switch
            {
                ModTrustEnforcement.Off => 0,
                ModTrustEnforcement.Warn => 1,
                _ => 2
            };
        }
        finally
        {
            _isSyncingModTrustMode = false;
        }
    }

    private async void OnModTrustSyncClick(object sender, RoutedEventArgs e)
    {
        if (ModTrustSyncButton != null) ModTrustSyncButton.IsEnabled = false;

        try
        {
            var confirmed = await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "ModTrust_SyncTitle".GetLocalized(),
                Content = "ModTrust_SyncConfirm".GetLocalized(),
                PrimaryButtonText = "OkBtn".GetLocalized(),
                CloseButtonText = "CancelBtn".GetLocalized(),
                XamlRoot = XamlRoot
            });

            if (confirmed != ContentDialogResult.Primary) return;

            var result = await TrustService.SyncFromEndpointAsync();

            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = result.Ok ? "Success".GetLocalized() : "Failure".GetLocalized(),
                Content = result.Message,
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = XamlRoot
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ModTrust] 同步信任包失败: {ex}");
            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "ErrorTitle".GetLocalized(),
                Content = ex.Message,
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = XamlRoot
            });
        }
        finally
        {
            if (ModTrustSyncButton != null) ModTrustSyncButton.IsEnabled = true;
            await RefreshModTrustUiAsync();
        }
    }

    private async Task InstallOrUninstallAsync(TrustStoreScope scope, bool install)
    {
        var status = await Task.Run(() => TrustService.GetStatus());
        if (!status.PackageVerified)
        {
            await ShowSafeDialogAsync(new ContentDialog
            {
                Title = "ModTrust_Title".GetLocalized(),
                Content = status.PackageError ?? "ModTrust_PackageMissing".GetLocalized(),
                CloseButtonText = "OkBtn".GetLocalized(),
                XamlRoot = XamlRoot
            });
            return;
        }

        var result = install
            ? await TrustInstallFlow.InstallAsync(TrustService, XamlRoot, scope)
            : await TrustInstallFlow.UninstallAsync(TrustService, XamlRoot, scope);

        if (result == null) return;

        await ShowSafeDialogAsync(new ContentDialog
        {
            Title = result.Ok ? "Success".GetLocalized() : "Failure".GetLocalized(),
            Content = result.Message,
            CloseButtonText = "OkBtn".GetLocalized(),
            XamlRoot = XamlRoot
        });

        await RefreshModTrustUiAsync();
    }
}