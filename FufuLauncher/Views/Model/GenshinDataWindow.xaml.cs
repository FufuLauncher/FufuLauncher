/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
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
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 720));
        this.CenterOnScreen();

        this.Activated += GenshinDataWindow_Activated;
    }

    private async void GenshinDataWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_isFirstActivation && args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _isFirstActivation = false;
            await TryLoadDataAsync();
        }
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isFirstActivation)
        {
            _isFirstActivation = false;
            await TryLoadDataAsync();
        }
    }

    private async Task TryLoadDataAsync()
    {
        if (ViewModel.LoadDataCommand.CanExecute(null))
        {
            await ViewModel.LoadDataCommand.ExecuteAsync(null);
        }
    }
}
