using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace FufuLauncher.Views;

public sealed partial class HiddenPage : Page
{
    public HiddenPage()
    {
        InitializeComponent();
    }

    private void Page_Loaded(object sender, RoutedEventArgs e)
    {
        var entrance = Resources["EntranceStoryboard"] as Storyboard;
        entrance?.Begin();
    }

    private void OnBackToHomeClick(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is MainWindow mainWindow)
            mainWindow.NavigateToPage("FufuLauncher.ViewModels.MainViewModel");
    }
}
