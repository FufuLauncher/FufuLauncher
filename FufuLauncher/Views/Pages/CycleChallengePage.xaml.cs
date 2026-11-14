using System.ComponentModel;
using FufuLauncher.Helpers;
using FufuLauncher.Models.CycleChallenge;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Navigation;

namespace FufuLauncher.Views;

public sealed partial class CycleChallengePage : Page
{
    private readonly Dictionary<ChallengeMode, (string Payload, string? Region)> _shownByMode = new();
    private bool _subscribed;
    private bool _updateQueued;

    public CycleChallengePage()
    {
        ViewModel = App.GetService<CycleChallengeViewModel>();
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CycleChallengeViewModel ViewModel
    {
        get;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.AttachCloudSyncListener();
        if (_subscribed)
        {
            return;
        }

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _subscribed = true;
        UpdateContent();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.DetachCloudSyncListener();
        if (!_subscribed)
        {
            return;
        }

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _subscribed = false;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CycleChallengeViewModel.Payload)
            or nameof(CycleChallengeViewModel.CurrentMode)
            or nameof(CycleChallengeViewModel.HasData)
            or nameof(CycleChallengeViewModel.HasError)
            or nameof(CycleChallengeViewModel.IsLoading)
            or nameof(CycleChallengeViewModel.SelectedPeriod))
        {
            QueueUpdate();
        }
    }

    private void QueueUpdate()
    {
        if (_updateQueued)
        {
            return;
        }

        _updateQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _updateQueued = false;
            UpdateContent();
        });
    }

    private async void OnModeTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton tab ||
            tab.Tag is not string tag ||
            !Enum.TryParse<ChallengeMode>(tag, out var mode))
        {
            SynchronizeModeTabs();
            return;
        }

        if (ViewModel.CurrentMode == mode)
        {
            SynchronizeModeTabs();
            return;
        }

        var switchTask = ViewModel.SetModeAsync(mode);
        SynchronizeModeTabs();
        await switchTask;
        SynchronizeModeTabs();
    }

    private void UpdateContent()
    {
        var mode = ViewModel.CurrentMode;
        SynchronizeModeTabs();
        UpdateModeHeader(mode);

        AbyssHost.Visibility = mode == ChallengeMode.SpiralAbyss ? Visibility.Visible : Visibility.Collapsed;
        TheaterHost.Visibility = mode == ChallengeMode.Theater ? Visibility.Visible : Visibility.Collapsed;
        StygianHost.Visibility = mode == ChallengeMode.Stygian ? Visibility.Visible : Visibility.Collapsed;

        var payload = ViewModel.Payload;
        var hasPayload = !string.IsNullOrEmpty(payload) && ViewModel.HasData;
        LoadingState.Visibility = ViewModel.IsLoading && !hasPayload ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasPayload || ViewModel.HasError || ViewModel.IsLoading
            ? Visibility.Collapsed
            : Visibility.Visible;
        PeriodEmptyState.Visibility = ViewModel.Periods.Count == 0 && !ViewModel.IsLoading
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!hasPayload)
        {
            AbyssHost.Visibility = Visibility.Collapsed;
            TheaterHost.Visibility = Visibility.Collapsed;
            StygianHost.Visibility = Visibility.Collapsed;
            return;
        }

        var region = ViewModel.CurrentRegion;
        if (_shownByMode.TryGetValue(mode, out var shown) &&
            string.Equals(shown.Payload, payload, StringComparison.Ordinal) &&
            string.Equals(shown.Region, region, StringComparison.Ordinal))
        {
            return;
        }

        AbyssPanel.Region = region;
        TheaterPanel.Region = region;
        StygianPanel.Region = region;

        switch (mode)
        {
            case ChallengeMode.SpiralAbyss:
                AbyssPanel.Show(payload!);
                break;
            case ChallengeMode.Theater:
                TheaterPanel.Show(payload!);
                break;
            case ChallengeMode.Stygian:
                StygianPanel.Show(payload!);
                break;
        }

        _shownByMode[mode] = (payload!, region);
        ResetDetailScroll(mode);
    }

    private void SynchronizeModeTabs()
    {
        var mode = ViewModel.CurrentMode;
        if (AbyssTab.IsChecked != (mode == ChallengeMode.SpiralAbyss))
        {
            AbyssTab.IsChecked = mode == ChallengeMode.SpiralAbyss;
        }

        if (TheaterTab.IsChecked != (mode == ChallengeMode.Theater))
        {
            TheaterTab.IsChecked = mode == ChallengeMode.Theater;
        }

        if (StygianTab.IsChecked != (mode == ChallengeMode.Stygian))
        {
            StygianTab.IsChecked = mode == ChallengeMode.Stygian;
        }
    }

    private void UpdateModeHeader(ChallengeMode mode)
    {
        DetailModeName.Text = mode.ToDisplayKey().GetLocalized();
        AbyssHeaderIcon.Visibility = mode == ChallengeMode.SpiralAbyss ? Visibility.Visible : Visibility.Collapsed;
        TheaterHeaderIcon.Visibility = mode == ChallengeMode.Theater ? Visibility.Visible : Visibility.Collapsed;
        StygianHeaderIcon.Visibility = mode == ChallengeMode.Stygian ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResetDetailScroll(ChallengeMode mode)
    {
        var host = mode switch
        {
            ChallengeMode.Theater => TheaterHost,
            ChallengeMode.Stygian => StygianHost,
            _ => AbyssHost,
        };

        host.ChangeView(null, 0, null, true);
    }
}