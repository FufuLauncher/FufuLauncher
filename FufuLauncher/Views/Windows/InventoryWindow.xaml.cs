/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net.Http.Json;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views
{
    public sealed partial class InventoryWindow : Window
    {
        private string _cachePath = "";
        private readonly GameRoleService _roles = App.GetService<GameRoleService>();

        public GameRoleScope RoleSelection
        {
            get;
        } = new();

        private SelectedGameRole? _loadedRole;
        private int _loadVersion;
        private bool _closed;
        private List<InventoryItemModel> _currentItems = new();

        private static readonly HttpClient _httpClient = new(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        });

        public InventoryWindow()
        {
            InitializeComponent();

            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            WeakReferenceMessenger.Default.Register<GameRoleChangedMessage>(this, (r, m) =>
            {
                if (!RoleSelection.HasOverride) DispatcherQueue.TryEnqueue(async () => await LoadInitialDataAsync());
            });
            WeakReferenceMessenger.Default.Register<FeatureGameRoleChangedMessage>(this, (r, m) =>
            {
                if (ReferenceEquals(m.Scope, RoleSelection))
                    DispatcherQueue.TryEnqueue(async () => await LoadInitialDataAsync());
            });
            WeakReferenceMessenger.Default.Register<GameRolesUpdatedMessage>(this, (r, m) =>
            {
                var account = App.GetService<AccountManager>().GetActiveAccountEntry();
                if (account?.Id == m.AccountId && RoleSelection.Current(account) == null)
                    DispatcherQueue.TryEnqueue(async () => await LoadInitialDataAsync());
            });
            WeakReferenceMessenger.Default.Register<AccountChangedMessage>(this, (r, m) =>
            {
                RoleSelection.Reset();
                DispatcherQueue.TryEnqueue(async () => await LoadInitialDataAsync());
            });
            Closed += (_, _) =>
            {
                _closed = true;
                ++_loadVersion;
                WeakReferenceMessenger.Default.UnregisterAll(this);
            };

            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Add("Referer", ApiEndpoints.WebstaticRefererUrl);
            }

            _ = LoadInitialDataAsync();
        }

        private async Task LoadInitialDataAsync()
        {
            if (_closed) return;
            var version = ++_loadVersion;
            _loadedRole = null;
            RefreshButton.IsEnabled = true;
            _currentItems = new();
            RefreshUiBindings();
            try
            {
                var selected = await _roles.GetCurrentAsync(RoleSelection);
                if (version != _loadVersion || _closed || selected == null) return;
                _cachePath = GetCachePath(selected);
                if (File.Exists(_cachePath))
                {
                    var json = await File.ReadAllTextAsync(_cachePath);
                    var data = JsonSerializer.Deserialize<InventoryData>(json);
                    if (version != _loadVersion || _closed || !_roles.IsCurrent(selected, RoleSelection)) return;
                    if (data != null)
                    {
                        _loadedRole = selected;
                        _currentItems = data.Items;
                        RefreshUiBindings();
                        StatusText.Text = string.Format(
                            "Inventory_LastUpdated_Format".GetLocalized(),
                            DateTimeOffset.FromUnixTimeSeconds(data.LastUpdateTime).LocalDateTime.ToString("MM-dd HH:mm"));
                        return;
                    }
                }

                await LoadInventoryDataAsync(false);
            }
            catch (Exception ex)
            {
                if (version == _loadVersion && !_closed) StatusText.Text = ex.Message;
            }
        }

        private static string GetCachePath(SelectedGameRole selected) => Path.Combine(
            Helpers.AppPaths.DataDir, $"inventory_{selected.Role.region}_{selected.Role.game_uid}.json");

        private void RefreshUiBindings()
        {
            var sortedList = _currentItems.OrderByDescending(x => x.OwnedCount).ToList();
            InventoryGridView.ItemsSource = sortedList;
            TargetListView.ItemsSource = sortedList;
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await LoadInventoryDataAsync(true);
        }

        private async void OnTargetValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            await SaveToCacheAsync();
            var sortedList = _currentItems.OrderByDescending(x => x.OwnedCount).ToList();
            InventoryGridView.ItemsSource = null;
            InventoryGridView.ItemsSource = sortedList;
        }

        private async Task SaveToCacheAsync()
        {
            if (_loadedRole == null || string.IsNullOrEmpty(_cachePath)) return;
            var path = _cachePath;
            try
            {
                var data = new InventoryData
                {
                    Items = _currentItems,
                    LastUpdateTime = DateTimeOffset.Now.ToUnixTimeSeconds()
                };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
                await File.WriteAllTextAsync(path, json);
            }
            catch
            {
            }
        }

        private async Task LoadInventoryDataAsync(bool isManualRefresh)
        {
            if (_closed) return;
            var version = ++_loadVersion;
            RefreshButton.IsEnabled = false;
            var existingItems = _currentItems.ToList();
            try
            {
                StatusText.Text = isManualRefresh ? "Inventory_RequestingMiyoushe".GetLocalized() : "Inventory_FetchingData".GetLocalized();
                var selected = await _roles.GetCurrentAsync(RoleSelection)
                               ?? throw new InvalidOperationException("Inventory_LoginRequired".GetLocalized());
                if (selected.ServerType != "cn")
                    throw new InvalidOperationException("Inventory_CnOnly".GetLocalized());
                if (version != _loadVersion || _closed) return;
                if (_loadedRole == null || _loadedRole.Role.game_uid != selected.Role.game_uid ||
                    _loadedRole.Role.region != selected.Role.region)
                    existingItems.Clear();
                var cookie = string.Join("; ", selected.Cookies.Select(x => $"{x.Key}={x.Value}"));
                var newItems = await SyncInventoryFromApiAsync(cookie, selected.Role.game_uid, selected.Role.region);
                if (version != _loadVersion || _closed || !_roles.IsCurrent(selected, RoleSelection)) return;
                foreach (var newItem in newItems)
                {
                    var existing = existingItems.FirstOrDefault(i => i.Id == newItem.Id);
                    if (existing != null) newItem.TargetCount = existing.TargetCount;
                }

                _loadedRole = selected;
                _cachePath = GetCachePath(selected);
                _currentItems = newItems;
                await SaveToCacheAsync();
                if (version != _loadVersion || _closed || !_roles.IsCurrent(selected, RoleSelection)) return;
                RefreshUiBindings();
                StatusText.Text = string.Format("Inventory_SyncSuccess_Format".GetLocalized(), DateTime.Now.ToString("HH:mm:ss"));
            }
            catch (Exception ex)
            {
                if (version == _loadVersion && !_closed) StatusText.Text = string.Format("Inventory_SyncFailed_Format".GetLocalized(), ex.Message);
            }
            finally
            {
                if (version == _loadVersion && !_closed) RefreshButton.IsEnabled = true;
            }
        }

        private async Task<List<InventoryItemModel>> SyncInventoryFromApiAsync(string cookie, string uid, string region)
        {
            var avatarPayload = new { page = 1, size = 1000, is_all = true };
            var avatarResp = await PostWithCookieAsync(ApiEndpoints.CalculateAvatarListUrl, avatarPayload, cookie);
            using var avatarDoc = JsonDocument.Parse(avatarResp);

            var avatars = new List<(int Id, List<int> SkillIds, int WeaponCatId)>();
            foreach (var avatar in avatarDoc.RootElement.GetProperty("data").GetProperty("list").EnumerateArray())
            {
                if (avatar.GetProperty("name").GetString() == "旅行者") continue;
                var skillIds = avatar.GetProperty("skill_list").EnumerateArray()
                    .Where(s => s.GetProperty("max_level").GetInt32() > 1)
                    .Select(s => s.GetProperty("group_id").GetInt32()).ToList();
                if (skillIds.Count > 0)
                    avatars.Add((avatar.GetProperty("id").GetInt32(), skillIds,
                        avatar.GetProperty("weapon_cat_id").GetInt32()));
            }

            var weaponPayload = new { page = 1, size = 1000, weapon_levels = new[] { 1, 2, 3, 4, 5 } };
            var weaponResp = await PostWithCookieAsync(ApiEndpoints.CalculateWeaponListUrl, weaponPayload, cookie);
            using var weaponDoc = JsonDocument.Parse(weaponResp);
            var weaponDict = weaponDoc.RootElement.GetProperty("data").GetProperty("list").EnumerateArray()
                .GroupBy(w => w.GetProperty("weapon_cat_id").GetInt32()).ToDictionary(g => g.Key, g => g.First());

            var deltas = avatars.Select(a => new
            {
                avatar_id = a.Id,
                avatar_level_current = 1,
                avatar_level_target = 90,
                skill_list = a.SkillIds.Select(sid => new { id = sid, level_current = 1, level_target = 10 }).ToArray(),
                weapon = new
                {
                    id = weaponDict[a.WeaponCatId].GetProperty("id").GetInt32(),
                    level_current = 1,
                    level_target = 90
                }
            }).ToList();

            var computePayload = new { items = deltas, region, uid };
            var computeResp = await PostWithCookieAsync(ApiEndpoints.CalculateBatchComputeUrl, computePayload, cookie);
            using var computeDoc = JsonDocument.Parse(computeResp);

            var items = new List<InventoryItemModel>();
            var overallConsume = computeDoc.RootElement.GetProperty("data").GetProperty("overall_consume");
            foreach (var item in overallConsume.EnumerateArray())
            {
                var owned = item.GetProperty("num").GetInt32() - item.GetProperty("lack_num").GetInt32();
                if (owned <= 0) continue;
                items.Add(new InventoryItemModel
                {
                    Id = item.GetProperty("id").GetInt32(),
                    Name = item.GetProperty("name").GetString() ?? "Common_Unknown".GetLocalized(),
                    OwnedCount = owned,
                    IconUrl = item.TryGetProperty("icon", out var icon) ? icon.GetString() : ""
                });
            }

            return items;
        }

        private async Task<string> PostWithCookieAsync(string url, object payload, string cookie)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Cookie", cookie);
            request.Content = JsonContent.Create(payload);
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("retcode", out var code) || code.GetInt32() != 0)
                throw new InvalidOperationException("Inventory_ApiRequestFailed".GetLocalized());
            return json;
        }
    }
}