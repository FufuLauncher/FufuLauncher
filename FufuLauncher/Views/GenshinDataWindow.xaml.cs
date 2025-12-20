using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Views;

public sealed partial class GenshinDataWindow : WindowEx
{
    public GenshinViewModel ViewModel
    {
        get;
    }
    private bool _isFirstActivation = true;

    public GenshinDataWindow()
    {
        InitializeComponent();
        ViewModel = App.GetService<GenshinViewModel>();

        RootGrid.DataContext = ViewModel;

        ExtendsContentIntoTitleBar = true;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 800));

        this.Activated += GenshinDataWindow_Activated;
    }

    private async void GenshinDataWindow_Activated(object sender, WindowActivatedEventArgs args)
    {

        if (_isFirstActivation && args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _isFirstActivation = false;
            if (ViewModel.LoadDataCommand.CanExecute(null))
            {
                await ViewModel.LoadDataCommand.ExecuteAsync(null);
            }
        }
    }
}