/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services;
using FufuLauncher.ViewModels;
using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Converters
{
    public class CountToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            int count = 0;
            if (value is int i) count = i;
            else if (value is string s) int.TryParse(s, out count);

            if (count <= 30) return new SolidColorBrush(Colors.LimeGreen);
            if (count <= 60) return new SolidColorBrush(Colors.Orange);
            return new SolidColorBrush(Colors.Red);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class TotalToPrimogemsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value == null) return "0";
            int total = 0;
            if (value is int i) total = i;
            else if (value is string s) int.TryParse(s, out total);
            return (total * 160).ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class PityStatusToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is PityStatus status && status != PityStatus.None)
                return Visibility.Visible;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class GachaTabForegroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var selected = value is bool b && b;
            return selected
                ? Application.Current.Resources["AccentTextFillColorPrimaryBrush"]
                : Application.Current.Resources["TextFillColorSecondaryBrush"];
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class GachaChartBrushConverter : IValueConverter
    {
        private static readonly Windows.UI.Color[] ChartPalette =
        {
            Windows.UI.Color.FromArgb(255, 76, 154, 255),
            Windows.UI.Color.FromArgb(255, 116, 201, 127),
            Windows.UI.Color.FromArgb(255, 255, 181, 71),
            Windows.UI.Color.FromArgb(255, 217, 118, 224),
            Windows.UI.Color.FromArgb(255, 255, 126, 103),
            Windows.UI.Color.FromArgb(255, 93, 207, 200),
            Windows.UI.Color.FromArgb(255, 155, 138, 255),
            Windows.UI.Color.FromArgb(255, 184, 196, 102),
            Windows.UI.Color.FromArgb(255, 232, 140, 177),
            Windows.UI.Color.FromArgb(255, 117, 174, 194)
        };

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            var index = value is int i ? i : 0;
            return new SolidColorBrush(ChartPalette[Math.Abs(index) % ChartPalette.Length]);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }

    public class GachaPieGeometryConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not GachaPieSlice slice) return null;

            const double center = 68;
            const double radius = 64;
            var sweepAngle = slice.SweepAngle >= 359.99 ? 359.99 : slice.SweepAngle;
            var start = PointOnCircle(center, radius, slice.StartAngle);
            var end = PointOnCircle(center, radius, slice.StartAngle + sweepAngle);

            var figure = new PathFigure
            {
                StartPoint = new Point(center, center),
                IsClosed = true
            };
            figure.Segments.Add(new LineSegment { Point = start });
            figure.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                IsLargeArc = sweepAngle > 180,
                SweepDirection = SweepDirection.Clockwise
            });

            return new PathGeometry { Figures = { figure } };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();

        private static Point PointOnCircle(double center, double radius, double angle)
        {
            var radians = Math.PI * angle / 180;
            return new Point(center + radius * Math.Cos(radians), center + radius * Math.Sin(radians));
        }
    }
}

namespace FufuLauncher.Views
{
    public sealed class GachaKpiPanel : Panel
    {
        public static readonly DependencyProperty ColumnsProperty =
            DependencyProperty.Register(nameof(Columns), typeof(int), typeof(GachaKpiPanel), new PropertyMetadata(5));

        public static readonly DependencyProperty ColumnSpacingProperty =
            DependencyProperty.Register(nameof(ColumnSpacing), typeof(double), typeof(GachaKpiPanel), new PropertyMetadata(10d));

        public static readonly DependencyProperty RowSpacingProperty =
            DependencyProperty.Register(nameof(RowSpacing), typeof(double), typeof(GachaKpiPanel), new PropertyMetadata(10d));

        public int Columns
        {
            get => (int)GetValue(ColumnsProperty);
            set => SetValue(ColumnsProperty, value);
        }

        public double ColumnSpacing
        {
            get => (double)GetValue(ColumnSpacingProperty);
            set => SetValue(ColumnSpacingProperty, value);
        }

