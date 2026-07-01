/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FufuLauncher.Views;

public sealed partial class HighCpuUsageWarningWindow : Window
{
    private FeedbackWindow? _feedbackWindow;

    public HighCpuUsageWarningWindow(double cpuUsage)
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        UsageText.Text = $"最近一次采样占用约 {cpuUsage:F1}%";

        var size = new SizeInt32(620, 430);
        AppWindow.Resize(size);
        CenterWindow(size);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }
    }

    private void CenterWindow(SizeInt32 size)
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea == null)
        {
            return;
        }

        AppWindow.Move(new PointInt32(
            displayArea.WorkArea.X + (displayArea.WorkArea.Width - size.Width) / 2,
            displayArea.WorkArea.Y + (displayArea.WorkArea.Height - size.Height) / 2));
    }

    private void OnDismissClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnFeedbackClick(object sender, RoutedEventArgs e)
    {
        _feedbackWindow ??= new FeedbackWindow();
        _feedbackWindow.Closed += (_, _) => _feedbackWindow = null;
        _feedbackWindow.Activate();
    }

    private void OnExitClick(object sender, RoutedEventArgs e)
    {
        App.Current.Exit();
    }
}

