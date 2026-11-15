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
using System.IO;
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
    public class GachaUidDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not string uid || uid.Length is not (9 or 10) || !uid.All(char.IsAsciiDigit))
                return value;
            var region = Models.MiHoYo.ServerRegion.Resolve(uid);
            return Models.MiHoYo.ServerRegion.IsOversea(region) ? uid : GameRoleDisplay.Archive(uid);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotImplementedException();
    }

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

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotImplementedException();
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

        public object ConvertBack(object value, Type targetType, object parameter, string language) =>
            throw new NotImplementedException();
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
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = sweepAngle > 180
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            return geometry;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();

        private static Point PointOnCircle(double center, double radius, double angleInDegrees)
        {
            var radians = (angleInDegrees - 90) * Math.PI / 180.0;
            return new Point(
                center + radius * Math.Cos(radians),
                center + radius * Math.Sin(radians));
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
            DependencyProperty.Register(nameof(ColumnSpacing), typeof(double), typeof(GachaKpiPanel),
                new PropertyMetadata(10d));

        public static readonly DependencyProperty RowSpacingProperty =
            DependencyProperty.Register(nameof(RowSpacing), typeof(double), typeof(GachaKpiPanel),
                new PropertyMetadata(10d));

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
            DependencyProperty.Register(nameof(MinSlotWidth), typeof(double), typeof(GachaChartPanel),
                new PropertyMetadata(72d));

        public static readonly DependencyProperty ChartHeightProperty =
            DependencyProperty.Register(nameof(ChartHeight), typeof(double), typeof(GachaChartPanel),
                new PropertyMetadata(174d));

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

            var slotWidth = Math.Max(1, MinSlotWidth);
            var totalWidth = slotWidth * count;
            for (var i = 0; i < count; i++)
            {
                Children[i].Measure(new Size(slotWidth, height));
            }

            return new Size(totalWidth, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var count = Children.Count;
            if (count == 0) return finalSize;

            var slotWidth = count > 0 ? finalSize.Width / count : finalSize.Width;
            if (slotWidth < MinSlotWidth)
            {
                slotWidth = MinSlotWidth;
            }

            for (var i = 0; i < count; i++)
            {
                var x = i * slotWidth;
                Children[i].Arrange(new Rect(x, 0, slotWidth, finalSize.Height));
            }

            return new Size(Math.Max(finalSize.Width, slotWidth * count), finalSize.Height);
        }
    }

    public sealed partial class GachaAnalysisWindow : Window
    {
        public GachaAnalysisViewModel ViewModel
        {
            get;
        }

        private bool _updatingTabSelection = true;
        private int _lastTabIndex = 0;
        private Storyboard? _analysisChartStoryboard;
        private Storyboard? _sectionTransitionStoryboard;
        private bool _firstActivated = true;

        public GachaAnalysisWindow()
        {
            ViewModel = App.GetService<GachaAnalysisViewModel>();

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
                        Title = "GachaAnalysis_DifferentAccountTitle".GetLocalized(),
                        Content = string.Format("GachaAnalysis_DifferentAccountContent_Format".GetLocalized(), currentUid, incomingUid),
                        PrimaryButtonText = "GachaAnalysis_CreateNewArchive".GetLocalized(),
                        CloseButtonText = "CancelBtn".GetLocalized(),
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
                        PrimaryButtonText = buttonText ?? "Common_Confirm".GetLocalized(),
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
                        Title = "GachaAnalysis_FetchFailedTitle".GetLocalized(),
                        Content = msg,
                        CloseButtonText = "Common_GotIt".GetLocalized(),
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
                            Title = "GachaAnalysis_ReloginTitle".GetLocalized(),
                            Content = message,
                            PrimaryButtonText = "GachaAnalysis_GoToLogin".GetLocalized(),
                            CloseButtonText = "CancelBtn".GetLocalized(),
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

            ViewModel.SubscribeToRoleChanges();
            Closed += (_, _) => ViewModel.CleanupRoleChanges();

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
                        if (ViewModel.HasGachaData && ViewModel.IsOverviewSelected)
                        {
                            var target = ViewModel.IsCardViewMode
                                ? (UIElement)OverviewCardScrollViewer
                                : OverviewListScrollViewer;
                            PlaySectionTransition(target);
                        }
                    });
                }
                else if (e.PropertyName == nameof(ViewModel.HasGachaData))
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        if (ViewModel.IsDataLoaded)
                            EmptyStatePanel.Visibility =
                                ViewModel.HasGachaData ? Visibility.Collapsed : Visibility.Visible;
                    });
                }
                else if (e.PropertyName == nameof(ViewModel.IsOverviewSelected) ||
                         e.PropertyName == nameof(ViewModel.IsHistorySelected))
                {
                    DispatcherQueue.TryEnqueue(UpdateTabIndicator);
                }
            };

            _updatingTabSelection = false;
            UpdateTabIndicator();
            ViewModel.IsDataLoaded = false;
        }
    }
}