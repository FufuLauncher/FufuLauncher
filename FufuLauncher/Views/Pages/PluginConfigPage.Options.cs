/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PluginConfigPage
{
    private async Task LoadConfigAsync()
    {
        StopGamepadWatcher();
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _loadCancellation = cancellation;
        _isLoading = true;
        _isInitialized = false;
        _autoSaveTimer.Stop();

        if (_configuration != null)
        {
            foreach (var option in _configuration.Options)
            {
                option.PropertyChanged -= OnOptionPropertyChanged;
            }
        }

        _configuration = null;
        _loadedConfigPath = null;
        _saveTask = null;
        _retryAction = null;
        _hasSaveError = false;
        _pinnedOptions.Clear();
        _regularOptions.Clear();
        _infoItems.Clear();
        ConfigMessageBar.IsOpen = false;
        ConfigLoadErrorBar.IsOpen = false;
        ConfigLockInfoBar.IsOpen = false;
        ConfigLoadErrorOverlay.Visibility = Visibility.Collapsed;
        SettingsSearchBox.IsEnabled = false;
        ReloadButton.IsEnabled = false;
        SaveStatusPanel.Visibility = Visibility.Collapsed;
        OptionCountTextBlock.Text = "PluginConfigPage_Title".GetLocalized();
        SettingsScrollViewer.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Collapsed;
        LoadingPanel.Visibility = Visibility.Visible;
        LoadingRing.IsActive = true;

        try
        {
            var path = _pluginItem?.ConfigFilePath;
            if (string.IsNullOrWhiteSpace(path) || !ConfigurationFileExists(path))
            {
                throw new FileNotFoundException(string.Format(
                    CultureInfo.CurrentCulture, "PluginConfigPage_MissingFile".GetLocalized(), path));
            }

            var configuration = await PluginConfiguration.LoadAsync(path, cancellation.Token);
            var pinnedSections = await LoadPinnedOptionsAsync(path, cancellation.Token);
            foreach (var option in configuration.Options) option.IsPinned = pinnedSections.Contains(option.SectionName);
            if (configuration.Options.Any(option => option.Kind == PluginConfigValueKind.Key))
            {
                var keyPreference = await App.GetService<ILocalSettingsService>().ReadSettingAsync("UseKeyListInput");
                foreach (var option in configuration.Options)
                    option.SetKeyInputMode(keyPreference == null || Convert.ToBoolean(keyPreference));
            }

            if (cancellation.IsCancellationRequested || !_isActive)
            {
                return;
            }

            _configuration = configuration;
            _loadedConfigPath = path;
            StartGamepadWatcher();
            foreach (var option in configuration.Options)
            {
                option.PropertyChanged += OnOptionPropertyChanged;
            }

            foreach (var item in configuration.GeneralInfo)
            {
                _infoItems.Add(new PluginConfigInfoItem(item.Key, item.Value));
            }

            UpdatePluginHeader(configuration.GeneralInfo);
            SetSaveStatus("PluginConfigPage_AutoSaveHint".GetLocalized(), "\uE73E");
            SaveStatusPanel.Visibility = configuration.Options.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateVisibleOptions();
            _isInitialized = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginConfigPage.Options", $"[PluginConfig] Load failed: {ex}");
            if (!cancellation.IsCancellationRequested && _isActive)
            {
                var details = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
                var message = string.Format(CultureInfo.CurrentCulture,
                    "PluginConfigPage_LoadFailedMessage".GetLocalized(), details);
                message += Environment.NewLine + "PluginConfigPage_LoadFailedHint".GetLocalized();
                ShowLoadError("PluginConfigPage_LoadFailed".GetLocalized(), message, LoadConfigAsync);
            }
        }
        finally
        {
            if (ReferenceEquals(_loadCancellation, cancellation))
            {
                _loadCancellation = null;
            }

            cancellation.Dispose();
            if (_isActive)
            {
                _isLoading = false;
                LoadingRing.IsActive = false;
                LoadingPanel.Visibility = Visibility.Collapsed;
                SettingsSearchBox.IsEnabled = _configuration?.Options.Count > 0;
                ReloadButton.IsEnabled = true;
            }
        }
    }

    private static bool ConfigurationFileExists(string path)
    {
        try
        {
            File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private void UpdatePluginHeader(IReadOnlyDictionary<string, string>? generalInfo = null)
    {
        if (_pluginItem == null)
        {
            return;
        }

        string GetInfo(string key, string fallback)
        {
            return generalInfo != null && generalInfo.TryGetValue(key, out var value) &&
                   !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        }

        TitleTextBlock.Text = GetInfo("Name", _pluginItem.DisplayName);
        ToolTipService.SetToolTip(TitleTextBlock, TitleTextBlock.Text);
        DescriptionTextBlock.Text = GetInfo("Description", _pluginItem.Description);
        if (string.IsNullOrWhiteSpace(DescriptionTextBlock.Text))
        {
            DescriptionTextBlock.Text = "PluginConfigPage_AdjustParams".GetLocalized();
        }

        ToolTipService.SetToolTip(DescriptionTextBlock, DescriptionTextBlock.Text);
        DeveloperTextBlock.Text = GetInfo("Developer", _pluginItem.Developer);
        DeveloperTextBlock.Visibility = string.IsNullOrWhiteSpace(DeveloperTextBlock.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ToolTipService.SetToolTip(DeveloperTextBlock, DeveloperTextBlock.Text);
        ModifiedDateTextBlock.Text = _pluginItem.DateModified.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        ConfigPathTextBlock.Text = _pluginItem.ConfigFilePath ?? string.Empty;
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_configuration != null && !_isLoading)
        {
            UpdateVisibleOptions();
        }
    }

    private void UpdateVisibleOptions(bool resetScroll = true)
    {
        if (_configuration == null)
        {
            return;
        }

        var query = SettingsSearchBox.Text.Trim();
        var options = _configuration.Options.Where(option => query.Length == 0 ||
                                                             option.DisplayName.Contains(query,
                                                                 StringComparison.CurrentCultureIgnoreCase) ||
                                                             option.SectionName.Contains(query,
                                                                 StringComparison.CurrentCultureIgnoreCase) ||
                                                             option.Description.Contains(query,
                                                                 StringComparison.CurrentCultureIgnoreCase)).ToArray();

        _pinnedOptions.Clear();
        _regularOptions.Clear();
        foreach (var option in options)
        {
            (option.IsPinned ? _pinnedOptions : _regularOptions).Add(option);
        }

        OptionCountTextBlock.Text = query.Length == 0
            ? string.Format(CultureInfo.CurrentCulture, "PluginConfigPage_OptionCount".GetLocalized(), options.Length)
            : string.Format(CultureInfo.CurrentCulture, "PluginConfigPage_FilteredOptionCount".GetLocalized(),
                options.Length, _configuration.Options.Count);
        if (resetScroll) SettingsScrollViewer.ChangeView(null, 0, null, true);
        SettingsScrollViewer.Visibility = options.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = options.Length > 0 ? Visibility.Collapsed : Visibility.Visible;

        if (options.Length == 0)
        {
            var hasOptions = _configuration.Options.Count > 0;
            ShowEmptyState(hasOptions ? "PluginConfigPage_NoResults" : "PluginConfigPage_NoOptions",
                hasOptions ? "PluginConfigPage_NoResultsHint" : "PluginConfigPage_NoOptionsHint",
                hasOptions ? "\uE721" : "\uE9CE", hasOptions);
        }
    }

    private void ShowEmptyState(string titleKey, string hintKey, string glyph, bool canClearSearch)
    {
        SettingsScrollViewer.Visibility = Visibility.Collapsed;
        EmptyStatePanel.Visibility = Visibility.Visible;
        EmptyStateIcon.Glyph = glyph;
        EmptyStateTitleTextBlock.Text = titleKey.GetLocalized();
        EmptyStateHintTextBlock.Text = hintKey.GetLocalized();
        ClearSearchButton.Visibility = canClearSearch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClearSearchClick(object sender, RoutedEventArgs e)
    {
        SettingsSearchBox.Text = string.Empty;
        SettingsSearchBox.Focus(FocusState.Programmatic);
    }

    private void OnTextOptionChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitialized && sender is TextBox { Tag: PluginConfigOption option } textBox)
        {
            option.Value = textBox.Text;
        }
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isInitialized || e.PropertyName != nameof(PluginConfigOption.Value))
        {
            return;
        }

        if (!_isActive)
        {
            _ = SavePendingChangesAsync();
            return;
        }

        SetSaveStatus("PluginConfigPage_Saving".GetLocalized(), "\uE895");
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }
}
