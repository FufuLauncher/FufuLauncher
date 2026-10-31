/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Numerics;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    private Action? _accountFlyoutClosedAction;
    private string? _accountAvatarUrl;
    private BitmapImage? _navAvatarImage;

    private static readonly Uri DefaultAccountAvatarUri = new("ms-appx:///Assets/DefaultAvatar.png");

    #region Navigation & Layout

    private async void NavigationView_Loaded(object sender, RoutedEventArgs e)
    {
        bool isAccepted = false;
        try
        {
            var accepted = await _localSettingsService.ReadSettingAsync("UserAgreementAccepted");
            isAccepted = accepted != null && Convert.ToBoolean(accepted);
        }
        catch
        {
        }

        if (!isAccepted)
        {
            return;
        }

        await PerformMainInitAsync();
    }

    private async Task PerformMainInitAsync()
    {
        try
        {
            foreach (var item in NavigationView.MenuItems)
            {
                if (item is FrameworkElement uiItem) SetupSpringAnimation(uiItem);
            }

            foreach (var item in NavigationView.FooterMenuItems)
            {
                if (item is FrameworkElement uiItem) SetupSpringAnimation(uiItem);
            }

            await LoadFrameBackgroundOpacityAsync();
            await LoadOverlayOpacityAsync();
            await LoadAcrylicOverlaySettingAsync();
            await LoadPageOverlayOpacitySettingAsync();
            await LoadHamburgerButtonSettingAsync();
            await LoadAndApplyAcrylicSettingAsync();
            _ = LoadGlobalBackgroundAsync();
            await LoadMinimizeToTraySettingAsync();
            await LoadMinWindowSizeLimitSettingAsync();
            await LoadNotificationPositionAsync();
            await LoadNavItemVisibilityAsync();
            ShowMainContent();

            _ = Task.Run(async () =>
            {
                var starService = new StarPromotionService(_localSettingsService, dispatcherQueue);
                await starService.CheckAndShowPromptAsync();
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    var accountManager = App.GetService<AccountManager>();
                    var activeId = accountManager.ActiveAccountId;
                    if (activeId != null)
                    {
                        var cookies = await accountManager.LoadCookiesAsync(activeId);
                        if (cookies != null && cookies.Count > 0)
                        {
                            var refreshService = new TokenRefreshService();
                            var newCookies = await refreshService.RefreshCookieAsync(cookies);
                            if (newCookies != null)
                            {
                                await accountManager.UpdateCookiesAsync(activeId, newCookies);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog.Info("MainWindow.Navigation", $"Failed to refresh token at startup: {ex.Message}");
                }
            });
            _ = Task.Run(async () => await HashValidationService.ValidateFilesAsync());
        }
        catch
        {
            ShowMainContent();
        }
    }

    private void SetupSpringAnimation(FrameworkElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;

        element.SizeChanged += (_, e) =>
        {
            visual.CenterPoint = new Vector3((float)e.NewSize.Width / 2f, (float)e.NewSize.Height / 2f, 0f);
        };

        element.PointerPressed += (_, _) =>
        {
            var anim = compositor.CreateSpringVector3Animation();
            anim.Target = "Scale";
            anim.FinalValue = new Vector3(0.92f, 0.92f, 1f);

            anim.Period = TimeSpan.FromMilliseconds(20);
            anim.DampingRatio = 0.6f;

            visual.StartAnimation("Scale", anim);
        };

        void ResetScale()
        {
            var anim = compositor.CreateSpringVector3Animation();
            anim.Target = "Scale";
            anim.FinalValue = new Vector3(1f, 1f, 1f);

            anim.Period = TimeSpan.FromMilliseconds(60);
            anim.DampingRatio = 0.5f;

            visual.StartAnimation("Scale", anim);
        }

        element.PointerReleased += (_, _) => ResetScale();
        element.PointerExited += (_, _) => ResetScale();
    }

    private void ShowAgreementPage()
    {
        _isMainUiLoaded = false;
        SystemMessageBar.Visibility = Visibility.Collapsed;
        _networkMonitorService.Stop();

        Width = 850;
        Height = 560;
        WindowManagerHelper.CenterWindowOnScreen(AppWindow, Width, Height);

        AgreementFrame.Visibility = Visibility.Visible;
        NavigationView.Visibility = Visibility.Collapsed;
        AgreementFrame.Navigate(typeof(Views.AgreementPage));
        SyncPageTheme();
    }

    private async void ShowMainContent()
    {
        AgreementFrame.Visibility = Visibility.Collapsed;
        NavigationView.Visibility = Visibility.Visible;
        RefreshAccountMenu();
        NavigationView.SelectedItem = NavigationView.MenuItems[0];

        try
        {
            if (ContentFrame.CurrentSourcePageType != typeof(Views.MainPage))
                ContentFrame.Navigate(typeof(Views.MainPage));
        }
        catch (InvalidCastException ex)
        {
            DebugLog.Info("MainWindow.Navigation", $"MainPage navigation type cast exception: {ex.Message}");
        }

        SyncPageTheme();

        UpdatePageOverlayState(true);

        _isMainUiLoaded = true;
        SystemMessageBar.Visibility = Visibility.Visible;
        _networkMonitorService.Start();
        _ = _networkMonitorService.CheckNetworkAndProxyStatusAsync();

        var notifyEnabledObj = await _localSettingsService.ReadSettingAsync("IsRedeemCodeNotificationEnabled");
        if (notifyEnabledObj == null || Convert.ToBoolean(notifyEnabledObj))
        {
            var redeemService = new RedeemCodeReminderService(_localSettingsService);
            _ = redeemService.CheckRedeemCodesAsync(msg =>
            {
                dispatcherQueue.TryEnqueue(() => ShowNotification(msg));
            });
        }
    }

    private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        RefreshAccountMenu();

        if (args.SelectedItem is NavigationViewItem selectedItem)
        {
            var viewModelTag = selectedItem.Tag?.ToString();

            if (viewModelTag == "FufuLauncher.ViewModels.SettingsViewModel")
            {
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = 360,
                    Duration = new Duration(TimeSpan.FromSeconds(0.7)),
                    EasingFunction = new CircleEase { EasingMode = EasingMode.EaseOut }
                };

                Storyboard.SetTarget(anim, SettingsIconRotation);
                Storyboard.SetTargetProperty(anim, "Angle");

                var sb = new Storyboard();
                sb.Children.Add(anim);
                sb.Begin();
            }

            if (!string.IsNullOrEmpty(viewModelTag)) NavigateToPage(viewModelTag);
        }
    }

    private void NavigationView_PaneOpened(NavigationView sender, object args)
    {
        if (AccountNavText != null)
            AccountNavText.Visibility = Visibility.Visible;

        if (_notificationPosition == NotificationPosition.TopLeft ||
            _notificationPosition == NotificationPosition.BottomLeft)
        {
            ApplyNotificationPosition(_notificationPosition);
        }
    }

    private void NavigationView_PaneClosed(NavigationView sender, object args)
    {
        if (AccountNavText != null)
            AccountNavText.Visibility = Visibility.Collapsed;

        if (_notificationPosition == NotificationPosition.TopLeft ||
            _notificationPosition == NotificationPosition.BottomLeft)
        {
            ApplyNotificationPosition(_notificationPosition);
        }
    }

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

    internal async void NavigateToPage(string viewModelTag, bool force = false)
    {
        if (!force && !IsNavItemVisible(viewModelTag))
        {
            ShowNotification(new NotificationMessage("HiddenFeatureTitle".GetLocalized(),
                "HiddenFeatureMessage".GetLocalized(), NotificationType.Warning, 3000));
            NavigationView.SelectedItem = null;
            ContentFrame.Navigate(typeof(Views.HiddenPage), null, new SuppressNavigationTransitionInfo());
            SyncPageTheme();
            return;
        }

        var pageType = viewModelTag switch
        {
            "FufuLauncher.ViewModels.MainViewModel" => typeof(Views.MainPage),
            "FufuLauncher.ViewModels.GameSettingsViewModel" or "FufuLauncher.ViewModels.BlankViewModel" =>
                typeof(Views.GameSettingsPage),
            "FufuLauncher.ViewModels.SettingsViewModel" => typeof(Views.SettingsPage),
            "FufuLauncher.ViewModels.AccountViewModel" => typeof(Views.AccountPage),
            "FufuLauncher.ViewModels.OtherViewModel" => typeof(Views.OtherPage),
            "FufuLauncher.ViewModels.ControlPanelViewModel" or "FufuLauncher.ViewModels.ControlPanelModel" =>
                typeof(Views.ControlPanelPage),
            "FufuLauncher.ViewModels.PluginViewModel" => typeof(Views.PluginPage),
            "FufuLauncher.ViewModels.PluginStoreViewModel" => typeof(Views.PluginStorePage),
            "FufuLauncher.ViewModels.PluginSettingsViewModel" => typeof(Views.PluginSettingsPage),
            "FufuLauncher.ViewModels.HelpViewModel" => typeof(Views.HelpPage),
            _ => null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
        {
            if (ContentFrame.Content is Page currentPage)
            {
                var exitStoryboard = currentPage.FindName("ExitStoryboard") as Storyboard;
                if (exitStoryboard != null)
                {
                    exitStoryboard.Begin();
                    await Task.Delay(300);
                }
            }

            try
            {
                ContentFrame.Navigate(pageType, null, new SuppressNavigationTransitionInfo());
                SyncPageTheme();
            }
            catch (InvalidCastException ex)
            {
                DebugLog.Info("MainWindow.Navigation", $"Page navigation type cast exception: {ex.Message}");
                return;
            }

            var isMainPage = pageType == typeof(Views.MainPage);
            UpdatePageOverlayState(isMainPage);

            if (isMainPage)
            {
                var mainItem = GetAllNavItems()
                    .FirstOrDefault(i => i.Tag?.ToString() == "FufuLauncher.ViewModels.MainViewModel");
                if (mainItem != null)
                    NavigationView.SelectedItem = mainItem;
            }
        }
    }

    public async Task NavigateToSettingsUpdateSectionAsync()
    {
        if (_isExit) return;
        try
        {
            Activate();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }

        for (var i = 0; i < 40 && !_isMainUiLoaded; i++)
        {
            await Task.Delay(100);
        }

        var settingsItem = NavigationView.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "FufuLauncher.ViewModels.SettingsViewModel");

        if (settingsItem != null)
        {
            NavigationView.SelectedItem = settingsItem;
        }
        else
        {
            NavigateToPage("FufuLauncher.ViewModels.SettingsViewModel");
        }

        for (var i = 0; i < 40; i++)
        {
            if (ContentFrame.Content is Views.SettingsPage settingsPage)
            {
                await settingsPage.NavigateToUpdateSectionAsync();
                return;
            }

            await Task.Delay(100);
        }
    }

    public async Task NavigateToSettingsPageAsync()
    {
        if (_isExit) return;
        try
        {
            Activate();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }

        for (var i = 0; i < 40 && !_isMainUiLoaded; i++)
        {
            await Task.Delay(100);
        }

        var settingsItem = NavigationView.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "FufuLauncher.ViewModels.SettingsViewModel");

        if (settingsItem != null)
        {
            NavigationView.SelectedItem = settingsItem;
        }
        else
        {
            NavigateToPage("FufuLauncher.ViewModels.SettingsViewModel");
        }

        for (var i = 0; i < 40; i++)
        {
            if (ContentFrame.Content is Views.SettingsPage settingsPage)
            {
                await settingsPage.NavigateToCheckinSettingsAsync();
                return;
            }

            await Task.Delay(100);
        }
    }

    public async Task NavigateToSettingsTrustSectionAsync()
    {
        if (_isExit) return;
        try
        {
            Activate();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }

        for (var i = 0; i < 40 && !_isMainUiLoaded; i++)
        {
            await Task.Delay(100);
        }

        var settingsItem = NavigationView.FooterMenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "FufuLauncher.ViewModels.SettingsViewModel");

        if (settingsItem != null)
        {
            NavigationView.SelectedItem = settingsItem;
        }
        else
        {
            NavigateToPage("FufuLauncher.ViewModels.SettingsViewModel");
        }

        for (var i = 0; i < 40; i++)
        {
            if (ContentFrame.Content is Views.SettingsPage settingsPage)
            {
                await settingsPage.NavigateToTrustInstallAsync();
                return;
            }

            await Task.Delay(100);
        }
    }

    public async Task NavigateToAccountPageAsync()
    {
        if (_isExit) return;
        try
        {
            Activate();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }

        for (var i = 0; i < 40 && !_isMainUiLoaded; i++)
        {
            await Task.Delay(100);
        }

        var accountItem = NavigationView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "FufuLauncher.ViewModels.AccountViewModel");

        if (accountItem != null)
            NavigationView.SelectedItem = accountItem;
        else
            NavigateToPage("FufuLauncher.ViewModels.AccountViewModel");
    }

    public void NavigateToGameSettingsPage()
    {
        if (_isExit) return;

        var item = GetAllNavItems()
            .FirstOrDefault(i =>
                i.Tag?.ToString() == "FufuLauncher.ViewModels.GameSettingsViewModel" ||
                i.Tag?.ToString() == "FufuLauncher.ViewModels.BlankViewModel");

        if (item != null && (IsNavItemVisible("FufuLauncher.ViewModels.GameSettingsViewModel") ||
                             IsNavItemVisible("FufuLauncher.ViewModels.BlankViewModel")))
        {
            if (NavigationView.SelectedItem == item)
            {
                NavigateToPage("FufuLauncher.ViewModels.GameSettingsViewModel");
            }
            else
            {
                NavigationView.SelectedItem = item;
            }
        }
        else
        {
            NavigationView.SelectedItem = null;
            NavigateToPage("FufuLauncher.ViewModels.GameSettingsViewModel", force: true);
        }
    }

    public void NavigateToBlankPage() => NavigateToGameSettingsPage();

    public async Task NavigateToGameSettingsPageAsync()
    {
        if (_isExit) return;
        try
        {
            Activate();
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return;
        }

        for (var i = 0; i < 40 && !_isMainUiLoaded; i++)
        {
            await Task.Delay(100);
        }

        NavigateToGameSettingsPage();
    }

    public Task NavigateToBlankPageAsync() => NavigateToGameSettingsPageAsync();

    public async Task LoadNavItemVisibilityAsync()
    {
        var settings = App.GetService<ILocalSettingsService>();
        var allKeys = new[]
        {
            "MainViewModel", "PluginSettingsViewModel", "ControlPanelViewModel", "ControlPanelModel",
            "GameSettingsViewModel", "BlankViewModel", "AccountViewModel", "OtherViewModel",
            "PluginViewModel", "PluginStoreViewModel", "HelpViewModel",
            "SettingsViewModel"
        };

        foreach (var key in allKeys)
        {
            var val = await settings.ReadSettingAsync($"NavVisible_{key}");
            bool visible = val is bool b ? b : (val is string s && bool.TryParse(s, out var p) ? p : true);
            _navItemVisibility[$"FufuLauncher.ViewModels.{key}"] = visible;
        }


        foreach (var item in GetAllNavItems())
        {
            var tag = item.Tag?.ToString();
            if (tag != null && _navItemVisibility.TryGetValue(tag, out var visible))
            {
                item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private bool IsNavItemVisible(string viewModelKey)
    {
        return _navItemVisibility.TryGetValue(viewModelKey, out var visible) ? visible : true;
    }

    internal bool CanNavigateToPage(string viewModelKey) => IsNavItemVisible(viewModelKey);

    private void ApplyNavItemVisibility(NavItemConfig config)
    {
        _navItemVisibility[config.ViewModelKey] = config.IsUserVisible;

        foreach (var item in GetAllNavItems())
        {
            if (item.Tag?.ToString() == config.ViewModelKey)
            {
                item.Visibility = config.IsUserVisible ? Visibility.Visible : Visibility.Collapsed;
                break;
            }
        }
    }

    private IEnumerable<NavigationViewItem> GetAllNavItems()
    {
        return NavigationView.MenuItems
            .Concat(NavigationView.FooterMenuItems)
            .OfType<NavigationViewItem>();
    }

    private void UpdatePageOverlayState(bool isMainPage)
    {
        try
        {
            var screenHeight = Bounds.Height > 0 ? Bounds.Height : 1000;
            var targetOpacity = _isPageOverlaySemiTransparent ? _pageOverlayTargetOpacity : 1.0;

            if (isMainPage && _isOverlayShown)
            {
                PageBackgroundOverlay.Opacity = targetOpacity;

                var translateAnim = new DoubleAnimation
                {
                    From = OverlayTranslate.Y,
                    To = screenHeight + 100,
                    Duration = TimeSpan.FromMilliseconds(400),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };

                var sb = new Storyboard();
                Storyboard.SetTarget(translateAnim, OverlayTranslate);
                Storyboard.SetTargetProperty(translateAnim, "Y");
                sb.Children.Add(translateAnim);

                sb.Completed += (_, _) =>
                {
                    if (!_isOverlayShown)
                    {
                        PageBackgroundOverlay.Opacity = 0.0;
                    }
                };

                _pageOverlayStoryboard?.Stop();
                _pageOverlayStoryboard = sb;
                sb.Begin();

                _isOverlayShown = false;
            }
            else if (!isMainPage && !_isOverlayShown)
            {
                OverlayTranslate.Y = screenHeight;
                PageBackgroundOverlay.Opacity = 0.0;

                var translateAnim = new DoubleAnimation
                {
                    From = screenHeight,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(500),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var opacityAnim = new DoubleAnimation
                {
                    From = 0.0,
                    To = targetOpacity,
                    Duration = TimeSpan.FromMilliseconds(500),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                var sb = new Storyboard();
                Storyboard.SetTarget(translateAnim, OverlayTranslate);
                Storyboard.SetTargetProperty(translateAnim, "Y");

                Storyboard.SetTarget(opacityAnim, PageBackgroundOverlay);
                Storyboard.SetTargetProperty(opacityAnim, "Opacity");

                sb.Children.Add(translateAnim);
                sb.Children.Add(opacityAnim);

                _pageOverlayStoryboard?.Stop();
                _pageOverlayStoryboard = sb;
                sb.Begin();

                _isOverlayShown = true;
            }
            else if (!isMainPage && _isOverlayShown)
            {
                OverlayTranslate.Y = 0;
                PageBackgroundOverlay.Opacity = targetOpacity;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("MainWindow.Navigation", $"[MainWindow] Overlay animation exception: {ex.Message}");
        }
    }

    private void MainWindow_SizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        if (!_isOverlayShown)
        {
            OverlayTranslate.Y = Bounds.Height + 100;
        }
    }

    private async Task LoadHamburgerButtonSettingAsync()
    {
        try
        {
            var valueObj = await _localSettingsService.ReadSettingAsync("IsHamburgerButtonEnabled");
            _isHamburgerButtonEnabled = valueObj != null && Convert.ToBoolean(valueObj);
            ApplyHamburgerButtonVisibility(_isHamburgerButtonEnabled);
        }
        catch
        {
            _isHamburgerButtonEnabled = false;
        }
    }

    private void ApplyHamburgerButtonVisibility(bool isEnabled)
    {
        NavigationView.IsPaneToggleButtonVisible = isEnabled;
        if (!isEnabled)
        {
            NavigationView.IsPaneOpen = false;
        }
    }

    #endregion
}