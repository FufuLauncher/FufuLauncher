using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow
{
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
}
