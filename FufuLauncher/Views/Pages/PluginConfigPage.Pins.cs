using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PluginConfigPage
{
    private readonly PluginConfigPinStore _pinStore = new(App.GetService<ILocalSettingsService>());
    private readonly SemaphoreSlim _pinActionGate = new(1, 1);

    private async Task<HashSet<string>> LoadPinnedOptionsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await _pinStore.ReadAsync(path, cancellationToken);
        }
        catch (JsonException ex)
        {
            DebugLog.Info("PluginConfigPage.Pins", $"[PluginConfig] Cannot read favorites: {ex.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async void OnPinOptionClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: PluginConfigOption option })
        {
            await ToggleOptionPinAsync(option);
        }
    }

    private async Task ToggleOptionPinAsync(PluginConfigOption option)
    {
        var configuration = _configuration;
        var path = _loadedConfigPath;
        await _pinActionGate.WaitAsync();
        try
        {
            if (!_isActive || !_isInitialized || _isLoading || path == null || configuration == null ||
                !ReferenceEquals(_configuration, configuration) || !configuration.Options.Contains(option)) return;

            var pinned = !option.IsPinned;
            if (!await _pinStore.SetPinnedAsync(path, option.SectionName, pinned)) return;
            if (!ReferenceEquals(_configuration, configuration) || !_isActive || _isLoading) return;

            foreach (var item in configuration.Options.Where(item =>
                         item.SectionName.Equals(option.SectionName, StringComparison.OrdinalIgnoreCase)))
            {
                item.IsPinned = pinned;
            }
            UpdateVisibleOptions(resetScroll: false);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginConfigPage.Pins", $"[PluginConfig] Cannot save favorite: {ex.Message}");
            if (_isActive && ReferenceEquals(_configuration, configuration))
            {
                ShowError("PluginConfigPage_SaveFailed".GetLocalized(), ex.Message, () => ToggleOptionPinAsync(option));
            }
        }
        finally
        {
            _pinActionGate.Release();
        }
    }
}
