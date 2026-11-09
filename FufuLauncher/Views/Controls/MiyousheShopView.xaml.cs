using System.Collections.ObjectModel;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Services.Miyoushe;
using HtmlAgilityPack;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Launcher = Windows.System.Launcher;

namespace FufuLauncher.Views;

public sealed partial class MiyousheShopView : UserControl
{
    private sealed record DetailSnapshot(MiyousheShopGoods Goods, string Notes, long LoadedAt);
    private const int DetailCacheLifetimeMs = 20_000;

    private readonly MiyousheShopClient _client = App.GetService<MiyousheShopClient>();
    private readonly MiyousheShopPlans _planStore = App.GetService<MiyousheShopPlans>();
    private readonly MiyousheShopExecution _execution = App.GetService<MiyousheShopExecution>();
    private readonly ObservableCollection<MiyousheShopGoods> _goods = [];
    private readonly Dictionary<string, DetailSnapshot> _detailCache = new(StringComparer.Ordinal);
    private readonly DispatcherQueueTimer _countdown;
    private IReadOnlyList<MiyousheShopPlan> _plans = [];
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _listRequest, _detailRequest;
    private MiyousheShopSession? _catalogSession;
    private MiyousheShopGoods? _detail;
    private DateTimeOffset? _nextUiDeadline;
    private int _page;
    private string? _loadedGame;
    private string _game = "all", _accountId = "", _accountName = "";
    private bool _open, _saving;
    private ContentDialog? _dialog;

    internal event Action<IReadOnlyList<MiyousheShopGame>>? GamesLoaded;

    public MiyousheShopView()
    {
        InitializeComponent();
        GoodsList.ItemsSource = _goods;
        GoodsGrid.ItemsSource = _goods;
        _countdown = DispatcherQueue.CreateTimer();
        _countdown.Interval = TimeSpan.FromSeconds(1);
        _countdown.Tick += (_, _) => TickCountdown();
    }

    internal async Task OpenAsync(string game)
    {
        if (_open) return;
        _open = true;
        _game = game;
        _lifetime = new();
        _planStore.Changed += OnPlansChanged;
        _execution.Completed += OnExchangeCompleted;
        ShowPlans(false);
        UpdateOpening();
        await Task.WhenAll(_layoutLoad ??= RestoreLayoutAsync(), LoadPlansAsync(), RefreshAsync());
    }

    internal void Stop()
    {
        _open = false;
        _planStore.Changed -= OnPlansChanged;
        _execution.Completed -= OnExchangeCompleted;
        _countdown.Stop();
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        _dialog?.Hide();
        _listRequest?.Cancel();
        _detailRequest?.Cancel();
        _catalogSession = null;
        _detail = null;
        _detailCache.Clear();
    }

    internal Task RefreshAsync() => PlansPane.Visibility == Visibility.Visible
        ? LoadPlansAsync() : LoadPageAsync(reset: true, renewSession: true);

    internal Task SetGameAsync(string game)
    {
        if (_game == game) return Task.CompletedTask;
        _game = game;
        ShowPlans(false);
        return LoadPageAsync(reset: true);
    }

    internal void SetAccount(string id, string name)
    {
        _accountName = name;
        if (_accountId == id) return;
        _accountId = id;
        _catalogSession = null;
        _detailCache.Clear();
        _loadedGame = null;
        _detailRequest?.Cancel();
        _detail = null;
        UpdateOpening();
        if (_open) _ = LoadPageAsync(reset: true);
    }

    internal bool TryClosePlans()
    {
        if (PlansPane.Visibility != Visibility.Visible) return false;
        ShowPlans(false);
        return true;
    }

