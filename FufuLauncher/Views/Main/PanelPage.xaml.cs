using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PanelPage
{
    public ControlPanelModel ViewModel
    {
        get;
    }

    public MainViewModel MainViewModel
    {
        get;
    }

    public PanelPage()
    {
        ViewModel = App.GetService<ControlPanelModel>();
        MainViewModel = App.GetService<MainViewModel>();
        DataContext = ViewModel;
        
        InitializeComponent();
    }
}