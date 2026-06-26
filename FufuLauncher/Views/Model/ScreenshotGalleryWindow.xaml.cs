/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.ObjectModel;
using FufuLauncher.Activation;
using FufuLauncher.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace FufuLauncher.Views;

public sealed partial class ScreenshotGalleryWindow : Window
{
    private readonly string _gameScreenshotDirectory;
    private readonly string _customScreenshotDirectory;
    private readonly ObservableCollection<ScreenshotGroup> _galleryData = new();
    private readonly ObservableCollection<ScreenshotItem> _flatItems = new();
    private ScreenshotItem? _currentDetailItem;
    private AppWindow _appWindow;
    private FileSystemWatcher? _gameFileWatcher;
    private FileSystemWatcher? _customFileWatcher;
    private Timer? _debounceTimer;

    private const string ConnectedAnimationKey = "ForwardConnectedAnimation";

    public ScreenshotGalleryWindow(string gameScreenshotDirectory, string customScreenshotDirectory)
    {
        this.InitializeComponent();
        _gameScreenshotDirectory = gameScreenshotDirectory;
        _customScreenshotDirectory = customScreenshotDirectory;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(null);
        CustomizeTitleBar();

        DetailImageViewer.ItemsSource = _flatItems;

        RootGrid.Loaded += async (s, e) =>
        {
            await RefreshViewAfterDataChangedAsync();

            if (Directory.Exists(_gameScreenshotDirectory))
            {
                _gameFileWatcher = new FileSystemWatcher(_gameScreenshotDirectory, "*.png")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                _gameFileWatcher.Created += (s, e) => DebounceRefresh();
                _gameFileWatcher.Deleted += (s, e) => DebounceRefresh();
                _gameFileWatcher.Renamed += (s, e) => DebounceRefresh();
            }

            if (Directory.Exists(_customScreenshotDirectory))
            {
                _customFileWatcher = new FileSystemWatcher(_customScreenshotDirectory, "*.png")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };
                _customFileWatcher.Created += (s, e) => DebounceRefresh();
                _customFileWatcher.Deleted += (s, e) => DebounceRefresh();
                _customFileWatcher.Renamed += (s, e) => DebounceRefresh();
            }
        };

