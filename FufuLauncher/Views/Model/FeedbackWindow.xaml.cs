using Microsoft.UI.Xaml;
using Windows.System;
using FufuLauncher.Constants;

namespace FufuLauncher.Views;

public sealed partial class FeedbackWindow : Window
{
    public FeedbackWindow()
    {
        InitializeComponent();
        
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 450));
    }

    private async void OnFeatureRequestClick(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(ApiEndpoints.GithubFeatureRequestUrl)); 
    }

    private async void OnBugReportClick(object sender, RoutedEventArgs e)
    {
        await Launcher.LaunchUriAsync(new Uri(ApiEndpoints.GithubBugReportUrl)); 
    }
}