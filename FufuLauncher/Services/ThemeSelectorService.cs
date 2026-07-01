/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Services
{
    public class ThemeSelectorService : IThemeSelectorService
    {
        private const string SettingsKey = "AppBackgroundRequestedTheme";

        public ElementTheme Theme { get; set; } = ElementTheme.Default;

        private readonly ILocalSettingsService _localSettingsService;

        public ThemeSelectorService(ILocalSettingsService localSettingsService)
        {
            _localSettingsService = localSettingsService;
        }

        public async Task InitializeAsync()
        {
            Theme = await LoadThemeFromSettingsAsync();
            await Task.CompletedTask;

            try
            {
                var appThemeColorObj = await _localSettingsService.ReadSettingAsync("AppThemeColor");
                if (appThemeColorObj != null)
                {
                    string appThemeColor = appThemeColorObj.ToString();
                    if (!string.IsNullOrEmpty(appThemeColor))
                    {
                        ThemeHelper.ApplyThemeColor(appThemeColor);
                    }
                }
            }
            catch { }
        }

        public async Task SetThemeAsync(ElementTheme theme)
        {
            Theme = theme;

            await SetRequestedThemeAsync();
            await SaveThemeInSettingsAsync(Theme);
        }

        public async Task SetRequestedThemeAsync()
        {
            if (App.MainWindow.Content is FrameworkElement rootElement)
            {
                if (rootElement is Panel rootPanel && rootElement.ActualWidth > 0 && rootElement.ActualHeight > 0)
                {
                    try
                    {
                        var rtb = new RenderTargetBitmap();
                        await rtb.RenderAsync(rootElement);

                        var image = new Image
                        {
                            Source = rtb,
                            Stretch = Microsoft.UI.Xaml.Media.Stretch.None,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Top,
                            IsHitTestVisible = false,
                            Width = rootElement.ActualWidth,
                            Height = rootElement.ActualHeight
                        };

                        Canvas.SetZIndex(image, 10000);
                        rootPanel.Children.Add(image);

                        rootElement.RequestedTheme = Theme;
                        TitleBarHelper.UpdateTitleBar(Theme);

                        var storyboard = new Storyboard();
                        var animation = new DoubleAnimation
                        {
                            From = 1.0,
                            To = 0.0,
                            Duration = new Duration(TimeSpan.FromMilliseconds(400)),
                            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                        };

                        Storyboard.SetTarget(animation, image);
                        Storyboard.SetTargetProperty(animation, "Opacity");
                        storyboard.Children.Add(animation);

                        storyboard.Completed += (s, e) =>
                        {
                            rootPanel.Children.Remove(image);
                        };

                        storyboard.Begin();
                    }
                    catch
                    {
                        rootElement.RequestedTheme = Theme;
                        TitleBarHelper.UpdateTitleBar(Theme);
                    }
                }
                else
                {
                    rootElement.RequestedTheme = Theme;
                    TitleBarHelper.UpdateTitleBar(Theme);
                }
            }

            await Task.CompletedTask;
        }

        private async Task<ElementTheme> LoadThemeFromSettingsAsync()
        {

            var themeObj = await _localSettingsService.ReadSettingAsync(SettingsKey);

            if (themeObj != null)
            {
                string themeName = themeObj.ToString();
                if (Enum.TryParse(themeName, out ElementTheme cacheTheme))
                {
                    return cacheTheme;
                }
            }

            return ElementTheme.Default;
        }

        private async Task SaveThemeInSettingsAsync(ElementTheme theme)
        {
            await _localSettingsService.SaveSettingAsync(SettingsKey, theme.ToString());
        }
    }
}
