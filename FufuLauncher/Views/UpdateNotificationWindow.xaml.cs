using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed partial class UpdateNotificationWindow : WindowEx
{

    public UpdateNotificationWindow(string updateInfoUrl)
    {
        InitializeComponent();

        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(AppTitleBar);

        UpdateWebView.Source = new Uri(updateInfoUrl);

        this.CenterOnScreen();
        this.SystemBackdrop = new DesktopAcrylicBackdrop();
        this.IsShownInSwitchers = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        this.Close();
    }
}