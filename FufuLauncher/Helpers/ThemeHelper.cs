/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.Diagnostics;
using Windows.UI;

namespace FufuLauncher.Helpers
{
    public static class ThemeHelper
    {
        public static void ApplyThemeColor(string hexColor)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hexColor))
                {
                    // If empty, we can try to revert to default system accent color, but in WinUI 3 it might be hard to reset cleanly without restart.
                    // For now, if empty, we just don't override or set to a default fallback.
                    return;
                }

                Color color = ParseColor(hexColor);
                
                // Set main accent color
                Application.Current.Resources["SystemAccentColor"] = color;

                // WinUI 3 often uses these variations
                Application.Current.Resources["SystemAccentColorLight1"] = ChangeColorLightness(color, 0.15f);
                Application.Current.Resources["SystemAccentColorLight2"] = ChangeColorLightness(color, 0.30f);
                Application.Current.Resources["SystemAccentColorLight3"] = ChangeColorLightness(color, 0.45f);
                Application.Current.Resources["SystemAccentColorDark1"] = ChangeColorLightness(color, -0.15f);
                Application.Current.Resources["SystemAccentColorDark2"] = ChangeColorLightness(color, -0.30f);
                Application.Current.Resources["SystemAccentColorDark3"] = ChangeColorLightness(color, -0.45f);

                Application.Current.Resources["AccentFillColorDefaultBrush"] = new SolidColorBrush(color);

                // Need to toggle theme to force resource update across the app
                if (App.MainWindow?.Content is FrameworkElement rootElement)
                {
                    var currentTheme = rootElement.RequestedTheme;
                    rootElement.RequestedTheme = ElementTheme.Light;
                    rootElement.RequestedTheme = ElementTheme.Dark;
                    rootElement.RequestedTheme = currentTheme;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to apply theme color: {ex.Message}");
            }
        }

        private static Color ParseColor(string hex)
        {
            hex = hex.Replace("#", "");
            byte a = 255;
            byte r = 0;
            byte g = 0;
            byte b = 0;

            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex.Substring(0, 2), 16);
                r = Convert.ToByte(hex.Substring(2, 2), 16);
                g = Convert.ToByte(hex.Substring(4, 2), 16);
                b = Convert.ToByte(hex.Substring(6, 2), 16);
            }
            else if (hex.Length == 6)
            {
                r = Convert.ToByte(hex.Substring(0, 2), 16);
                g = Convert.ToByte(hex.Substring(2, 2), 16);
                b = Convert.ToByte(hex.Substring(4, 2), 16);
            }

            return Color.FromArgb(a, r, g, b);
        }

        private static Color ChangeColorLightness(Color color, float factor)
        {
            float r = color.R;
            float g = color.G;
            float b = color.B;

            if (factor < 0)
            {
                factor = 1 + factor;
                r *= factor;
                g *= factor;
                b *= factor;
            }
            else
            {
                r = (255 - r) * factor + r;
                g = (255 - g) * factor + g;
                b = (255 - b) * factor + b;
            }

            return Color.FromArgb(color.A, (byte)Math.Clamp(r, 0, 255), (byte)Math.Clamp(g, 0, 255), (byte)Math.Clamp(b, 0, 255));
        }
    }
}

