using System.Collections.Concurrent;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Models.MiHoYo.Identity;
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Services.MiHoYo;
using static FufuLauncher.Models.Miyoushe.MiyousheShopPlanStatus;
using static FufuLauncher.Services.Miyoushe.MiyousheShopClient;

namespace FufuLauncher.Services.Miyoushe;

internal sealed class MiyousheShopExecution(
    MiyousheShopPlans plans,
    MiyousheShopClient client,
    AccountManager accounts,
    AccountIdentityService identity)
{
    private static readonly TimeSpan PrepareLead = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan ReserveLead = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DeadlineGrace = TimeSpan.FromSeconds(2);
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly SemaphoreSlim _recoveryGate = new(1, 1);
    private bool _recovered;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _accountGates = new();
    private readonly SemaphoreSlim _wake = new(0);

    internal event Action<MiyousheShopPlan>? Completed;

    internal async Task<MiyousheShopSession> GetCatalogSessionAsync(string accountId, CancellationToken ct)
    {
        var (account, context) = await GetIdentityAsync(accountId, ct);
        return new(context, account.Stuid);
    }

    internal async Task<IReadOnlyList<MiyousheShopTarget>> GetTargetsAsync(
        string accountId, MiyousheShopGoods goods, CancellationToken ct)
    {
        var (account, context) = await GetIdentityAsync(accountId, ct);
        return await client.GetTargetsAsync(goods, context, account.Stuid, ct);
    }

    internal async Task<bool> ScheduleAsync(MiyousheShopPlan plan)
    {
        await RecoverAsync();
        return await plans.AddAsync(plan);
    }

    internal async Task<MiyousheShopResult> ExchangeNowAsync(MiyousheShopPlan plan)
    {
        await RecoverAsync();
        return await ExecuteAsync(plan, CancellationToken.None) ?? new(Unknown);
    }

    internal async Task RunAsync(CancellationToken ct)
    {
        var workers = new Dictionary<Guid, Task>();
        plans.Changed += Wake;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var delay = Timeout.InfiniteTimeSpan;
                try
                {
                    await RecoverAsync();
                    foreach (var finished in workers.Where(w => w.Value.IsCompleted).ToArray())
                    {
                        await finished.Value;
                        workers.Remove(finished.Key);
                    }

                    var pending = await plans.GetAsync();
                    var now = DateTimeOffset.UtcNow;
                    foreach (var plan in pending.Where(item => item.Scheduled && item.Status == Pending)
                                 .OrderBy(item => item.OpensAt))
                    {
                        if (workers.ContainsKey(plan.Id)) continue;
                        var untilPrepare = plan.OpensAt - now - PrepareLead;
                        if (untilPrepare > TimeSpan.Zero)
                        {
                            delay = untilPrepare < TimeSpan.FromSeconds(5) ? untilPrepare : TimeSpan.FromSeconds(5);
                            break;
                        }

                        workers.Add(plan.Id, RunScheduledAsync(plan, ct));
                    }

                    if (delay == Timeout.InfiniteTimeSpan && workers.Count > 0)
                        delay = TimeSpan.FromSeconds(5);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    DebugLog.Info(nameof(MiyousheShopExecution), ex.GetType().Name);
                    delay = TimeSpan.FromSeconds(30);
                }

                await _wake.WaitAsync(delay, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            plans.Changed -= Wake;
            await Task.WhenAll(workers.Values);
        }
    }

    private async Task RunScheduledAsync(MiyousheShopPlan plan, CancellationToken ct)
    {
        try
        {
            var result = await ExecuteAsync(plan, ct);
            if (result != null && !ct.IsCancellationRequested)
                Completed?.Invoke(plan with { Status = result.Status, Result = result.Message });
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(MiyousheShopExecution), ex.GetType().Name);
        }
    }

    private async Task<MiyousheShopResult?> ExecuteAsync(MiyousheShopPlan plan, CancellationToken ct)
    {
        bool reserved = false;
        bool sending = false;
        MiyousheShopResult result;
        try
        {
            if (TooLate(plan)) return await FinishAsync(plan, Pending, new(Missed));
            using var prepare = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (plan.Scheduled)
            {
                var limit = plan.OpensAt - DateTimeOffset.UtcNow + DeadlineGrace;
                if (limit <= TimeSpan.Zero) return await FinishAsync(plan, Pending, new(Missed));
                prepare.CancelAfter(limit);
            }

            using var request = await PrepareAsync(plan, prepare.Token);
            if (plan.Scheduled) await WaitUntilAsync(plan.OpensAt - ReserveLead, prepare.Token);

            var gate = _accountGates.GetOrAdd(plan.AccountId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(prepare.Token);
            try
            {
                if (TooLate(plan)) return await FinishAsync(plan, Pending, new(Missed));
                reserved = plan.Scheduled
                    ? await plans.TransitionAsync(plan.Id, Pending, Submitting)
                    : await plans.AddAsync(plan with { Status = Submitting });
                if (!reserved)
                    return await FinishAsync(plan, Pending, new(Failed, "MiyousheShop_Conflict".GetLocalized()));
                if (plan.Scheduled) await WaitUntilAsync(plan.OpensAt, ct);
                ct.ThrowIfCancellationRequested();
                if (TooLate(plan)) result = new(Missed);
                else
                {
                    if (FindAccount(plan.AccountId) == null) throw ShopError("MiyousheShop_LoginRequired");
                    sending = true;
                    result = await client.SubmitAsync(request, ct);
                }

                return await FinishAsync(plan, Submitting, result);
            }
            finally
            {
                gate.Release();
            }
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && ct.IsCancellationRequested && !reserved) return null;
            result = sending ? new(Unknown) :
                TooLate(plan) || ct.IsCancellationRequested ? new(Missed) :
                new(Failed, ex is MiyousheShopException ? ex.Message : "MiyousheShop_PrepareError".GetLocalized());
            try
            {
                return await FinishAsync(plan, reserved ? Submitting : Pending, result);
            }
            catch when (sending)
            {
                return result;
            }
        }
    }

    private async Task<MiyousheShopResult?> FinishAsync(MiyousheShopPlan plan, MiyousheShopPlanStatus expected,
        MiyousheShopResult result)
    {
        if (!plan.Scheduled && expected == Pending) return result;
        if (!plan.Scheduled && result.Status != Unknown)
        {
            await plans.TransitionAsync(plan.Id, expected, result.Status, result.Message);
            await plans.RemoveAsync(plan.Id);
            return result;
        }

        return await plans.TransitionAsync(plan.Id, expected, result.Status, result.Message) ? result : null;
    }

    private async Task<HttpRequestMessage> PrepareAsync(MiyousheShopPlan plan, CancellationToken ct)
    {
        var (account, context) = await GetIdentityAsync(plan.AccountId, ct);
        var goods = await client.GetAccountGoodsAsync(plan.GoodsId, context, account.Stuid, ct);
        if (goods.Type != plan.GoodsType || goods.Game != plan.Game || goods.GameBiz != plan.GameBiz ||
            (goods.End is { } end && end <= plan.OpensAt) ||
            (plan.Scheduled ? !MatchesSale(goods, plan.OpensAt) : !goods.IsAvailable))
            throw ShopError("MiyousheShop_SaleChanged");
        var targets = await client.GetTargetsAsync(goods, context, account.Stuid, ct);
        if (plan.Target is not { } target || !targets.Any(candidate =>
                candidate.Uid == target.Uid && candidate.Region == target.Region &&
                candidate.GameBiz == target.GameBiz && candidate.AddressId == target.AddressId))
            throw ShopError("MiyousheShop_NoTarget");
        return client.PrepareExchange(plan, context, account.Stuid);
    }

    private async Task RecoverAsync()
    {
        await _recoveryGate.WaitAsync();
        try
        {
            if (_recovered) return;
            await plans.RecoverAsync(_startedAt);
            _recovered = true;
        }
        finally
        {
            _recoveryGate.Release();
        }
    }

    private async Task<(AccountEntry Account, AccountContext Context)> GetIdentityAsync(string id, CancellationToken ct)
    {
        var account = FindAccount(id) ?? throw ShopError("MiyousheShop_LoginRequired");
        var context = await identity.BuildAsync(id, ct);
        return (account, context);
    }

    private AccountEntry? FindAccount(string id) =>
        accounts.GetAllAccounts().FirstOrDefault(item => item.Id == id && item.ServerType == "cn" &&
                                                         item.Id == "cn_" + item.Stuid);

    private static bool TooLate(MiyousheShopPlan plan) =>
        plan.Scheduled && DateTimeOffset.UtcNow - plan.OpensAt > DeadlineGrace;

    private static bool MatchesSale(MiyousheShopGoods goods, DateTimeOffset opensAt) =>
        goods.Status is "not_in_sell" or "online" &&
        (goods.NextOpening == opensAt ||
         (goods.IsAvailable && (goods.Restock == opensAt || goods.SaleStart == opensAt)));

    private static async Task WaitUntilAsync(DateTimeOffset time, CancellationToken ct)
    {
        while (time - DateTimeOffset.UtcNow is { } delay && delay > TimeSpan.Zero)
            await Task.Delay(delay, ct);
    }

    private void Wake()
    {
        if (_wake.CurrentCount == 0) _wake.Release();
    }
}