        this.Closed += (s, e) =>
        {
            _gameFileWatcher?.Dispose();
            _customFileWatcher?.Dispose();
            _debounceTimer?.Dispose();
        };
    }


    private void DebounceRefresh()
    {
        if (_debounceTimer == null)
        {
            _debounceTimer = new Timer(
                async _ =>
                {
                    await DispatcherQueue.EnqueueAsync(async () =>
                    {
                        await RefreshViewAfterDataChangedAsync();
                    });
                },
                null,
                TimeSpan.FromMilliseconds(500),
                Timeout.InfiniteTimeSpan
            );
        }
        else
        {
            _debounceTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }
    }

    private void CustomizeTitleBar()
    {
        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow != null)
        {
            AppTitleBarRow.Height = new GridLength(_appWindow.TitleBar.Height);

            _appWindow.TitleBar.ExtendsContentIntoTitleBar = true;
            _appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            _appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    private async Task LoadScreenshotsAsync()
    {
        _galleryData.Clear();
        _flatItems.Clear();

        var allFiles = new List<(FileInfo file, string sourceLabel)>();
        
        if (Directory.Exists(_gameScreenshotDirectory))
        {
            var gameFiles = await Task.Run(() =>
            {
                try
                {
                    return Directory.GetFiles(_gameScreenshotDirectory, "*.png", SearchOption.TopDirectoryOnly)
                        .Select(f => new FileInfo(f))
                        .ToList();
                }
                catch { return new List<FileInfo>(); }
            });
            allFiles.AddRange(gameFiles.Select(f => (f, "游戏截图")));
        }
        
        if (Directory.Exists(_customScreenshotDirectory))
        {
            var customFiles = await Task.Run(() =>
            {
                try
                {
                    return Directory.GetFiles(_customScreenshotDirectory, "*.png", SearchOption.TopDirectoryOnly)
                        .Select(f => new FileInfo(f))
                        .ToList();
                }
                catch { return new List<FileInfo>(); }
            });
            allFiles.AddRange(customFiles.Select(f => (f, "启动器截图")));
        }

        if (!allFiles.Any()) return;
        
        var sorted = allFiles.OrderByDescending(x => x.file.CreationTime).ToList();
        var groupedFiles = sorted.GroupBy(x => x.file.CreationTime.ToString("yyyy年MM月dd日"));

        foreach (var group in groupedFiles)
        {
            var folderGroup = new ScreenshotGroup { DateKey = group.Key };
            foreach (var (file, sourceLabel) in group)
            {
                var bitmap = new BitmapImage(new Uri(file.FullName));
                var item = new ScreenshotItem
                {
                    FilePath = file.FullName,
                    FileName = file.Name,
                    CreationTime = file.CreationTime,
                    ImageSource = bitmap,
                    SourceLabel = sourceLabel
                };
                folderGroup.Items.Add(item);
                _flatItems.Add(item);
            }
            _galleryData.Add(folderGroup);
        }

        GalleryViewSource.Source = _galleryData;
    }

    private void GridItem_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.RenderTransform is ScaleTransform scaleTransform)
        {
            AnimateScale(scaleTransform, 1.05);
        }
    }

    private void GridItem_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.RenderTransform is ScaleTransform scaleTransform)
        {
            AnimateScale(scaleTransform, 1.0);
        }
    }

    private void AnimateScale(ScaleTransform target, double toScale)
    {
        var storyboard = new Storyboard();

        var animX = new DoubleAnimation { To = toScale, Duration = new Duration(TimeSpan.FromMilliseconds(200)), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        var animY = new DoubleAnimation { To = toScale, Duration = new Duration(TimeSpan.FromMilliseconds(200)), EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };

        Storyboard.SetTarget(animX, target);
        Storyboard.SetTargetProperty(animX, "ScaleX");

        Storyboard.SetTarget(animY, target);
        Storyboard.SetTargetProperty(animY, "ScaleY");

        storyboard.Children.Add(animX);
        storyboard.Children.Add(animY);
        storyboard.Begin();
    }

    private async void GalleryGridView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ScreenshotItem item)
        {
            _currentDetailItem = item;

            var gridViewItem = GalleryGridView.ContainerFromItem(item) as GridViewItem;
            if (gridViewItem != null)
            {
                var imageInGrid = (Image)FindDescendantByName(gridViewItem, "GridImage");

                if (imageInGrid != null)
                {
                    ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(ConnectedAnimationKey, imageInGrid);
                }
            }

            DetailImageViewer.SelectedItem = item;

            DetailOverlayGrid.Opacity = 0;
            GalleryGridView.Visibility = Visibility.Collapsed;
            DetailOverlayGrid.Visibility = Visibility.Visible;

            DetailOverlayGrid.UpdateLayout();
            await Task.Yield();

            var anim = ConnectedAnimationService.GetForCurrentView().GetAnimation(ConnectedAnimationKey);
            if (anim != null)
            {
                anim.Configuration = new BasicConnectedAnimationConfiguration();
                anim.TryStart(DetailImageViewer);
            }
            DetailOverlayGrid.Opacity = 1;

        }
    }

    private async void CloseDetailView_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailItem == null) return;

        ConnectedAnimationService.GetForCurrentView().PrepareToAnimate(ConnectedAnimationKey, DetailImageViewer);

        DetailOverlayGrid.Visibility = Visibility.Collapsed;
        GalleryGridView.Visibility = Visibility.Visible;

        var gridViewItem = GalleryGridView.ContainerFromItem(_currentDetailItem) as GridViewItem;
        if (gridViewItem != null)
        {
            GalleryGridView.ScrollIntoView(_currentDetailItem);
            await Task.Delay(1);

            ConnectedAnimationService.GetForCurrentView().GetAnimation(ConnectedAnimationKey).TryStart(gridViewItem);
        }

        _currentDetailItem = null;
        DetailImageViewer.SelectedItem = null;
    }

    private async void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailItem != null)
        {
            await SetClipboardDataAsync(DataPackageOperation.Copy);
        }
    }

    private async void CutImage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailItem != null)
        {
            await SetClipboardDataAsync(DataPackageOperation.Move);
        }
    }

    private async Task SetClipboardDataAsync(DataPackageOperation operation)
    {
        try
        {
            if (_currentDetailItem == null || !File.Exists(_currentDetailItem.FilePath)) return;
            var storageFile = await StorageFile.GetFileFromPathAsync(_currentDetailItem.FilePath);
            var dataPackage = new DataPackage();
            dataPackage.SetStorageItems(new List<IStorageItem> { storageFile });
            dataPackage.RequestedOperation = operation;
            Clipboard.SetContent(dataPackage);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"剪贴板操作失败: {ex.Message}");
        }
    }

    private async void DeleteImage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailItem == null || !File.Exists(_currentDetailItem.FilePath)) return;

        try
        {
            var dialog = new ContentDialog
            {
                Title = "确认删除",
                Content = "确定要将此截图移到回收站吗？",
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = RootGrid.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                // 直接移到回收站，不再处理原先逻辑。因为删除操作也会触发FileSystemWatcher喵
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        _currentDetailItem.FilePath,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"删除文件失败: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"弹窗显示失败: {ex.Message}");
        }
    }

    private async Task RefreshViewAfterDataChangedAsync()
    {
        await LoadScreenshotsAsync();

        bool gameExists = Directory.Exists(_gameScreenshotDirectory);
        bool customExists = Directory.Exists(_customScreenshotDirectory);
        
        if (!gameExists && !customExists)
        {
            EmptyStateGrid.Visibility = Visibility.Visible;
            GalleryGridView.Visibility = Visibility.Collapsed;
            return;
        }
        
        var hasItems = _galleryData.Count > 0;
        EmptyStateGrid.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        GalleryGridView.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

        
        if (DetailOverlayGrid.Visibility != Visibility.Visible)
        {
            _currentDetailItem = null;
            DetailImageViewer.SelectedItem = null;
            return;
        }
        
        if (_flatItems.Count == 0)
        {
            DetailOverlayGrid.Visibility = Visibility.Collapsed;
            GalleryGridView.Visibility = Visibility.Visible;
            _currentDetailItem = null;
            DetailImageViewer.SelectedItem = null;
            return;
        }

        ScreenshotItem? nextItem = null;

        if (_currentDetailItem is not null && !string.IsNullOrWhiteSpace(_currentDetailItem.FilePath))
        {
            var nextIndex = Math.Clamp(_flatItems.IndexOf(_currentDetailItem) + 1, 0, _flatItems.Count - 1);
            nextItem = _flatItems[nextIndex];
        }

        _currentDetailItem = nextItem;
        DetailImageViewer.SelectedItem = nextItem;

        DetailOverlayGrid.Visibility = Visibility.Visible;
        GalleryGridView.Visibility = Visibility.Collapsed;
    }

    private async void OpenWithSystemApp_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailItem != null && File.Exists(_currentDetailItem.FilePath))
        {
            try
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(_currentDetailItem.FilePath);
                var options = new Windows.System.LauncherOptions { DisplayApplicationPicker = true };
                await Windows.System.Launcher.LaunchFileAsync(storageFile, options);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"系统应用打开失败: {ex.Message}");
            }
        }
    }

    private void DetailImageViewer_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 切换图片时要重置缩放喵
        if (DetailImageViewer.SelectedItem is not ScreenshotItem item) return;

        _currentDetailItem = item;

        if (DetailImageViewer.ContainerFromItem(item) is not DependencyObject flipViewItem) return;

        if (FindDescendantByName(flipViewItem, "DetailScrollViewer") is ScrollViewer scrollViewer)
        {
            _ = scrollViewer.ChangeView(0, 0, 1.0f, true);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailItem != null && File.Exists(_currentDetailItem.FilePath))
        {
            try
            {
                var argument = $"/select, \"{_currentDetailItem.FilePath}\"";
                System.Diagnostics.Process.Start("explorer.exe", argument);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"系统文件夹打开失败: {ex.Message}");
            }
        }
    }

    private DependencyObject FindDescendantByName(DependencyObject parent, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement fe && fe.Name == name)
            {
                return child;
            }
            DependencyObject descendant = FindDescendantByName(child, name);
            if (descendant != null)
            {
                return descendant;
            }
        }
        return null;
    }

    private void OpenEmptyFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_gameScreenshotDirectory))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", _gameScreenshotDirectory); } catch { }
            return;
        }
        if (Directory.Exists(_customScreenshotDirectory))
        {
            try { System.Diagnostics.Process.Start("explorer.exe", _customScreenshotDirectory); } catch { }
        }
    }
}