        public double RowSpacing
        {
            get => (double)GetValue(RowSpacingProperty);
            set => SetValue(RowSpacingProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var count = Children.Count;
            if (count == 0) return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 0);

            var columns = Math.Max(1, Columns);
            var columnSpacing = Math.Max(0, ColumnSpacing);
            var rowSpacing = Math.Max(0, RowSpacing);
            var rowCount = (int)Math.Ceiling(count / (double)columns);
            var desiredWidth = double.IsInfinity(availableSize.Width)
                ? columns * 190 + (columns - 1) * columnSpacing
                : availableSize.Width;
            var columnWidth = Math.Max(0, (desiredWidth - (columns - 1) * columnSpacing) / columns);
            var rowHeights = new double[rowCount];

            for (var i = 0; i < count; i++)
            {
                var row = i / columns;
                var child = Children[i];
                child.Measure(new Size(columnWidth, availableSize.Height));
                rowHeights[row] = Math.Max(rowHeights[row], child.DesiredSize.Height);
            }

            var desiredHeight = rowHeights.Sum() + Math.Max(0, rowCount - 1) * rowSpacing;
            return new Size(desiredWidth, desiredHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var count = Children.Count;
            if (count == 0) return finalSize;

            var columns = Math.Max(1, Columns);
            var columnSpacing = Math.Max(0, ColumnSpacing);
            var rowSpacing = Math.Max(0, RowSpacing);
            var rowCount = (int)Math.Ceiling(count / (double)columns);
            var columnWidth = Math.Max(0, (finalSize.Width - (columns - 1) * columnSpacing) / columns);
            var rowHeights = new double[rowCount];

            for (var i = 0; i < count; i++)
            {
                rowHeights[i / columns] = Math.Max(rowHeights[i / columns], Children[i].DesiredSize.Height);
            }

            var y = 0d;
            for (var row = 0; row < rowCount; row++)
            {
                var x = 0d;
                for (var column = 0; column < columns; column++)
                {
                    var index = row * columns + column;
                    if (index >= count) break;

                    Children[index].Arrange(new Rect(x, y, columnWidth, rowHeights[row]));
                    x += columnWidth + columnSpacing;
                }

                y += rowHeights[row] + rowSpacing;
            }

            return finalSize;
        }
    }

    public sealed class GachaChartPanel : Panel
    {
        public static readonly DependencyProperty MinSlotWidthProperty =
            DependencyProperty.Register(nameof(MinSlotWidth), typeof(double), typeof(GachaChartPanel), new PropertyMetadata(72d));

        public static readonly DependencyProperty ChartHeightProperty =
            DependencyProperty.Register(nameof(ChartHeight), typeof(double), typeof(GachaChartPanel), new PropertyMetadata(174d));

        public double MinSlotWidth
        {
            get => (double)GetValue(MinSlotWidthProperty);
            set => SetValue(MinSlotWidthProperty, value);
        }

        public double ChartHeight
        {
            get => (double)GetValue(ChartHeightProperty);
            set => SetValue(ChartHeightProperty, value);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var count = Children.Count;
            var height = Math.Max(1, ChartHeight);
            if (!double.IsInfinity(availableSize.Height))
            {
                height = Math.Max(height, availableSize.Height);
            }

            if (count == 0)
            {
                return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, height);
            }

            var minSlotWidth = Math.Max(24, MinSlotWidth);
            var desiredWidth = count * minSlotWidth;
            if (!double.IsInfinity(availableSize.Width))
            {
                desiredWidth = Math.Max(availableSize.Width, desiredWidth);
            }

            var slotWidth = desiredWidth / count;
            foreach (var child in Children)
            {
                child.Measure(new Size(slotWidth, height));
            }

