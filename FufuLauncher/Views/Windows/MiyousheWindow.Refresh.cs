using FufuLauncher.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace FufuLauncher.Views;

public sealed partial class MiyousheWindow
{
    private void UpdateRefreshButtonState()
    {
        if (_closed) return;
        RefreshButton.IsEnabled = !_initializing && !_refreshing && !_feedLoading && !_readerLoading && !_goingBack &&
                                  !_interactionBusy;
        RefreshSpinner.IsActive = _refreshing;
        RefreshSpinner.Visibility = _refreshing ? Visibility.Visible : Visibility.Collapsed;
        RefreshIcon.Visibility = _refreshing ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnRefreshAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_dialogOpen) return;
        args.Handled = true;
        if (_shopOpen) await ShopPane.RefreshAsync();
        else await RefreshCurrentPageAsync();
    }

    private async Task<bool> RefreshCurrentPageAsync()
    {
        if (_closed || _dialogOpen || !RefreshButton.IsEnabled) return false;
        var client = _client;
        var feed = _feed;
        var target = _readerTarget;
        bool postOpen = _isPostOpen;
        _refreshing = true;
        UpdateRefreshButtonState();
        ClearNavigationError();
        try
        {
            bool completed;
            if (postOpen)
                completed = target is { } post && await OpenPostAsync(post.Id, post.Game, remember: false);
            else if (!_feedInitialized)
                completed = await ReloadNavigationAsync();
            else
                completed = await LoadFeedAsync(true);
            if (!completed || _closed || client != _client || postOpen != _isPostOpen || feed != _feed) return false;
            ShowRefreshFeedback();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (!_closed) ReportError(ex, RefreshCurrentPageAsync);
            return false;
        }
        finally
        {
            _refreshing = false;
            UpdateRefreshButtonState();
        }
    }

    private void ShowRefreshFeedback()
    {
        if (StatusBar.IsOpen && StatusBar.Severity is InfoBarSeverity.Warning or InfoBarSeverity.Error) return;
        string message = string.Format("Miyoushe_Refreshed".GetLocalized(), DateTime.Now.ToString("HH:mm:ss"));
        long version = ++_refreshFeedbackVersion;
        ShowStatus(message, InfoBarSeverity.Success);
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(3);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (!_closed && version == _refreshFeedbackVersion && StatusBar.Severity == InfoBarSeverity.Success &&
                StatusBar.Message == message)
                StatusBar.IsOpen = false;
        };
        timer.Start();
    }
}