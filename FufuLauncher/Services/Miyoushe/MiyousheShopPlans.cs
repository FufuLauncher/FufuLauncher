using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Models.Miyoushe;

namespace FufuLauncher.Services.Miyoushe;

internal sealed class MiyousheShopPlans(ILocalSettingsService settings)
{
    private const string Setting = "MiyousheShopPlans";
    private const string LegacySetting = "MiyousheShopReminders";
    private sealed record LegacyReminder(Guid Id, string GoodsId, string GoodsName, DateTimeOffset OpensAt);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<MiyousheShopPlan>? _items;

    internal event Action? Changed;

    internal async Task<IReadOnlyList<MiyousheShopPlan>> GetAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await LoadAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    internal Task<bool> AddAsync(MiyousheShopPlan plan) => UpdateAsync(items =>
    {
        if (plan.Target == null || string.IsNullOrWhiteSpace(plan.AccountId) ||
            (plan.Scheduled && plan.OpensAt <= DateTimeOffset.UtcNow) ||
            items.Any(item => item.AccountId == plan.AccountId &&
                ((!plan.Scheduled && item.Scheduled && item.Status == MiyousheShopPlanStatus.Pending &&
                  item.OpensAt <= DateTimeOffset.UtcNow.AddSeconds(12)) ||
                 (item.GoodsId == plan.GoodsId &&
                  (item.Status is MiyousheShopPlanStatus.Submitting or MiyousheShopPlanStatus.Unknown ||
                   (item.Status == MiyousheShopPlanStatus.Pending &&
                    (!plan.Scheduled || item.OpensAt == plan.OpensAt)))))))
            return false;

        items.Add(plan);
        return true;
    });

    internal Task<bool> RemoveAsync(Guid id) => UpdateAsync(items =>
        items.RemoveAll(item => item.Id == id && item.CanRemove) > 0);

    internal Task<bool> TransitionAsync(Guid id, MiyousheShopPlanStatus expected, MiyousheShopPlanStatus next,
        string result = "") => UpdateAsync(items =>
    {
        int index = items.FindIndex(item => item.Id == id && item.Status == expected);
        if (index < 0) return false;
        if (next == MiyousheShopPlanStatus.Submitting && items.Any(item =>
                item.Id != id && item.AccountId == items[index].AccountId && item.GoodsId == items[index].GoodsId &&
                item.Status == MiyousheShopPlanStatus.Unknown))
            return false;
        items[index] = items[index] with { Status = next, Result = result };
        return true;
    });

    internal Task<bool> RecoverAsync(DateTimeOffset startedAt) => UpdateAsync(items =>
    {
        bool changed = false;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var status = item.Status switch
            {
                MiyousheShopPlanStatus.Submitting => MiyousheShopPlanStatus.Unknown,
                MiyousheShopPlanStatus.Pending when item.Target == null || item.OpensAt < startedAt =>
                    MiyousheShopPlanStatus.Missed,
                _ => item.Status
            };
            if (status == item.Status) continue;
            items[i] = item with { Status = status };
            changed = true;
        }
        return changed;
    });

    private async Task<IReadOnlyList<MiyousheShopPlan>> LoadAsync()
    {
        if (_items != null) return _items;
        var stored = await settings.ReadSettingAsync(Setting);
        if (stored is string json)
            return _items = JsonSerializer.Deserialize<MiyousheShopPlan[]>(json) ??
                            throw new InvalidDataException("Invalid shop plans.");
        if (stored != null) throw new InvalidDataException("Invalid shop plans.");
        var legacy = await settings.ReadSettingAsync(LegacySetting);
        _items = legacy switch
        {
            null => [],
            string oldJson => (JsonSerializer.Deserialize<LegacyReminder[]>(oldJson) ?? [])
                .Select(item => new MiyousheShopPlan(item.Id, item.GoodsId, item.GoodsName, item.OpensAt,
                    "", "", 0, "", "", null, true, MiyousheShopPlanStatus.Legacy)).ToArray(),
            _ => throw new InvalidDataException("Invalid shop reminders.")
        };
        return _items;
    }

    private async Task<bool> UpdateAsync(Func<List<MiyousheShopPlan>, bool> update)
    {
        await _gate.WaitAsync();
        try
        {
            var next = (await LoadAsync()).ToList();
            if (!update(next)) return false;
            if (!await settings.TrySaveSettingAsync(Setting, JsonSerializer.Serialize(next)))
                throw new IOException("Could not save shop plans.");
            _items = next.ToArray();
        }
        finally
        {
            _gate.Release();
        }
        Changed?.Invoke();
        return true;
    }
}