            return new Size(desiredWidth, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var count = Children.Count;
            var height = Math.Max(Math.Max(1, ChartHeight), finalSize.Height);
            if (count == 0)
            {
                return new Size(finalSize.Width, height);
            }

            var minSlotWidth = Math.Max(24, MinSlotWidth);
            var slotWidth = Math.Max(minSlotWidth, finalSize.Width / count);
            var x = 0d;

            foreach (var child in Children)
            {
                child.Arrange(new Rect(x, 0, slotWidth, height));
                x += slotWidth;
            }

            return new Size(x, height);
        }
    }

    public sealed partial class GachaAnalysisWindow : Window
    {
        public GachaAnalysisModel ViewModel { get; }
        private bool _updatingTabSelection = true;
        private Storyboard _analysisChartStoryboard;

        public GachaAnalysisWindow()
        {
            ViewModel = App.GetService<GachaAnalysisModel>();
            
            InitializeComponent();
            
            RootGrid.DataContext = this;
            ExtendsContentIntoTitleBar = true;
            WindowManagerHelper.ResizeWithDpi(AppWindow, this, 1120, 720);
            WindowManagerHelper.CenterWindowOnScreen(AppWindow, 1120, 720);
            LoadingRing.IsActive = true;

            ViewModel.GetWindow = () => this;
            ViewModel.RequestMetadataScrapeAction = async () => await ViewModel.FetchMetadataFromApiAsync();
            this.Activated += OnWindowFirstActivated;
            ViewModel.OnUidMismatchAsync = async (currentUid, incomingUid) =>
            {
                var tcs = new TaskCompletionSource<bool>();
                DispatcherQueue.TryEnqueue(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "检测到不同账号",
                        Content = $"当前数据属于 UID: {currentUid}\n即将导入的数据来自 UID: {incomingUid}\n\n是否为 UID {incomingUid} 创建新的数据存档？",
                        PrimaryButtonText = "创建新存档",
                        CloseButtonText = "取消",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = Content.XamlRoot
                    };
                    var result = await dialog.ShowAsync();
                    tcs.TrySetResult(result == ContentDialogResult.Primary);
                });
                return await tcs.Task;
            };
            ViewModel.OnShowConfirmDialogAsync = async (title, content, buttonText) =>
            {
                var tcs = new TaskCompletionSource();
                DispatcherQueue.TryEnqueue(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = title,
                        Content = content,
                        PrimaryButtonText = buttonText ?? "确定",
                        DefaultButton = ContentDialogButton.Primary,
                        XamlRoot = Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                    tcs.TrySetResult();
                });
                await tcs.Task;
            };
            ViewModel.OnErrorAction = (msg) =>
            {
                DispatcherQueue.TryEnqueue(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "获取失败",
                        Content = msg,
                        CloseButtonText = "知道了",
                        DefaultButton = ContentDialogButton.Close,
                        XamlRoot = Content.XamlRoot
                    };
                    await dialog.ShowAsync();
                });
            };

            ViewModel.OnRequireReLoginAsync = async (message) =>
            {
                var tcs = new TaskCompletionSource();
                DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "需要登录目标账户",
                            Content = message,
                            PrimaryButtonText = "前往登录",
                            CloseButtonText = "取消",
                            DefaultButton = ContentDialogButton.Primary,
                            XamlRoot = Content.XamlRoot
                        };
                        var result = await dialog.ShowAsync();
                        if (result == ContentDialogResult.Primary && App.MainWindow is MainWindow mainWindow)
                            await mainWindow.NavigateToAccountPageAsync();
                    }
                    finally
                    {
                        tcs.TrySetResult();
                    }
                });
                await tcs.Task;
            };

            ViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ViewModel.IsDataLoaded) && ViewModel.IsDataLoaded)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        LoadingRing.IsActive = false;
                        EmptyStatePanel.Visibility = ViewModel.HasGachaData ? Visibility.Collapsed : Visibility.Visible;
                        if (!string.IsNullOrEmpty(ViewModel.SelectedUid))
                            UidComboBox.SelectedItem = ViewModel.SelectedUid;
                    });
                }
                else if (e.PropertyName == nameof(ViewModel.HasGachaData))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (ViewModel.IsDataLoaded)
                            EmptyStatePanel.Visibility = ViewModel.HasGachaData ? Visibility.Collapsed : Visibility.Visible;
                    });
                }
                else if (e.PropertyName == nameof(ViewModel.IsOverviewSelected))
                {
                    DispatcherQueue.TryEnqueue(UpdateTabIndicator);
                }
            };

            _updatingTabSelection = false;
            UpdateTabIndicator();
            ViewModel.IsDataLoaded = false;
        }

        private async void OnOverviewTabClick(object sender, RoutedEventArgs e)
        {
            await ViewModel.ShowOverviewAsync();
            UpdateTabIndicator();
        }

        private async void OnAnalysisTabClick(object sender, RoutedEventArgs e)
        {
            await ViewModel.ShowAnalysisAsync();
            UpdateTabIndicator();
        }
        
        private async void OnAboutButtonClick(object sender, RoutedEventArgs e)
        {
            var contentPanel = new StackPanel { Spacing = 8 };
    
            contentPanel.Children.Add(new TextBlock 
            { 
                Text = "该项目使用UIGF v4.2/v4.1/v4.0/v3.0/v2.4/v2.3/v2.2标准格式处理祈愿数据",
                TextWrapping = TextWrapping.Wrap 
            });

            contentPanel.Children.Add(new HyperlinkButton
            {
                Content = "UIGF-Org",
                NavigateUri = new Uri("https://uigf.org/")
            });
            
            var badgePanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                Spacing = 8,
                Margin = new Thickness(0, 4, 0, 0)
            };
    
            badgePanel.Children.Add(new FontIcon 
            { 
                Glyph = "\uE734",
                FontSize = 16,
                Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]
            });
    
            badgePanel.Children.Add(new TextBlock 
            { 
                Text = "已获UIGF/UIAF标准项目合作",
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });

            contentPanel.Children.Add(badgePanel);

            var dialog = new ContentDialog
            {
                Title = "关于",
                Content = contentPanel,
                CloseButtonText = "关闭",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private void UpdateTabIndicator()
        {
            if (GachaTabPivot == null) return;

            var selectedIndex = ViewModel.IsOverviewSelected ? 0 : 1;
            if (GachaTabPivot.SelectedIndex == selectedIndex) return;

            _updatingTabSelection = true;
            try
            {
                GachaTabPivot.SelectedIndex = selectedIndex;
            }
            finally
            {
                _updatingTabSelection = false;
            }
        }

        private async void OnGachaTabPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_updatingTabSelection) return;

            if (GachaTabPivot.SelectedIndex == 0)
            {
                await ViewModel.ShowOverviewAsync();
            }
            else
            {
                await ViewModel.ShowAnalysisAsync();
                DispatcherQueue.TryEnqueue(PlayAnalysisChartAnimations);
            }

            UpdateTabIndicator();
        }

        private void OnListViewModeClick(object sender, RoutedEventArgs e)
        {
            if (!ViewModel.IsCardViewMode)
            {
                ListViewModeButton.IsChecked = true;
                return;
            }

            ViewModel.IsCardViewMode = false;
            DispatcherQueue.TryEnqueue(() => PlayOverviewTransition(OverviewListScrollViewer));
        }

        private void OnCardViewModeClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel.IsCardViewMode)
            {
                CardViewModeButton.IsChecked = true;
                return;
            }

            ViewModel.IsCardViewMode = true;
            DispatcherQueue.TryEnqueue(() => PlayOverviewTransition(OverviewCardScrollViewer));
        }

        private void PlayOverviewTransition(UIElement target)
        {
            if (target == null) return;

            if (target.RenderTransform is not CompositeTransform transform)
            {
                transform = new CompositeTransform();
                target.RenderTransform = transform;
                target.RenderTransformOrigin = new Point(0.5, 0.5);
            }

            target.Opacity = 0;
            transform.TranslateY = 20;
            transform.ScaleX = 0.985;
            transform.ScaleY = 0.985;

            var storyboard = new Storyboard();
            AddViewModeAnimation(storyboard, target, "Opacity", 0, 1, 0, 320);
            AddViewModeAnimation(storyboard, transform, "TranslateY", 20, 0, 0, 380);
            AddViewModeAnimation(storyboard, transform, "ScaleX", 0.985, 1, 0, 380);
            AddViewModeAnimation(storyboard, transform, "ScaleY", 0.985, 1, 0, 380);
            storyboard.Begin();
        }

        private static void AddViewModeAnimation(Storyboard storyboard, DependencyObject target, string property, double from, double to, int delayMs, int durationMs)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        private void PlayAnalysisChartAnimations()
        {
            if (!ViewModel.ShowAnalysisContent) return;

            _analysisChartStoryboard?.Stop();
            _analysisChartStoryboard = new Storyboard();

            AddPieAnimation(RarityPieChart, RarityPieScale, 0);
            AddPieAnimation(PoolPieChart, PoolPieScale, 80);
            AddChartAnimation(RecentFiveStarChart, 130);
            AddChartAnimation(FourStarTopChart, 190);
            AddChartAnimation(PityBucketsChart, 250);
            AddChartAnimation(MonthlyPullsChart, 310);

            _analysisChartStoryboard.Begin();
        }

        private void AddPieAnimation(UIElement element, ScaleTransform scale, int delayMs)
        {
            if (element == null || scale == null) return;

            element.Opacity = 0;
            scale.ScaleX = 0.96;
            scale.ScaleY = 0.96;

            AddDoubleAnimation(element, "Opacity", 0, 1, delayMs, 260);
            AddDoubleAnimation(scale, "ScaleX", 0.96, 1.235, delayMs, 360);
            AddDoubleAnimation(scale, "ScaleY", 0.96, 1.235, delayMs, 360);
        }

        private void AddChartAnimation(UIElement element, int delayMs)
        {
            if (element == null) return;

            if (element.RenderTransform is not CompositeTransform transform)
            {
                transform = new CompositeTransform();
                element.RenderTransform = transform;
            }

            element.Opacity = 0;
            transform.TranslateY = 18;
            transform.ScaleY = 0.94;

            AddDoubleAnimation(element, "Opacity", 0, 1, delayMs, 240);
            AddDoubleAnimation(transform, "TranslateY", 18, 0, delayMs, 340);
            AddDoubleAnimation(transform, "ScaleY", 0.94, 1, delayMs, 340);
        }

        private void AddDoubleAnimation(DependencyObject target, string property, double from, double to, int delayMs, int durationMs)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(animation, target);
            Storyboard.SetTargetProperty(animation, property);
            _analysisChartStoryboard.Children.Add(animation);
        }

        private void OnChartScrollViewerLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                StretchChartContentToViewport(scrollViewer);
            }
        }

        private void OnChartScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                StretchChartContentToViewport(scrollViewer);
            }
        }
        
        private async Task<InMemoryRandomAccessStream> RenderElementToStreamAsync(UIElement element)
        {
            AnalysisExportButtons.Visibility = Visibility.Collapsed;
            AnalysisExportLogo.Visibility = Visibility.Visible;
            AnalysisExportTarget.Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
                ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);

            var tcs = new TaskCompletionSource();
            DispatcherQueue.TryEnqueue(() => tcs.SetResult());
            await tcs.Task;

            try
            {
                var renderTargetBitmap = new RenderTargetBitmap();
                await renderTargetBitmap.RenderAsync(element);

                var pixels = await renderTargetBitmap.GetPixelsAsync();

                var stream = new InMemoryRandomAccessStream();
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);

                encoder.SetPixelData(
                    BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Premultiplied,
                    (uint)renderTargetBitmap.PixelWidth,
                    (uint)renderTargetBitmap.PixelHeight,
                    96,
                    96,
                    pixels.ToArray());

                await encoder.FlushAsync();
                return stream;
            }
            finally
            {
                AnalysisExportButtons.Visibility = Visibility.Visible;
                AnalysisExportLogo.Visibility = Visibility.Collapsed;
                AnalysisExportTarget.Background = null;
            }
        }

        private async void ShowDialogMessage(string title, string content)
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                CloseButtonText = "知道了",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async void OnCopyAnalysisImageClick(object sender, RoutedEventArgs e)
        {
            try
            {
                using var stream = await RenderElementToStreamAsync(AnalysisExportTarget);
                stream.Seek(0);
                var dataPackage = new DataPackage();
                dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                Clipboard.SetContent(dataPackage);

                for (var i = 0; i < 5; i++)
                {
                    try
                    {
                        Clipboard.Flush();
                        break;
                    }
                    catch (System.Runtime.InteropServices.COMException) when (i < 4)
                    {
                        await Task.Delay(100);
                    }
                }

                ShowDialogMessage("复制成功", "祈愿分析图片已复制到剪贴板。");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Gacha] 复制图片失败: {ex}");
                ShowDialogMessage("复制失败", ex.Message);
            }
        }

        private async void OnSaveAnalysisImageClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var savePicker = new FileSavePicker();
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

                savePicker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
                savePicker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
                savePicker.SuggestedFileName = $"祈愿分析_{ViewModel.SelectedUid}_{DateTime.Now:yyyyMMddHHmmss}";

                var file = await savePicker.PickSaveFileAsync();
                if (file == null) return;

                using var stream = await RenderElementToStreamAsync(AnalysisExportTarget);
                using var fileStream = await file.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite);
                using var reader = new DataReader(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)stream.Size);
                var buffer = new byte[stream.Size];
                reader.ReadBytes(buffer);
                
                using var dataWriter = new DataWriter(fileStream);
                dataWriter.WriteBytes(buffer);
                await dataWriter.StoreAsync();
                await fileStream.FlushAsync();

                ShowDialogMessage("保存成功", $"祈愿分析图片已保存至文件。");
            }
            catch (Exception ex)
            {
                ShowDialogMessage("保存失败", $"详细信息: {ex.Message}");
            }
        }

        private static void StretchChartContentToViewport(ScrollViewer scrollViewer)
        {
            if (scrollViewer.Content is FrameworkElement content)
            {
                content.MinWidth = Math.Max(0, scrollViewer.ActualWidth);
                content.MinHeight = Math.Max(0, scrollViewer.ActualHeight);
            }
        }

        private void OnHorizontalScrollViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
        {
            if (sender is not ScrollViewer scrollViewer || scrollViewer.ScrollableWidth <= 0) return;

            var delta = e.GetCurrentPoint(scrollViewer).Properties.MouseWheelDelta;
            if (delta == 0) return;

            var nextOffset = Math.Clamp(scrollViewer.HorizontalOffset - delta, 0, scrollViewer.ScrollableWidth);
            scrollViewer.ChangeView(nextOffset, null, null, true);
            e.Handled = true;
        }

        private async void OnDeleteGachaDataClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ViewModel.SelectedUid))
            {
                var noDataDialog = new ContentDialog
                {
                    Title = "提示",
                    Content = "当前没有选中任何账号，无法删除记录。",
                    CloseButtonText = "知道了",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };
                await noDataDialog.ShowAsync();
                return;
            }

            ContentDialog deleteDialog = new()
            {
                Title = "警告",
                Content = $"确定要删除 UID: {ViewModel.SelectedUid} 的所有抽卡记录吗？\n此操作不可逆转！",
                PrimaryButtonText = "确认删除",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = Content.XamlRoot
            };

            ContentDialogResult result = await deleteDialog.ShowAsync();
            if (result == ContentDialogResult.Primary) await ViewModel.ClearGachaDataAsync();
        }

        private async void OnMiYouSheLoginClick(object sender, RoutedEventArgs e)
        {
            await PerformMiYouSheFetchAsync(incremental: false);
        }

        private async void OnMiYouSheFullUpdateClick(object sender, RoutedEventArgs e)
        {
            await PerformMiYouSheFetchAsync(incremental: false);
        }

        private async void OnMiYouSheIncrementalUpdateClick(object sender, RoutedEventArgs e)
        {
            await PerformMiYouSheFetchAsync(incremental: true);
        }

        private async Task PerformMiYouSheFetchAsync(bool incremental)
        {
            var localSettingsService = App.GetService<ILocalSettingsService>();
            var isOsObj = await localSettingsService.ReadSettingAsync("IsInternationalAccount");
            bool isInternational = isOsObj is bool isOs && isOs;

            if (isInternational)
            {
                var osDialog = new ContentDialog
                {
                    Title = "国际服暂不支持",
                    Content = "国际服（HoYoLAB）账号的抽卡记录获取功能正在适配中，敬请期待。\n\n国际服用户可通过「通过URL获取」方式导入抽卡记录。",
                    CloseButtonText = "知道了",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };
                await osDialog.ShowAsync();
                return;
            }

            var accountManager = App.GetService<AccountManager>();
            var activeAccount = accountManager.GetActiveAccountEntry();
            var isLoggedIn = activeAccount != null && !string.IsNullOrEmpty(activeAccount.GameUid);

            if (!isLoggedIn)
            {
                var dialog = new ContentDialog
                {
                    Title = "未登录米游社",
                    Content = "检测到尚未登录米游社账号，即将跳转到账户设置页面进行登录。",
                    CloseButtonText = "知道了",
                    DefaultButton = ContentDialogButton.Close,
                    XamlRoot = Content.XamlRoot
                };
                await dialog.ShowAsync();

                if (App.MainWindow is MainWindow mainWindow)
                    await mainWindow.NavigateToAccountPageAsync();
                return;
            }

            ViewModel.FetchFromMiYouSheCommand.Execute(incremental);
        }

        private async void OnUrlFetchClick(object sender, RoutedEventArgs e)
        {
            var urlBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 120,
                Text = ViewModel.GachaUrl ?? "",
                PlaceholderText = "在此处粘贴抽卡分析链接 (URL)..."
            };

            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(new TextBlock
            {
                Text = "粘贴从米游社或其他工具获取的抽卡记录链接，链接应包含 authkey 参数。",
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(urlBox);

            var dialog = new ContentDialog
            {
                Title = "通过 URL 获取抽卡记录",
                Content = panel,
                PrimaryButtonText = "开始获取",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                ViewModel.GachaUrl = urlBox.Text;
                ViewModel.FetchGachaDataCommand.Execute(null);
            }
        }

        private bool _firstActivated = true;

        private async void OnWindowFirstActivated(object sender, WindowActivatedEventArgs e)
        {
            if (!_firstActivated) return;
            _firstActivated = false;
            this.Activated -= OnWindowFirstActivated;

            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, async () =>
            {
                await ViewModel.LoadSavedGachaDataAsync();
            });
        }

        private async void OnUidComboBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox combo) return;
            if (combo.SelectedItem is not string selected) return;

            System.Diagnostics.Debug.WriteLine($"[Gacha] OnUidComboBoxSelectionChanged: selected={selected}, ViewModel.SelectedUid={ViewModel.SelectedUid}");

            if (selected == GachaAnalysisModel.AddNewUserItem)
            {
                var previous = ViewModel.SelectedUid;
                combo.SelectedItem = null;
                await ViewModel.AddNewUserCommand.ExecuteAsync(null);
            }
            else
            {
                await ViewModel.SwitchUidCommand.ExecuteAsync(selected);
            }
        }
    }
}

