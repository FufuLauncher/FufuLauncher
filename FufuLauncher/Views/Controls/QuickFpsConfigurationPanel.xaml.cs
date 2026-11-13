using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views.Controls;

public sealed partial class QuickFpsConfigurationPanel : UserControl
{
    public QuickFpsConfigurationViewModel ViewModel
    {
        get;
    }

    public QuickFpsConfigurationPanel()
    {
        ViewModel = App.GetService<QuickFpsConfigurationViewModel>();
        DataContext = ViewModel;
        InitializeComponent();
    }

    public void Load() => ViewModel.Load();
}