    private async Task LoadPageAsync(bool reset, bool renewSession = false)
    {
        if (!_open || (!reset && _listRequest != null)) return;
        string game = _game;
        string accountId = _accountId;
        if (_loadedGame != game)
        {
            _goods.Clear();
            EmptyGoods.Visibility = Visibility.Collapsed;
            MoreGoods.Visibility = Visibility.Collapsed;
        }
        _listRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _listRequest = request;
        int page = reset ? 1 : _page + 1;
        ListProgress.Visibility = Visibility.Visible;
        MoreGoods.IsEnabled = false;
        ShopStatus.IsOpen = false;
        try
        {
            var session = _catalogSession;
            if (accountId.Length > 0 && (renewSession || session == null))
                session = await _execution.GetCatalogSessionAsync(accountId, request.Token);
            var result = await _client.GetPageAsync(game == "all" ? "" : game, page, session, request.Token);
            if (request.IsCancellationRequested) return;
            _catalogSession = session;
            if (result.Games.Count > 0) GamesLoaded?.Invoke(result.Games);
            string? selectedId = (ActiveGoodsView.SelectedItem as MiyousheShopGoods)?.Id;
            if (reset)
            {
                _detailCache.Clear();
                _goods.Clear();
            }
            var seen = _goods.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            int availableIndex = _goods.TakeWhile(item => !item.AccountLimitReached).Count();
            foreach (var item in result.Goods)
            {
                if (!seen.Add(item.Id)) continue;
                if (item.AccountLimitReached) _goods.Add(item);
                else _goods.Insert(availableIndex++, item);
            }
            if (reset) ActiveGoodsView.SelectedItem = _goods.FirstOrDefault(item => item.Id == selectedId) ??
                                                     _goods.FirstOrDefault();
            _page = page;
            _loadedGame = game;
            EmptyGoods.Visibility = _goods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            MoreGoods.Visibility = page * (long)MiyousheShopClient.PageSize < result.Total && result.Goods.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (MiyousheShopException ex)
        {
            if (!request.IsCancellationRequested)
            {
                _catalogSession = null;
                _detailCache.Clear();
                _detailRequest?.Cancel();
                _detail = null;
                UpdateOpening();
                ShowStatus(ex.Message, InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex) when (request.IsCancellationRequested || ex is HttpRequestException or
                                   System.Text.Json.JsonException or InvalidDataException or TaskCanceledException or
                                   IOException or InvalidOperationException)
        {
            if (!request.IsCancellationRequested) ShowError("MiyousheShop_LoadError", ex);
        }
        finally
        {
            if (_listRequest == request)
            {
                _listRequest = null;
                ListProgress.Visibility = Visibility.Collapsed;
                MoreGoods.IsEnabled = true;
            }
        }
    }

    private async void OnGoodsChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingSelection && (ListViewBase)sender == ActiveGoodsView)
            await LoadSelectedGoodsAsync(resetScroll: true);
    }

    private async Task LoadSelectedGoodsAsync(bool resetScroll, bool reportError = true)
    {
        _detailRequest?.Cancel();
        _detailRequest = null;
        DetailProgress.Visibility = Visibility.Collapsed;
        _detail = null;
        if (!_open || ActiveGoodsView.SelectedItem is not MiyousheShopGoods selected)
        {
            DetailsScroll.Visibility = Visibility.Collapsed;
            DetailsPlaceholder.Visibility = Visibility.Visible;
            UpdateOpening();
            return;
        }

        var session = _catalogSession;
        if (_detailCache.TryGetValue(selected.Id, out var cached) &&
            Environment.TickCount64 - cached.LoadedAt < DetailCacheLifetimeMs)
        {
            _detail = cached.Goods;
            ShowGoods(cached.Goods, cached.Notes);
            UpdateOpening();
            if (resetScroll) DetailsScroll.ChangeView(null, 0, null, true);
            return;
        }

        ShowGoods(selected, resetScroll ? "" : GoodsDetails.Text);
        UpdateOpening();
        if (resetScroll) DetailsScroll.ChangeView(null, 0, null, true);
        if (_accountId.Length > 0 && session == null) return;
        using var request = new CancellationTokenSource();
        _detailRequest = request;
        DetailProgress.Visibility = Visibility.Visible;
        try
        {
            var detail = await _client.GetGoodsAsync(selected.Id, session, request.Token);
            if (request.IsCancellationRequested) return;
            string notes = ReadGoodsNotes(detail);
            _detailCache[detail.Id] = new(detail, notes, Environment.TickCount64);
            _detail = detail;
            UpdateCatalogGoods(detail);
            ShowGoods(detail, notes);
            UpdateOpening();
        }
        catch (MiyousheShopException ex)
        {
            if (reportError && !request.IsCancellationRequested) ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
        catch (Exception ex) when (request.IsCancellationRequested || ex is HttpRequestException or
                                   System.Text.Json.JsonException or InvalidDataException or TaskCanceledException)
        {
            if (reportError && !request.IsCancellationRequested) ShowError("MiyousheShop_DetailError", ex);
        }
        finally
        {
            if (_detailRequest == request)
            {
                _detailRequest = null;
                DetailProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void UpdateCatalogGoods(MiyousheShopGoods detail)
    {
        var current = _goods.FirstOrDefault(item => item.Id == detail.Id);
        if (current == null) return;
        var updated = current with
        {
            AccountLimitReached = detail.AccountLimitReached, Status = detail.Status,
            Stock = detail.Stock, Unlimited = detail.Unlimited, SaleStart = detail.SaleStart, Restock = detail.Restock
        };
        if (current == updated) return;

        int index = _goods.IndexOf(current);
        int firstLimited = _goods.TakeWhile(item => !item.AccountLimitReached).Count();
        var selected = ActiveGoodsView.SelectedItem as MiyousheShopGoods;
        _updatingSelection = true;
        try
        {
            _goods[index] = updated;
            if (updated.AccountLimitReached != current.AccountLimitReached)
                _goods.Move(index, updated.AccountLimitReached ? _goods.Count - 1 : firstLimited);
            ActiveGoodsView.SelectedItem = selected?.Id == detail.Id ? updated : selected;
        }
        finally
        {
            _updatingSelection = false;
        }
    }

    private void OnExchangeCompleted(MiyousheShopPlan plan)
    {
        if (plan.Status == MiyousheShopPlanStatus.Succeeded)
            DispatcherQueue.TryEnqueue(async () => await RefreshRedeemedGoodsAsync(plan));
    }

    private async Task RefreshRedeemedGoodsAsync(MiyousheShopPlan plan)
    {
        if (!_open || plan.AccountId != _accountId || _catalogSession is not { } session ||
            !_goods.Any(item => item.Id == plan.GoodsId) || _lifetime == null) return;
        _detailCache.Remove(plan.GoodsId);
        if (ActiveGoodsView.SelectedItem is MiyousheShopGoods selected && selected.Id == plan.GoodsId)
        {
            await LoadSelectedGoodsAsync(resetScroll: false, reportError: false);
            return;
        }

        try
        {
            var detail = await _client.GetGoodsAsync(plan.GoodsId, session, _lifetime.Token);
            if (_open && session == _catalogSession) UpdateCatalogGoods(detail);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or
                                   InvalidDataException or OperationCanceledException or MiyousheShopException)
        {
            DebugLog.Info(nameof(MiyousheShopView), ex.GetType().Name);
        }
    }

    private void ShowGoods(MiyousheShopGoods goods, string notes)
    {
        DetailsScroll.Visibility = Visibility.Visible;
        DetailsPlaceholder.Visibility = Visibility.Collapsed;
        GoodsName.Text = goods.Name;
        if (GoodsImage.DataContext is not MiyousheShopGoods shown || shown.Icon != goods.Icon)
            GoodsImage.DataContext = goods;
        GoodsPrice.Text = goods.Price.ToString("N0");
        GoodsStock.Text = goods.StockText;
        GoodsDetails.Text = notes;
        GoodsNotes.Visibility = notes.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string ReadGoodsNotes(MiyousheShopGoods goods)
    {
        if (string.IsNullOrEmpty(goods.Details)) return HtmlEntity.DeEntitize(goods.Description).Trim();
        var html = new HtmlDocument();
        html.LoadHtml(goods.Details);
        var paragraphs = html.DocumentNode.SelectNodes("//p|//li")?.Select(node => node.InnerText) ??
                         [html.DocumentNode.InnerText];
        return string.Join("\n\n", new[] { goods.Description }.Concat(paragraphs)
            .Select(p => HtmlEntity.DeEntitize(p).Trim()).Where(p => p.Length > 0).Distinct());
    }

    private void UpdateOpening()
    {
        bool alreadyScheduled = false;
        bool canSchedule = false;
        DateTimeOffset? time = null;
        string label = "", countdown = "", quantity = "";
        _nextUiDeadline = null;
        var display = _detail ?? ActiveGoodsView.SelectedItem as MiyousheShopGoods;
        if (display is { NextOpening: { } opening } detail)
        {
            time = opening;
            var remaining = opening - DateTimeOffset.UtcNow;
            bool restock = detail.Status == "online";
            label = (restock ? "MiyousheShop_NextBatchTime" : "MiyousheShop_StartTime").GetLocalized();
            if (restock && detail.RestockQuantity > 0)
                quantity = string.Format("MiyousheShop_NextBatchQuantity".GetLocalized(), detail.RestockQuantity);
            if (remaining > TimeSpan.Zero)
            {
                _nextUiDeadline = opening;
                countdown = FormatCountdown(remaining);
            }
            alreadyScheduled = _plans.Any(item => item.AccountId == _accountId && item.GoodsId == detail.Id &&
                item.OpensAt == opening && item.Status is MiyousheShopPlanStatus.Pending or MiyousheShopPlanStatus.Submitting);
            canSchedule = _detail != null && detail.SupportsExchange && remaining > TimeSpan.Zero &&
                (detail.End is not { } end || opening < end);
        }
        else if (display is { IsAvailable: true, End: { } end })
        {
            time = end;
            _nextUiDeadline = end;
            label = "MiyousheShop_EndTime".GetLocalized();
        }

        GoodsTiming.Visibility = time.HasValue ? Visibility.Visible : Visibility.Collapsed;
        GoodsOpeningLabel.Text = label;
        GoodsOpening.Text = time?.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd HH:mm:ss") ?? "";
        GoodsRestockQuantity.Text = quantity;
        GoodsRestockQuantity.Visibility = quantity.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        GoodsCountdown.Text = countdown;
        GoodsCountdown.Visibility = countdown.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        ScheduleButton.Visibility = canSchedule ? Visibility.Visible : Visibility.Collapsed;
        ScheduleButton.IsEnabled = canSchedule && !alreadyScheduled && !_saving;
        ScheduleButton.Content = (alreadyScheduled ? "MiyousheShop_Scheduled" : "MiyousheShop_Schedule").GetLocalized();
        ExchangeButton.Visibility = _detail is { SupportsExchange: true, IsAvailable: true }
            ? Visibility.Visible : Visibility.Collapsed;
        ExchangeButton.IsEnabled = !_saving;
        UpdateCountdownTimer();
    }

    private void UpdateCountdownTimer()
    {
        bool running = _open && _nextUiDeadline.HasValue && CatalogPane.Visibility == Visibility.Visible;
        if (running && !_countdown.IsRunning) _countdown.Start();
        else if (!running && _countdown.IsRunning) _countdown.Stop();
    }

    private void TickCountdown()
    {
        if (_nextUiDeadline is not { } deadline) return;
        var remaining = deadline - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero) UpdateOpening();
        else if (GoodsCountdown.Visibility == Visibility.Visible) GoodsCountdown.Text = FormatCountdown(remaining);
    }

    private static string FormatCountdown(TimeSpan remaining)
    {
        var seconds = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));
        return string.Format("MiyousheShop_Countdown".GetLocalized(),
            seconds.Days, seconds.Hours, seconds.Minutes, seconds.Seconds);
    }

    private async void OnSchedule(object sender, RoutedEventArgs e) => await ConfirmExchangeAsync(scheduled: true);

    private async void OnExchangeNow(object sender, RoutedEventArgs e) => await ConfirmExchangeAsync(scheduled: false);

    private async Task ConfirmExchangeAsync(bool scheduled)
    {
        if (_detail is not { SupportsExchange: true } detail || _saving || _lifetime == null) return;
        if (_accountId.Length == 0)
        {
            ShowError("MiyousheShop_LoginRequired");
            return;
        }
        var opens = scheduled ? detail.NextOpening : DateTimeOffset.UtcNow;
        if (opens is not { } opening || (scheduled ? opening <= DateTimeOffset.UtcNow : !detail.IsAvailable)) return;
        string id = _accountId, name = _accountName;
        var ct = _lifetime.Token;
        _saving = true;
        UpdateOpening();
        try
        {
            var targets = await _execution.GetTargetsAsync(id, detail, ct);
            if (ct.IsCancellationRequested || _accountId != id) return;
            if (targets.Count == 0)
            {
                ShowError("MiyousheShop_NoTarget");
                return;
            }
            var selector = new ComboBox
            {
                Header = "MiyousheShop_Target".GetLocalized(), ItemsSource = targets,
                SelectedIndex = targets.Count == 1 ? 0 : -1, HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var content = new StackPanel { Spacing = 10 };
            content.Children.Add(new TextBlock
            {
                Text = string.Format("MiyousheShop_ConfirmItem".GetLocalized(), detail.Name, detail.PriceText, name),
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(new TextBlock
            {
                Text = scheduled
                    ? string.Format("MiyousheShop_ConfirmScheduled".GetLocalized(), opening.ToOffset(TimeSpan.FromHours(8)))
                    : "MiyousheShop_ConfirmImmediate".GetLocalized(),
                TextWrapping = TextWrapping.Wrap
            });
            content.Children.Add(selector);
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Content = content,
                Title = (scheduled ? "MiyousheShop_Schedule" : "MiyousheShop_ExchangeNow").GetLocalized(),
                PrimaryButtonText = "MiyousheShop_Confirm".GetLocalized(),
                CloseButtonText = "MiyousheShop_Cancel".GetLocalized(),
                IsPrimaryButtonEnabled = selector.SelectedIndex >= 0
            };
            selector.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = selector.SelectedIndex >= 0;
            if (!await ShowConfirmationAsync(dialog) || ct.IsCancellationRequested || _accountId != id ||
                selector.SelectedItem is not MiyousheShopTarget target) return;
            var plan = new MiyousheShopPlan(Guid.NewGuid(), detail.Id, detail.Name, opening, id, name, detail.Type,
                detail.Game, detail.GameBiz, target, scheduled, MiyousheShopPlanStatus.Pending);
            if (scheduled)
            {
                if (await _execution.ScheduleAsync(plan))
                    ShowStatus("MiyousheShop_Scheduled".GetLocalized(), InfoBarSeverity.Success);
                else
                    ShowError("MiyousheShop_Conflict");
                return;
            }

            var result = await _execution.ExchangeNowAsync(plan);
            var status = ("MiyousheShop_Plan" + result.Status).GetLocalized();
            if (!ct.IsCancellationRequested && _accountId == id)
                ShowStatus(string.IsNullOrEmpty(result.Message) ? status : status + " · " + result.Message,
                    result.Status == MiyousheShopPlanStatus.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
            if (result.Status == MiyousheShopPlanStatus.Succeeded)
                await RefreshRedeemedGoodsAsync(plan);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (MiyousheShopException ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowError("MiyousheShop_PrepareError", ex);
        }
        finally
        {
            _saving = false;
            if (_open) UpdateOpening();
        }
    }

    private async Task<bool> ShowConfirmationAsync(ContentDialog dialog)
    {
        if (_dialog != null || !_open) return false;
        _dialog = dialog;
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _dialog = null;
        }
    }

    private async void OnRemovePlan(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MiyousheShopPlan plan }) return;
        try
        {
            await _planStore.RemoveAsync(plan.Id);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            ShowError("MiyousheShop_PlanError", ex);
        }
    }

    private async void OnResolvePlan(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MiyousheShopPlan { CanResolve: true } plan }) return;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "MiyousheShop_Resolve".GetLocalized(),
                Content = "MiyousheShop_ResolveHint".GetLocalized(),
                PrimaryButtonText = "MiyousheShop_Confirm".GetLocalized(),
                CloseButtonText = "MiyousheShop_Cancel".GetLocalized()
            };
            if (await ShowConfirmationAsync(dialog))
                await _planStore.TransitionAsync(plan.Id, MiyousheShopPlanStatus.Unknown, MiyousheShopPlanStatus.Reviewed);
        }
        catch (Exception ex)
        {
            ShowError("MiyousheShop_PlanError", ex);
        }
    }

