/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PluginPage
{
    #region 对话框交互

    private async void ViewModel_DuplicateDetected(object? sender, string message)
    {
        await Task.Delay(1000);

        DispatcherQueue.TryEnqueue(async () =>
        {
            if (XamlRoot == null || !IsLoaded) return;

            await ShowDuplicateDialog(message);
        });
    }

    private async void OnFreeCamHelpClick(object sender, RoutedEventArgs e)
    {
        try
        {
            string imagePath = Path.Combine(AppContext.BaseDirectory, "Assets", "freecam.png");

            if (File.Exists(imagePath))
            {
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(imagePath));
                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                };

                var dialog = new ContentDialog
                {
                    Title = "PluginPage_FreeCamHelpTitle".GetLocalized(),
                    Content = image,
                    CloseButtonText = "CloseBtn".GetLocalized(),
                    XamlRoot = XamlRoot,
                    Resources = { ["ContentDialogMaxWidth"] = 900.0 }
                };
                await dialog.ShowAsync();
            }
            else
            {
                var dialog = new ContentDialog
                {
                    Title = "PluginPage_FileNotFoundTitle".GetLocalized(),
                    Content = "PluginPage_FreeCamImageMissing".GetLocalized(),
                    CloseButtonText = "OkBtn".GetLocalized(),
                    XamlRoot = XamlRoot
                };
                await dialog.ShowAsync();
            }
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = string.Format("PluginPage_CannotOpenGuideImage_Format".GetLocalized(), ex.Message);
        }
    }

    private async Task ShowDuplicateDialog(string message)
    {
        if (XamlRoot == null) return;

        var dialog = new ContentDialog
        {
            Title = "PluginPage_DuplicateTitle".GetLocalized(),
            Content = new ScrollViewer
            {
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                MaxHeight = 300
            },
            PrimaryButtonText = "PluginPage_OpenPluginFolderBtn".GetLocalized(),
            CloseButtonText = "PluginPage_IgnoreBtn".GetLocalized(),
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();

        if (result == ContentDialogResult.Primary)
        {
            ViewModel.OpenFolderCommand.Execute(null);
        }
    }

    #endregion
}