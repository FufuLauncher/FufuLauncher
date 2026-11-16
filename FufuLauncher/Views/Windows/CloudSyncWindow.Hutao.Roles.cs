using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class CloudSyncWindow
{
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
                .OrderByDescending(a =>
                    activeAcc != null && string.Equals(a.Id, activeAcc.Id, StringComparison.Ordinal))
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
                !string.Equals(FufuChallengeArchiveComboBox.SelectedItem as string, preferredSlot,
                    StringComparison.Ordinal))
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
        var matched = _accountRoles.FirstOrDefault(r =>
            !r.IsAllRolesOption && string.Equals(r.Uid, uid, StringComparison.Ordinal));
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

        var exactPrefixed =
            slots.FirstOrDefault(s => string.Equals(s, $"uid_{uid}", StringComparison.OrdinalIgnoreCase));
        if (exactPrefixed != null) return exactPrefixed;

        var exactUid = slots.FirstOrDefault(s => string.Equals(s, uid, StringComparison.OrdinalIgnoreCase));
        if (exactUid != null) return exactUid;

        return slots.FirstOrDefault(s => s.Contains(uid, StringComparison.OrdinalIgnoreCase));
    }
}