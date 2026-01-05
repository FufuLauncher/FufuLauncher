using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel
    {
        get;
    }

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (ViewModel != null)
        {
            await ViewModel.ReloadSettingsAsync();
        }
    }

    private void SettingsNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem selectedItem &&
            selectedItem.Tag is string tag)
        {
            var element = FindName(tag) as FrameworkElement;
            if (element != null)
            {
                if (element.ActualHeight > 0)
                {
                    BringElementIntoView(element);
                }
                else
                {
                    RoutedEventHandler loadedHandler = null;
                    loadedHandler = (s, e) =>
                    {
                        BringElementIntoView(element);
                        element.Loaded -= loadedHandler;
                    };
                    element.Loaded += loadedHandler;
                }
            }
        }
    }
    private async void OnOpenHDRSettingsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new GenshinHDRLuminanceSettingDialog();
        dialog.XamlRoot = this.XamlRoot; 
        await dialog.ShowAsync();
    }
    private void BringElementIntoView(FrameworkElement element)
    {
        if (element == null) return;

        var bringIntoViewOptions = new BringIntoViewOptions
        {
            AnimationDesired = true,
            VerticalAlignmentRatio = 0.0,
            VerticalOffset = -52
        };

        element.StartBringIntoView(bringIntoViewOptions);
    }
}