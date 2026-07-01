/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using FufuLauncher.Helpers;
using AppWindow = Microsoft.UI.Windowing.AppWindow;

namespace FufuLauncher.Views;

public sealed partial class BackgroundInitWindow : Window
{
    public BackgroundInitWindow()
    {
        InitializeComponent();
        
        SystemBackdrop = new MicaBackdrop();
        
        ExtendsContentIntoTitleBar = true;
        
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        
        AppWindow.Resize(new SizeInt32(460, 280));
        
        WindowManagerHelper.CenterWindowOnScreen(AppWindow, 460, 280);
    }
}
