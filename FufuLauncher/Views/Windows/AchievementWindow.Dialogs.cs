/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class AchievementWindow
{
    private async Task ShowDialogAsync(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 },
            CloseButtonText = "OkBtn".GetLocalized(),
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async Task<string> ShowInputAsync(string title, string instruction)
    {
        var inputTextBox = new TextBox
        {
            PlaceholderText = "AchievementWindow_EnterName".GetLocalized(),
            MaxLength = 20
        };
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new StackPanel
            {
                Spacing = 10,
                Children = { new TextBlock { Text = instruction }, inputTextBox }
            },
            PrimaryButtonText = "OkBtn".GetLocalized(),
            CloseButtonText = "CancelBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string text = inputTextBox.Text.Trim();
            if (text.IndexOfAny(invalid) >= 0)
            {
                await ShowDialogAsync("AchievementWindow_InvalidName".GetLocalized(), "AchievementWindow_InvalidChars".GetLocalized());
                return null;
            }

            return text;
        }

        return null;
    }
}