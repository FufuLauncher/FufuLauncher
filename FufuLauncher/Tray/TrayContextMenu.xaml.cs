/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace FufuLauncher.Tray;

internal sealed partial class TrayContextMenu : Flyout
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

    public TrayContextMenu(TrayContextMenuViewModel viewModel)
    {
        AllowFocusOnInteraction = false;
        InitializeComponent();
        Root.DataContext = viewModel;
        Opened += OnOpened;
    }

    public void ApplyTheme(ElementTheme theme)
    {
        Root.RequestedTheme = theme;
    }

    private void OnOpened(object? sender, object e)
    {
        Root.DispatcherQueue.TryEnqueue(ApplyRoundedCorners);
    }

    private void ApplyRoundedCorners()
    {
        try
        {
            var mainWindowHandle = App.MainWindow is { } mainWindow
                ? WindowNative.GetWindowHandle(mainWindow)
                : IntPtr.Zero;

            var flyoutWindowHandle = Root.XamlRoot?.ContentIslandEnvironment is { } environment
                ? Win32Interop.GetWindowFromWindowId(environment.AppWindowId)
                : IntPtr.Zero;

            ApplyToWindow(flyoutWindowHandle, mainWindowHandle);
            ApplyToWindow(GetForegroundWindow(), mainWindowHandle);
        }
        catch
        {
        }
    }

    private static void ApplyToWindow(IntPtr hwnd, IntPtr mainWindowHandle)
    {
        if (hwnd == IntPtr.Zero || hwnd == mainWindowHandle)
        {
            return;
        }

        GetWindowThreadProcessId(hwnd, out var processId);
        if (processId != (uint)Environment.ProcessId)
        {
            return;
        }

        var preference = DwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref preference, 4);

        var borderColor = DwmwaColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, 4);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}