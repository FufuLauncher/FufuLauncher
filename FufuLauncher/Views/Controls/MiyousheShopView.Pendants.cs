using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.Miyoushe;
using FufuLauncher.Services.Miyoushe;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Views;

public sealed partial class MiyousheShopView
{
    private enum ShopPaneKind
    {
        Catalog,
        Plans,
        Pendants
    }

    private const int TileImageWidth = 240;
    private const int PreviewImageWidth = 720;
    private const int TileDecodeWidth = 120;
    private const int PreviewDecodeWidth = 320;

    private readonly ObservableCollection<MiyoushePendantItem> _pendants = [];
    private readonly Dictionary<string, ImageSource> _pendantIcons = new(StringComparer.Ordinal);
    private IReadOnlyList<MiyoushePendant> _allPendants = [];
    private CancellationTokenSource? _pendantRequest, _pendantSave;
    private MiyoushePendantItem? _pendant;
    private ShopPaneKind _pane = ShopPaneKind.Catalog;
    private bool _pendantsLoaded, _pendantSaving, _renderingPendants;
    private int _previewVersion;

    private bool PendantsVisible => _pane == ShopPaneKind.Pendants;

    private async void OnTogglePendants(object sender, RoutedEventArgs e)
    {
        if (PendantsVisible)
        {
            ShowPane(ShopPaneKind.Catalog);
            return;
        }

        ShowPane(ShopPaneKind.Pendants);
        await LoadPendantsAsync();
    }

    private void OnOwnedOnlyChanged(object sender, RoutedEventArgs e) => RenderPendants();

