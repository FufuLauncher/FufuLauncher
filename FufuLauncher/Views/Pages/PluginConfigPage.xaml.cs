/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using FufuLauncher.Models;
using FufuLauncher.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PluginConfigPage : Page
{
    private static readonly TimeSpan ExitAnimationTimeout = TimeSpan.FromMilliseconds(600);
    private readonly ObservableCollection<PluginConfigOption> _pinnedOptions = new();
    private readonly ObservableCollection<PluginConfigOption> _regularOptions = new();
    private readonly ObservableCollection<PluginConfigInfoItem> _infoItems = new();
    private readonly DispatcherQueueTimer _autoSaveTimer;
    private PluginItem? _pluginItem;
    private PluginConfiguration? _configuration;
    private GamepadConnectionWatcher? _gamepadWatcher;
    private string? _loadedConfigPath;
    private CancellationTokenSource? _loadCancellation;
    private Task<bool>? _saveTask;
    private TaskCompletionSource<bool>? _exitAnimationCompletion;
    private Func<Task>? _retryAction;
    private bool _isActive;
    private bool _isLoading;
    private bool _isInitialized;
    private bool _isNavigatingBack;
    private bool _isRetrying;
    private bool _hasSaveError;

    public PluginConfigPage()
    {
        InitializeComponent();
        ConfigOptionsViewSource.Source = new[]
        {
            new PluginConfigOptionGroup(_pinnedOptions),
            new PluginConfigOptionGroup(_regularOptions, _pinnedOptions)
        };
        ConfigGridView.ItemsSource = ConfigOptionsViewSource.View;
        InfoItemsControl.ItemsSource = _infoItems;
        _autoSaveTimer = DispatcherQueue.CreateTimer();
        _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(350);
        _autoSaveTimer.IsRepeating = false;
        _autoSaveTimer.Tick += OnAutoSaveTimerTick;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        _isActive = ReferenceEquals(Frame?.Content, this);
        RestorePageInteraction();
        EntranceStoryboard.Begin();
        StartGamepadWatcher();
    }

    private void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        StopGamepadWatcher();
        _exitAnimationCompletion?.TrySetResult(false);
        EntranceStoryboard.Stop();
        ExitStoryboard.Stop();
        RootLayoutGrid.IsHitTestVisible = true;
    }
}
