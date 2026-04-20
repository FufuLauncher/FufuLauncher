using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FufuLauncher.Views
{
    public sealed partial class VerifyGamePage : Page
    {
        private string _gameDir = string.Empty;
        private Window _parentWindow;
        private ContentDialog _progressDialog;
        private TextBlock _statusText;

        public VerifyGamePage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is BlankPage.SwitchPageParams param)
            {
                _gameDir = param.GameDir;
                _parentWindow = param.ParentWindow;
            }
        }

        private async void StartVerifyBtn_Click(object sender, RoutedEventArgs e)
        {
            StartVerifyBtn.IsEnabled = false;
            _statusText = new TextBlock { Text = "准备中...", TextWrapping = TextWrapping.Wrap };
            var sp = new StackPanel { Spacing = 16, Margin = new Thickness(0, 16, 0, 0) };
            sp.Children.Add(new ProgressBar { IsIndeterminate = true, HorizontalAlignment = HorizontalAlignment.Stretch });
            sp.Children.Add(_statusText);

            _progressDialog = new ContentDialog
            {
                Title = "正在校验游戏",
                Content = sp,
                XamlRoot = XamlRoot
            };
            
            _ = _progressDialog.ShowAsync();

            try
            {
                string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher", "VerifyCache");
                var converter = new PackageConverter(_gameDir, cacheDir, UpdateProgressText);
                
                await Task.Run(() => converter.RunVerificationAsync());

                _progressDialog.Hide();
                
                var successDialog = new ContentDialog
                {
                    Title = "完成",
                    Content = "游戏文件完整性校验并修复完成",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                
                await successDialog.ShowAsync();
                
                _parentWindow?.Close();
            }
            catch (Exception ex)
            {
                _progressDialog.Hide();
                StartVerifyBtn.IsEnabled = true;
                var errDialog = new ContentDialog
                {
                    Title = "校验失败",
                    Content = ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
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