    private async Task LoadPendantsAsync(bool renew = false)
    {
        if (!_open || _lifetime == null || (_pendantsLoaded && !renew)) return;
        if (_accountId.Length == 0)
        {
            ShowError("MiyoushePendant_LoginRequired");
            return;
        }

        _pendantRequest?.Cancel();
        using var request = new CancellationTokenSource();
        _pendantRequest = request;
        var ct = request.Token;
        PendantProgress.Visibility = Visibility.Visible;
        try
        {
            var session = await _execution.GetCatalogSessionAsync(_accountId, ct);
            using var client = new MiyousheClient(session.Context);
            var pendants = await client.GetPendantsAsync(ct);
            if (ct.IsCancellationRequested) return;
            string avatar = "";
            try
            {
                avatar = await client.GetMyAvatarAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                DebugLog.Info(nameof(MiyousheShopView), ex.GetType().Name);
            }

            if (ct.IsCancellationRequested) return;
            _allPendants = pendants;
            _pendantsLoaded = true;
            ShowAvatar(avatar);
            RenderPendants();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (CommunityApiException ex)
        {
            _pendantsLoaded = false;
            _allPendants = [];
            RenderPendants();
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
        catch (MiyousheShopException ex)
        {
            _pendantsLoaded = false;
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidDataException or IOException
                                       or TaskCanceledException or OperationCanceledException
                                       or InvalidOperationException)
        {
            if (!ct.IsCancellationRequested) ShowError("MiyoushePendant_LoadError", ex);
        }
        finally
        {
            if (_pendantRequest == request)
            {
                _pendantRequest = null;
                PendantProgress.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void RenderPendants()
    {
        bool ownedOnly = OwnedOnlyCheck.IsChecked != false;
        var items = _allPendants.Where(item => !ownedOnly || item.Owned)
            .OrderByDescending(item => item.Owned).ThenByDescending(item => item.IsUsed)
            .ThenByDescending(item => item.Available)
            .Select(pendant => new MiyoushePendantItem(pendant)
            {
                Icon = _pendantIcons.GetValueOrDefault(pendant.Id)
            }).ToArray();
        _renderingPendants = true;
        try
        {
            _pendants.Clear();
            foreach (var item in items) _pendants.Add(item);
            PendantGrid.ItemsSource = _pendants;
            PendantGrid.Visibility = items.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyPendants.Visibility = items.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            string selectedId = _pendant?.Id ?? "";
            if (selectedId.Length == 0) selectedId = items.FirstOrDefault(item => item.IsUsed)?.Id ?? "";
            PendantGrid.SelectedItem = items.FirstOrDefault(item => item.Id == selectedId);
        }
        finally
        {
            _renderingPendants = false;
        }

        ShowPendant(PendantGrid.SelectedItem as MiyoushePendantItem);
        _ = LoadPendantIconsAsync(items);
    }

    private async Task LoadPendantIconsAsync(IReadOnlyList<MiyoushePendantItem> items)
    {
        var ct = _lifetime?.Token ?? CancellationToken.None;
        var pending = items.Where(item => item.Icon == null).ToArray();
        for (int round = 0; round < 3 && pending.Length > 0 && !ct.IsCancellationRequested; round++)
        {
            if (round > 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * round), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            await Task.WhenAll(pending.Select(item => LoadPendantIconAsync(item, ct)));
            pending = pending.Where(item => item.Icon == null).ToArray();
        }

        if (ct.IsCancellationRequested) return;
        foreach (var item in pending)
        {
            if (!CommunityContent.TryWebUri(item.Pendant.Url, out var uri)) continue;
            var bitmap = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = TileDecodeWidth, UriSource = uri
            };
            _pendantIcons[item.Id] = bitmap;
            foreach (var current in _pendants.Where(current => current.Id == item.Id)) current.Icon = bitmap;
        }
    }

    private async Task LoadPendantIconAsync(MiyoushePendantItem item, CancellationToken ct)
    {
        try
        {
            var bytes = await MiyoushePendantImages.GetAsync(
                MiyoushePendantImages.Thumbnail(item.Pendant.Url, TileImageWidth), ct);
            if (bytes == null || ct.IsCancellationRequested) return;
            var bitmap = await DecodeAsync(bytes, TileDecodeWidth);
            if (bitmap == null || ct.IsCancellationRequested) return;
            _pendantIcons[item.Id] = bitmap;
            foreach (var current in _pendants.Where(current => current.Id == item.Id)) current.Icon = bitmap;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info(nameof(MiyousheShopView), ex.GetType().Name);
        }
    }

    private void OnPendantChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_renderingPendants) return;
        ShowPendant(PendantGrid.SelectedItem as MiyoushePendantItem);
    }

    private void ShowPendant(MiyoushePendantItem? pendant)
    {
        string previousId = _pendant?.Id ?? "";
        _pendant = pendant;
        if (pendant == null)
        {
            _previewVersion++;
            PendantPreview.Source = null;
            PendantDetailsScroll.Visibility = Visibility.Collapsed;
            PendantPlaceholder.Visibility = Visibility.Visible;
            UpdatePendantButtons();
            return;
        }

        PendantDetailsScroll.Visibility = Visibility.Visible;
        PendantPlaceholder.Visibility = Visibility.Collapsed;
        PendantName.Text = pendant.Name;
        PendantDescription.Text = pendant.Pendant.Description;
        PendantDescription.Visibility =
            pendant.Pendant.Description.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendantAuthor.Text = pendant.Pendant.AuthorText;
        PendantAuthor.Visibility = pendant.Pendant.AuthorText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendantRemain.Text = pendant.Pendant.RemainText;
        PendantRemain.Visibility = pendant.Pendant.RemainText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        PendantState.Text = pendant.Pendant.StateText;
        PendantStateBadge.Visibility = pendant.Pendant.StateText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (previousId != pendant.Id) PendantPreview.Source = null;
        _ = LoadPendantPreviewAsync(pendant);
        UpdatePendantButtons();
    }

    private async Task LoadPendantPreviewAsync(MiyoushePendantItem pendant)
    {
        int version = ++_previewVersion;
        var ct = _lifetime?.Token ?? CancellationToken.None;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (attempt > 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            try
            {
                var bytes = await MiyoushePendantImages.GetAsync(
                    MiyoushePendantImages.Thumbnail(pendant.Pendant.Url, PreviewImageWidth), ct);
                if (ct.IsCancellationRequested || version != _previewVersion) return;
                if (bytes == null) continue;
                var bitmap = await DecodeAsync(bytes, PreviewDecodeWidth);
                if (ct.IsCancellationRequested || version != _previewVersion) return;
                if (bitmap == null) continue;
                PendantPreview.Source = bitmap;
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                DebugLog.Info(nameof(MiyousheShopView), ex.GetType().Name);
            }
        }

        if (version == _previewVersion && !ct.IsCancellationRequested &&
            CommunityContent.TryWebUri(pendant.Pendant.Url, out var uri))
            PendantPreview.Source = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = PreviewDecodeWidth, UriSource = uri
            };
    }

    private static async Task<BitmapImage?> DecodeAsync(byte[] bytes, int width)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var stream = new MemoryStream(bytes).AsRandomAccessStream();
                var bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = width };
                await bitmap.SetSourceAsync(stream);
                return bitmap;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (attempt == 1) DebugLog.Info(nameof(MiyousheShopView), ex.GetType().Name);
            }
        }

        return null;
    }

    private void ShowAvatar(string url)
    {
        if (CommunityContent.TryWebUri(url, out var uri))
        {
            PendantAvatarBrush.ImageSource = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = 320, UriSource = uri
            };
            PendantAvatarFallback.Visibility = Visibility.Collapsed;
        }
        else
        {
            PendantAvatarBrush.ImageSource = null;
            PendantAvatarFallback.Visibility = Visibility.Visible;
        }
    }

    private void UpdatePendantButtons()
    {
        if (EquipPendantButton == null) return;
        EquipPendantButton.Visibility = _pendant is { Available: true, IsUsed: false }
            ? Visibility.Visible
            : Visibility.Collapsed;
        EquipPendantButton.IsEnabled = !_pendantSaving;
        RemovePendantButton.Visibility = !_pendantSaving && _allPendants.Any(item => item.IsUsed)
            ? Visibility.Visible
            : Visibility.Collapsed;
        RemovePendantButton.IsEnabled = !_pendantSaving;
        PendantSaveProgress.Visibility = _pendantSaving ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnEquipPendant(object sender, RoutedEventArgs e)
    {
        if (_pendant is { Available: true, IsUsed: false } pendant) await ApplyPendantAsync(pendant.Id);
    }

    private async void OnRemovePendant(object sender, RoutedEventArgs e) => await ApplyPendantAsync("0");

    private async Task ApplyPendantAsync(string id)
    {
        if (_pendantSaving || !_open || _lifetime == null) return;
        if (_accountId.Length == 0)
        {
            ShowError("MiyoushePendant_LoginRequired");
            return;
        }

        _pendantSave?.Cancel();
        using var save = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _pendantSave = save;
        _pendantSaving = true;
        UpdatePendantButtons();
        try
        {
            var session = await _execution.GetCatalogSessionAsync(_accountId, save.Token);
            using var client = new MiyousheClient(session.Context);
            await client.SetPendantAsync(id, save.Token);
            if (save.IsCancellationRequested) return;
            _allPendants = _allPendants.Select(item => item with { IsUsed = item.Id == id }).ToArray();
            RenderPendants();
            ShowStatus((id == "0" ? "MiyoushePendant_Removed" : "MiyoushePendant_Updated").GetLocalized(),
                InfoBarSeverity.Success);
            _pendantsLoaded = false;
            await LoadPendantsAsync(renew: true);
        }
        catch (OperationCanceledException) when (save.IsCancellationRequested)
        {
        }
        catch (CommunityApiException ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
        catch (MiyousheShopException ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowError("MiyoushePendant_UpdateError", ex);
        }
        finally
        {
            if (_pendantSave == save) _pendantSave = null;
            _pendantSaving = false;
            if (_open)
            {
                UpdatePendantButtons();
                if (_pendant != null)
                    PendantGrid.SelectedItem = _pendants.FirstOrDefault(item => item.Id == _pendant.Id);
            }
        }
    }

    private bool TryClosePane()
    {
        if (_pane == ShopPaneKind.Catalog) return false;
        ShowPane(ShopPaneKind.Catalog);
        return true;
    }

    private void ResetPendants()
    {
        _pendantRequest?.Cancel();
        _pendantSave?.Cancel();
        _previewVersion++;
        _pendant = null;
        _pendantsLoaded = false;
        _allPendants = [];
        _pendants.Clear();
        if (PendantGrid == null) return;
        PendantGrid.ItemsSource = _pendants;
        PendantGrid.SelectedItem = null;
        ShowPendant(null);
    }
}