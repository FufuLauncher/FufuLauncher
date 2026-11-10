/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public sealed class CheckinSettingsStore(ILocalSettingsService settings)
{
    private const string AccountOptionsPrefix = "CheckinAccountOptions_";
    private const string AccountOrderKey = "CheckinAccountOrder";

    public async Task<bool> ReadBoolAsync(string key, bool defaultValue)
    {
        var value = await settings.ReadSettingAsync(key);
        return bool.TryParse(value?.ToString(), out var enabled) ? enabled : defaultValue;
    }

    public async Task<CheckinOptions> LoadDefaultsAsync()
    {
        var options = new CheckinOptions
        {
            IsGameCheckinEnabled = await ReadBoolAsync("IsGameCheckinEnabled", true),
            IsCommunityCheckinEnabled = await ReadBoolAsync("IsCommunityCheckinEnabled", true),
            IsCloudGameCheckinEnabled = await ReadBoolAsync("IsCloudGameCheckinEnabled", false)
        };
        foreach (var game in MiyousheCheckinGame.SupportedGames.Where(g => g.GameBiz != "hk4e_cn"))
            options.Games[game.GameBiz] = await ReadBoolAsync(game.SettingKey, true);
        return options;
    }

    public async Task<CheckinOptions?> LoadAccountAsync(string accountId)
    {
        var options = await ReadJsonAsync<CheckinOptions>(AccountOptionsPrefix + accountId);

        if (options != null) options.Games ??= new();
        return options;
    }

    public Task SaveAccountAsync(string accountId, CheckinOptions options) =>
        settings.SaveSettingAsync(AccountOptionsPrefix + accountId, JsonSerializer.Serialize(options));

    public async Task<List<string>> LoadOrderAsync() =>
        await ReadJsonAsync<List<string>>(AccountOrderKey) ?? new();

    public Task<bool> SaveOrderAsync(IEnumerable<string> accountIds) =>
        settings.TrySaveSettingAsync(AccountOrderKey, JsonSerializer.Serialize(accountIds.ToList()));

    public static List<T> ApplyOrder<T>(IEnumerable<T> accounts, IReadOnlyList<string> order, Func<T, string> getId)
    {
        var positions = new Dictionary<string, int>();
        for (int i = 0; i < order.Count; i++)
            if (!string.IsNullOrEmpty(order[i]))
                positions.TryAdd(order[i], i);

        return accounts.OrderBy(a => positions.GetValueOrDefault(getId(a), int.MaxValue)).ToList();
    }

    private async Task<T?> ReadJsonAsync<T>(string key) where T : class
    {
        var value = await settings.ReadSettingAsync(key);
        if (value == null) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(value.ToString() ?? "null");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
