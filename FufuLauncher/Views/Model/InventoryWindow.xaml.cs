using System.Net.Http.Json;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views
{

    public sealed partial class InventoryWindow : Window
    {
        private readonly string _cachePath;
        private readonly string _configPath;
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

            var folder = Path.Combine(AppContext.BaseDirectory, "Data");
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            _cachePath = Path.Combine(folder, "inventory_cache.json");
            _configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                _httpClient.DefaultRequestHeaders.Add("Referer", ApiEndpoints.WebstaticRefererUrl);
            }

            _ = LoadInitialDataAsync();
        }

        private async Task LoadInitialDataAsync()
        {
            try
            {
                if (File.Exists(_cachePath))
                {
                    var json = await File.ReadAllTextAsync(_cachePath);
                    var data = JsonSerializer.Deserialize<InventoryData>(json);

                    if (data?.Items.Count > 0)
                    {
                        _currentItems = data.Items;
                        RefreshUiBindings();
                        StatusText.Text = $"上次更新: {DateTimeOffset.FromUnixTimeSeconds(data.LastUpdateTime).LocalDateTime:MM-dd HH:mm}";
                        return;
                    }
                }
                await LoadInventoryDataAsync(false);
            }
            catch { StatusText.Text = "加载失败，请检查配置"; }
        }
        
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
            try
            {
                var data = new InventoryData
                {
                    Items = _currentItems,
                    LastUpdateTime = DateTimeOffset.Now.ToUnixTimeSeconds()
                };
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = false });
                await File.WriteAllTextAsync(_cachePath, json);
            }
            catch
            {
                // ignored
            }
        }

        private async Task LoadInventoryDataAsync(bool isManualRefresh)
        {
            RefreshButton.IsEnabled = false;
            try
            {
                StatusText.Text = isManualRefresh ? "正在请求米游社..." : "正在获取数据...";
                if (!File.Exists(_configPath)) throw new Exception("未找到配置文件");

                var configJson = await File.ReadAllTextAsync(_configPath);
                using var configDoc = JsonDocument.Parse(configJson);
                var cookie = configDoc.RootElement.GetProperty("Account").GetProperty("Cookie").GetString();

                if (string.IsNullOrEmpty(cookie)) throw new Exception("Cookie未配置");

                var newItems = await Task.Run(async () =>
                {
                    var uid = await GetUidAsync(cookie);
                    return await SyncInventoryFromApiAsync(cookie, uid);
                });
                
                foreach (var newItem in newItems)
                {
                    var existing = _currentItems.FirstOrDefault(i => i.Id == newItem.Id);
                    if (existing != null)
                    {
                        newItem.TargetCount = existing.TargetCount;
                    }
                }

                _currentItems = newItems;
                await SaveToCacheAsync();
                RefreshUiBindings();
                StatusText.Text = $"同步成功: {DateTime.Now:HH:mm:ss}";
            }
            catch (Exception ex) { StatusText.Text = $"同步失败: {ex.Message}"; }
            finally { RefreshButton.IsEnabled = true; }
        }

        private async Task<string?> GetUidAsync(string cookie)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, ApiEndpoints.MihoyoBbsUserGameRolesUrl); 
            request.Headers.Add("Cookie", cookie);
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.GetProperty("data").GetProperty("list")[0].GetProperty("game_uid").GetString();
        }

        private async Task<List<InventoryItemModel>> SyncInventoryFromApiAsync(string cookie, string? uid)
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
                    avatars.Add((avatar.GetProperty("id").GetInt32(), skillIds, avatar.GetProperty("weapon_cat_id").GetInt32()));
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

            var computePayload = new { items = deltas, region = "cn_gf01", uid };
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
                    Name = item.GetProperty("name").GetString() ?? "未知",
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
            var response = await _httpClient.SendAsync(request);
            return await response.Content.ReadAsStringAsync();
        }
    }
}