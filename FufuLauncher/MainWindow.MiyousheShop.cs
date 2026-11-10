using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Services.Miyoushe;
using H.NotifyIcon.Core;

namespace FufuLauncher;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _shopPlanLifetime;

    internal void StartMiyousheShopPlans()
    {
        if (_shopPlanLifetime != null) return;
        _shopPlanLifetime = new();
        var ct = _shopPlanLifetime.Token;
        var execution = App.GetService<MiyousheShopExecution>();
        execution.Completed += OnShopPlanCompleted;
        _ = Task.Run(async () =>
        {
            try
            {
                await execution.RunAsync(ct);
            }
            catch (Exception ex)
            {
                DebugLog.Info(nameof(MiyousheShopExecution), ex.GetType().Name);
            }
            finally
            {
                execution.Completed -= OnShopPlanCompleted;
            }
        });
    }

    private void OnShopPlanCompleted(MiyousheShopPlan plan) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_isExit) return;
        try
        {
            TrayIcon.ShowNotification("MiyousheShop_Notification".GetLocalized(),
                $"{plan.GoodsName} · {plan.StatusText}", NotificationIcon.Info);
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(MiyousheShopExecution), ex.GetType().Name);
        }
    });
}
