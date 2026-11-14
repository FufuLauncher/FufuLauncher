using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow
{
    private readonly ObservableCollection<HutaoGachaEntrySelectionItem> _hutaoGachaEntries = new();
    private readonly ObservableCollection<FufuCloudUigfUidItem> _fufuUigfUids = new();
    private readonly ObservableCollection<FufuCloudChallengeItem> _fufuChallengeItems = new();
    private readonly ObservableCollection<HutaoAccountExtraFieldItem> _hutaoExtraFields = new();
    private readonly ObservableCollection<HutaoDeviceListItem> _hutaoDevices = new();
    private readonly ObservableCollection<HutaoRankDisplayItem> _hutaoStatsRows = new();
    private readonly ObservableCollection<HutaoUidRankDisplayItem> _hutaoUidRankRows = new();
    private readonly List<HutaoGachaEntrySelectionItem> _allHutaoGachaEntries = new();
    private readonly List<FufuCloudUigfUidItem> _allFufuUigfUids = new();
    private readonly List<FufuCloudChallengeItem> _allFufuChallengeItems = new();
    private readonly List<AccountBoundRoleOption> _accountRoles = new();
    private readonly List<string> _fufuUigfSlots = new();
    private readonly List<string> _fufuChallengeSlots = new();
    private DispatcherQueueTimer? _hutaoOtpTimer;
    private int _currentHutaoSubTabIndex;
    private bool _hutaoInitialized;
    private bool _suppressHutaoSlotComboSync;
    private bool _suppressHutaoRoleSync;
    private bool _suppressHutaoSaveModeSync;
    private bool _suppressHutaoStatsFilterSync;
    private string _currentHutaoSubTab = "HutaoAccount";
    private string _selectedRoleUid = string.Empty;
    private string? _hutaoDevicesLoadedForEmail;
    private string? _hutaoEntriesLoadedForEmail;
    private bool _hutaoDevicesLoading;
    private bool _hutaoEntriesLoading;
    private bool _hutaoStatsLoaded;
    private bool _hutaoStatsLoading;
    private string _hutaoStatsLoadedForRoleUid = string.Empty;
    private HutaoStatisticsBundle? _hutaoStatsBundle;
    private bool _hutaoStatsLastPeriod;
    private string _hutaoStatsCategory = "Util";
    private int _hutaoStatsFloor = 12;
    private string? _lastInspectedUigfSlot;
    private string? _lastInspectedChallengeSlot;
    private long _uigfInspectVersion;
    private long _challengeInspectVersion;

    private void InitializeHutaoBridge()
    {
        HutaoAuthModeSelector.ItemsSource = new[]
        {
            "HutaoSync_ModeLogin".GetLocalized(),
            "HutaoSync_ModeRegister".GetLocalized(),
            "HutaoSync_ModeReset".GetLocalized()
        };
        HutaoAuthModeSelector.SelectedIndex = 0;

        HutaoGachaEntriesRepeater.ItemsSource = _hutaoGachaEntries;
        FufuUigfUidsRepeater.ItemsSource = _fufuUigfUids;
        FufuChallengeItemsRepeater.ItemsSource = _fufuChallengeItems;
        HutaoExtraFieldsRepeater.ItemsSource = _hutaoExtraFields;
        HutaoDevicesRepeater.ItemsSource = _hutaoDevices;
        HutaoStatsRowsRepeater.ItemsSource = _hutaoStatsRows;
        HutaoStatsUidRanksRepeater.ItemsSource = _hutaoUidRankRows;

        InitializeHutaoPullSaveModeCombo();
        InitializeHutaoStatsSelectors();

        _hutaoOtpTimer = DispatcherQueue.CreateTimer();
        _hutaoOtpTimer.Interval = TimeSpan.FromSeconds(1);
        _hutaoOtpTimer.Tick += (_, _) => UpdateHutaoOtpButton();

        ReloadAccountBoundRoles();

        if (_accountManager != null)
        {
            WeakReferenceMessenger.Default.Register<AccountChangedMessage>(
                this,
                (_, _) => DispatcherQueue.TryEnqueue(HandleAccountRolesUpdated));
            WeakReferenceMessenger.Default.Register<GameRoleChangedMessage>(
                this,
                (_, _) => DispatcherQueue.TryEnqueue(HandleAccountRolesUpdated));
            WeakReferenceMessenger.Default.Register<GameRolesUpdatedMessage>(
                this,
                (_, _) => DispatcherQueue.TryEnqueue(HandleAccountRolesUpdated));
        }

        SelectHutaoSubTab("HutaoAccount");

        _hutaoCloud.SessionChanged += OnHutaoSessionChanged;
        _ = InitializeHutaoSessionAsync();
    }

    private void CleanupHutaoBridge()
    {
        _hutaoOtpTimer?.Stop();
        _hutaoCloud.SessionChanged -= OnHutaoSessionChanged;
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    private void HandleAccountRolesUpdated()
    {
        if (_closed) return;
        ReloadAccountBoundRoles();
        RefreshHutaoRoleLabels();
        ApplyAllHutaoRoleFilters();
        if (_currentHutaoSubTab == "HutaoStats")
        {
            _ = RefreshHutaoStatisticsAsync(silent: true);
        }
        else
        {
            _hutaoStatsLoaded = false;
        }
    }

    private async Task InitializeHutaoSessionAsync()
    {
        await _hutaoCloud.InitializeAsync();
        if (_closed) return;
        _hutaoInitialized = true;
        DispatcherQueue.TryEnqueue(RefreshHutaoUiState);
    }

    private void OnHutaoSessionChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(RefreshHutaoUiState);
    }

    private void SelectHutaoSubTab(string tag)
    {
        _currentHutaoSubTab = string.IsNullOrWhiteSpace(tag) ? "HutaoAccount" : tag;
        if (HutaoSubTabSelector != null)
        {
            var matched = HutaoSubTabSelector.Items
                .OfType<SelectorBarItem>()
                .FirstOrDefault(i => string.Equals(i.Tag?.ToString(), _currentHutaoSubTab, StringComparison.Ordinal));
            if (matched != null && HutaoSubTabSelector.SelectedItem != matched)
            {
                HutaoSubTabSelector.SelectedItem = matched;
            }
        }

        UpdateHutaoSubTabVisibility();
    }

    private void OnHutaoSubTabSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem is SelectorBarItem item && item.Tag is string tag)
        {
            _currentHutaoSubTab = tag;
            UpdateHutaoSubTabVisibility();
        }
    }

    private void UpdateHutaoSubTabVisibility()
    {
        if (HutaoSubAccountPanel == null || HutaoSubPullPanel == null ||
            HutaoSubPushGachaPanel == null || HutaoSubChallengePanel == null ||
            HutaoSubStatsPanel == null || HutaoSubSponsorPanel == null)
        {
            return;
        }

        var targetIndex = _currentHutaoSubTab switch
        {
            "HutaoAccount" => 0,
            "HutaoPull" => 1,
            "HutaoPushGacha" => 2,
            "HutaoChallenge" => 3,
            "HutaoStats" => 4,
            "HutaoSponsor" => 5,
            _ => 0
        };

        var previousIndex = _currentHutaoSubTabIndex;
        _currentHutaoSubTabIndex = targetIndex;

        var subPanels = new UIElement[]
        {
            HutaoSubAccountPanel,
            HutaoSubPullPanel,
            HutaoSubPushGachaPanel,
            HutaoSubChallengePanel,
            HutaoSubStatsPanel,
            HutaoSubSponsorPanel
        };
        var direction = targetIndex == previousIndex ? 0 : (targetIndex > previousIndex ? 1 : -1);
        var animate = _isWindowContentLoaded && HutaoTabPanel?.Visibility == Visibility.Visible;
        SwitchSlidePanel(SlideChannel.HutaoSubTab, subPanels, targetIndex, direction, HutaoSubTabViewport, animate);

        UpdateHutaoFufuRequiredBanner();

        if (_currentHutaoSubTab == "HutaoPull" && _hutaoCloud.IsLoggedIn &&
            !string.Equals(_hutaoEntriesLoadedForEmail, _hutaoCloud.CurrentEmail, StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshHutaoGachaEntriesAsync(silent: true);
        }
        else if (_currentHutaoSubTab == "HutaoPushGacha")
        {
            _ = AutoInspectFufuUigfIfNeededAsync();
        }
        else if (_currentHutaoSubTab == "HutaoChallenge")
        {
            _ = AutoInspectFufuChallengeIfNeededAsync();
        }
        else if (ShouldRefreshHutaoStats())
        {
            _ = RefreshHutaoStatisticsAsync(silent: true);
        }
        else if (_currentHutaoSubTab == "HutaoAccount" && _hutaoCloud.IsLoggedIn &&
                 !string.Equals(_hutaoDevicesLoadedForEmail, _hutaoCloud.CurrentEmail, StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshHutaoDevicesAsync(silent: true);
        }
    }

    private void UpdateHutaoFufuRequiredBanner()
    {
        if (HutaoFufuRequiredBanner == null) return;
        var fufuReady = _cloudSync.IsLoggedIn && !_cloudSync.IsLocked;
        HutaoFufuRequiredBanner.IsOpen = !fufuReady &&
                                         _currentHutaoSubTab != "HutaoAccount" &&
                                         _currentHutaoSubTab != "HutaoStats" &&
                                         _currentHutaoSubTab != "HutaoSponsor";
    }

    private void RefreshHutaoUiState()
    {
        if (_closed || HutaoUnauthPanel == null) return;

        var fufuReady = _cloudSync.IsLoggedIn && !_cloudSync.IsLocked;
        var hutaoLoggedIn = _hutaoCloud.IsLoggedIn;
        var hutaoAuthPanels = new UIElement[] { HutaoUnauthPanel, HutaoLoggedInPanel };
        var targetIndex = hutaoLoggedIn ? 1 : 0;
        var targetPanel = hutaoAuthPanels[targetIndex];
        var direction = hutaoLoggedIn ? 1 : -1;
        var animateHutaoAuth = _isWindowContentLoaded &&
                               HutaoTabPanel?.Visibility == Visibility.Visible &&
                               HutaoSubAccountPanel?.Visibility == Visibility.Visible &&
                               targetPanel.Visibility != Visibility.Visible;
        SwitchSlidePanel(SlideChannel.HutaoAuth, hutaoAuthPanels, targetIndex, direction, HutaoSubTabViewport, animateHutaoAuth);

        if (!hutaoLoggedIn)
        {
            if (string.IsNullOrWhiteSpace(HutaoEmailTextBox.Text) && !string.IsNullOrWhiteSpace(_hutaoCloud.CurrentEmail))
            {
                HutaoEmailTextBox.Text = _hutaoCloud.CurrentEmail;
            }

            _allHutaoGachaEntries.Clear();
            _hutaoGachaEntries.Clear();
            _hutaoExtraFields.Clear();
            _hutaoDevices.Clear();
            _hutaoDevicesLoadedForEmail = null;
            _hutaoEntriesLoadedForEmail = null;
            if (HutaoExtraFieldsRepeater != null)
            {
                HutaoExtraFieldsRepeater.Visibility = Visibility.Collapsed;
            }

            if (HutaoSponsorAccountText != null)
            {
                HutaoSponsorAccountText.Text = "CloudSync_AccountHutaoSignedOut".GetLocalized();
                HutaoSponsorGachaText.Text = "-";
                HutaoSponsorCdnText.Text = "-";
                HutaoSponsorActionBtn.Content = "HutaoSync_SponsorGotoLoginBtn".GetLocalized();
                HutaoSponsorSignInHintText.Visibility = Visibility.Visible;
                HutaoRedeemCodeBox.IsEnabled = false;
                HutaoRedeemButton.IsEnabled = false;
            }

            UpdateHutaoRepeaterEmptyStates();
            if (AccountHutaoStatusText != null && AccountHutaoActionButton != null)
            {
                AccountHutaoStatusText.Text = "CloudSync_AccountHutaoSignedOut".GetLocalized();
                AccountHutaoActionButton.Content = "CloudSync_AccountHutaoSignInBtn".GetLocalized();
            }
        }
        else
        {
            var info = _hutaoCloud.CurrentUserInfo;
            var displayAccount = !string.IsNullOrWhiteSpace(info?.UserName)
                ? info!.UserName!
                : (!string.IsNullOrWhiteSpace(_hutaoCloud.CurrentEmail) ? _hutaoCloud.CurrentEmail : "-");
            var normalizedAccount = !string.IsNullOrWhiteSpace(info?.NormalizedUserName)
                ? info!.NormalizedUserName!
                : displayAccount;

            HutaoCurrentEmailText.Text = displayAccount;
            HutaoNormalizedNameText.Text = normalizedAccount;
            HutaoAccountRoleText.Text = FormatHutaoRoleIdentity(info);
            HutaoCurrentDeviceIdText.Text = !string.IsNullOrWhiteSpace(_hutaoCloud.CurrentDeviceId)
                ? _hutaoCloud.CurrentDeviceId
                : "-";

            if (AccountHutaoStatusText != null && AccountHutaoActionButton != null)
            {
                AccountHutaoStatusText.Text = string.Format("CloudSync_AccountHutaoSignedInFormat".GetLocalized(), displayAccount);
                AccountHutaoActionButton.Content = "CloudSync_AccountHutaoManageBtn".GetLocalized();
            }

            var gachaPlanText = FormatHutaoGachaPlan(info);
            var cdnPlanText = FormatHutaoCdnPlan(info);
            HutaoPlanStatusText.Text = gachaPlanText;
            HutaoCdnStatusText.Text = cdnPlanText;

            UpdateHutaoExtraFields(info);

            if (HutaoSponsorAccountText != null)
            {
                HutaoSponsorAccountText.Text = displayAccount;
                HutaoSponsorGachaText.Text = gachaPlanText;
                HutaoSponsorCdnText.Text = cdnPlanText;
                HutaoSponsorActionBtn.Content = "HutaoSync_RefreshAccountBtn".GetLocalized();
                HutaoSponsorSignInHintText.Visibility = Visibility.Collapsed;
                HutaoRedeemCodeBox.IsEnabled = true;
                HutaoRedeemButton.IsEnabled = !IsBusy;
            }

            if (!string.Equals(_hutaoDevicesLoadedForEmail, _hutaoCloud.CurrentEmail, StringComparison.OrdinalIgnoreCase) &&
                !_hutaoDevicesLoading)
            {
                _ = RefreshHutaoDevicesAsync(silent: true);
            }

            if (_currentHutaoSubTab == "HutaoPull" &&
                !string.Equals(_hutaoEntriesLoadedForEmail, _hutaoCloud.CurrentEmail, StringComparison.OrdinalIgnoreCase) &&
                !_hutaoEntriesLoading)
            {
                _ = RefreshHutaoGachaEntriesAsync(silent: true);
            }
        }

        if (!fufuReady)
        {
            _allFufuUigfUids.Clear();
            _allFufuChallengeItems.Clear();
            _fufuUigfUids.Clear();
            _fufuChallengeItems.Clear();
            _fufuUigfSlots.Clear();
            _fufuChallengeSlots.Clear();
            _lastInspectedUigfSlot = null;
            _lastInspectedChallengeSlot = null;
            SyncHutaoArchivePickers();
        }

        HutaoPullActionButton.IsEnabled = fufuReady && hutaoLoggedIn;
        HutaoPushGachaActionButton.IsEnabled = fufuReady && hutaoLoggedIn;
        HutaoChallengeActionButton.IsEnabled = fufuReady && hutaoLoggedIn;

        UpdateHutaoFufuRequiredBanner();
        UpdateHutaoOtpButton();
        UpdateHutaoRepeaterEmptyStates();
    }

    private async Task RefreshHutaoTabOnSelectAsync()
    {
        ReloadAccountBoundRoles();
        RefreshHutaoUiState();
        if (!_hutaoInitialized)
        {
            await _hutaoCloud.InitializeAsync();
            _hutaoInitialized = true;
            RefreshHutaoUiState();
        }

        if (_hutaoCloud.IsLoggedIn &&
            !string.Equals(_hutaoDevicesLoadedForEmail, _hutaoCloud.CurrentEmail, StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshHutaoDevicesAsync(silent: true);
        }

        if (_currentHutaoSubTab == "HutaoPull" && _hutaoCloud.IsLoggedIn &&
            !string.Equals(_hutaoEntriesLoadedForEmail, _hutaoCloud.CurrentEmail, StringComparison.OrdinalIgnoreCase))
        {
            _ = RefreshHutaoGachaEntriesAsync(silent: true);
        }

        if (ShouldRefreshHutaoStats())
        {
            _ = RefreshHutaoStatisticsAsync(silent: true);
        }

        if (_cloudSync.IsLoggedIn && !_cloudSync.IsLocked)
        {
            await RefreshFufuArchivePickersForHutaoAsync(silent: true);
            if (_currentHutaoSubTab == "HutaoPushGacha")
            {
                await AutoInspectFufuUigfIfNeededAsync();
            }
            else if (_currentHutaoSubTab == "HutaoChallenge")
            {
                await AutoInspectFufuChallengeIfNeededAsync();
            }
        }
    }

    private void UpdateHutaoRepeaterEmptyStates()
    {
        HutaoGachaEntriesEmptyText.Text = !string.IsNullOrWhiteSpace(_selectedRoleUid) && _allHutaoGachaEntries.Count > 0
            ? string.Format("HutaoSync_PullFilteredEmptyHintFormat".GetLocalized(), _selectedRoleUid)
            : "HutaoSync_PullEmptyHint".GetLocalized();
        HutaoGachaEntriesEmptyText.Visibility = _hutaoGachaEntries.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        FufuUigfUidsEmptyText.Text = !string.IsNullOrWhiteSpace(_selectedRoleUid) && _allFufuUigfUids.Count > 0
            ? string.Format("HutaoSync_PushGachaFilteredEmptyHintFormat".GetLocalized(), _selectedRoleUid)
            : "HutaoSync_PushGachaEmptyHint".GetLocalized();
        FufuUigfUidsEmptyText.Visibility = _fufuUigfUids.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        FufuChallengeItemsEmptyText.Text = !string.IsNullOrWhiteSpace(_selectedRoleUid) && _allFufuChallengeItems.Count > 0
            ? string.Format("HutaoSync_ChallengeFilteredEmptyHintFormat".GetLocalized(), _selectedRoleUid)
            : "HutaoSync_ChallengeInspectEmptyHint".GetLocalized();
        FufuChallengeItemsEmptyText.Visibility = _fufuChallengeItems.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (HutaoDevicesEmptyText != null)
        {
            HutaoDevicesEmptyText.Visibility = _hutaoDevices.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void SyncHutaoArchivePickers()
    {
        _suppressHutaoSlotComboSync = true;
        try
        {
            var prevPullExisting = HutaoPullExistingSlotCombo.SelectedItem as string;
            var prevUigf = FufuUigfArchiveComboBox.SelectedItem as string;
            var prevChallenge = FufuChallengeArchiveComboBox.SelectedItem as string;

            HutaoPullExistingSlotCombo.ItemsSource = _fufuUigfSlots.ToList();
            FufuUigfArchiveComboBox.ItemsSource = _fufuUigfSlots.ToList();
            FufuChallengeArchiveComboBox.ItemsSource = _fufuChallengeSlots.ToList();

            if (!string.IsNullOrEmpty(prevPullExisting) && _fufuUigfSlots.Contains(prevPullExisting))
            {
                HutaoPullExistingSlotCombo.SelectedItem = prevPullExisting;
            }
            else if (_fufuUigfSlots.Count > 0)
            {
                var preferred = FindPreferredSlotForRole(_fufuUigfSlots, _selectedRoleUid);
                HutaoPullExistingSlotCombo.SelectedItem = preferred ?? _fufuUigfSlots[0];
            }

            if (!string.IsNullOrEmpty(prevUigf) && _fufuUigfSlots.Contains(prevUigf))
            {
                FufuUigfArchiveComboBox.SelectedItem = prevUigf;
            }
            else if (_fufuUigfSlots.Count > 0)
            {
                var preferred = FindPreferredSlotForRole(_fufuUigfSlots, _selectedRoleUid);
                FufuUigfArchiveComboBox.SelectedItem = preferred ?? _fufuUigfSlots[0];
            }

            if (!string.IsNullOrEmpty(prevChallenge) && _fufuChallengeSlots.Contains(prevChallenge))
            {
                FufuChallengeArchiveComboBox.SelectedItem = prevChallenge;
            }
            else if (_fufuChallengeSlots.Count > 0)
            {
                var preferred = FindPreferredSlotForRole(_fufuChallengeSlots, _selectedRoleUid);
                FufuChallengeArchiveComboBox.SelectedItem = preferred ?? _fufuChallengeSlots[0];
            }
        }
        finally
        {
            _suppressHutaoSlotComboSync = false;
        }

        UpdatePullSaveModeUi();
        ApplyDefaultNewPullSlotName();

        if (_currentHutaoSubTab == "HutaoPushGacha")
        {
            _ = AutoInspectFufuUigfIfNeededAsync();
        }
        else if (_currentHutaoSubTab == "HutaoChallenge")
        {
            _ = AutoInspectFufuChallengeIfNeededAsync();
        }
    }

    private async Task RefreshFufuArchivePickersForHutaoAsync(bool silent)
    {
        if (!_cloudSync.IsLoggedIn || _cloudSync.IsLocked) return;

        var res = await _cloudSync.ListArchivesAsync("all");
        if (_closed) return;

        if (!res.Ok || res.Data == null)
        {
            if (!silent)
            {
                ShowMessage(InfoBarSeverity.Error, "CloudSync_RefreshFailedTitle".GetLocalized(),
                    _cloudSync.FormatFriendlyError(res.Code, res.Message, res.HttpStatusCode));
            }
            return;
        }

        _fufuUigfSlots.Clear();
        _fufuChallengeSlots.Clear();

        foreach (var item in res.Data.Archives)
        {
            if (item.IsUigf) _fufuUigfSlots.Add(item.Slot);
            else if (item.IsChallenge) _fufuChallengeSlots.Add(item.Slot);
        }

        _lastInspectedUigfSlot = null;
        _lastInspectedChallengeSlot = null;
        SyncHutaoArchivePickers();
    }

    private void OnHutaoGotoFufuAccountClick(object sender, RoutedEventArgs e)
    {
        SelectTab("Account");
    }

    private void OnAccountGotoHutaoClick(object sender, RoutedEventArgs e)
    {
        SelectHutaoSubTab("HutaoAccount");
        SelectTab("Hutao");
    }

    private bool EnsureFufuReadyForUiAction()
    {
        if (_cloudSync.IsLocked)
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "CloudSync_ErrLocked".GetLocalized());
            return false;
        }

        if (!_cloudSync.IsLoggedIn)
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrFufuLoginRequired".GetLocalized());
            return false;
        }

        return true;
    }

    private bool EnsureHutaoLoggedInForUiAction()
    {
        if (_hutaoCloud.IsLoggedIn) return true;

        ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
            "HutaoSync_ErrHutaoLoginRequired".GetLocalized());
        return false;
    }
}
