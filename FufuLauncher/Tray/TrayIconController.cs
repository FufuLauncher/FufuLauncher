/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.ViewModels;
using FufuLauncher.Views;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Tray;

internal sealed class TrayIconController : IDisposable
{
    private readonly TaskbarIcon _taskbarIcon;
    private readonly MainWindow _mainWindow;
    private readonly IRelayCommand _showWindowCommand;
    private readonly IRelayCommand _exitCommand;
    private readonly IGameLauncherService _gameLauncherService;
    private readonly DispatcherQueue _dispatcherQueue;
    private TrayIconHostWindow? _hostWindow;
    private TrayContextMenu? _menu;
    private bool _disposed;

    public TrayIconController(TaskbarIcon taskbarIcon, MainWindow mainWindow, IRelayCommand showWindowCommand, IRelayCommand exitCommand)
    {
        _taskbarIcon = taskbarIcon;
        _mainWindow = mainWindow;
        _showWindowCommand = showWindowCommand;
        _exitCommand = exitCommand;
        _gameLauncherService = App.GetService<IGameLauncherService>();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _taskbarIcon.TrayIcon.MessageWindow.KeyboardEventReceived += OnKeyboardEventReceived;

        Trace("controller created");

        try
        {
            _hostWindow = new TrayIconHostWindow();
            _hostWindow.MoveAndResize(default);
            Trace("host window created");
        }
        catch (Exception ex)
        {
            LogError("create host", ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _taskbarIcon.TrayIcon.MessageWindow.KeyboardEventReceived -= OnKeyboardEventReceived;
        }
        catch
        {
        }

        try
        {
            _menu?.Hide();
        }
        catch
        {
        }

        try
        {
            _hostWindow?.Close();
        }
        catch
        {
        }

        _menu = null;
        _hostWindow = null;
    }

    private void OnKeyboardEventReceived(object? sender, MessageWindow.KeyboardEventReceivedEventArgs e)
    {
        if (e.KeyboardEvent == KeyboardEvent.ContextMenu)
        {
            Trace("keyboard context menu received");
            RequestMenu();
        }
    }

    public void ShowTrayMenu()
    {
        Trace("tray menu requested");
        RequestMenu();
    }

    private void RequestMenu()
    {
        if (_disposed)
        {
            return;
        }

        if (!_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(ShowMenu);
            return;
        }

        ShowMenu();
    }

    private void ShowMenu()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            Trace("show menu begin");

            if (_taskbarIcon.IsDisposed)
            {
                Trace("taskbar icon disposed");
                return;
            }

            if (_menu is { IsOpen: true })
            {
                _menu.Hide();
                return;
            }

            var bounds = GetTrayIconBounds();
            Trace($"tray bounds {bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}");

            var host = EnsureHostWindow();
            var menu = EnsureMenu();
            var theme = ResolveTheme();

            menu.ApplyTheme(theme);
            host.ApplyTheme(theme);
            host.ShowFlyoutAt(menu, bounds);
            Trace("show flyout requested");
        }
        catch (Exception ex)
        {
            LogError("show menu", ex);
        }
    }

    private TrayIconHostWindow EnsureHostWindow()
    {
        if (_hostWindow is null)
        {
            _hostWindow = new TrayIconHostWindow();
            _hostWindow.MoveAndResize(default);
            Trace("host window created lazily");
        }

        return _hostWindow;
    }

    private TrayContextMenu EnsureMenu()
    {
        if (_menu is null)
        {
            _menu = new TrayContextMenu(new TrayContextMenuViewModel(
                ShowMainWindow,
                LaunchGame,
                Checkin,
                SwitchServer,
                OpenMcp,
                ExitApplication));
            Trace("menu created");
        }

        return _menu;
    }

    private void ShowMainWindow()
    {
        _menu?.Hide();
        _showWindowCommand.Execute(null);
    }

    private void LaunchGame()
    {
        _menu?.Hide();

        var mainViewModel = App.GetService<MainViewModel>();
        if (mainViewModel.IsGameRunning || !_gameLauncherService.IsGamePathSelected())
        {
            ShowMainWindow();
        }

        if (!mainViewModel.IsGameRunning)
        {
            mainViewModel.LaunchGameCommand.Execute(null);
        }
    }

    private void Checkin()
    {
        _menu?.Hide();
        App.GetService<MainViewModel>().ExecuteCheckinCommand.Execute(null);
    }

    private void SwitchServer()
    {
        _menu?.Hide();

        var gamePath = _gameLauncherService.GetGamePath();
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            ShowMainWindow();
            if (App.MainWindow is MainWindow mainWindow)
            {
                mainWindow.NavigateToGameSettingsPage();
            }

            return;
        }

        var gameDir = gamePath.Trim().Trim('"');
        if (File.Exists(gameDir))
        {
            gameDir = Path.GetDirectoryName(gameDir) ?? gameDir;
        }

        GameSettingsPage.OpenAdvancedServerSwitchWindow(gameDir);
    }

    private void OpenMcp()
    {
        _menu?.Hide();
        McpWindow.ShowOrActivate();
    }

    private void ExitApplication()
    {
        _menu?.Hide();
        _exitCommand.Execute(null);
    }

    private ElementTheme ResolveTheme()
    {
        if (_mainWindow.Content is FrameworkElement root)
        {
            var theme = root.ActualTheme;
            if (theme != ElementTheme.Default)
            {
                return theme;
            }
        }

        return Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light;
    }

    private static void Trace(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "FufuLauncher-tray.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private static void LogError(string stage, Exception ex)
    {
        DebugLog.Info("TrayIcon", $"[TrayIcon] {stage} failed: {ex}");
        Trace($"{stage} failed: {ex}");
    }

    private TrayNativeMethods.TrayRect GetTrayIconBounds()
    {
        var identifier = new TrayNativeMethods.NotifyIconIdentifier
        {
            cbSize = (uint)Marshal.SizeOf<TrayNativeMethods.NotifyIconIdentifier>(),
            guidItem = _taskbarIcon.TrayIcon.Id,
        };

        if (TrayNativeMethods.Shell_NotifyIconGetRect(ref identifier, out var bounds) == 0 &&
            bounds.Right > bounds.Left &&
            bounds.Bottom > bounds.Top)
        {
            return bounds;
        }

        if (TrayNativeMethods.GetCursorPos(out var point))
        {
            return new TrayNativeMethods.TrayRect
            {
                Left = point.X - 16,
                Top = point.Y - 16,
                Right = point.X + 16,
                Bottom = point.Y + 16,
            };
        }

        return new TrayNativeMethods.TrayRect
        {
            Left = 0,
            Top = 0,
            Right = 32,
            Bottom = 32,
        };
    }
}