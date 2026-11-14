using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
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
    private readonly List<HutaoGachaEntrySelectionItem> _allHutaoGachaEntries = new();
    private readonly List<FufuCloudUigfUidItem> _allFufuUigfUids = new();
    private readonly List<FufuCloudChallengeItem> _allFufuChallengeItems = new();
    private readonly List<AccountBoundRoleOption> _accountRoles = new();
    private readonly List<string> _fufuUigfSlots = new();
    private readonly List<string> _fufuChallengeSlots = new();
    private DispatcherQueueTimer? _hutaoOtpTimer;
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

        HutaoSubAccountPanel.Visibility = _currentHutaoSubTab == "HutaoAccount" ? Visibility.Visible : Visibility.Collapsed;
        HutaoSubPullPanel.Visibility = _currentHutaoSubTab == "HutaoPull" ? Visibility.Visible : Visibility.Collapsed;
        HutaoSubPushGachaPanel.Visibility = _currentHutaoSubTab == "HutaoPushGacha" ? Visibility.Visible : Visibility.Collapsed;
        HutaoSubChallengePanel.Visibility = _currentHutaoSubTab == "HutaoChallenge" ? Visibility.Visible : Visibility.Collapsed;
        HutaoSubStatsPanel.Visibility = _currentHutaoSubTab == "HutaoStats" ? Visibility.Visible : Visibility.Collapsed;
        HutaoSubSponsorPanel.Visibility = _currentHutaoSubTab == "HutaoSponsor" ? Visibility.Visible : Visibility.Collapsed;

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
        else if (_currentHutaoSubTab == "HutaoStats" && !_hutaoStatsLoaded && !_hutaoStatsLoading)
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
        HutaoUnauthPanel.Visibility = hutaoLoggedIn ? Visibility.Collapsed : Visibility.Visible;
        HutaoLoggedInPanel.Visibility = hutaoLoggedIn ? Visibility.Visible : Visibility.Collapsed;

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

        if (_currentHutaoSubTab == "HutaoStats" && !_hutaoStatsLoaded && !_hutaoStatsLoading)
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

    private void OnHutaoAuthModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HutaoAuthModeSelector == null || HutaoPasswordInput == null || HutaoConfirmPasswordInput == null ||
            HutaoOtpRow == null || HutaoAuthSubmitButton == null)
        {
            return;
        }

        HutaoPasswordInput.MaskPassword();
        HutaoConfirmPasswordInput.MaskPassword();

        var mode = HutaoAuthModeSelector.SelectedIndex;
        if (mode == 0)
        {
            HutaoConfirmPasswordInput.Visibility = Visibility.Collapsed;
            HutaoOtpRow.Visibility = Visibility.Collapsed;
            HutaoAuthSubmitButton.Content = "HutaoSync_SubmitLogin".GetLocalized();
        }
        else if (mode == 1)
        {
            HutaoConfirmPasswordInput.Visibility = Visibility.Visible;
            HutaoOtpRow.Visibility = Visibility.Visible;
            HutaoAuthSubmitButton.Content = "HutaoSync_SubmitRegister".GetLocalized();
        }
        else
        {
            HutaoConfirmPasswordInput.Visibility = Visibility.Visible;
            HutaoOtpRow.Visibility = Visibility.Visible;
            HutaoAuthSubmitButton.Content = "HutaoSync_SubmitReset".GetLocalized();
        }
    }

    private void UpdateHutaoOtpButton()
    {
        if (_closed || HutaoSendOtpButton == null) return;

        var until = _hutaoCloud.VerifyCodeCooldownUntil;
        var remaining = until.HasValue
            ? (int)Math.Ceiling((until.Value - DateTimeOffset.UtcNow).TotalSeconds)
            : 0;

        if (remaining > 0)
        {
            HutaoSendOtpButton.IsEnabled = false;
            HutaoSendOtpButton.Content = string.Format("HutaoSync_SendOtpCooldownFormat".GetLocalized(), remaining);
            if (_hutaoOtpTimer is { IsRunning: false }) _hutaoOtpTimer.Start();
        }
        else
        {
            HutaoSendOtpButton.IsEnabled = !IsBusy;
            HutaoSendOtpButton.Content = "HutaoSync_SendOtpBtn".GetLocalized();
            if (_hutaoOtpTimer is { IsRunning: true }) _hutaoOtpTimer.Stop();
        }
    }

    private async void OnHutaoSendOtpClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        var email = HutaoEmailTextBox.Text.Trim();
        var isReset = HutaoAuthModeSelector.SelectedIndex == 2;

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.SendVerifyCodeAsync(email, isReset);
            if (res.Ok)
            {
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(), res.Message);
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
            }
        }
        finally
        {
            SetBusy(false);
            UpdateHutaoOtpButton();
        }
    }

    private async void OnHutaoAuthSubmitClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;

        var email = HutaoEmailTextBox.Text.Trim();
        var password = HutaoPasswordInput.Password;
        var confirm = HutaoConfirmPasswordInput.Password;
        var otp = HutaoOtpTextBox.Text.Trim();
        var mode = HutaoAuthModeSelector.SelectedIndex;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrEmpty(password))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrEnterAccountPassword".GetLocalized());
            return;
        }

        if (mode != 0 && password != confirm)
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrPasswordMismatch".GetLocalized());
            return;
        }

        if (mode != 0 && string.IsNullOrWhiteSpace(otp))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrEnterCompleteAuth".GetLocalized());
            return;
        }

        HutaoPasswordInput.MaskPassword();
        HutaoConfirmPasswordInput.MaskPassword();

        SetBusy(true);
        try
        {
            var res = mode switch
            {
                1 => await _hutaoCloud.RegisterAsync(email, password, otp),
                2 => await _hutaoCloud.ResetPasswordAsync(email, password, otp),
                _ => await _hutaoCloud.LoginAsync(email, password)
            };

            if (res.Ok)
            {
                HutaoPasswordInput.Password = string.Empty;
                HutaoConfirmPasswordInput.Password = string.Empty;
                HutaoOtpTextBox.Text = string.Empty;
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(), res.Message);
                RefreshHutaoUiState();
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoRefreshUserInfoClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.RefreshUserInfoAsync();
            if (res.Ok)
            {
                await RefreshHutaoDevicesAsync(silent: true);
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                    "HutaoSync_MsgAccountRefreshed".GetLocalized());
            }
            else
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
            }
        }
        finally
        {
            SetBusy(false);
            RefreshHutaoUiState();
        }
    }

    private async void OnHutaoLogoutClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            await _hutaoCloud.LogoutAsync(revokeRemote: true);
            ShowMessage(InfoBarSeverity.Informational, "HutaoSync_SuccessTitle".GetLocalized(),
                "HutaoSync_MsgLogoutSuccess".GetLocalized());
        }
        finally
        {
            SetBusy(false);
            RefreshHutaoUiState();
        }
    }

    private void OnHutaoPullSaveModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoSaveModeSync) return;
        UpdatePullSaveModeUi();
    }

    private void OnHutaoPullExistingSlotChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void OnHutaoToggleSelectAllPullClick(object sender, RoutedEventArgs e)
    {
        if (_hutaoGachaEntries.Count == 0) return;
        var selectAll = _hutaoGachaEntries.Any(x => !x.IsSelected);
        foreach (var entry in _hutaoGachaEntries)
        {
            entry.IsSelected = selectAll;
        }
    }

    private async void OnHutaoLoadEntriesClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        if (!EnsureHutaoLoggedInForUiAction()) return;

        SetBusy(true);
        try
        {
            await RefreshHutaoGachaEntriesAsync(silent: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<bool> RefreshHutaoGachaEntriesAsync(bool silent)
    {
        if (_closed || !_hutaoCloud.IsLoggedIn || _hutaoEntriesLoading)
        {
            return false;
        }

        _hutaoEntriesLoading = true;
        try
        {
            var res = await _hutaoCloud.GetGachaEntriesAsync();
            if (_closed) return false;

            if (!res.Ok)
            {
                if (!silent)
                {
                    ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                }
                return false;
            }

            _hutaoEntriesLoadedForEmail = _hutaoCloud.CurrentEmail;
            _allHutaoGachaEntries.Clear();
            foreach (var entry in (res.Data ?? new List<HutaoGachaEntry>()).OrderBy(x => x.Uid, StringComparer.Ordinal))
            {
                _allHutaoGachaEntries.Add(new HutaoGachaEntrySelectionItem
                {
                    Uid = entry.Uid,
                    ItemCount = entry.ItemCount,
                    Excluded = entry.Excluded,
                    IsSelected = true,
                    RoleTitle = ResolveUidDisplayTitle(entry.Uid)
                });
            }

            ApplyHutaoGachaRoleFilter();
            if (!silent)
            {
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                    string.Format("HutaoSync_MsgEntriesLoadedFormat".GetLocalized(), _allHutaoGachaEntries.Count));
            }
            return true;
        }
        finally
        {
            _hutaoEntriesLoading = false;
        }
    }

    private async void OnHutaoDeleteGachaUidClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || sender is not FrameworkElement { Tag: string uid } || string.IsNullOrWhiteSpace(uid)) return;

        var dialog = new ContentDialog
        {
            Title = "HutaoSync_ConfirmDeleteUidTitle".GetLocalized(),
            Content = string.Format("HutaoSync_ConfirmDeleteUidMsgFormat".GetLocalized(), uid),
            PrimaryButtonText = "CloudSync_DeleteConfirmBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.DeleteGachaUidAsync(uid);
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            _allHutaoGachaEntries.RemoveAll(x => string.Equals(x.Uid, uid, StringComparison.Ordinal));
            var target = _hutaoGachaEntries.FirstOrDefault(x => string.Equals(x.Uid, uid, StringComparison.Ordinal));
            if (target != null)
            {
                _hutaoGachaEntries.Remove(target);
            }

            UpdateHutaoRepeaterEmptyStates();
            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                string.Format("HutaoSync_MsgDeleteUidSuccessFormat".GetLocalized(), uid, res.Data));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoPullToFufuClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        if (!EnsureFufuReadyForUiAction() || !EnsureHutaoLoggedInForUiAction()) return;

        if (_allHutaoGachaEntries.Count == 0)
        {
            SetBusy(true);
            try
            {
                var loaded = await RefreshHutaoGachaEntriesAsync(silent: false);
                if (!loaded) return;
            }
            finally
            {
                SetBusy(false);
            }

            if (_allHutaoGachaEntries.Count == 0)
            {
                ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                    "HutaoSync_ErrNoHutaoGachaData".GetLocalized());
                return;
            }
        }

        var selectedUids = _hutaoGachaEntries
            .Where(x => x.IsSelected && !string.IsNullOrWhiteSpace(x.Uid))
            .Select(x => x.Uid)
            .ToList();

        if (selectedUids.Count == 0)
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrSelectUidFirst".GetLocalized());
            return;
        }

        var isExistingMode = (HutaoPullSaveModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "existing";
        string targetSlot;
        bool mergeExisting;
        bool overwriteExisting;

        if (isExistingMode)
        {
            if (HutaoPullExistingSlotCombo.SelectedItem is not string existingSlot || string.IsNullOrWhiteSpace(existingSlot))
            {
                ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                    "HutaoSync_ErrSelectFufuArchive".GetLocalized());
                return;
            }

            targetSlot = existingSlot.Trim();
            mergeExisting = HutaoMergeExistingCheckBox.IsChecked == true;
            overwriteExisting = true;
        }
        else
        {
            targetSlot = HutaoPullTargetSlotBox.Text.Trim();
            if (!_cloudSync.IsValidSlotName(targetSlot))
            {
                ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                    "CloudSync_ErrInvalidSlot".GetLocalized());
                HutaoPullTargetSlotBox.Focus(FocusState.Programmatic);
                return;
            }

            if (_fufuUigfSlots.Contains(targetSlot, StringComparer.OrdinalIgnoreCase))
            {
                var conflictDialog = new ContentDialog
                {
                    Title = "HutaoSync_PullConflictTitle".GetLocalized(),
                    Content = string.Format("HutaoSync_PullConflictMsgFormat".GetLocalized(), targetSlot),
                    PrimaryButtonText = "HutaoSync_PullConflictMergeBtn".GetLocalized(),
                    SecondaryButtonText = "HutaoSync_PullConflictOverwriteBtn".GetLocalized(),
                    CloseButtonText = "HutaoSync_PullConflictCancelBtn".GetLocalized(),
                    DefaultButton = ContentDialogButton.Primary,
                    XamlRoot = RootGrid.XamlRoot
                };

                var choice = await conflictDialog.ShowAsync();
                if (choice == ContentDialogResult.Primary)
                {
                    mergeExisting = true;
                    overwriteExisting = true;
                }
                else if (choice == ContentDialogResult.Secondary)
                {
                    mergeExisting = false;
                    overwriteExisting = true;
                }
                else
                {
                    return;
                }
            }
            else
            {
                mergeExisting = false;
                overwriteExisting = false;
            }
        }

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.PullGachaToFufuCloudAsync(
                selectedUids,
                targetSlot,
                mergeExisting,
                overwriteExisting);
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(), res.Message);
            await RefreshFufuArchivePickersForHutaoAsync(silent: true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoRefreshFufuArchivesClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        if (!EnsureFufuReadyForUiAction()) return;
        SetBusy(true);
        try
        {
            await RefreshFufuArchivePickersForHutaoAsync(silent: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoPushUigfArchiveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoSlotComboSync) return;
        if (FufuUigfArchiveComboBox.SelectedItem is string slot && !string.IsNullOrWhiteSpace(slot))
        {
            await InspectFufuUigfSlotAsync(slot, silent: true);
        }
    }

    private async void OnHutaoInspectFufuUigfClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        if (!EnsureFufuReadyForUiAction()) return;
        if (FufuUigfArchiveComboBox.SelectedItem is not string slot || string.IsNullOrWhiteSpace(slot))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrSelectFufuArchive".GetLocalized());
            return;
        }

        SetBusy(true);
        try
        {
            await InspectFufuUigfSlotAsync(slot, silent: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoPushGachaToHutaoClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        if (!EnsureFufuReadyForUiAction() || !EnsureHutaoLoggedInForUiAction()) return;

        if (FufuUigfArchiveComboBox.SelectedItem is not string slot || string.IsNullOrWhiteSpace(slot))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrSelectFufuArchive".GetLocalized());
            return;
        }

        if (_allFufuUigfUids.Count == 0 || !string.Equals(_lastInspectedUigfSlot, slot, StringComparison.Ordinal))
        {
            SetBusy(true);
            try
            {
                await InspectFufuUigfSlotAsync(slot, silent: false);
            }
            finally
            {
                SetBusy(false);
            }
        }

        var selectedUids = _fufuUigfUids
            .Where(x => x.IsSelected && !string.IsNullOrWhiteSpace(x.Uid))
            .Select(x => x.Uid)
            .ToList();

        if (selectedUids.Count == 0)
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrSelectUidFirst".GetLocalized());
            return;
        }

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.PushFufuCloudGachaToHutaoAsync(slot, selectedUids);
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(), res.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoChallengeArchiveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoSlotComboSync) return;
        if (FufuChallengeArchiveComboBox.SelectedItem is string slot && !string.IsNullOrWhiteSpace(slot))
        {
            await InspectFufuChallengeSlotAsync(slot, silent: true);
        }
    }

    private async void OnHutaoInspectFufuChallengeClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        if (!EnsureFufuReadyForUiAction()) return;
        if (FufuChallengeArchiveComboBox.SelectedItem is not string slot || string.IsNullOrWhiteSpace(slot))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrSelectFufuArchive".GetLocalized());
            return;
        }

        SetBusy(true);
        try
        {
            await InspectFufuChallengeSlotAsync(slot, silent: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoPushChallengeToHutaoClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        if (!EnsureFufuReadyForUiAction() || !EnsureHutaoLoggedInForUiAction()) return;

        if (FufuChallengeArchiveComboBox.SelectedItem is not string slot || string.IsNullOrWhiteSpace(slot))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrSelectFufuArchive".GetLocalized());
            return;
        }

        if (_allFufuChallengeItems.Count == 0 || !string.Equals(_lastInspectedChallengeSlot, slot, StringComparison.Ordinal))
        {
            SetBusy(true);
            try
            {
                await InspectFufuChallengeSlotAsync(slot, silent: false);
            }
            finally
            {
                SetBusy(false);
            }
        }

        var selectedItems = _fufuChallengeItems.Where(x => x.IsSelected).ToList();
        if (selectedItems.Count == 0)
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrSelectChallengeFirst".GetLocalized());
            return;
        }

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.PushFufuCloudChallengeToHutaoAsync(slot, selectedItems);
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(), res.Message);
            RefreshHutaoUiState();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task AutoInspectFufuUigfIfNeededAsync()
    {
        if (_closed || !_cloudSync.IsLoggedIn || _cloudSync.IsLocked) return;
        if (FufuUigfArchiveComboBox.SelectedItem is not string slot || string.IsNullOrWhiteSpace(slot)) return;
        if (string.Equals(slot, _lastInspectedUigfSlot, StringComparison.Ordinal) && _allFufuUigfUids.Count > 0) return;
        await InspectFufuUigfSlotAsync(slot, silent: true);
    }

    private async Task InspectFufuUigfSlotAsync(string slot, bool silent)
    {
        var version = ++_uigfInspectVersion;
        if (!string.Equals(_lastInspectedUigfSlot, slot, StringComparison.Ordinal))
        {
            _allFufuUigfUids.Clear();
            ApplyFufuUigfRoleFilter();
        }

        var res = await _hutaoCloud.InspectFufuCloudUigfAsync(slot);
        if (_closed || version != _uigfInspectVersion) return;

        var currentSlot = FufuUigfArchiveComboBox.SelectedItem as string;
        if (!string.Equals(currentSlot, slot, StringComparison.Ordinal)) return;

        if (!res.Ok)
        {
            if (!silent)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
            }
            return;
        }

        _lastInspectedUigfSlot = slot;
        _allFufuUigfUids.Clear();
        foreach (var item in res.Data ?? new List<FufuCloudUigfUidItem>())
        {
            item.RoleTitle = ResolveUidDisplayTitle(item.Uid);
            item.IsSelected = true;
            _allFufuUigfUids.Add(item);
        }

        ApplyFufuUigfRoleFilter();
        if (!silent)
        {
            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                string.Format("HutaoSync_MsgFufuUigfLoadedFormat".GetLocalized(), _allFufuUigfUids.Count));
        }
    }

    private async Task AutoInspectFufuChallengeIfNeededAsync()
    {
        if (_closed || !_cloudSync.IsLoggedIn || _cloudSync.IsLocked) return;
        if (FufuChallengeArchiveComboBox.SelectedItem is not string slot || string.IsNullOrWhiteSpace(slot)) return;
        if (string.Equals(slot, _lastInspectedChallengeSlot, StringComparison.Ordinal) && _allFufuChallengeItems.Count > 0) return;
        await InspectFufuChallengeSlotAsync(slot, silent: true);
    }

    private async Task InspectFufuChallengeSlotAsync(string slot, bool silent)
    {
        var version = ++_challengeInspectVersion;
        if (!string.Equals(_lastInspectedChallengeSlot, slot, StringComparison.Ordinal))
        {
            _allFufuChallengeItems.Clear();
            ApplyFufuChallengeRoleFilter();
        }

        var res = await _hutaoCloud.InspectFufuCloudChallengeAsync(slot);
        if (_closed || version != _challengeInspectVersion) return;

        var currentSlot = FufuChallengeArchiveComboBox.SelectedItem as string;
        if (!string.Equals(currentSlot, slot, StringComparison.Ordinal)) return;

        if (!res.Ok)
        {
            if (!silent)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
            }
            return;
        }

        _lastInspectedChallengeSlot = slot;
        _allFufuChallengeItems.Clear();
        foreach (var item in res.Data ?? new List<FufuCloudChallengeItem>())
        {
            item.RoleDisplayName = ResolveUidDisplayTitle(item.Uid);
            _allFufuChallengeItems.Add(item);
        }

        ApplyFufuChallengeRoleFilter();
        if (!silent)
        {
            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                string.Format("HutaoSync_MsgFufuChallengeLoadedFormat".GetLocalized(), _allFufuChallengeItems.Count));
        }
    }

    private async void OnHutaoRefreshDevicesClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !EnsureHutaoLoggedInForUiAction()) return;
        SetBusy(true);
        try
        {
            await RefreshHutaoDevicesAsync(silent: false);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RefreshHutaoDevicesAsync(bool silent)
    {
        if (!_hutaoCloud.IsLoggedIn || _hutaoDevicesLoading) return;
        _hutaoDevicesLoading = true;
        try
        {
            var res = await _hutaoCloud.GetLoggedInDevicesAsync();
            if (_closed) return;
            if (!res.Ok)
            {
                if (!silent)
                {
                    ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                }
                return;
            }

            _hutaoDevicesLoadedForEmail = _hutaoCloud.CurrentEmail;
            _hutaoDevices.Clear();

            var unknownName = "HutaoSync_DeviceUnknownName".GetLocalized();
            var currentBadge = "HutaoSync_DeviceCurrentBadge".GetLocalized();
            var currentId = _hutaoCloud.CurrentDeviceId;

            foreach (var dev in res.Data ?? new List<HutaoLoggedInDevice>())
            {
                var devId = dev.EffectiveDeviceId;
                var devName = !string.IsNullOrWhiteSpace(dev.EffectiveDeviceName) ? dev.EffectiveDeviceName : unknownName;
                var os = dev.EffectiveOs;
                var isCurrent = !string.IsNullOrWhiteSpace(devId) &&
                                string.Equals(devId, currentId, StringComparison.OrdinalIgnoreCase);

                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(os))
                {
                    parts.Add(os);
                }

                var formattedTime = FormatHutaoDateString(dev.EffectiveLoginTime);
                if (!string.IsNullOrWhiteSpace(formattedTime))
                {
                    parts.Add(string.Format("HutaoSync_DeviceLoginTimeFormat".GetLocalized(), formattedTime));
                }

                if (!string.IsNullOrWhiteSpace(devId))
                {
                    parts.Add(string.Format("HutaoSync_DeviceIdFormat".GetLocalized(), devId));
                }

                _hutaoDevices.Add(new HutaoDeviceListItem
                {
                    DeviceId = devId,
                    TitleText = devName,
                    SubtitleText = string.Join(" · ", parts),
                    BadgeText = isCurrent ? currentBadge : string.Empty,
                    IsCurrentDevice = isCurrent
                });
            }

            UpdateHutaoRepeaterEmptyStates();
        }
        finally
        {
            _hutaoDevicesLoading = false;
        }
    }

    private async void OnHutaoRevokeDeviceClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !EnsureHutaoLoggedInForUiAction()) return;
        if (sender is not FrameworkElement { Tag: string deviceId } || string.IsNullOrWhiteSpace(deviceId)) return;

        var matched = _hutaoDevices.FirstOrDefault(x => string.Equals(x.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        var displayName = matched?.TitleText ?? deviceId;

        var dialog = new ContentDialog
        {
            Title = "HutaoSync_ConfirmRevokeDeviceTitle".GetLocalized(),
            Content = string.Format("HutaoSync_ConfirmRevokeDeviceMsgFormat".GetLocalized(), displayName),
            PrimaryButtonText = "HutaoSync_RevokeDeviceBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.RevokeDeviceAsync(deviceId);
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                "HutaoSync_MsgDeviceRevoked".GetLocalized());

            if (_hutaoCloud.IsLoggedIn)
            {
                await RefreshHutaoDevicesAsync(silent: true);
            }
            RefreshHutaoUiState();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OnHutaoRevokeAllDevicesClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !EnsureHutaoLoggedInForUiAction()) return;

        var dialog = new ContentDialog
        {
            Title = "HutaoSync_ConfirmRevokeAllDevicesTitle".GetLocalized(),
            Content = "HutaoSync_ConfirmRevokeAllDevicesMsg".GetLocalized(),
            PrimaryButtonText = "HutaoSync_RevokeAllDevicesBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        SetBusy(true);
        try
        {
            var res = await _hutaoCloud.RevokeAllDevicesAsync();
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            ShowMessage(InfoBarSeverity.Informational, "HutaoSync_SuccessTitle".GetLocalized(),
                "HutaoSync_MsgAllDevicesRevoked".GetLocalized());
            RefreshHutaoUiState();
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnHutaoSponsorPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        if (_hutaoCloud.IsLoggedIn)
        {
            OnHutaoRefreshUserInfoClick(sender, e);
            return;
        }

        SelectHutaoSubTab("HutaoAccount");
    }

    private void OnHutaoOpenSponsorPageClick(object sender, RoutedEventArgs e)
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri(ApiEndpoints.HutaoSponsorUrl));
    }

    private async void OnHutaoRedeemCodeClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy || !EnsureHutaoLoggedInForUiAction()) return;
        var code = HutaoRedeemCodeBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrRedeemEmpty".GetLocalized());
            HutaoRedeemCodeBox.Focus(FocusState.Programmatic);
            return;
        }

        SetBusy(true);
        HutaoRedeemButton.IsEnabled = false;
        try
        {
            var res = await _hutaoCloud.RedeemCodeAsync(code);
            if (!res.Ok)
            {
                ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                return;
            }

            HutaoRedeemCodeBox.Text = string.Empty;
            ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(), res.Message);
            RefreshHutaoUiState();
        }
        finally
        {
            SetBusy(false);
            if (!_closed)
            {
                HutaoRedeemButton.IsEnabled = _hutaoCloud.IsLoggedIn;
            }
        }
    }

    private void ReloadAccountBoundRoles()
    {
        _accountRoles.Clear();
        _accountRoles.Add(new AccountBoundRoleOption
        {
            IsAllRolesOption = true,
            Uid = string.Empty,
            DisplayText = "HutaoSync_AllRolesOption".GetLocalized(),
            ShortDisplayText = "HutaoSync_AllRolesOption".GetLocalized()
        });

        if (_accountManager != null)
        {
            var activeAcc = _accountManager.GetActiveAccountEntry();
            var activeRole = activeAcc != null ? GameRoleSelection.Current(activeAcc) : null;
            var activeUid = activeRole?.game_uid ?? string.Empty;

            var orderedAccounts = _accountManager.GetAllAccounts()
                .OrderByDescending(a => activeAcc != null && string.Equals(a.Id, activeAcc.Id, StringComparison.Ordinal))
                .ToList();

            var seenUids = new HashSet<string>(StringComparer.Ordinal);
            var activeSuffix = "HutaoSync_ActiveRoleSuffix".GetLocalized();
            var roleEntries = new List<AccountBoundRoleOption>();

            foreach (var acc in orderedAccounts)
            {
                var roles = GameRoleSelection.Filter(acc.GameRoles ?? [], acc.ServerType);
                foreach (var role in roles)
                {
                    if (string.IsNullOrWhiteSpace(role.game_uid) || !seenUids.Add(role.game_uid))
                    {
                        continue;
                    }

                    var isActive = !string.IsNullOrWhiteSpace(activeUid) &&
                                   string.Equals(role.game_uid, activeUid, StringComparison.Ordinal);
                    var shortText = GameRoleDisplay.BoundRole(role);
                    var displayText = isActive ? $"{shortText} ({activeSuffix})" : shortText;

                    roleEntries.Add(new AccountBoundRoleOption
                    {
                        IsAllRolesOption = false,
                        Uid = role.game_uid,
                        Nickname = role.nickname,
                        ServerName = GameRoleDisplay.ServerName(role.region, role.region_name),
                        Level = role.level,
                        IsActiveRole = isActive,
                        ShortDisplayText = shortText,
                        DisplayText = displayText
                    });
                }
            }

            foreach (var opt in roleEntries.OrderByDescending(r => r.IsActiveRole))
            {
                _accountRoles.Add(opt);
            }
        }

        if (!string.IsNullOrEmpty(_selectedRoleUid) &&
            !_accountRoles.Any(r => string.Equals(r.Uid, _selectedRoleUid, StringComparison.Ordinal)))
        {
            _selectedRoleUid = string.Empty;
        }

        SyncHutaoRoleFilterCombos();
    }

    private void SyncHutaoRoleFilterCombos()
    {
        if (HutaoPullRoleFilterCombo == null || HutaoPushRoleFilterCombo == null ||
            HutaoChallengeRoleFilterCombo == null || HutaoStatsRoleFilterCombo == null)
        {
            return;
        }

        _suppressHutaoRoleSync = true;
        try
        {
            var items = _accountRoles.ToList();
            var targetIdx = items.FindIndex(r => string.Equals(r.Uid, _selectedRoleUid, StringComparison.Ordinal));
            if (targetIdx < 0) targetIdx = 0;

            HutaoPullRoleFilterCombo.ItemsSource = items;
            HutaoPullRoleFilterCombo.SelectedIndex = targetIdx;

            HutaoPushRoleFilterCombo.ItemsSource = items;
            HutaoPushRoleFilterCombo.SelectedIndex = targetIdx;

            HutaoChallengeRoleFilterCombo.ItemsSource = items;
            HutaoChallengeRoleFilterCombo.SelectedIndex = targetIdx;

            HutaoStatsRoleFilterCombo.ItemsSource = items;
            HutaoStatsRoleFilterCombo.SelectedIndex = targetIdx;
        }
        finally
        {
            _suppressHutaoRoleSync = false;
        }
    }

    private async void OnHutaoRefreshAccountRolesClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        SetBusy(true);
        try
        {
            if (_gameRoleService != null)
            {
                await _gameRoleService.RefreshAsync();
            }

            ReloadAccountBoundRoles();
            RefreshHutaoRoleLabels();
            ApplyAllHutaoRoleFilters();

            if (_currentHutaoSubTab == "HutaoStats")
            {
                await RefreshHutaoStatisticsAsync(silent: true);
            }

            var roleCount = Math.Max(0, _accountRoles.Count - 1);
            if (roleCount == 0)
            {
                ShowMessage(InfoBarSeverity.Informational, "HutaoSync_HintTitle".GetLocalized(),
                    "HutaoSync_NoBoundRolesHint".GetLocalized());
            }
            else
            {
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                    string.Format("HutaoSync_MsgRolesRefreshedFormat".GetLocalized(), roleCount));
            }
        }
        catch
        {
            ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(),
                "HutaoSync_ErrGeneric".GetLocalized());
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnHutaoPullRoleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoRoleSync) return;
        var opt = HutaoPullRoleFilterCombo.SelectedItem as AccountBoundRoleOption;
        _selectedRoleUid = opt?.Uid ?? string.Empty;
        SyncHutaoRoleFilterCombos();
        ApplyAllHutaoRoleFilters();

        if (!string.IsNullOrWhiteSpace(_selectedRoleUid))
        {
            var preferredSlot = FindPreferredSlotForRole(_fufuUigfSlots, _selectedRoleUid);
            if (!string.IsNullOrWhiteSpace(preferredSlot))
            {
                HutaoPullExistingSlotCombo.SelectedItem = preferredSlot;
            }
        }

        ApplyDefaultNewPullSlotName();
    }

    private void OnHutaoPushRoleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoRoleSync) return;
        var opt = HutaoPushRoleFilterCombo.SelectedItem as AccountBoundRoleOption;
        _selectedRoleUid = opt?.Uid ?? string.Empty;
        SyncHutaoRoleFilterCombos();

        if (!string.IsNullOrWhiteSpace(_selectedRoleUid))
        {
            var preferredSlot = FindPreferredSlotForRole(_fufuUigfSlots, _selectedRoleUid);
            if (!string.IsNullOrWhiteSpace(preferredSlot) &&
                !string.Equals(FufuUigfArchiveComboBox.SelectedItem as string, preferredSlot, StringComparison.Ordinal))
            {
                FufuUigfArchiveComboBox.SelectedItem = preferredSlot;
                return;
            }
        }

        ApplyAllHutaoRoleFilters();
    }

    private void OnHutaoChallengeRoleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoRoleSync) return;
        var opt = HutaoChallengeRoleFilterCombo.SelectedItem as AccountBoundRoleOption;
        _selectedRoleUid = opt?.Uid ?? string.Empty;
        SyncHutaoRoleFilterCombos();

        if (!string.IsNullOrWhiteSpace(_selectedRoleUid))
        {
            var preferredSlot = FindPreferredSlotForRole(_fufuChallengeSlots, _selectedRoleUid);
            if (!string.IsNullOrWhiteSpace(preferredSlot) &&
                !string.Equals(FufuChallengeArchiveComboBox.SelectedItem as string, preferredSlot, StringComparison.Ordinal))
            {
                FufuChallengeArchiveComboBox.SelectedItem = preferredSlot;
                return;
            }
        }

        ApplyAllHutaoRoleFilters();
    }

    private void RefreshHutaoRoleLabels()
    {
        foreach (var entry in _allHutaoGachaEntries)
        {
            entry.RoleTitle = ResolveUidDisplayTitle(entry.Uid);
        }

        foreach (var item in _allFufuUigfUids)
        {
            item.RoleTitle = ResolveUidDisplayTitle(item.Uid);
        }

        foreach (var item in _allFufuChallengeItems)
        {
            item.RoleDisplayName = ResolveUidDisplayTitle(item.Uid);
        }
    }

    private string ResolveUidDisplayTitle(string uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return string.Empty;
        var matched = _accountRoles.FirstOrDefault(r => !r.IsAllRolesOption && string.Equals(r.Uid, uid, StringComparison.Ordinal));
        return matched != null ? matched.ShortDisplayText : GameRoleDisplay.Archive(uid);
    }

    private void ApplyAllHutaoRoleFilters()
    {
        ApplyHutaoGachaRoleFilter();
        ApplyFufuUigfRoleFilter();
        ApplyFufuChallengeRoleFilter();
    }

    private void ApplyHutaoGachaRoleFilter()
    {
        _hutaoGachaEntries.Clear();
        var filtered = string.IsNullOrWhiteSpace(_selectedRoleUid)
            ? _allHutaoGachaEntries
            : _allHutaoGachaEntries.Where(x => string.Equals(x.Uid, _selectedRoleUid, StringComparison.Ordinal));

        foreach (var item in filtered)
        {
            item.RoleTitle = ResolveUidDisplayTitle(item.Uid);
            if (!string.IsNullOrWhiteSpace(_selectedRoleUid))
            {
                item.IsSelected = true;
            }

            _hutaoGachaEntries.Add(item);
        }

        UpdateHutaoRepeaterEmptyStates();
    }

    private void ApplyFufuUigfRoleFilter()
    {
        _fufuUigfUids.Clear();
        var filtered = string.IsNullOrWhiteSpace(_selectedRoleUid)
            ? _allFufuUigfUids
            : _allFufuUigfUids.Where(x => string.Equals(x.Uid, _selectedRoleUid, StringComparison.Ordinal));

        foreach (var item in filtered)
        {
            item.RoleTitle = ResolveUidDisplayTitle(item.Uid);
            if (!string.IsNullOrWhiteSpace(_selectedRoleUid))
            {
                item.IsSelected = true;
            }

            _fufuUigfUids.Add(item);
        }

        UpdateHutaoRepeaterEmptyStates();
    }

    private void ApplyFufuChallengeRoleFilter()
    {
        _fufuChallengeItems.Clear();
        var filtered = string.IsNullOrWhiteSpace(_selectedRoleUid)
            ? _allFufuChallengeItems
            : _allFufuChallengeItems.Where(x => string.Equals(x.Uid, _selectedRoleUid, StringComparison.Ordinal));

        foreach (var item in filtered)
        {
            item.RoleDisplayName = ResolveUidDisplayTitle(item.Uid);
            _fufuChallengeItems.Add(item);
        }

        UpdateHutaoRepeaterEmptyStates();
    }

    private static string? FindPreferredSlotForRole(IReadOnlyList<string> slots, string uid)
    {
        if (slots.Count == 0 || string.IsNullOrWhiteSpace(uid)) return null;

        var exactPrefixed = slots.FirstOrDefault(s => string.Equals(s, $"uid_{uid}", StringComparison.OrdinalIgnoreCase));
        if (exactPrefixed != null) return exactPrefixed;

        var exactUid = slots.FirstOrDefault(s => string.Equals(s, uid, StringComparison.OrdinalIgnoreCase));
        if (exactUid != null) return exactUid;

        return slots.FirstOrDefault(s => s.Contains(uid, StringComparison.OrdinalIgnoreCase));
    }

    private void InitializeHutaoPullSaveModeCombo()
    {
        if (HutaoPullSaveModeCombo == null) return;
        _suppressHutaoSaveModeSync = true;
        try
        {
            HutaoPullSaveModeCombo.Items.Clear();
            HutaoPullSaveModeCombo.Items.Add(new ComboBoxItem
            {
                Tag = "new",
                Content = "HutaoSync_PullSaveModeNew".GetLocalized()
            });
            HutaoPullSaveModeCombo.Items.Add(new ComboBoxItem
            {
                Tag = "existing",
                Content = "HutaoSync_PullSaveModeExisting".GetLocalized()
            });
            HutaoPullSaveModeCombo.SelectedIndex = 0;
        }
        finally
        {
            _suppressHutaoSaveModeSync = false;
        }

        UpdatePullSaveModeUi();
    }

    private void UpdatePullSaveModeUi()
    {
        if (HutaoPullSaveModeCombo == null || HutaoPullNewSlotPanel == null || HutaoPullExistingSlotPanel == null)
        {
            return;
        }

        var hasExisting = _fufuUigfSlots.Count > 0;
        if (HutaoPullSaveModeCombo.Items.Count > 1 && HutaoPullSaveModeCombo.Items[1] is ComboBoxItem existingItem)
        {
            existingItem.IsEnabled = hasExisting;
        }

        if (!hasExisting && HutaoPullSaveModeCombo.SelectedIndex != 0)
        {
            _suppressHutaoSaveModeSync = true;
            try
            {
                HutaoPullSaveModeCombo.SelectedIndex = 0;
            }
            finally
            {
                _suppressHutaoSaveModeSync = false;
            }
        }

        var isExistingMode = hasExisting &&
                             (HutaoPullSaveModeCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "existing";

        HutaoPullNewSlotPanel.Visibility = isExistingMode ? Visibility.Collapsed : Visibility.Visible;
        HutaoPullExistingSlotPanel.Visibility = isExistingMode ? Visibility.Visible : Visibility.Collapsed;

        if (HutaoMergeExistingCheckBox != null)
        {
            HutaoMergeExistingCheckBox.Visibility = isExistingMode ? Visibility.Visible : Visibility.Collapsed;
        }

        if (HutaoPullNoExistingHintText != null)
        {
            HutaoPullNoExistingHintText.Visibility = !isExistingMode && !hasExisting
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void ApplyDefaultNewPullSlotName()
    {
        if (HutaoPullTargetSlotBox == null) return;
        var current = HutaoPullTargetSlotBox.Text.Trim();
        var isAutoManaged = string.IsNullOrWhiteSpace(current) ||
                            string.Equals(current, "hutao_gacha", StringComparison.Ordinal) ||
                            current.StartsWith("hutao_gacha_", StringComparison.OrdinalIgnoreCase) ||
                            current.StartsWith("uid_", StringComparison.OrdinalIgnoreCase);
        if (!isAutoManaged)
        {
            return;
        }

        var baseName = !string.IsNullOrWhiteSpace(_selectedRoleUid) ? $"uid_{_selectedRoleUid}" : "hutao_gacha";
        if (!_fufuUigfSlots.Contains(baseName, StringComparer.OrdinalIgnoreCase))
        {
            HutaoPullTargetSlotBox.Text = baseName;
            return;
        }

        for (var i = 1; i <= 999; i++)
        {
            var candidate = $"{baseName}_{i}";
            if (!_fufuUigfSlots.Contains(candidate, StringComparer.OrdinalIgnoreCase))
            {
                HutaoPullTargetSlotBox.Text = candidate;
                return;
            }
        }
    }

    private void InitializeHutaoStatsSelectors()
    {
        if (HutaoStatsPeriodCombo == null || HutaoStatsCategoryCombo == null || HutaoStatsFloorCombo == null)
        {
            return;
        }

        _suppressHutaoStatsFilterSync = true;
        try
        {
            HutaoStatsPeriodCombo.Items.Clear();
            HutaoStatsPeriodCombo.Items.Add(new ComboBoxItem { Tag = "current", Content = "HutaoSync_StatsPeriodCurrent".GetLocalized() });
            HutaoStatsPeriodCombo.Items.Add(new ComboBoxItem { Tag = "last", Content = "HutaoSync_StatsPeriodLast".GetLocalized() });
            HutaoStatsPeriodCombo.SelectedIndex = 0;

            HutaoStatsCategoryCombo.Items.Clear();
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Util", Content = "HutaoSync_StatsCatUtil".GetLocalized() });
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Attend", Content = "HutaoSync_StatsCatAttend".GetLocalized() });
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Hold", Content = "HutaoSync_StatsCatHold".GetLocalized() });
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Team", Content = "HutaoSync_StatsCatTeam".GetLocalized() });
            HutaoStatsCategoryCombo.Items.Add(new ComboBoxItem { Tag = "Theater", Content = "HutaoSync_StatsCatTheater".GetLocalized() });
            HutaoStatsCategoryCombo.SelectedIndex = 0;

            HutaoStatsFloorCombo.Items.Clear();
            foreach (var fl in new[] { 12, 11, 10, 9 })
            {
                HutaoStatsFloorCombo.Items.Add(new ComboBoxItem
                {
                    Tag = fl.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Content = string.Format("HutaoSync_StatsFloorFormat".GetLocalized(), fl)
                });
            }
            HutaoStatsFloorCombo.SelectedIndex = 0;
        }
        finally
        {
            _suppressHutaoStatsFilterSync = false;
        }
    }

    private async void OnHutaoStatsRefreshClick(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;
        await RefreshHutaoStatisticsAsync(silent: false);
    }

    private async void OnHutaoStatsRoleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoRoleSync || _suppressHutaoStatsFilterSync) return;
        var opt = HutaoStatsRoleFilterCombo.SelectedItem as AccountBoundRoleOption;
        _selectedRoleUid = opt?.Uid ?? string.Empty;
        SyncHutaoRoleFilterCombos();
        ApplyAllHutaoRoleFilters();

        if (_currentHutaoSubTab == "HutaoStats")
        {
            await RefreshHutaoStatisticsAsync(silent: true);
        }
    }

    private async void OnHutaoStatsPeriodChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoStatsFilterSync) return;
        var tag = (HutaoStatsPeriodCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        _hutaoStatsLastPeriod = string.Equals(tag, "last", StringComparison.Ordinal);
        await RefreshHutaoStatisticsAsync(silent: true);
    }

    private void OnHutaoStatsCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoStatsFilterSync) return;
        _hutaoStatsCategory = (HutaoStatsCategoryCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Util";
        if (HutaoStatsFloorCombo != null)
        {
            var usesFloor = _hutaoStatsCategory is "Util" or "Attend" or "Team";
            HutaoStatsFloorCombo.Visibility = usesFloor ? Visibility.Visible : Visibility.Collapsed;
        }

        RenderHutaoStatisticsView();
    }

    private void OnHutaoStatsFloorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressHutaoStatsFilterSync) return;
        var tag = (HutaoStatsFloorCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (int.TryParse(tag, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var floor))
        {
            _hutaoStatsFloor = floor;
        }

        RenderHutaoStatisticsView();
    }

    private long _hutaoStatsRequestVersion;

    private async Task RefreshHutaoStatisticsAsync(bool silent)
    {
        if (_closed) return;
        var version = ++_hutaoStatsRequestVersion;
        _hutaoStatsLoading = true;
        if (HutaoStatsRefreshButton != null)
        {
            HutaoStatsRefreshButton.IsEnabled = false;
        }

        if (_hutaoStatsRows.Count == 0 && HutaoStatsEmptyText != null)
        {
            HutaoStatsEmptyText.Text = "HutaoSync_StatsLoadingHint".GetLocalized();
            HutaoStatsEmptyText.Visibility = Visibility.Visible;
        }

        try
        {
            var res = await _hutaoCloud.GetHutaoStatisticsBundleAsync(_hutaoStatsLastPeriod, _selectedRoleUid);
            if (_closed || version != _hutaoStatsRequestVersion) return;

            if (!res.Ok || res.Data == null)
            {
                if (!silent)
                {
                    ShowMessage(InfoBarSeverity.Error, "HutaoSync_FailedTitle".GetLocalized(), res.Message);
                }

                RenderHutaoStatisticsView();
                return;
            }

            _hutaoStatsLoaded = true;
            _hutaoStatsBundle = res.Data;
            RenderHutaoStatisticsView();

            if (!silent)
            {
                ShowMessage(InfoBarSeverity.Success, "HutaoSync_SuccessTitle".GetLocalized(),
                    "HutaoSync_StatsMsgRefreshed".GetLocalized());
            }
        }
        finally
        {
            if (version == _hutaoStatsRequestVersion)
            {
                _hutaoStatsLoading = false;
                if (HutaoStatsRefreshButton != null)
                {
                    HutaoStatsRefreshButton.IsEnabled = true;
                }
            }
        }
    }

    private void RenderHutaoStatisticsView()
    {
        if (HutaoStatsOverviewScheduleText == null || HutaoStatsRowsRepeater == null)
        {
            return;
        }

        var bundle = _hutaoStatsBundle;
        if (bundle == null)
        {
            HutaoStatsOverviewScheduleText.Text = "-";
            HutaoStatsOverviewRecordsText.Text = "-";
            HutaoStatsOverviewFullStarText.Text = "-";
            HutaoStatsOverviewAvgText.Text = "-";
            HutaoStatsOverviewTheaterText.Text = "-";
            HutaoStatsOverviewUpdatedText.Text = "-";
            HutaoStatsUidRankGrid.Visibility = Visibility.Collapsed;
            HutaoStatsUidRankEmptyText.Visibility = Visibility.Visible;
            _hutaoStatsRows.Clear();
            HutaoStatsEmptyText.Text = "HutaoSync_StatsEmptyHint".GetLocalized();
            HutaoStatsEmptyText.Visibility = Visibility.Visible;
            return;
        }

        var ov = bundle.Overview;
        if (ov != null)
        {
            HutaoStatsOverviewScheduleText.Text = ov.ScheduleId > 0
                ? string.Format("HutaoSync_ScheduleFallbackFormat".GetLocalized(), ov.ScheduleId)
                : "-";
            HutaoStatsOverviewRecordsText.Text = ov.RecordTotal.ToString("N0");
            HutaoStatsOverviewFullStarText.Text = string.Format(
                "HutaoSync_StatsFullStarSummaryFormat".GetLocalized(),
                ov.SpiralAbyssFullStar.ToString("N0"),
                ov.SpiralAbyssTotal.ToString("N0"),
                ov.SpiralAbyssPassed.ToString("N0"));
            HutaoStatsOverviewAvgText.Text = ov.SpiralAbyssTotal > 0
                ? string.Format(
                    "HutaoSync_StatsAvgSummaryFormat".GetLocalized(),
                    (double)ov.SpiralAbyssStarTotal / ov.SpiralAbyssTotal,
                    (double)ov.SpiralAbyssBattleTotal / ov.SpiralAbyssTotal)
                : "-";
            HutaoStatsOverviewUpdatedText.Text = ov.Timestamp > 0
                ? (ov.Timestamp > 10_000_000_000L
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ov.Timestamp)
                    : DateTimeOffset.FromUnixTimeSeconds(ov.Timestamp)).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : "-";
        }
        else
        {
            HutaoStatsOverviewScheduleText.Text = "-";
            HutaoStatsOverviewRecordsText.Text = "-";
            HutaoStatsOverviewFullStarText.Text = "-";
            HutaoStatsOverviewAvgText.Text = "-";
            HutaoStatsOverviewUpdatedText.Text = "-";
        }

        HutaoStatsOverviewTheaterText.Text = bundle.RoleCombat != null
            ? string.Format(
                "HutaoSync_StatsRoleCombatSummaryFormat".GetLocalized(),
                bundle.RoleCombat.ScheduleId,
                bundle.RoleCombat.RecordTotal.ToString("N0"))
            : "-";

        if (!bundle.IsLastPeriod &&
            !string.IsNullOrWhiteSpace(bundle.QueriedUid) &&
            bundle.UidRank is { } uidRank &&
            (uidRank.Damage != null || uidRank.TakeDamage != null))
        {
            HutaoStatsUidRankGrid.Visibility = Visibility.Visible;
            HutaoStatsUidRankEmptyText.Visibility = Visibility.Collapsed;

            HutaoStatsUidDamageText.Text = uidRank.Damage != null
                ? $"{bundle.UidDamageAvatarName} · " + string.Format(
                    "HutaoSync_StatsUidRankMetricFormat".GetLocalized(),
                    uidRank.Damage.Value.ToString("N0"),
                    $"{uidRank.Damage.Rate * 100:0.0}%",
                    $"{uidRank.Damage.RateOnAvatar * 100:0.0}%")
                : "-";

            HutaoStatsUidTakeDamageText.Text = uidRank.TakeDamage != null
                ? $"{bundle.UidTakeDamageAvatarName} · " + string.Format(
                    "HutaoSync_StatsUidRankMetricFormat".GetLocalized(),
                    uidRank.TakeDamage.Value.ToString("N0"),
                    $"{uidRank.TakeDamage.Rate * 100:0.0}%",
                    $"{uidRank.TakeDamage.RateOnAvatar * 100:0.0}%")
                : "-";
        }
        else
        {
            HutaoStatsUidRankGrid.Visibility = Visibility.Collapsed;
            HutaoStatsUidRankEmptyText.Visibility = Visibility.Visible;
        }

        IReadOnlyList<HutaoRankDisplayItem> sourceRows = _hutaoStatsCategory switch
        {
            "Attend" => bundle.AttendanceByFloor.GetValueOrDefault(_hutaoStatsFloor) ?? new List<HutaoRankDisplayItem>(),
            "Hold" => bundle.HoldingRates,
            "Team" => bundle.TeamsByFloor.GetValueOrDefault(_hutaoStatsFloor) ?? new List<HutaoRankDisplayItem>(),
            "Theater" => bundle.RoleCombatRates,
            _ => bundle.UtilizationByFloor.GetValueOrDefault(_hutaoStatsFloor) ?? new List<HutaoRankDisplayItem>()
        };

        _hutaoStatsRows.Clear();
        foreach (var row in sourceRows)
        {
            _hutaoStatsRows.Add(row);
        }

        HutaoStatsEmptyText.Text = "HutaoSync_StatsEmptyHint".GetLocalized();
        HutaoStatsEmptyText.Visibility = _hutaoStatsRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatHutaoRoleIdentity(HutaoUserInfo? info)
    {
        if (info == null) return "-";
        if (info.IsMaintainer) return "HutaoSync_RoleMaintainer".GetLocalized();
        if (info.IsLicensedDeveloper) return "HutaoSync_RoleDeveloper".GetLocalized();
        return "HutaoSync_RoleStandard".GetLocalized();
    }

    private static string FormatHutaoGachaPlan(HutaoUserInfo? info)
    {
        if (info == null) return "HutaoSync_PlanExpired".GetLocalized();
        if (info.IsMaintainer || info.IsLicensedDeveloper) return "HutaoSync_PlanDeveloper".GetLocalized();
        if (info.GachaLogExpireAt.HasValue && info.GachaLogExpireAt.Value > DateTimeOffset.UtcNow)
        {
            return string.Format(
                "HutaoSync_PlanActiveFormat".GetLocalized(),
                info.GachaLogExpireAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        }

        return "HutaoSync_PlanExpired".GetLocalized();
    }

    private static string FormatHutaoCdnPlan(HutaoUserInfo? info)
    {
        if (info == null) return "HutaoSync_AccelPlanExpired".GetLocalized();
        if (info.IsMaintainer || info.IsLicensedDeveloper) return "HutaoSync_PlanDeveloper".GetLocalized();
        if (info.CdnExpireAt.HasValue && info.CdnExpireAt.Value > DateTimeOffset.UtcNow)
        {
            return string.Format(
                "HutaoSync_PlanActiveFormat".GetLocalized(),
                info.CdnExpireAt.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"));
        }

        return "HutaoSync_AccelPlanExpired".GetLocalized();
    }

    private void UpdateHutaoExtraFields(HutaoUserInfo? info)
    {
        _hutaoExtraFields.Clear();
        if (info?.ExtraFields != null)
        {
            foreach (var kv in info.ExtraFields)
            {
                var valueText = FormatExtraFieldValue(kv.Value);
                if (!string.IsNullOrWhiteSpace(valueText))
                {
                    _hutaoExtraFields.Add(new HutaoAccountExtraFieldItem
                    {
                        Label = kv.Key,
                        Value = valueText
                    });
                }
            }
        }

        if (HutaoExtraFieldsRepeater != null)
        {
            HutaoExtraFieldsRepeater.Visibility = _hutaoExtraFields.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private static string FormatExtraFieldValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                return string.Empty;
            case JsonValueKind.True:
                return bool.TrueString;
            case JsonValueKind.False:
                return bool.FalseString;
            case JsonValueKind.String:
                var raw = element.GetString()?.Trim() ?? string.Empty;
                return FormatHutaoDateString(raw) is { Length: > 0 } dt ? dt : raw;
            default:
                return element.ToString();
        }
    }

    private static string FormatHutaoDateString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        if (DateTimeOffset.TryParse(raw, out var dto) && dto.Year > 2000)
        {
            return dto.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }

        return raw.Trim();
    }

    private async Task MergeUiafArchiveToLocalAsync(string slot, string jsonContent)
    {
        var summary = CloudArchiveLocalBridge.InspectUiafJson(jsonContent);
        if (!summary.Ok)
        {
            ShowMessage(InfoBarSeverity.Error,
                "CloudSync_UiafMergeFailedTitle".GetLocalized(),
                "CloudSync_ErrInvalidFormat".GetLocalized());
            return;
        }

        var profiles = CloudArchiveLocalBridge.GetLocalAchievementProfiles();
        var profileCombo = new ComboBox
        {
            Header = "CloudSync_UiafTargetHeader".GetLocalized(),
            ItemsSource = profiles,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var newNameBox = new TextBox
        {
            Header = "CloudSync_UiafNewProfileHeader".GetLocalized(),
            PlaceholderText = "CloudSync_UiafNewProfilePlaceholder".GetLocalized(),
            Visibility = Visibility.Collapsed,
            MaxLength = 64
        };

        profileCombo.SelectionChanged += (_, _) =>
        {
            var selected = profileCombo.SelectedItem as LocalAchievementProfileOption;
            newNameBox.Visibility = selected is { IsNewProfileOption: true }
                ? Visibility.Visible
                : Visibility.Collapsed;
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 400 };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format("CloudSync_UiafImportSummaryFormat".GetLocalized(), summary.TotalItems, summary.CompletedItems),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });
        panel.Children.Add(profileCombo);
        panel.Children.Add(newNameBox);
        panel.Children.Add(new TextBlock
        {
            Text = "CloudSync_UiafImportTip".GetLocalized(),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        var dialog = new ContentDialog
        {
            Title = "CloudSync_UiafImportDialogTitle".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "CloudSync_ImportConfirmBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (profileCombo.SelectedItem is not LocalAchievementProfileOption chosen) return;

        SetBusy(true);
        try
        {
            var merge = await CloudArchiveLocalBridge.MergeUiafToLocalProfileAsync(
                jsonContent,
                chosen,
                newNameBox.Text);

            if (!merge.Ok)
            {
                ShowMessage(InfoBarSeverity.Error,
                    "CloudSync_UiafMergeFailedTitle".GetLocalized(),
                    merge.ErrorMessage ?? "CloudSync_UiafMergeFailed".GetLocalized());
                return;
            }

            ShowMessage(InfoBarSeverity.Success,
                "CloudSync_UiafMergeSuccessTitle".GetLocalized(),
                string.Format(
                    "CloudSync_UiafMergeSuccessFormat".GetLocalized(),
                    slot,
                    merge.TargetDisplayName,
                    merge.UpdatedCount));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task MergeUigfArchiveToLocalAsync(string slot, string jsonContent)
    {
        var uidItems = CloudArchiveLocalBridge.InspectUigfUids(jsonContent);
        if (uidItems.Count == 0)
        {
            ShowMessage(InfoBarSeverity.Error,
                "CloudSync_UigfMergeFailedTitle".GetLocalized(),
                "CloudSync_ErrInvalidFormat".GetLocalized());
            return;
        }

        var checkBoxes = new List<(string Uid, CheckBox Box)>();
        var listPanel = new StackPanel { Spacing = 6 };
        foreach (var entry in uidItems)
        {
            var cb = new CheckBox
            {
                IsChecked = true,
                Content = $"UID {entry.Uid}  ·  {entry.DetailText}"
            };
            checkBoxes.Add((entry.Uid, cb));
            listPanel.Children.Add(cb);
        }

        var panel = new StackPanel { Spacing = 10, MinWidth = 400 };
        panel.Children.Add(new TextBlock
        {
            Text = "CloudSync_UigfImportSummary".GetLocalized(),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        });
        panel.Children.Add(new ScrollViewer
        {
            MaxHeight = 240,
            Content = listPanel
        });
        panel.Children.Add(new TextBlock
        {
            Text = "CloudSync_UigfImportTip".GetLocalized(),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
        });

        var dialog = new ContentDialog
        {
            Title = "CloudSync_UigfImportDialogTitle".GetLocalized(),
            Content = panel,
            PrimaryButtonText = "CloudSync_ImportConfirmBtn".GetLocalized(),
            CloseButtonText = "CloudSync_CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = RootGrid.XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var selectedUids = checkBoxes
            .Where(x => x.Box.IsChecked == true)
            .Select(x => x.Uid)
            .ToHashSet(StringComparer.Ordinal);

        if (selectedUids.Count == 0)
        {
            ShowMessage(InfoBarSeverity.Warning, "HutaoSync_HintTitle".GetLocalized(),
                "HutaoSync_ErrSelectUidFirst".GetLocalized());
            return;
        }

        SetBusy(true);
        try
        {
            var result = await CloudArchiveLocalBridge.ImportUigfToLocalAsync(jsonContent, selectedUids);
            if (!result.Success)
            {
                ShowMessage(InfoBarSeverity.Error,
                    "CloudSync_UigfMergeFailedTitle".GetLocalized(),
                    "CloudSync_ErrInvalidFormat".GetLocalized());
                return;
            }

            ShowMessage(InfoBarSeverity.Success,
                "CloudSync_UigfMergeSuccessTitle".GetLocalized(),
                string.Format(
                    "CloudSync_UigfMergeSuccessFormat".GetLocalized(),
                    slot,
                    result.ImportedAccounts,
                    result.ImportedRecords,
                    result.DisplayUid ?? "-"));
        }
        catch (Exception ex)
        {
            DebugLog.Info("CloudSyncWindow", $"[CloudSyncWindow] Merge UIGF failed: {ex.Message}");
            ShowMessage(InfoBarSeverity.Error,
                "CloudSync_UigfMergeFailedTitle".GetLocalized(),
                "CloudSync_ErrInvalidFormat".GetLocalized());
        }
        finally
        {
            SetBusy(false);
        }
    }
}
