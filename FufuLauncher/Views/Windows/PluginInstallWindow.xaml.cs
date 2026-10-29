/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace FufuLauncher.Views;

public sealed partial class PluginInstallWindow : Window
{
    private readonly PluginStoreItem _item;
    private readonly LuaPluginInstaller _installer;
    private readonly bool _isUninstall;
    private readonly Action? _cancelRequested;
    private readonly DispatcherQueue _dispatcherQueue;

    private bool _isDarkTheme;
    private bool _isClosed;
    private bool _isFinished;
    private bool _cancelInvoked;

    public PluginInstallWindow(PluginStoreItem item, LuaPluginInstaller installer, bool isUninstall,
        Action? cancelRequested)
    {
        _item = item;
        _installer = installer;
        _isUninstall = isUninstall;
        _cancelRequested = cancelRequested;

        InitializeComponent();

        _dispatcherQueue = DispatcherQueue;

        Title = GetWindowTitle();
        TitleBarText.Text = _item.Name;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ApplyTheme();
        ApplyWindow();

        try
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch
        {
            RootGrid.Background = new SolidColorBrush(_isDarkTheme
                ? Color.FromArgb(0xFF, 0x20, 0x20, 0x20)
                : Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3));
        }

        LoadPluginInfo();
        ApplyInitialState();

        _installer.ProgressChanged += OnInstallerProgressChanged;
        _item.PropertyChanged += OnItemPropertyChanged;

