using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.Json;
using FufuLauncher.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using FufuLauncher.Constants;

namespace FufuLauncher.Views
{
    public class StringFormatConverter : Microsoft.UI.Xaml.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            return string.Format((string)parameter, value);
        }
        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public sealed partial class PlayerInfoWindow : Window, System.ComponentModel.INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient = new();
        
        private readonly string _cacheFilePath;
        private readonly string _userConfigPath;
        private string _myUid;

        private RoleData _currentRole;
        public RoleData CurrentRole
        {
            get => _currentRole;
            set
            {
                if (_currentRole != value)
                {
                    _currentRole = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentRole)));
                    
                    if (_currentRole != null)
                    {
                        CurrentRoleGradient = new SolidColorBrush(Colors.Transparent);
                        _ = UpdateRoleGradientAsync(_currentRole.PortraitUrl);
                    }
                }
            }
        }
        
        private Brush _currentRoleGradient;
        public Brush CurrentRoleGradient
        {
            get => _currentRoleGradient;
            set
            {
                _currentRoleGradient = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CurrentRoleGradient)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;

        public PlayerInfoWindow()
        {
            InitializeComponent();
            
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            
            _userConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user.config.json");
            
            string baseDocsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string folder = Path.Combine(baseDocsFolder, "fufu");

            try
            {
                if (File.Exists(folder))
                {
                    System.Diagnostics.Debug.WriteLine($"[异常标记] 存在与目标文件夹同名的文件: {folder}，将切换到备用路径");
                    folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher");
                }
                
                if (!Directory.Exists(folder)) 
                {
                    Directory.CreateDirectory(folder);
                }
            }
            catch (FileNotFoundException fnfe)
            {
                System.Diagnostics.Debug.WriteLine($"[异常标记] 找不到路径异常(玩家信息窗口): {fnfe.Message}");
                folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fufu_data");
            }
            catch (UnauthorizedAccessException uae)
            {
                System.Diagnostics.Debug.WriteLine($"[异常标记] 权限不足(玩家信息窗口): {uae.Message}");
                folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[异常标记] 创建 fufu 文件夹发生未知错误(玩家信息窗口): {ex.Message}");
                folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fufu_data");
            }
            
            try 
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[严重异常] 备用文件夹创建依然失败(玩家信息窗口): {ex.Message}");
                folder = AppDomain.CurrentDomain.BaseDirectory;
            }
            
            _cacheFilePath = Path.Combine(folder, "player_roles.json");
            
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await LoadUserConfigAsync();
            
            if (!string.IsNullOrEmpty(_myUid))
            {
                UidTextBox.Text = _myUid;
                await LoadCachedDataAsync();
            }
        }

        private async Task LoadUserConfigAsync()
        {
            try
            {
                if (File.Exists(_userConfigPath))
                {
                    string json = await File.ReadAllTextAsync(_userConfigPath);
                    var config = JsonSerializer.Deserialize<UserConfig>(json);
                    if (config != null && !string.IsNullOrEmpty(config.GameUid))
                    {
                        _myUid = config.GameUid;
                    }
                }
            }
            catch
            {
                // ignored
            }
        }

        private async Task LoadCachedDataAsync()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    string json = await File.ReadAllTextAsync(_cacheFilePath);
                    var roles = JsonSerializer.Deserialize<List<RoleData>>(json);
                    if (roles != null && roles.Count > 0)
                    {
                        DisplayData(roles);
                    }
                }
                else if (!string.IsNullOrEmpty(_myUid))
                {
                    await FetchDataAsync(_myUid, true);
                }
            }
            catch
            {
                // ignored
            }
        }

        private async Task FetchDataAsync(string uid, bool isSave)
        {
            if (string.IsNullOrEmpty(uid)) return;

            LoadingRing.IsActive = true;
            RoleListView.IsEnabled = false;

            try
            {
                string url = string.Format(ApiEndpoints.LelaerPlayerRecordApiUrl, uid); 
                string json = await _httpClient.GetStringAsync(url);

                var response = JsonSerializer.Deserialize<PlayerRecordResponse>(json);

                if (response != null && response.Result != null && response.Result.RoleData != null)
                {
                    var roles = response.Result.RoleData;
                    DisplayData(roles);
                    
                    if (isSave)
                    {
                        await SaveToCacheAsync(roles);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fetch failed: {ex.Message}");
            }
            finally
            {
                LoadingRing.IsActive = false;
                RoleListView.IsEnabled = true;
            }
        }

        private void DisplayData(List<RoleData> roles)
        {
            RoleListView.ItemsSource = roles;
            if (roles.Count > 0)
            {
                RoleListView.SelectedIndex = 0;
            }
        }

        private async Task SaveToCacheAsync(List<RoleData> roles)
        {
            try
            {
                var dir = Path.GetDirectoryName(_cacheFilePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(roles);
                await File.WriteAllTextAsync(_cacheFilePath, json);
            }
            catch
            {
                // ignored
            }
        }
        

        private async void OnQueryClick(object sender, RoutedEventArgs e)
        {
            var inputUid = UidTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(inputUid))
            {
                await FetchDataAsync(inputUid, false);
            }
        }

        private async void OnRefreshMyDataClick(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(_myUid))
            {
                UidTextBox.Text = _myUid;
                await FetchDataAsync(_myUid, true);
            }
        }

        private void OnRoleSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RoleListView.SelectedItem is RoleData role)
            {
                CurrentRole = role;
                EmptyTipText.Visibility = Visibility.Collapsed;
                DetailContainer.Visibility = Visibility.Visible;
            }
        }

        public Visibility IsNotZero(string val)
        {
            if (string.IsNullOrEmpty(val) || val == "0%" || val == "0")
            {
                return Visibility.Collapsed;
            }
            return Visibility.Visible;
        }
        private async void OnTeamWikiClick(object sender, RoutedEventArgs e)
        {
            try
            {
                string url = ApiEndpoints.TeamWikiUrl; 
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Launch URL failed: {ex.Message}");
            }
        }
        
        private async void OnArtifactSearchClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ArtifactDetail artifact)
            {
                if (!string.IsNullOrEmpty(artifact.Name))
                {
                    string url = $"{ApiEndpoints.MiyousheSearchUrl}{Uri.EscapeDataString(artifact.Name)}"; 
                    await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
                }
            }
        }
        
        private async Task UpdateRoleGradientAsync(string imageUrl)
        {
            if (string.IsNullOrEmpty(imageUrl))
            {
                return;
            }

            try
            {
                byte[] imageBytes = await _httpClient.GetByteArrayAsync(imageUrl);
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(imageBytes.AsBuffer());
                stream.Seek(0);
                
                var decoder = await BitmapDecoder.CreateAsync(stream);

                var transform = new BitmapTransform { ScaledWidth = 50, ScaledHeight = 50, InterpolationMode = BitmapInterpolationMode.NearestNeighbor };
                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Premultiplied,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage
                );

                byte[] pixels = pixelData.DetachPixelData();
                
                var colorCounts = new Dictionary<uint, int>();

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte r = pixels[i];
                    byte g = pixels[i + 1];
                    byte b = pixels[i + 2];
                    byte a = pixels[i + 3];
                    
                    if (a < 100) continue;
                    
                    int brightness = r + g + b;
                    if (brightness < 50 || brightness > 700) continue;
                    
                    uint quantizedColor = (uint)((r / 32) << 16 | (g / 32) << 8 | (b / 32));

                    if (colorCounts.ContainsKey(quantizedColor))
                        colorCounts[quantizedColor]++;
                    else
                        colorCounts[quantizedColor] = 1;
                }
                
                var topColors = colorCounts.OrderByDescending(x => x.Value).Take(3).Select(x => x.Key).ToList();
                
                while (topColors.Count < 3)
                {
                    if (topColors.Count > 0) topColors.Add(topColors[0]);
                    else topColors.Add(0x00808080);
                }
                
                Color GetColorFromUint(uint c)
                {
                    byte r = (byte)((c >> 16) & 0xFF);
                    byte g = (byte)((c >> 8) & 0xFF);
                    byte b = (byte)(c & 0xFF);
                    
                    return Color.FromArgb(120, (byte)(r * 32), (byte)(g * 32), (byte)(b * 32));
                }

                var color1 = GetColorFromUint(topColors[0]);
                var color2 = GetColorFromUint(topColors[1]);
                var color3 = GetColorFromUint(topColors[2]);
                
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1)
                };
                gradient.GradientStops.Add(new GradientStop { Color = color1, Offset = 0.0 });
                gradient.GradientStops.Add(new GradientStop { Color = color2, Offset = 0.5 });
                gradient.GradientStops.Add(new GradientStop { Color = color3, Offset = 1.0 });
                
                CurrentRoleGradient = gradient;
            }
            catch
            {
                CurrentRoleGradient = new SolidColorBrush(Color.FromArgb(50, 128, 128, 128));
            }
        }
    }
}