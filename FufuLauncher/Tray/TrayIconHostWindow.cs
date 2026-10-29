using FufuLauncher.Helpers;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.IO;
using System.Runtime.InteropServices;
using Windows.Graphics;
using WinRT.Interop;

namespace FufuLauncher.Tray;

internal sealed class TrayIconHostWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExLayered = 0x00080000L;
    private const long WsExToolWindow = 0x00000080L;
    private const uint LwaAlpha = 0x00000002;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _retryTimer;
    private FlyoutBase? _pendingFlyout;
    private int _retryCount;

    public TrayIconHostWindow()
    {
        Content = new Border();

        if (Content is FrameworkElement root)
        {
            root.Loaded += OnContentLoaded;
        }

        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _retryTimer = _dispatcherQueue.CreateTimer();
        _retryTimer.Interval = TimeSpan.FromMilliseconds(50);
        _retryTimer.Tick += OnRetryTick;

        var handle = WindowNative.GetWindowHandle(this);
        var extendedStyle = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(extendedStyle | WsExLayered | WsExToolWindow));
        SetLayeredWindowAttributes(handle, 0, 0, LwaAlpha);

        AppWindow.Title = "FufuLauncherTrayHost";

        try
        {
            AppWindow.IsShownInSwitchers = false;
        }
        catch
        {
        }

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
            presenter.SetBorderAndTitleBar(false, false);
        }
    }

    public void ApplyTheme(ElementTheme theme)
    {
        if (Content is FrameworkElement element)
        {
            element.RequestedTheme = theme;
        }
    }

    public void ShowFlyoutAt(FlyoutBase flyout, TrayNativeMethods.TrayRect icon)
    {
        icon.Left -= 8;
        icon.Top -= 8;
        icon.Right += 8;
        icon.Bottom += 8;

        MoveAndResize(icon);

        _pendingFlyout = flyout;
        _retryCount = 0;
        Trace("host show requested");

        Activate();
        this.SetForegroundWindow();

        _dispatcherQueue.TryEnqueue(TryShowPending);
    }

    public void MoveAndResize(TrayNativeMethods.TrayRect icon)
    {
        var width = Math.Max(1, icon.Right - icon.Left);
        var height = Math.Max(1, icon.Bottom - icon.Top);

        try
        {
            AppWindow.MoveAndResize(new RectInt32(icon.Left, icon.Top, width, height));
        }
        catch
        {
        }
    }

    private void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        _dispatcherQueue.TryEnqueue(TryShowPending);
    }

    private void TryShowPending()
    {
        if (_pendingFlyout is not { } flyout)
        {
            return;
        }

        if (Content is not FrameworkElement anchor || anchor.XamlRoot is null)
        {
            Trace($"host content not ready, retry {_retryCount}");
            if (_retryCount++ < 20)
            {
                _retryTimer.Start();
            }
            else
            {
                DebugLog.Info("TrayIcon", "[TrayIcon] Host content is not ready; flyout was not shown.");
            }

            return;
        }

        try
        {
            Trace("showing flyout");
            flyout.ShowAt(anchor, new FlyoutShowOptions
            {
                Placement = FlyoutPlacementMode.Auto,
                ShowMode = FlyoutShowMode.Standard,
            });
            Trace("flyout shown");
            _pendingFlyout = null;
            _retryTimer.Stop();
        }
        catch (Exception ex)
        {
            DebugLog.Info("TrayIcon", $"[TrayIcon] Failed to open flyout: {ex.Message}");
            Trace($"flyout failed: {ex}");
        }
    }

    private static void Trace(string message)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "FufuLauncher-tray.log");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff} host {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private void OnRetryTick(DispatcherQueueTimer sender, object args)
    {
        TryShowPending();
    }

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, value)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, value.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint crKey, byte bAlpha, uint dwFlags);
}