        Closed += OnWindowClosed;
    }

    public void NotifyFinished(PluginInstallOutcome outcome, string? statusText, string? detail)
    {
        _dispatcherQueue.TryEnqueue(() => ApplyFinished(outcome, statusText, detail));
    }

    private string GetWindowTitle()
    {
        var key = _isUninstall ? "PluginInstallWindowUninstallTitle" : "PluginInstallWindowTitle";
        return key.GetLocalized();
    }

    private void ApplyTheme()
    {
        var theme = ElementTheme.Default;
        try
        {
            theme = App.GetService<IThemeSelectorService>().Theme;
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginInstallWindow", $"[PluginInstallWindow] Theme service unavailable: {ex.Message}");
        }

        RootGrid.RequestedTheme = theme;
        _isDarkTheme = theme == ElementTheme.Dark ||
                       (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);
    }

    private void ApplyWindow()
    {
        try
        {
            var appWindow = AppWindow;
            if (appWindow == null) return;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "WindowIcon.ico");
            if (File.Exists(iconPath))
            {
                appWindow.SetIcon(iconPath);
            }

            var titleBar = appWindow.TitleBar;
            if (titleBar != null)
            {
                titleBar.BackgroundColor = Colors.Transparent;
                titleBar.ButtonBackgroundColor = Colors.Transparent;
                titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                titleBar.ButtonForegroundColor = _isDarkTheme ? Colors.White : Colors.Black;
                titleBar.ButtonInactiveForegroundColor = _isDarkTheme
                    ? Color.FromArgb(0xFF, 0x9A, 0x9A, 0x9A)
                    : Color.FromArgb(0xFF, 0x6B, 0x6B, 0x6B);
                titleBar.ButtonHoverBackgroundColor = _isDarkTheme
                    ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x1A, 0x00, 0x00, 0x00);
                titleBar.ButtonPressedBackgroundColor = _isDarkTheme
                    ? Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF)
                    : Color.FromArgb(0x33, 0x00, 0x00, 0x00);
                titleBar.ButtonHoverForegroundColor = _isDarkTheme ? Colors.White : Colors.Black;
                titleBar.ButtonPressedForegroundColor = _isDarkTheme ? Colors.White : Colors.Black;
            }

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }

            WindowManagerHelper.ResizeWithDpi(appWindow, this, 520, 320);
            WindowManagerHelper.CenterWindowOnScreen(appWindow, 520, 320);

            ImmersiveWindowHelper.ApplyImmersiveFrame(this, _isDarkTheme);
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginInstallWindow", $"[PluginInstallWindow] Window setup failed: {ex.Message}");
        }
    }

    private void LoadPluginInfo()
    {
        PluginNameText.Text = _item.Name;

        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(_item.Developer)) meta.Add(_item.Developer);
        if (!string.IsNullOrWhiteSpace(_item.VersionDisplay)) meta.Add(_item.VersionDisplay);
        if (!string.IsNullOrWhiteSpace(_item.SizeDisplay)) meta.Add(_item.SizeDisplay);
        PluginMetaText.Text = string.Join(" · ", meta);

        if (!string.IsNullOrWhiteSpace(_item.IconUrl) &&
            Uri.TryCreate(_item.IconUrl, UriKind.Absolute, out var iconUri))
        {
            try
            {
                PluginIcon.Source = new BitmapImage(iconUri);
            }
            catch (Exception ex)
            {
                DebugLog.Info("PluginInstallWindow", $"[PluginInstallWindow] Icon load failed: {ex.Message}");
            }
        }
    }

    private void ApplyInitialState()
    {
        PercentText.Text = "0%";
        PercentText.Visibility = _isUninstall ? Visibility.Collapsed : Visibility.Visible;
        InstallProgressBar.Value = 0;
        InstallProgressBar.IsIndeterminate = _isUninstall;

        StatusText.Text = string.IsNullOrWhiteSpace(_item.InstallStatusText)
            ? (_isUninstall ? "PluginStoreUninstalling".GetLocalized() : "PluginStoreVerifying".GetLocalized())
            : _item.InstallStatusText;

        CancelButton.Visibility = _isUninstall ? Visibility.Collapsed : Visibility.Visible;
        CloseButton.Visibility = Visibility.Collapsed;
        DetailText.Visibility = Visibility.Collapsed;
        ErrorText.Visibility = Visibility.Collapsed;
    }

    private void OnInstallerProgressChanged(DownloadProgressInfo info)
    {
        if (_isClosed || _isFinished || _isUninstall) return;

        _dispatcherQueue.TryEnqueue(() => ApplyProgress(info));
    }

    private void ApplyProgress(DownloadProgressInfo info)
    {
        if (_isClosed || _isFinished || _isUninstall) return;

        var percent = Math.Clamp(info.Percent, 0, 100);
        PercentText.Text = $"{percent:0}%";
        InstallProgressBar.IsIndeterminate = false;
        InstallProgressBar.Value = percent;

        if (!string.IsNullOrWhiteSpace(info.StatusText))
        {
            StatusText.Text = info.StatusText;
        }

        var detail = new List<string>();
        if (info.HasTotalSize)
        {
            detail.Add($"{info.DownloadedSizeDisplay} / {info.TotalSizeDisplay}");
        }

        if (info.SpeedBytesPerSecond > 0)
        {
            detail.Add(info.SpeedDisplay);
        }

        DetailText.Text = string.Join("  ·  ", detail);
        DetailText.Visibility = detail.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PluginStoreItem.InstallStatusText)) return;

        var statusText = _item.InstallStatusText;
        if (string.IsNullOrWhiteSpace(statusText)) return;

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || _isFinished) return;
            StatusText.Text = statusText;
        });
    }

    private void ApplyFinished(PluginInstallOutcome outcome, string? statusText, string? detail)
    {
        if (_isClosed) return;
        _isFinished = true;

        switch (outcome)
        {
            case PluginInstallOutcome.Succeeded:
                InstallProgressBar.IsIndeterminate = false;
                InstallProgressBar.Value = 100;
                PercentText.Text = "100%";
                StatusText.Text = (_isUninstall ? "PluginStoreUninstallComplete" : "PluginStoreInstallComplete")
                    .GetLocalized();
                ShowFinishedButtons();
                break;

            case PluginInstallOutcome.Cancelled:
                InstallProgressBar.IsIndeterminate = false;
                StatusText.Text = "PluginStoreCancelled".GetLocalized();
                ShowFinishedButtons();
                _ = CloseAfterDelayAsync(1200);
                break;

            default:
                InstallProgressBar.IsIndeterminate = false;
                StatusText.Text = string.IsNullOrWhiteSpace(statusText)
                    ? (_isUninstall ? "PluginStoreUninstallFailed" : "PluginStoreInstallFailedShort").GetLocalized()
                    : statusText;

                if (!string.IsNullOrWhiteSpace(detail))
                {
                    ErrorText.Text = detail;
                    ErrorText.Visibility = Visibility.Visible;
                }

                ShowFinishedButtons();
                break;
        }
    }

    private void ShowFinishedButtons()
    {
        CancelButton.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Visible;
    }

    private async Task CloseAfterDelayAsync(int milliseconds)
    {
        try
        {
            await Task.Delay(milliseconds);
            if (_isClosed) return;
            Close();
        }
        catch (Exception ex)
        {
            DebugLog.Info("PluginInstallWindow", $"[PluginInstallWindow] Deferred close failed: {ex.Message}");
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (_isClosed || _isFinished || _cancelInvoked) return;

        _cancelInvoked = true;

        CancelButton.IsEnabled = false;
        StatusText.Text = "PluginStoreCancelling".GetLocalized();
        InstallProgressBar.IsIndeterminate = true;

        _cancelRequested?.Invoke();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _isClosed = true;

        _installer.ProgressChanged -= OnInstallerProgressChanged;
        _item.PropertyChanged -= OnItemPropertyChanged;

        if (_isFinished || _isUninstall || _cancelInvoked) return;

        _cancelInvoked = true;
        _cancelRequested?.Invoke();
    }
}
