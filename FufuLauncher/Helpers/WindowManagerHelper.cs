using System;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace FufuLauncher.Helpers
{
    public static class WindowManagerHelper
    {
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
            catch
            {
                // ignored
            }
        }
    }
}