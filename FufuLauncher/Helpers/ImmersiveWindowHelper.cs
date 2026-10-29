/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace FufuLauncher.Helpers;

public static class ImmersiveWindowHelper
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;

    private const int DwmwcpRound = 2;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);
    private const int DwmColorDefault = unchecked((int)0xFFFFFFFF);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int size);

    public static void ApplyImmersiveFrame(Window window, bool isDarkTheme, bool transparentBorder = true)
    {
        if (window == null) return;

        try
        {
            var windowHandle = WindowNative.GetWindowHandle(window);
            if (windowHandle == IntPtr.Zero) return;

            var darkMode = isDarkTheme ? 1 : 0;
            DwmSetWindowAttribute(windowHandle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));

            var cornerPreference = DwmwcpRound;
            DwmSetWindowAttribute(windowHandle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));

            var borderColor = transparentBorder ? DwmColorNone : DwmColorDefault;
            DwmSetWindowAttribute(windowHandle, DwmwaBorderColor, ref borderColor, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }
}
