using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Navigation;
using FufuLauncher.ViewModels;
using Microsoft.Web.WebView2.Core;

namespace FufuLauncher.Views;

public sealed partial class DataPage : Page
{
    public DataViewModel ViewModel { get; }
    private int _currentAbyssFilterIndex;
    private int _currentAbyssSortIndex;

    public DataPage()
    {
        ViewModel = App.GetService<DataViewModel>();
        InitializeComponent();
    }
    
    private void OnAbyssFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb)
        {
            _currentAbyssFilterIndex = cb.SelectedIndex;
            ViewModel.ApplyAbyssFilter(_currentAbyssFilterIndex, _currentAbyssSortIndex);
        }
    }
    
    private void OnRoleSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchRoles(sender.Text);
    }
    
    private void OnSwitchToTimelineData(object sender, RoutedEventArgs e)
    {
        ViewModel.SetViewMode(5);
        
        if (TimelineWebView.Source == null)
        {
            TimelineLoadingTip.Visibility = Visibility.Visible;
            
            TimelineWebView.Opacity = 0;
            
            TimelineWebView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 28, 28, 34);
            
            TimelineWebView.NavigationCompleted += TimelineWebView_NavigationCompleted;
            TimelineWebView.WebMessageReceived += TimelineWebView_WebMessageReceived;
            
            TimelineWebView.Source = new Uri("https://paimon.moe/timeline");
        }
        else
        {
            TimelineWebView.Opacity = 1;
            TimelineLoadingTip.Visibility = Visibility.Collapsed;
        }
    }

    private async void OnWishImageTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is WishHistoryItem item)
        {
            var image = new Image
            {
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(item.Avatar)),
                Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
            };
            
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = item.Version + " 卡池图片",
                Content = image,
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Close,
                MaxWidth = double.PositiveInfinity
            };
            
            await dialog.ShowAsync();
        }
    }
    
    private void TimelineWebView_WebMessageReceived(WebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var message = args.TryGetWebMessageAsString();

        if (message == "TimelineReady")
        {
            TimelineLoadingTip.Visibility = Visibility.Collapsed;
            
            TimelineWebView.Opacity = 1;
        }
    }
    
    private async void TimelineWebView_NavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            TimelineLoadingTip.Visibility = Visibility.Collapsed;
            return; 
        }
        if (args.IsSuccess)
        {
            var script = @"
            (function() {
                var maxRetries = 50; 
                var attempts = 0;
                var checkExist = setInterval(function() {
                    var targetSelector = 'div.w-full.overflow-x-auto.px-4.md\\:px-8.svelte-1ga4ett';
                    var target = document.querySelector(targetSelector);
                    if (target) {
                        clearInterval(checkExist);
                        document.body.innerHTML = '';
                        document.body.appendChild(target);
                        document.body.style.backgroundColor = '#1c1c22';
                        document.body.style.paddingTop = '20px';
                        target.style.display = 'block';
                        target.style.width = '100%';
                        setTimeout(function() {
                            window.chrome.webview.postMessage('TimelineReady');
                        }, 50);
                    } else {
                        attempts++;
                        if (attempts >= maxRetries) {
                            clearInterval(checkExist);
                            window.chrome.webview.postMessage('TimelineReady');
                        }
                    }
                }, 100);
            })();
        ";

            await sender.ExecuteScriptAsync(script);
        }
    }
    
    private void OnAbyssSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb)
        {
            _currentAbyssSortIndex = cb.SelectedIndex;
            ViewModel.ApplyAbyssFilter(_currentAbyssFilterIndex, _currentAbyssSortIndex);
        }
    }
    
    private async void OnSwitchToRerunData(object sender, RoutedEventArgs e)
    {
        ViewModel.SetViewMode(4);

        if (ViewModel.RerunRoles.Count == 0)
        {
            await ViewModel.LoadRerunDataAsync();
        }
    }
    
    private void OnRoleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox cb)
        {
            ViewModel.ApplyRoleFilter(cb.SelectedIndex);
        }
    }

    protected async override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        if (ViewModel.Roles.Count == 0)
        {
            await ViewModel.LoadDataAsync();
        }
    }
    
    private async void OnSwitchToRoleData(object sender, RoutedEventArgs e)
    {
        ViewModel.SetViewMode(0); 
        
        if (ViewModel.Roles.Count == 0)
        {
            await ViewModel.LoadDataAsync();
        }
    }

    private async void OnSwitchToAbyssData(object sender, RoutedEventArgs e)
    {
        ViewModel.SetViewMode(1);

        if (ViewModel.AbyssRoles.Count == 0)
        {
            await ViewModel.LoadAbyssDataAsync();
        }
    }
    
    private async void OnSwitchToWishData(object sender, RoutedEventArgs e)
    {
        ViewModel.SetViewMode(2);
        
        if (ViewModel.WishHistory.Count == 0)
        {
            await ViewModel.LoadWishDataAsync();
        }
    }
    private async void OnSwitchToSpiralAbyssData(object sender, RoutedEventArgs e)
    {
        ViewModel.SetViewMode(3);

        if (ViewModel.SpiralAbyssRoles.Count == 0)
        {
            await ViewModel.LoadSpiralAbyssDataAsync();
        }
    }
}

public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

