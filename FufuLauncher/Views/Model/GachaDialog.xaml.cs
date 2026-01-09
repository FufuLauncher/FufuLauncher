using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class GachaDialog : ContentDialog
{
    public GachaViewModel ViewModel { get; }

    public GachaDialog()
    {
        ViewModel = App.GetService<GachaViewModel>();
        this.InitializeComponent();
    }
    
    private async void GachaDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.OnViewLoadedAsync();
        }
    }
}