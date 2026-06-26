/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace FufuLauncher.Views;

public sealed partial class DiagnosticsWindow : Window
{
    public DiagnosticsViewModel ViewModel
    {
        get;
    }

    public DiagnosticsWindow()
    {
        this.InitializeComponent();

        ViewModel = new DiagnosticsViewModel();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = AppWindow.GetFromWindowId(windowId);

            if (appWindow != null)
            {
                appWindow.Resize(new Windows.Graphics.SizeInt32(600, 750));

                if (appWindow.Presenter is OverlappedPresenter presenter)
                {
                    presenter.IsMaximizable = false;
                }
            }
        }
        catch { }

        ViewModel.InitializeAsync();
    }

    public Visibility ToVisible(bool isLoading) => isLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ToCollapsed(bool isLoading) => isLoading ? Visibility.Collapsed : Visibility.Visible;
}