    private void OnPlansChanged() => DispatcherQueue.TryEnqueue(async () => await LoadPlansAsync());

    private async Task LoadPlansAsync()
    {
        try
        {
            var plans = await _planStore.GetAsync();
            if (!_open) return;
            _plans = plans;
            if (PlansPane.Visibility == Visibility.Visible) RenderPlans();
            UpdateOpening();
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            ShowError("MiyousheShop_PlanError", ex);
        }
    }

    private void RenderPlans()
    {
        var displayed = _plans.Where(item => item.Scheduled || item.CanResolve ||
            item.Status == MiyousheShopPlanStatus.Submitting).OrderBy(item => item.Status)
            .ThenBy(item => item.OpensAt).ToArray();
        PlansList.ItemsSource = displayed;
        EmptyPlans.Visibility = displayed.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTogglePlans(object sender, RoutedEventArgs e) => ShowPlans(PlansPane.Visibility != Visibility.Visible);

    private void ShowPlans(bool visible)
    {
        PlansPane.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        CatalogPane.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        ViewModeControls.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
        PlansButton.Content = (visible ? "MiyousheShop_BackToGoods" : "MiyousheShop_Plans").GetLocalized();
        if (visible) RenderPlans();
        UpdateCountdownTimer();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await RefreshAsync();
    private async void OnLoadMore(object sender, RoutedEventArgs e) => await LoadPageAsync(reset: false);
    private async void OnOpenSelectedGoods(object sender, RoutedEventArgs e)
    {
        if (ActiveGoodsView.SelectedItem is MiyousheShopGoods item)
            await OpenOfficialAsync(MiyousheShopClient.OfficialUri(item.Id));
    }

    private async void OnOpenPlanGoods(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: MiyousheShopPlan item })
            await OpenOfficialAsync(item.CanResolve || item.Status == MiyousheShopPlanStatus.Succeeded
                ? MiyousheShopClient.OfficialHistoryUri : MiyousheShopClient.OfficialUri(item.GoodsId));
    }

    private async Task OpenOfficialAsync(Uri uri)
    {
        try
        {
            if (await Launcher.LaunchUriAsync(uri)) return;
        }
        catch (Exception ex)
        {
            ShowError("MiyousheShop_OpenError", ex);
            return;
        }
        ShowError("MiyousheShop_OpenError");
    }

    private void ShowError(string key, Exception? ex = null)
    {
        if (ex != null) DebugLog.Info(nameof(MiyousheShopView), ex.GetType().Name);
        ShowStatus(key.GetLocalized(), InfoBarSeverity.Error);
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        if (!_open) return;
        ShopStatus.Severity = severity;
        ShopStatus.Message = message;
        ShopStatus.IsOpen = true;
    }
}
