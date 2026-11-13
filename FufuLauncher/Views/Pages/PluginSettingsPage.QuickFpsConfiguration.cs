using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views;

public sealed partial class PluginSettingsPage
{
    private void OnQuickFpsConfigurationFlyoutOpening(object sender, object e)
    {
        if (sender is not Flyout flyout) return;
        flyout.Content ??= new Controls.QuickFpsConfigurationPanel();
        if (flyout.Content is not Controls.QuickFpsConfigurationPanel panel || XamlRoot == null) return;
        panel.Width = Math.Min(820, Math.Max(240, XamlRoot.Size.Width - 80));
        panel.Height = Math.Min(560, Math.Max(180, XamlRoot.Size.Height - 100));
        panel.Load();
    }
}