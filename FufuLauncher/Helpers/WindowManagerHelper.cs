/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;
using WinRT.Interop;

namespace FufuLauncher.Helpers
{
    public static class WindowManagerHelper
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        public static double GetScaleFactor(Microsoft.UI.Xaml.Window window)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var dpi = GetDpiForWindow(hwnd);
            return dpi / 96.0;
        }

        public static void ResizeWithDpi(AppWindow appWindow, Microsoft.UI.Xaml.Window window, int logicalWidth, int logicalHeight)
        {
            var hwnd = WindowNative.GetWindowHandle(window);
            var dpi = GetDpiForWindow(hwnd);
            var scale = dpi / 96.0;
            var physicalWidth = (int)Math.Round(logicalWidth * scale);
            var physicalHeight = (int)Math.Round(logicalHeight * scale);
            appWindow.Resize(new SizeInt32(physicalWidth, physicalHeight));
        }

        public static void CenterWindowOnScreen(AppWindow appWindow, double currentWidth, double currentHeight)
        {
            try
            {
                var displayArea = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
                if (displayArea == null) return;

                var workArea = displayArea.WorkArea;
                var currentSize = appWindow.Size;
                
                if (currentSize.Width <= 0 || currentSize.Height <= 0)
                {
                    currentSize = new SizeInt32((int)Math.Round(currentWidth), (int)Math.Round(currentHeight));
                }

                var targetX = workArea.X + Math.Max(0, (workArea.Width - currentSize.Width) / 2);
                var targetY = workArea.Y + Math.Max(0, (workArea.Height - currentSize.Height) / 2);

                appWindow.Move(new PointInt32(targetX, targetY));
            }
            catch { }
        }
    }
}
