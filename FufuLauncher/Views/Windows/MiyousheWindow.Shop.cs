using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Views;

public sealed partial class MiyousheWindow
{
    private bool _shopOpen;
    private IReadOnlyList<CommunityGame> _communityGames = [];
    private CommunityGame? _communityGame;
    private IReadOnlyList<MiyousheShopGame> _shopGames = [];
    private string? _shopGameKey;

    private async void OnOpenShop(object sender, RoutedEventArgs args)
    {
        if (_shopOpen) return;
        _shopOpen = true;
        CommunityContentPane.Visibility = Visibility.Collapsed;
        ShopPane.Visibility = Visibility.Visible;
        _shopGameKey ??= _communityGame?.Slug switch
        {
            "ys" => "hk4e",
            "sr" => "hkrpg",
            "zzz" => "nap",
            "bh3" => "bh3",
            "bh2" => "bh2",
            "wd" => "nxx",
            _ => "all"
        };
        SetShopGames(_shopGames);
        UpdateShopAccount();
        await ShopPane.OpenAsync(_shopGameKey);
    }

    private void CloseShop()
    {
        if (!_shopOpen) return;
        _shopOpen = false;
        ShopPane.Stop();
        ShopPane.Visibility = Visibility.Collapsed;
        SetCommunityGames(_communityGames, _communityGame?.Id ?? 2);
        CommunityContentPane.Visibility = Visibility.Visible;
        UpdateNavigationSelection();
    }

    private void SetCommunityGames(IReadOnlyList<CommunityGame> games, int preferred)
    {
        _communityGames = games;
        _communityGame = games.FirstOrDefault(game => game.Id == preferred) ?? games.FirstOrDefault();
        if (_shopOpen) return;
        SetGameSelector(games, _communityGame, "MiyousheShop_CommunityGame");
    }

    private void SetShopGames(IReadOnlyList<MiyousheShopGame> games)
    {
        if (!_shopOpen) return;
        var all = new MiyousheShopGame("all", "MiyousheShop_AllGoods".GetLocalized());
        var categories = new[] { all }.Concat(games.Where(game => game.Key != "all")).ToArray();
        if (GameSelector.SelectedItem is MiyousheShopGame && _shopGames.SequenceEqual(categories)) return;
        _shopGames = categories;
        var selected = _shopGames.FirstOrDefault(game => game.Key == _shopGameKey);
        if (selected == null && _shopGameKey is { } key)
            selected = new(key, _communityGame?.Name ?? all.Name);
        var choices = selected != null && !_shopGames.Contains(selected)
            ? _shopGames.Append(selected).ToArray()
            : _shopGames;
        SetGameSelector(choices, selected ?? all, "MiyousheShop_GameFilter");
    }

    private void SetGameSelector(object items, object? selected, string label)
    {
        bool wasChanging = _changingSelectors;
        _changingSelectors = true;
        GameSelector.Header = label.GetLocalized();
        GameSelector.ItemsSource = items;
        GameSelector.SelectedItem = selected;
        _changingSelectors = wasChanging;
    }

    private void UpdateShopAccount()
    {
        if (_shopOpen && AccountSelector.SelectedItem is Choice choice)
            ShopPane.SetAccount(choice.Id, choice.Label);
    }
}