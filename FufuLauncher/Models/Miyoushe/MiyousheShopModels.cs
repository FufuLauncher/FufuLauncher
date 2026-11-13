using System.Text.Json;
using System.Text.Json.Serialization;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models.Miyoushe;

public sealed record MiyousheShopGame(string Key, string Name)
{
    public override string ToString() => Name;
}

public sealed record MiyousheShopGoods(
    string Id,
    string Name,
    string Icon,
    long Price,
    long Stock,
    bool Unlimited,
    string Status,
    DateTimeOffset? SaleStart,
    DateTimeOffset? Restock,
    long RestockQuantity,
    DateTimeOffset? End,
    string Description,
    string Details,
    long Type,
    string Game,
    string GameBiz)
{
    public bool AccountLimitReached
    {
        get;
        init;
    }

    public bool IsAvailable => !AccountLimitReached && Status == "online" && (Unlimited || Stock > 0) &&
                               (End is not { } end || end > DateTimeOffset.UtcNow);

    public bool RequiresAddress => Type is 1 or 4;

    public bool RequiresRole => Type == 2 && Game != "bbs";

    public bool SupportsExchange => Type is >= 1 and <= 6 && (!RequiresRole || GameBiz.Length > 0);

    public DateTimeOffset? NextOpening => Status switch
    {
        _ when AccountLimitReached => null,
        "not_in_sell" => SaleStart ?? Restock,
        "online" when !Unlimited && Stock == 0 => Restock,
        _ => null
    };

    public string PriceText => string.Format("MiyousheShop_Price".GetLocalized(), Price);

    public string CatalogTimingText => NextOpening is { } opening
        ? string.Format(
            (Status == "online" ? "MiyousheShop_CatalogRestock" : "MiyousheShop_CatalogOpening").GetLocalized(),
            opening.ToOffset(TimeSpan.FromHours(8)))
        : "";

    public string StockText => Status switch
    {
        _ when AccountLimitReached => "MiyousheShop_LimitReachedLabel".GetLocalized(),
        "not_in_sell" => "MiyousheShop_Upcoming".GetLocalized(),
        "online" when Unlimited => "MiyousheShop_Unlimited".GetLocalized(),
        "online" when Stock > 0 => string.Format("MiyousheShop_Stock".GetLocalized(), Stock),
        "online" => "MiyousheShop_SoldOut".GetLocalized(),
        _ => "MiyousheShop_Unavailable".GetLocalized()
    };

    internal static MiyousheShopGoods Parse(JsonElement item, bool authenticated) => new(
        item.Text("goods_id"), item.Text("goods_name"),
        item.Text("icon") is { Length: > 0 } icon ? icon : item.Get("cover").Text("url"), item.Number("price"),
        item.Number("total"), item.Flag("unlimit"), item.Text("status"),
        ReadTime(item, "sale_start_time") ?? ReadTime(item, "start"), ReadTime(item, "next_time"),
        item.Number("next_num"), ReadTime(item, "end"), item.Text("goods_desc"), item.Text("goods_detail"),
        item.Number("type"), item.Text("game"), item.Text("game_biz"))
    {
        AccountLimitReached = authenticated && item.Text("account_cycle_type") != "not_limit" &&
                              item.Number("account_cycle_limit") is > 0 and var limit &&
                              item.Number("account_exchange_num") >= limit
    };

    private static DateTimeOffset? ReadTime(JsonElement item, string key)
    {
        long seconds = item.Number(key);
        return seconds is > 0 and <= 253402300799 ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }
}

public sealed record MiyousheShopPage(
    IReadOnlyList<MiyousheShopGoods> Goods,
    IReadOnlyList<MiyousheShopGame> Games,
    long Total);

public enum MiyousheShopPlanStatus
{
    Pending,
    Submitting,
    Succeeded,
    Failed,
    Verification,
    Unknown,
    Missed,
    Reviewed = 8
}

public sealed record MiyousheShopTarget(string Label, string Uid, string Region, string GameBiz, long AddressId)
{
    public override string ToString() => Label;
}

public sealed record MiyousheShopPlan(
    Guid Id,
    string GoodsId,
    string GoodsName,
    DateTimeOffset OpensAt,
    string AccountId,
    string AccountName,
    long GoodsType,
    string Game,
    string GameBiz,
    MiyousheShopTarget? Target,
    bool Scheduled,
    MiyousheShopPlanStatus Status,
    string Result = "")
{
    [JsonIgnore]
    public string TimeText => string.Format("MiyousheShop_BeijingTime".GetLocalized(),
        OpensAt.ToOffset(TimeSpan.FromHours(8)));

    [JsonIgnore] public string StatusText => ("MiyousheShop_Plan" + Status).GetLocalized();

    [JsonIgnore] public string RecipientText => Target == null ? "" : $"{AccountName} · {Target.Label}";

    [JsonIgnore]
    public bool CanRemove => Status is not (MiyousheShopPlanStatus.Submitting or MiyousheShopPlanStatus.Unknown);

    [JsonIgnore] public bool CanResolve => Status == MiyousheShopPlanStatus.Unknown;

    [JsonIgnore]
    public string OfficialLinkText => (CanResolve || Status == MiyousheShopPlanStatus.Succeeded
        ? "MiyousheShop_OfficialHistory"
        : "MiyousheShop_OfficialGoods").GetLocalized();
}