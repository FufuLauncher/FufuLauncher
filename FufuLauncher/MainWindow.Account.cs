/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    private Action? _accountFlyoutClosedAction;
    private string? _accountAvatarUrl;
    private string? _accountPendantUrl;
    private bool _showAccountAvatarPendant;
    private BitmapImage? _navAvatarImage;
    private BitmapImage? _pendantImage;
    private bool _accountPendantLoadFailed;
    private bool _accountPendantCacheInvalidated;
    private bool _accountPendantUrlRefreshRequested;

    private static readonly Uri DefaultAccountAvatarUri = new("ms-appx:///Assets/DefaultAvatar.png");

    private void AccountNavButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshAccountMenu();
    }

    private void RefreshAccountMenu()
    {
        AccountNavText.Visibility = NavigationView.IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
        var entry = _accountManager.GetActiveAccountEntry();
        if (entry == null)
        {
            _accountAvatarUrl = null;
            ResetNavAvatar();
            ApplyAccountAvatarPendant(null);
            AccountNavText.Text = "Account_MenuTitle".GetLocalized();
            AccountFlyoutProfilePanel.Visibility = Visibility.Collapsed;
            AccountFlyoutGuestPanel.Visibility = Visibility.Visible;
            return;
        }

        var role = GameRoleSelection.Current(entry);
        var nickname = entry.Nickname;
        if (string.IsNullOrWhiteSpace(nickname))
            nickname = role?.nickname ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nickname))
            nickname = string.Format("Account_User_Format".GetLocalized(), entry.Stuid);

        if (_navAvatarImage == null ||
            !string.Equals(_accountAvatarUrl, entry.AvatarUrl, StringComparison.Ordinal))
        {
            _accountAvatarUrl = entry.AvatarUrl;
            ShowNavAvatar(entry.AvatarUrl);
            ApplyFlyoutAvatar(entry.AvatarUrl);
        }

        ApplyAccountAvatarPendant(_showAccountAvatarPendant ? entry.AvatarPendantUrl : null);

        AccountNavText.Text = nickname;
        AccountFlyoutNickname.Text = nickname;
        AccountFlyoutCommunityUid.Text = FormatAccountValue(entry.Stuid);
        AccountFlyoutGameUid.Text = FormatAccountValue(role?.game_uid ?? entry.GameUid);
        var regionName = role?.region_name;
        AccountFlyoutServer.Text = !string.IsNullOrWhiteSpace(regionName)
            ? regionName
            : (entry.ServerType == "os" || entry.Id.StartsWith("os_", StringComparison.Ordinal)
                ? "Account_Server_Global".GetLocalized()
                : "Account_Server_CN".GetLocalized());
        AccountFlyoutLevel.Text = role != null && role.level > 0
            ? role.level.ToString()
            : "Common_None".GetLocalized();
        AccountFlyoutProfilePanel.Visibility = Visibility.Visible;
        AccountFlyoutGuestPanel.Visibility = Visibility.Collapsed;
    }

    private static string FormatAccountValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Common_None".GetLocalized() : value;

    private void ApplyAccountAvatarPendant(string? url)
    {
        var normalized = AccountAvatarPendant.NormalizeUrl(url);
        if (string.Equals(_accountPendantUrl, normalized, StringComparison.Ordinal) && !_accountPendantLoadFailed)
            return;

        if (!string.Equals(_accountPendantUrl, normalized, StringComparison.Ordinal))
        {
            _accountPendantCacheInvalidated = false;
            _accountPendantUrlRefreshRequested = false;
        }

        _accountPendantUrl = normalized;
        _accountPendantLoadFailed = false;

        if (normalized == null)
        {
            SetPendantSource(null);
            return;
        }

        SetPendantSource(AccountAvatarPendantCache.TryGetCachedFile(normalized, out var cachedPath)
            ? cachedPath
            : normalized);
        _ = AccountAvatarPendantCache.EnsureCachedAsync(normalized);
    }

    private void SetPendantSource(string? source)
    {
        try
        {
            DetachPendantImage();

            if (source == null)
            {
                AccountNavPendant.Source = null;
                AccountFlyoutPendant.Source = null;
                return;
            }

            var image = new BitmapImage(new Uri(source));
            image.ImageFailed += OnPendantImageFailed;
            _pendantImage = image;
            AccountNavPendant.Source = image;
            AccountFlyoutPendant.Source = image;
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Navigation", $"[Pendant] Unable to apply pendant: {ex.Message}");
        }
    }

    private void DetachPendantImage()
    {
        if (_pendantImage == null)
            return;

        _pendantImage.ImageFailed -= OnPendantImageFailed;
        _pendantImage = null;
    }

    private void OnPendantImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not BitmapImage image || !ReferenceEquals(image, _pendantImage))
            return;

        var failedUri = image.UriSource;
        var url = _accountPendantUrl;
        if (url == null)
        {
            SetPendantSource(null);
            return;
        }

        if (failedUri is { IsFile: true })
        {
            if (!_accountPendantCacheInvalidated && AccountAvatarPendantCache.Invalidate(url))
            {
                _accountPendantCacheInvalidated = true;
                _accountPendantLoadFailed = true;
                DebugLog.Warn("MainWindow.Navigation",
                    "[Pendant] Cached pendant file was corrupt, removed and retrying from remote: " + e.ErrorMessage);
                SetPendantSource(url);
                _ = AccountAvatarPendantCache.EnsureCachedAsync(url);
                return;
            }
        }
        else if (AccountAvatarPendantCache.TryGetCachedFile(url, out var cachedPath))
        {
            DebugLog.Info("MainWindow.Navigation",
                "[Pendant] Remote pendant failed, falling back to cache: " + e.ErrorMessage);
            SetPendantSource(cachedPath);
            return;
        }

        _accountPendantLoadFailed = true;
        SetPendantSource(null);
        DebugLog.Info("MainWindow.Navigation", "[Pendant] Pendant image failed to load: " + e.ErrorMessage);

        if (_accountPendantUrlRefreshRequested) return;
        _accountPendantUrlRefreshRequested = true;
        _ = SyncAccountAvatarPendantAsync(forceRefreshUrl: true);
    }

    private async Task SyncAccountAvatarPendantAsync(bool forceRefreshUrl)
    {
        try
        {
            if (!_showAccountAvatarPendant) return;

            var accountManager = App.GetService<AccountManager>();
            var entry = accountManager.GetActiveAccountEntry();
            if (entry == null || entry.Id.StartsWith("os", StringComparison.OrdinalIgnoreCase)) return;

            var pendant = AccountAvatarPendant.NormalizeUrl(entry.AvatarPendantUrl);
            if (pendant != null && !forceRefreshUrl)
            {
                await AccountAvatarPendantCache.EnsureCachedAsync(pendant);
                return;
            }

            var cookies = await accountManager.LoadCookiesAsync(entry.Id);
            if (cookies == null || cookies.Count == 0) return;

            var cookieString = string.Join("; ", cookies.Select(kv => $"{kv.Key}={kv.Value}"));
            var response = await App.GetService<IUserInfoService>().GetUserFullInfoAsync(cookieString, entry.ServerType);
            if (response is not { retcode: 0 }) return;

            var fetched = AccountAvatarPendant.NormalizeUrl(response.data?.user_info?.pendant);
            if (fetched == null || string.Equals(fetched, pendant, StringComparison.Ordinal)) return;

            await accountManager.UpdateAvatarPendantAsync(entry.Id, fetched);
            await AccountAvatarPendantCache.EnsureCachedAsync(fetched);
            dispatcherQueue.TryEnqueue(() => WeakReferenceMessenger.Default.Send(new AccountAvatarChangedMessage()));
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Navigation", $"[Pendant] Cloud sync failed: {ex.Message}");
        }
    }

    private static Uri ResolveAccountAvatarUri(string? avatarUrl)
    {
        if (!string.IsNullOrWhiteSpace(avatarUrl) &&
            Uri.TryCreate(avatarUrl, UriKind.Absolute, out var parsed))
        {
            return parsed;
        }

        return DefaultAccountAvatarUri;
    }

    private void ShowNavAvatar(string? avatarUrl)
    {
        DetachNavAvatarImage();

        var image = new BitmapImage();
        image.ImageOpened += OnNavAvatarImageOpened;
        image.ImageFailed += OnNavAvatarImageFailed;
        _navAvatarImage = image;

        AccountNavPlaceholderIcon.Visibility = Visibility.Visible;
        AccountNavAvatar.Source = image;

        image.UriSource = ResolveAccountAvatarUri(avatarUrl);
    }

    private void ApplyFlyoutAvatar(string? avatarUrl)
    {
        var image = new BitmapImage();
        image.ImageFailed += (_, _) =>
        {
            if (!ReferenceEquals(AccountFlyoutAvatar.ProfilePicture, image)) return;
            if (image.UriSource != DefaultAccountAvatarUri)
                image.UriSource = DefaultAccountAvatarUri;
        };

        AccountFlyoutAvatar.ProfilePicture = image;
        image.UriSource = ResolveAccountAvatarUri(avatarUrl);
    }

    private void OnNavAvatarImageOpened(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _navAvatarImage))
            AccountNavPlaceholderIcon.Visibility = Visibility.Collapsed;
    }

    private void OnNavAvatarImageFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (sender is not BitmapImage image || !ReferenceEquals(image, _navAvatarImage))
            return;

        if (image.UriSource != DefaultAccountAvatarUri)
        {
            image.UriSource = DefaultAccountAvatarUri;
            return;
        }

        DebugLog.Info("MainWindow.Navigation", $"[NavAvatar] Avatar image failed to load: {e.ErrorMessage}");
        ResetNavAvatar();
    }

    private void ResetNavAvatar()
    {
        DetachNavAvatarImage();

        AccountNavAvatar.Source = null;
        AccountNavPlaceholderIcon.Visibility = Visibility.Visible;
    }

    private void DetachNavAvatarImage()
    {
        if (_navAvatarImage == null)
            return;

        _navAvatarImage.ImageOpened -= OnNavAvatarImageOpened;
        _navAvatarImage.ImageFailed -= OnNavAvatarImageFailed;
        _navAvatarImage = null;
    }

    private void AccountSwitchButton_Click(object sender, RoutedEventArgs e)
    {
        _accountFlyoutClosedAction = ShowAccountSwitchFlyout;
        AccountFlyout.Hide();
    }

    private void RoleSwitchButton_Click(object sender, RoutedEventArgs e)
    {
        _accountFlyoutClosedAction = ShowRoleSwitchFlyout;
        AccountFlyout.Hide();
    }

    private void ShowAccountSwitchFlyout()
    {
        var flyout = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedBottom
        };
        var activeId = _accountManager.ActiveAccountId;
        foreach (var account in _accountManager.GetAllAccounts())
        {
            var nickname = string.IsNullOrWhiteSpace(account.Nickname) ? account.Stuid : account.Nickname;
            var item = new RadioMenuFlyoutItem
            {
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["AppGlobalFont"],
                Text = $"{nickname} · {account.Stuid}",
                GroupName = "AccountSwitch",
                IsChecked = account.Id == activeId
            };
            item.Click += async (_, _) => await SwitchAccountAsync(account);
            flyout.Items.Add(item);
        }

        if (flyout.Items.Count == 0)
            flyout.Items.Add(new MenuFlyoutItem
            {
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["AppGlobalFont"],
                Text = "Account_NotLoggedIn".GetLocalized(),
                IsEnabled = false
            });

        flyout.ShowAt(AccountNavButton);
    }

    private async Task SwitchAccountAsync(AccountEntry account)
    {
        if (account.Id == _accountManager.ActiveAccountId) return;
        try
        {
            await _accountManager.SwitchAccountAsync(account.Id);
            WeakReferenceMessenger.Default.Send(new AccountChangedMessage());
            RefreshAccountMenu();
            await RefreshAccountPageAsync(accountChanged: true);
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Navigation", $"[AccountSwitch] {ex.Message}");
        }
    }

    private void ShowRoleSwitchFlyout()
    {
        var flyout = new MenuFlyout
        {
            Placement = FlyoutPlacementMode.RightEdgeAlignedBottom
        };
        var entry = _accountManager.GetActiveAccountEntry();
        var roles = entry == null
            ? new List<GameRoleInfo>()
            : GameRoleSelection.Filter(entry.GameRoles ?? [], entry.ServerType);
        var current = entry == null ? null : GameRoleSelection.Current(entry);

        foreach (var role in roles)
        {
            var label = string.IsNullOrWhiteSpace(role.nickname)
                ? role.game_uid
                : $"{role.nickname} · {role.region_name} · {role.game_uid}";
            var item = new RadioMenuFlyoutItem
            {
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["AppGlobalFont"],
                Text = label,
                GroupName = "RoleSwitch",
                IsChecked = current != null && role.game_uid == current.game_uid && role.region == current.region
            };
            item.Click += async (_, _) => await SwitchRoleAsync(entry!.Id, role);
            flyout.Items.Add(item);
        }

        if (roles.Count == 0)
            flyout.Items.Add(new MenuFlyoutItem
            {
                FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["AppGlobalFont"],
                Text = "Account_NoCharacters".GetLocalized(),
                IsEnabled = false
            });

        flyout.ShowAt(AccountNavButton);
    }

    private async Task SwitchRoleAsync(string accountId, GameRoleInfo role)
    {
        try
        {
            await App.GetService<GameRoleService>().SelectAsync(accountId, role);
            RefreshAccountMenu();
            await RefreshAccountPageAsync(accountChanged: false);
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Navigation", $"[RoleSwitch] {ex.Message}");
        }
    }

    private async Task RefreshAccountPageAsync(bool accountChanged)
    {
        if (ContentFrame.Content is Views.AccountPage accountPage)
            await accountPage.RefreshForSidebarSwitchAsync(accountChanged);
    }

    private async void AccountFlyoutGoToAccount_Click(object sender, RoutedEventArgs e)
    {
        AccountFlyout.Hide();
        await NavigateToAccountPageAsync();
    }
}
