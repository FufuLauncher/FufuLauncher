using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FufuLauncher.Views
{
    public enum ServerType
    {
        Unknown,
        CN_Official,
        CN_Bilibili,
        OS_Global
    }
    public sealed partial class PreDownloadWindow : Window
    {
        private readonly string _gameDir;
        private ContentDialog _progressDialog;
        private TextBlock _statusText;
        

        public PreDownloadWindow(string gameDir)
        {
            InitializeComponent();
            _gameDir = gameDir;

            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
            ExtendsContentIntoTitleBar = true;
            Title = "游戏预下载";

            var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var winId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(winId);
            appWindow.Resize(new Windows.Graphics.SizeInt32(600, 400));
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            _statusText = new TextBlock { Text = "准备中...", TextWrapping = TextWrapping.Wrap };
            var sp = new StackPanel { Spacing = 16, Margin = new Thickness(0, 16, 0, 0) };
            sp.Children.Add(new ProgressBar { IsIndeterminate = true, HorizontalAlignment = HorizontalAlignment.Stretch });
            sp.Children.Add(_statusText);

            _progressDialog = new ContentDialog
            {
                Title = "正在下载并安装预下载内容",
                Content = sp,
                XamlRoot = Content.XamlRoot
            };
            
            _ = _progressDialog.ShowAsync();

            try
            {
                string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher", "ServerCache");
                var converter = new PackageConverter(_gameDir, cacheDir, UpdateProgressText);
                
                await Task.Run(() => converter.ExecutePreDownloadAsync());

                _progressDialog.Hide();
                
                var successDialog = new ContentDialog
                {
                    Title = "完成",
                    Content = "预下载文件已下载并成功安装覆盖。", 
                    CloseButtonText = "确定",
                    XamlRoot = Content.XamlRoot
                };
                
                await successDialog.ShowAsync();
            }
            catch (Exception ex)
            {
                _progressDialog.Hide();
                var errDialog = new ContentDialog
                {
                    Title = "预下载失败",
                    Content = ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = Content.XamlRoot
                };
                await errDialog.ShowAsync();
            }
        }

        private void UpdateProgressText(string msg)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_statusText != null)
                {
                    _statusText.Text = msg; 
                }
            });
        }
    }
}