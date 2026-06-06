using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel : ObservableObject
{
    private string _iniPath;
    private string _pluginDir;
    private string _presetsDir;
    private string _dllPath;
    private IniFile _iniFile;
    private bool _isAutoUpdatePluginEnabled;
    private bool _useKeyListInput = true;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDownloadSupported))]
    private int selectedPluginIndex = 0;
    
    public bool IsDownloadSupported => SelectedPluginIndex == 0;
    
    [ObservableProperty]
    private string pluginName;

    [ObservableProperty]
    private string pluginDescription;

    [ObservableProperty]
    private string pluginDeveloper;

    [ObservableProperty]
    private string lastModifiedDate;

    [ObservableProperty]
    private ObservableCollection<PresetModel> availablePresets = new();

    [ObservableProperty]
    private PresetModel currentPreset;

    [ObservableProperty]
    private Microsoft.UI.Xaml.Media.ImageSource currentAvatarSource;

    [ObservableProperty]
    private bool hasAvatar;
    
    
    [ObservableProperty]
    private Microsoft.UI.Xaml.Media.ImageSource avatar512Source;

    [ObservableProperty]
    private Microsoft.UI.Xaml.Media.ImageSource avatar256Source;

    [ObservableProperty]
    private Microsoft.UI.Xaml.Media.ImageSource avatar128Source;

    [ObservableProperty]
    private bool hasAvatar512;

    [ObservableProperty]
    private bool hasAvatar256;

    [ObservableProperty]
    private bool hasAvatar128;

    public string GetAvatarPath(int size) => Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar", $"avatar{size}.png");
    public string GetAvatarOriginalPath(int size) => Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar", $"avatar{size}_original.png");
    
    
    private bool _isAutoCreatePresetEnabled = true;

    public bool IsAutoCreatePresetEnabled
    {
        get => _isAutoCreatePresetEnabled;
        set
        {
            if (SetProperty(ref _isAutoCreatePresetEnabled, value))
            {
                var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
                if (localSettings != null)
                {
                    _ = localSettings.SaveSettingAsync("IsAutoCreatePresetEnabled", value);
                }
            }
        }
    }
    
    private bool _isMainPluginEnabled;
    public bool IsMainPluginEnabled
    {
        get => _isMainPluginEnabled;
        set
        {
            if (_isMainPluginEnabled != value)
            {
                ChangeMainPluginState(value);
            }
        }
    }

    public ObservableCollection<PluginSettingItem> Settings { get; } = new();

    public Microsoft.UI.Xaml.Visibility AvatarSettingsVisibility => 
        SelectedPluginIndex == 2 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility MainSettingsVisibility => 
        SelectedPluginIndex != 2 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public string AvatarPath => Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar", "avatar.png");
    public string AvatarOriginalPath => Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar", "avatar_original.png");

    partial void OnSelectedPluginIndexChanged(int value)
    {
        CheckPluginStates();
        UpdatePaths();
        LoadConfiguration();
        UpdateAvatarPreview();
        RefreshUIState();
    }
    
    private bool _isFpsPluginEnabled;
    public bool IsFpsPluginEnabled
    {
        get => _isFpsPluginEnabled;
        set
        {
            if (_isFpsPluginEnabled != value)
            {
                ChangeFpsPluginState(value);
            }
        }
    }

    private bool _isAvatarPluginEnabled;
    public bool IsAvatarPluginEnabled
    {
        get => _isAvatarPluginEnabled;
        set
        {
            if (_isAvatarPluginEnabled != value)
            {
                ChangeAvatarPluginState(value);
            }
        }
    }

    private static bool _isHwidAuthorized = false;
    private static bool _hasCheckedHwid = false;

    private async Task<bool> CheckHwidAuthorizationAsync()
    {
        if (_hasCheckedHwid && _isHwidAuthorized) return true;

        string hwid = await Task.Run(() => SystemEnvironmentHelper.GetHwid());

        if (string.IsNullOrEmpty(hwid) || hwid == "Unknown") return false;

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var content = new StringContent(
                JsonSerializer.Serialize(new { hwid = hwid }),
                System.Text.Encoding.UTF8,
                "application/json"
            );

            var response = await client.PostAsync("https://dev.s1ky3.xyz/api/verify-hwid", content);
            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                var result = JsonDocument.Parse(responseString).RootElement;
                if (result.TryGetProperty("authorized", out var authElement) && authElement.GetBoolean())
                {
                    _isHwidAuthorized = true;
                }
            }
        }
        catch
        {
        }

        _hasCheckedHwid = true;
        System.Diagnostics.Debug.WriteLine($"[HWID_DEBUG] 授权结果: {_isHwidAuthorized}");
        return _isHwidAuthorized;
    }

    public Microsoft.UI.Xaml.Visibility SettingsOverlayVisibility => 
        (SelectedPluginIndex == 0 && !_isMainPluginEnabled) || (SelectedPluginIndex == 1 && !_isFpsPluginEnabled) || (SelectedPluginIndex == 2 && !_isAvatarPluginEnabled) 
            ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public bool IsSettingsInteractable => (SelectedPluginIndex == 0 && _isMainPluginEnabled) || (SelectedPluginIndex == 1 && _isFpsPluginEnabled) || (SelectedPluginIndex == 2 && _isAvatarPluginEnabled);

    public string OverlayWarningText
    {
        get
        {
            if (SelectedPluginIndex == 0) return "已被禁用，请启用主插件才能调试配置";
            if (SelectedPluginIndex == 1) return "已被禁用，请启用FPS插件才能调试插件配置";
            if (SelectedPluginIndex == 2) return "已被禁用，该插件存在安全风险，无法启用";
            return string.Empty;
        }
    }

    private void CheckPluginStates()
    {
        string fpsDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS");
        string fpsEnabledPath = Path.Combine(fpsDir, "FPS.dll");
        string fpsDisabledPath = Path.Combine(fpsDir, "FPS.disabled");
        
        string avatarDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar");
        string avatarEnabledPath = Path.Combine(avatarDir, "Avatar.dll");
        string avatarDisabledPath = Path.Combine(avatarDir, "Avatar.disabled");
        
        string mainDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "FuFuPlugin");
        string mainEnabledPath = Path.Combine(mainDir, "FufuLauncher.UnlockerIsland.dll");
        string mainDisabledPath = Path.Combine(mainDir, "FufuLauncher.UnlockerIsland.disabled");

        if (File.Exists(mainEnabledPath) && File.Exists(mainDisabledPath))
        {
            try { File.Delete(mainDisabledPath); } catch { }
        }
    
        _isMainPluginEnabled = !File.Exists(mainDisabledPath);
        OnPropertyChanged(nameof(IsMainPluginEnabled));

        if (File.Exists(fpsEnabledPath) && File.Exists(fpsDisabledPath))
        {
            try { File.Delete(fpsDisabledPath); } catch { }
        }
        
        if (File.Exists(avatarEnabledPath) && File.Exists(avatarDisabledPath))
        {
            try { File.Delete(avatarDisabledPath); } catch { }
        }

        bool fpsEnabled = File.Exists(fpsEnabledPath);
        bool avatarEnabled = File.Exists(avatarEnabledPath);

        if (fpsEnabled && avatarEnabled)
        {
            try
            {
                File.Move(fpsEnabledPath, fpsDisabledPath);
                File.Move(avatarEnabledPath, avatarDisabledPath);
            }
            catch { }
            
            _isFpsPluginEnabled = false;
            _isAvatarPluginEnabled = false;
        }
        else
        {
            _isFpsPluginEnabled = fpsEnabled;
            _isAvatarPluginEnabled = avatarEnabled;
        }

        OnPropertyChanged(nameof(IsFpsPluginEnabled));
        OnPropertyChanged(nameof(IsAvatarPluginEnabled));
        RefreshUIState();
    }

public async Task TriggerBackgroundAuthCheckAsync()
{
    if (_hasCheckedHwid && _isHwidAuthorized) return;

    bool isAuthorized = await CheckHwidAuthorizationAsync();
    var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

    if (!isAuthorized)
    {
        string avatarDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar");
        string avatarEnabledPath = Path.Combine(avatarDir, "Avatar.dll");
        string avatarDisabledPath = Path.Combine(avatarDir, "Avatar.disabled");
        
        if (File.Exists(avatarEnabledPath))
        {
            try { File.Move(avatarEnabledPath, avatarDisabledPath); } catch {}
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => 
                {
                    _isAvatarPluginEnabled = false;
                    OnPropertyChanged(nameof(IsAvatarPluginEnabled));
                    RefreshUIState();
                });
            }
        }
    }
    else
    {
        if (dispatcher != null)
        {
            dispatcher.TryEnqueue(() => LoadConfiguration());
        }
        else
        {
            LoadConfiguration();
        }
    }
}
    
    private void ChangeMainPluginState(bool enable)
    {
        string mainDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "FuFuPlugin");
        string enabledPath = Path.Combine(mainDir, "FufuLauncher.UnlockerIsland.dll");
        string disabledPath = Path.Combine(mainDir, "FufuLauncher.UnlockerIsland.disabled");

        if (!Directory.Exists(mainDir)) Directory.CreateDirectory(mainDir);

        try
        {
            if (enable && File.Exists(disabledPath))
            {
                File.Move(disabledPath, enabledPath);
            }
            else if (!enable && File.Exists(enabledPath))
            {
                File.Move(enabledPath, disabledPath);
            }
        
            SetProperty(ref _isMainPluginEnabled, enable, nameof(IsMainPluginEnabled));
            RefreshUIState();
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "状态切换失败",
                $"无法修改文件后缀名。\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    private void ChangeFpsPluginState(bool enable)
    {
        if (enable && IsAvatarPluginEnabled)
        {
            IsAvatarPluginEnabled = false;
        }

        string fpsDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS");
        string enabledPath = Path.Combine(fpsDir, "FPS.dll");
        string disabledPath = Path.Combine(fpsDir, "FPS.disabled");

        try
        {
            if (enable && File.Exists(disabledPath))
            {
                File.Move(disabledPath, enabledPath);
            }
            else if (!enable && File.Exists(enabledPath))
            {
                File.Move(enabledPath, disabledPath);
            }
            
            SetProperty(ref _isFpsPluginEnabled, enable, nameof(IsFpsPluginEnabled));
            RefreshUIState();
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "状态切换失败",
                $"无法修改插件文件后缀名。\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    private async void ChangeAvatarPluginState(bool enable)
    {
        if (enable)
        {
            bool isAuthorized = await CheckHwidAuthorizationAsync();
            if (!isAuthorized)
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "认证未通过",
                    "已被禁用，该插件存在安全风险，无法启用",
                    NotificationType.Error,
                    6000
                ));
                var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                if (dispatcher != null)
                {
                    dispatcher.TryEnqueue(() => { SetProperty(ref _isAvatarPluginEnabled, false, nameof(IsAvatarPluginEnabled)); });
                }
                else
                {
                    SetProperty(ref _isAvatarPluginEnabled, false, nameof(IsAvatarPluginEnabled));
                }
                
                string avatarDirCheck = Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar");
                string enabledPathCheck = Path.Combine(avatarDirCheck, "Avatar.dll");
                string disabledPathCheck = Path.Combine(avatarDirCheck, "Avatar.disabled");
                if (File.Exists(enabledPathCheck))
                {
                    try { File.Move(enabledPathCheck, disabledPathCheck); } catch { }
                }
                return;
            }

            if (IsFpsPluginEnabled)
            {
                IsFpsPluginEnabled = false;
            }
        }

        string avatarDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar");
        string enabledPath = Path.Combine(avatarDir, "Avatar.dll");
        string disabledPath = Path.Combine(avatarDir, "Avatar.disabled");

        if (!Directory.Exists(avatarDir)) Directory.CreateDirectory(avatarDir);

        try
        {
            if (enable && File.Exists(disabledPath))
            {
                File.Move(disabledPath, enabledPath);
            }
            else if (!enable && File.Exists(enabledPath))
            {
                File.Move(enabledPath, disabledPath);
            }
            
            SetProperty(ref _isAvatarPluginEnabled, enable, nameof(IsAvatarPluginEnabled));
            RefreshUIState();
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "状态切换失败",
                $"无法修改插件文件后缀名。\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    private void RefreshUIState()
    {
        OnPropertyChanged(nameof(SettingsOverlayVisibility));
        OnPropertyChanged(nameof(IsSettingsInteractable));
        OnPropertyChanged(nameof(OverlayWarningText));
        OnPropertyChanged(nameof(AvatarSettingsVisibility));
        OnPropertyChanged(nameof(MainSettingsVisibility));
        UpdatePaths();
    }
    
    private void UpdatePaths()
    {
        string subDir = SelectedPluginIndex == 0 ? "FuFuPlugin" : (SelectedPluginIndex == 1 ? "FPS" : "Avatar");
        _pluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins", subDir);
        
        if (SelectedPluginIndex == 2)
        {
            _iniPath = string.Empty;
            string avatarEnabledPath = Path.Combine(_pluginDir, "Avatar.dll");
            string avatarDisabledPath = Path.Combine(_pluginDir, "Avatar.disabled");
            _dllPath = File.Exists(avatarDisabledPath) ? avatarDisabledPath : avatarEnabledPath;
        }
        else
        {
            _iniPath = Path.Combine(_pluginDir, "config.ini");
            if (subDir == "FuFuPlugin")
            {
                string mainEnabledPath = Path.Combine(_pluginDir, "FufuLauncher.UnlockerIsland.dll");
                string mainDisabledPath = Path.Combine(_pluginDir, "FufuLauncher.UnlockerIsland.disabled");
                _dllPath = File.Exists(mainDisabledPath) ? mainDisabledPath : mainEnabledPath;
            }
            else
            {
                string fpsEnabledPath = Path.Combine(_pluginDir, "FPS.dll");
                string fpsDisabledPath = Path.Combine(_pluginDir, "FPS.disabled");
                _dllPath = File.Exists(fpsDisabledPath) ? fpsDisabledPath : fpsEnabledPath;
            }
        }
        
        _presetsDir = Path.Combine(AppPaths.PluginPresetsDir, subDir);
        
        if (!string.IsNullOrEmpty(_iniPath))
        {
            _iniFile = new IniFile(_iniPath);
        }
        else
        {
            _iniFile = null;
        }

        if (!Directory.Exists(_presetsDir))
        {
            Directory.CreateDirectory(_presetsDir);
        }
    }

    public PluginSettingsViewModel()
    {
        CheckPluginStates();
        UpdatePaths();
        _pluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "FuFuPlugin");
        _iniPath = Path.Combine(_pluginDir, "config.ini");
        _dllPath = Path.Combine(_pluginDir, "FufuLauncher.UnlockerIsland.dll");
        _presetsDir = AppPaths.PluginPresetsDir;
    
        _iniFile = new IniFile(_iniPath);
    
        try
        {
            if (!Directory.Exists(_presetsDir))
            {
                Directory.CreateDirectory(_presetsDir);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"目录创建失败: {ex.Message}");
        }
        
        var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
        if (localSettings != null)
        {
            var task = localSettings.ReadSettingAsync("IsAutoUpdatePluginEnabled");
            task.Wait();
            _isAutoUpdatePluginEnabled = task.Result != null && Convert.ToBoolean(task.Result);

            var keyInputTask = localSettings.ReadSettingAsync("UseKeyListInput");
            keyInputTask.Wait();
            _useKeyListInput = keyInputTask.Result == null || Convert.ToBoolean(keyInputTask.Result);
            
            var autoCreateTask = localSettings.ReadSettingAsync("IsAutoCreatePresetEnabled");
            autoCreateTask.Wait();
            _isAutoCreatePresetEnabled = autoCreateTask.Result == null || Convert.ToBoolean(autoCreateTask.Result);

            // 在下方补充以下代码：
            var devFeaturesTask = localSettings.ReadSettingAsync("IsDevFeaturesEnabled");
            devFeaturesTask.Wait();
            bool savedDevFeatures = devFeaturesTask.Result != null && Convert.ToBoolean(devFeaturesTask.Result);

            if (savedDevFeatures)
            {
                _isDevFeaturesEnabled = true;
                _ = VerifyAndApplyDevFeaturesOnStartupAsync();
            }
            else
            {
                _isDevFeaturesEnabled = false;
            }
        }

        // 纯同步本地加载，[DEV] 默认不加载
        LoadConfiguration();
        UpdateAvatarPreview();
    }
    
    private async Task InitializeAuthAndReloadAsync()
    {
        if (!_hasCheckedHwid)
        {
            await CheckHwidAuthorizationAsync();
            
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dispatcher != null)
            {
                dispatcher.TryEnqueue(() => LoadConfiguration());
            }
            else
            {
                LoadConfiguration();
            }
        }
    }
    
    public void UpdateAvatarPreview()
    {
        if (SelectedPluginIndex != 2) return;
        
        OnPropertyChanged(nameof(AvatarSettingsVisibility));
        OnPropertyChanged(nameof(MainSettingsVisibility));
        
        Avatar512Source = LoadImageSource(512, out bool has512);
        HasAvatar512 = has512;
        
        Avatar256Source = LoadImageSource(256, out bool has256);
        HasAvatar256 = has256;
        
        Avatar128Source = LoadImageSource(128, out bool has128);
        HasAvatar128 = has128;
    }
    
    private Microsoft.UI.Xaml.Media.Imaging.BitmapImage LoadImageSource(int size, out bool hasAvatar)
    {
        var path = GetAvatarPath(size);
        if (File.Exists(path))
        {
            try
            {
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                bmp.CreateOptions = Microsoft.UI.Xaml.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path);
                hasAvatar = true;
                return bmp;
            }
            catch { }
        }
        hasAvatar = false;
        return null;
    }

    public bool IsPluginCorrupted()
    {
        if (File.Exists(_dllPath))
        {
            var fileInfo = new FileInfo(_dllPath);
            return fileInfo.Length < 10 * 1024;
        }
        return false; 
    }
    
    public bool IsAutoUpdatePluginEnabled
    {
        get => _isAutoUpdatePluginEnabled;
        set
        {
            if (SetProperty(ref _isAutoUpdatePluginEnabled, value))
            {
                var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
                if (localSettings != null)
                {
                    _ = localSettings.SaveSettingAsync("IsAutoUpdatePluginEnabled", value);
                }
            }
        }
    }

    public bool UseKeyListInput
    {
        get => _useKeyListInput;
        set
        {
            if (SetProperty(ref _useKeyListInput, value))
            {
                foreach (var setting in Settings.Where(s => string.Equals(s.Type, "key", StringComparison.OrdinalIgnoreCase)))
                {
                    setting.SetKeyInputMode(value);
                }

                var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
                if (localSettings != null)
                {
                    _ = localSettings.SaveSettingAsync("UseKeyListInput", value);
                }
            }
        }
    }

    private string GetTargetDllHash()
    {
        if (!File.Exists(_dllPath)) return string.Empty;
        
        try
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(_dllPath);
            var hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

public void LoadConfiguration()
    {
        Settings.Clear();

        if (SelectedPluginIndex == 2)
        {
            PluginName = "千星奇域头像替换";
            PluginDescription = "注意：开启此功能会自动禁用FPS插件，两者不可同时开启，替换头像是永久性的";
            PluginDeveloper = "不可用";
            LastModifiedDate = "不可用";
            AvailablePresets.Clear();
            CurrentPreset = null;
            return;
        }

        if (!File.Exists(_iniPath))
        {
            PluginName = SelectedPluginIndex == 0 ? "未安装 FuFuPlugin" : "未安装 FPS 插件";
            PluginDescription = "请确保Plugins目录下存在对应的文件夹及config.ini文件";
            return;
        }

        try
        {
            var fileInfo = new FileInfo(_iniPath);
            LastModifiedDate = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

            var configData = _iniFile.ReadAll();
            if (configData.TryGetValue("General", out var generalSection))
            {
                PluginName = generalSection.GetValueOrDefault("Name", "未知插件");
                PluginDescription = generalSection.GetValueOrDefault("Description", "无描述");
                PluginDeveloper = generalSection.GetValueOrDefault("Developer", "未知作者");
            }

            ManagePresets(configData);

            bool isDevZone = false;
            foreach (var section in _iniFile.ReadAll())
            {
                if (section.Key.Equals("General", StringComparison.OrdinalIgnoreCase)) continue;

                if (section.Key.Equals("DEV", StringComparison.OrdinalIgnoreCase))
                {
                    isDevZone = true;
                    continue;
                }

                if (isDevZone && !_isDevFeaturesEnabled)
                {
                    continue;
                }

                var dic = section.Value;
                var name = dic.GetValueOrDefault("Name", section.Key);
                var type = dic.GetValueOrDefault("Type", "string");
                var value = dic.GetValueOrDefault("Value", "");
                var help = dic.GetValueOrDefault("help", "");
                
                var settingItem = new PluginSettingItem(_iniFile, section.Key, name, type, value, help, OnSettingValueChanged, UseKeyListInput);
                Settings.Add(settingItem);
            }
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "配置读取失败",
                $"无法读取插件配置文件。\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    public async Task StartAsynchronousAuthAsync()
    {
        bool isAuthorized = await CheckHwidAuthorizationAsync();
        
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (dispatcher != null)
        {
            dispatcher.TryEnqueue(() => HandleAuthResult(isAuthorized));
        }
        else
        {
            HandleAuthResult(isAuthorized);
        }
    }
    
    private void HandleAuthResult(bool isAuthorized)
    {
        if (isAuthorized)
        {
            LoadConfiguration();
        }
        else
        {
            string avatarDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "Avatar");
            string avatarEnabledPath = Path.Combine(avatarDir, "Avatar.dll");
            string avatarDisabledPath = Path.Combine(avatarDir, "Avatar.disabled");

            if (File.Exists(avatarEnabledPath))
            {
                try
                {
                    File.Move(avatarEnabledPath, avatarDisabledPath);
                }
                catch { }
                
                _isAvatarPluginEnabled = false;
                OnPropertyChanged(nameof(IsAvatarPluginEnabled));
                RefreshUIState();
            }
        }
    }
    
    private bool _isDevFeaturesEnabled;
    public bool IsDevFeaturesEnabled
    {
        get => _isDevFeaturesEnabled;
        set
        {
            if (_isDevFeaturesEnabled != value)
            {
                _isDevFeaturesEnabled = value;
                OnPropertyChanged();

                if (value)
                {
                    _ = VerifyAndApplyDevFeaturesAsync();
                }
                else
                {
                    SaveDevFeaturesSetting(false);
                    LoadConfiguration();
                }
            }
        }
    }

    private async Task VerifyAndApplyDevFeaturesAsync()
    {
        bool isAuthorized = await CheckHwidAuthorizationAsync();
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        Action action = () =>
        {
            if (isAuthorized)
            {
                SaveDevFeaturesSetting(true);
                LoadConfiguration();
            }
            else
            {
                WeakReferenceMessenger.Default.Send(new NotificationMessage(
                    "权限验证失败",
                    "您当前不具备开发者功能权限，无法开启该功能",
                    NotificationType.Error,
                    4000
                ));
                
                _isDevFeaturesEnabled = false;
                OnPropertyChanged(nameof(IsDevFeaturesEnabled));
                SaveDevFeaturesSetting(false);
                LoadConfiguration();
            }
        };

        if (dispatcher != null) dispatcher.TryEnqueue(() => action());
        else action();
    }

    private async Task VerifyAndApplyDevFeaturesOnStartupAsync()
    {
        bool isAuthorized = await CheckHwidAuthorizationAsync();
        if (!isAuthorized)
        {
            var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            Action action = () =>
            {
                _isDevFeaturesEnabled = false;
                OnPropertyChanged(nameof(IsDevFeaturesEnabled));
                SaveDevFeaturesSetting(false);
                LoadConfiguration();
            };

            if (dispatcher != null) dispatcher.TryEnqueue(() => action());
            else action();
        }
    }

    private void SaveDevFeaturesSetting(bool isEnabled)
    {
        var localSettings = App.GetService<FufuLauncher.Contracts.Services.ILocalSettingsService>();
        if (localSettings != null)
        {
            _ = localSettings.SaveSettingAsync("IsDevFeaturesEnabled", isEnabled);
        }
    }

    private void ManagePresets(Dictionary<string, Dictionary<string, string>> currentIniData)
    {
        AvailablePresets.Clear();
        var currentHash = GetTargetDllHash();
        var stateFile = Path.Combine(_presetsDir, "active_state.json");
        string activePresetId = string.Empty;

        if (File.Exists(stateFile))
        {
            try
            {
                var stateContent = File.ReadAllText(stateFile);
                var stateDict = JsonSerializer.Deserialize<Dictionary<string, string>>(stateContent);
                if (stateDict != null && stateDict.TryGetValue("ActiveId", out var id))
                {
                    activePresetId = id;
                }
            }
            catch
            {
            }
        }

        try
        {
            if (Directory.Exists(_presetsDir))
            {
                var presetFiles = Directory.GetFiles(_presetsDir, "*.json").Where(f => !f.EndsWith("active_state.json"));
                PresetModel activeModel = null;

                foreach (var file in presetFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(file);
                        var preset = JsonSerializer.Deserialize<PresetModel>(content);
                        if (preset != null)
                        {
                            preset.FilePath = file;
                            
                            bool presetModified = false;
                            
                            if (preset.ConfigData.Remove("General"))
                            {
                                presetModified = true;
                            }
                            
                            foreach (var sectionKey in preset.ConfigData.Keys.ToList())
                            {
                                if (currentIniData.TryGetValue(sectionKey, out var currentSectionData))
                                {
                                    preset.ConfigData[sectionKey].TryGetValue("Name", out var presetName);
                                    currentSectionData.TryGetValue("Name", out var currentName);
                                    
                                    if (presetName != currentName)
                                    {
                                        preset.ConfigData[sectionKey] = new Dictionary<string, string>(currentSectionData, StringComparer.OrdinalIgnoreCase);
                                        presetModified = true;
                                    }
                                }
                            }
                            
                            if (preset.DllHash != currentHash)
                            {
                                if (IsAutoCreatePresetEnabled)
                                {
                                    preset.IsLocked = true;
                                }
                                else
                                {
                                    preset.DllHash = currentHash;
                                    preset.IsLocked = false;
                                    SavePresetToFile(preset);
                                }
                            }
                            else
                            {
                                preset.IsLocked = false;
                                if (presetModified)
                                {
                                    SavePresetToFile(preset);
                                }
                            }

                            AvailablePresets.Add(preset);

                            if (preset.Id == activePresetId)
                            {
                                activeModel = preset;
                            }
                        }
                    }
                    catch { }
                }

                if (activeModel != null && activeModel.IsLocked)
                {
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "插件变更",
                        "当前预设与最新插件版本不匹配，已自动生成新预设",
                        NotificationType.Warning,
                        5000
                    ));
                    activeModel = null;
                }

                if (activeModel == null)
                {
                    activeModel = CreateNewPreset("默认预设", currentIniData, currentHash);
                }

                CurrentPreset = activeModel;
                SaveActiveState();

                try
                {
                    _iniFile.UpdateMultiple(CurrentPreset.ConfigData);
                }
                catch (Exception ex)
                {
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "配置应用失败",
                        $"无法将预设写入配置文件，请检查权限\n详细信息: {ex.Message}",
                        NotificationType.Error,
                        6000
                    ));
                }
            }
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "预设目录访问失败",
                $"无法访问预设目录\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }
    
    public void ClearAllPresets()
    {
        try
        {
            if (Directory.Exists(_presetsDir))
            {
                var files = Directory.GetFiles(_presetsDir, "*.json");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
            AvailablePresets.Clear();
            CurrentPreset = null;
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "清除失败",
                $"无法清除预设文件\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    public PresetModel CreateNewPreset(string name, Dictionary<string, Dictionary<string, string>> data, string hash)
    {
        var cleanData = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in data)
        {
            if (!kvp.Key.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                cleanData[kvp.Key] = new Dictionary<string, string>(kvp.Value, StringComparer.OrdinalIgnoreCase);
            }
        }

        var preset = new PresetModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            DllHash = hash,
            ConfigData = cleanData
        };

        preset.FilePath = Path.Combine(_presetsDir, $"{preset.Id}.json");
        SavePresetToFile(preset);
        AvailablePresets.Add(preset);
        return preset;
    }

    private void SavePresetToFile(PresetModel preset)
    {
        if (string.IsNullOrEmpty(preset.FilePath)) return;
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(preset.FilePath, JsonSerializer.Serialize(preset, options));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "预设保存失败",
                $"无法保存预设文件，可能缺少写入权限\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    private void SaveActiveState()
    {
        if (CurrentPreset == null) return;
        try
        {
            var stateFile = Path.Combine(_presetsDir, "active_state.json");
            var stateDict = new Dictionary<string, string> { { "ActiveId", CurrentPreset.Id } };
            File.WriteAllText(stateFile, JsonSerializer.Serialize(stateDict));
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "状态保存失败",
                $"无法保存激活状态记录，可能缺少写入权限\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    private void OnSettingValueChanged(string section, string key, string value)
    {
        if (CurrentPreset == null) return;
        
        if (section.Equals("General", StringComparison.OrdinalIgnoreCase)) return;

        if (!CurrentPreset.ConfigData.ContainsKey(section))
        {
            CurrentPreset.ConfigData[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        CurrentPreset.ConfigData[section][key] = value;
        SavePresetToFile(CurrentPreset);
    }

    public void SwitchPreset(PresetModel targetPreset)
    {
        if (targetPreset == null || targetPreset.IsLocked) return;

        CurrentPreset = targetPreset;
        SaveActiveState();
        
        try
        {
            _iniFile.UpdateMultiple(CurrentPreset.ConfigData);
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "配置更新失败",
                $"切换预设时无法写入配置文件\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
        
        LoadConfiguration();
        
        WeakReferenceMessenger.Default.Send(new NotificationMessage(
            "预设已切换",
            $"当前预设: {targetPreset.Name}",
            NotificationType.Success,
            3000
        ));
    }
    
    public void DeletePreset(PresetModel targetPreset)
    {
        if (targetPreset == null || string.IsNullOrEmpty(targetPreset.FilePath)) return;
        
        try
        {
            if (File.Exists(targetPreset.FilePath))
            {
                File.Delete(targetPreset.FilePath);
            }
            
            AvailablePresets.Remove(targetPreset);
            
            if (CurrentPreset?.Id == targetPreset.Id)
            {
                LoadConfiguration();
            }
        }
        catch (Exception ex)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "预设删除失败",
                $"无法删除指定的预设文件，文件可能被占用或权限不足\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }
}

public class PresetModel : ObservableObject
{
    public string Id { get; set; }
    public string Name { get; set; }
    public string DllHash { get; set; }
    public Dictionary<string, Dictionary<string, string>> ConfigData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    
    [System.Text.Json.Serialization.JsonIgnore]
    public string FilePath { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsLocked { get; set; }

    private bool _isActive;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(IsNotActive));
            }
        }
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsNotActive => !IsActive;
}

public class VirtualKeyOption
{
    public int KeyCode { get; set; }
    public string KeyName { get; set; }
}

public class PluginSettingItem : ObservableObject
{
    private readonly IniFile _iniFile;
    private readonly Action<string, string, string> _onValueChanged;
    public string SectionKey { get; }
    public string DisplayName { get; }
    public string Type { get; }
    public string HelpUrl { get; }

    public Microsoft.UI.Xaml.Visibility HelpVisibility => !string.IsNullOrEmpty(HelpUrl) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility GifImageVisibility => !string.IsNullOrEmpty(HelpUrl) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public Microsoft.UI.Xaml.Visibility GifErrorVisibility => Microsoft.UI.Xaml.Visibility.Collapsed;
    private string _rawValue;
    private static readonly ObservableCollection<VirtualKeyOption> _availableKeys = new ObservableCollection<VirtualKeyOption>(GetAvailableKeys());
    private bool _useKeyListInput;

    public ObservableCollection<VirtualKeyOption> AvailableKeys => _availableKeys;
    
    public Microsoft.UI.Xaml.Media.ImageSource HelpImageSource
    {
        get
        {
            if (string.IsNullOrEmpty(HelpUrl)) return null;
            try
            {
                var resolvedPath = HelpUrl;
                if (!Uri.IsWellFormedUriString(HelpUrl, UriKind.Absolute) && !Path.IsPathRooted(HelpUrl))
                {
                    resolvedPath = Path.Combine(AppContext.BaseDirectory, HelpUrl);
                }
                return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(resolvedPath));
            }
            catch
            {
                return null;
            }
        }
    }
    
    public PluginSettingItem(IniFile iniFile, string sectionKey, string displayName, string type, string value, string helpUrl, Action<string, string, string> onValueChanged, bool useKeyListInput)
    {
        _iniFile = iniFile;
        SectionKey = sectionKey;
        DisplayName = displayName;
        Type = type;
        _rawValue = value;
        HelpUrl = helpUrl;
        _onValueChanged = onValueChanged;
        _useKeyListInput = useKeyListInput;
        if (string.Equals(Type, "key", StringComparison.OrdinalIgnoreCase) && int.TryParse(_rawValue, out var currentKey))
        {
            EnsureKeyOption(currentKey);
        }
    }
    
    private static List<VirtualKeyOption> GetAvailableKeys()
    {
        var list = new List<VirtualKeyOption>();
        foreach (Windows.System.VirtualKey key in Enum.GetValues(typeof(Windows.System.VirtualKey)))
        {
            if (key == Windows.System.VirtualKey.None) continue;
            list.Add(new VirtualKeyOption { KeyCode = (int)key, KeyName = key.ToString() });
        }
        return list.GroupBy(k => k.KeyCode).Select(g => g.First()).OrderBy(k => k.KeyCode).ToList();
    }
    
    public int? KeyValue
    {
        get => int.TryParse(_rawValue, out var result) ? result : 0;
        set
        {
            if (value == null) return;
            
            var targetValue = value.Value.ToString();
            if (_rawValue != targetValue)
            {
                var previousValue = _rawValue;
                _rawValue = targetValue;
                
                bool isNew = EnsureKeyOption(value.Value);

                if (isNew)
                {
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "内部视图已刷新",
                        $"新增未知键值 {value}",
                        NotificationType.Success,
                        3000
                    ));
                    
                    var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    if (dispatcher != null)
                    {
                        dispatcher.TryEnqueue(() =>
                        {
                            OnPropertyChanged();
                            OnPropertyChanged(nameof(KeyNumberValue));
                        });
                    }
                    else
                    {
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(KeyNumberValue));
                    }
                }
                else
                {
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(KeyNumberValue));
                }

                UpdatePhysicalConfig(targetValue, previousValue, nameof(KeyValue));
            }
        }
    }

    public PluginSettingItem(IniFile iniFile, string sectionKey, string displayName, string type, string value, Action<string, string, string> onValueChanged, bool useKeyListInput)
    {
        _iniFile = iniFile;
        SectionKey = sectionKey;
        DisplayName = displayName;
        Type = type;
        _rawValue = value;
        _onValueChanged = onValueChanged;
        _useKeyListInput = useKeyListInput;
        if (string.Equals(Type, "key", StringComparison.OrdinalIgnoreCase) && int.TryParse(_rawValue, out var currentKey))
        {
            EnsureKeyOption(currentKey);
        }
    }

    public bool UseKeyListInput
    {
        get => _useKeyListInput;
        set
        {
            if (SetProperty(ref _useKeyListInput, value))
            {
                OnPropertyChanged(nameof(KeyListVisibility));
                OnPropertyChanged(nameof(KeyNumberVisibility));
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility KeyListVisibility =>
        UseKeyListInput ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility KeyNumberVisibility =>
        UseKeyListInput ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public double KeyNumberValue
    {
        get => KeyValue ?? 0;
        set
        {
            if (double.IsNaN(value)) return;
            KeyValue = (int)Math.Round(value);
        }
    }

    public void SetKeyInputMode(bool useKeyListInput)
    {
        UseKeyListInput = useKeyListInput;
    }

    public bool BoolValue
    {
        get => _rawValue == "1" || _rawValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        set
        {
            var targetValue = value ? "1" : "0";
            if (_rawValue != targetValue)
            {
                var previousValue = _rawValue;
                _rawValue = targetValue;
                OnPropertyChanged();
                UpdatePhysicalConfig(targetValue, previousValue, nameof(BoolValue));
            }
        }
    }

    public double FloatValue
    {
        get => double.TryParse(_rawValue, out var result) ? result : 0;
        set
        {
            var targetValue = value.ToString("G");
            if (_rawValue != targetValue)
            {
                var previousValue = _rawValue;
                _rawValue = targetValue;
                OnPropertyChanged();
                UpdatePhysicalConfig(targetValue, previousValue, nameof(FloatValue));
            }
        }
    }

    public string StringValue
    {
        get => _rawValue;
        set
        {
            if (_rawValue != value)
            {
                var previousValue = _rawValue;
                _rawValue = value;
                OnPropertyChanged();
                UpdatePhysicalConfig(value, previousValue, nameof(StringValue));
            }
        }
    }

    private void UpdatePhysicalConfig(string newValue, string previousValue, string propertyName)
    {
        try
        {
            _iniFile.WriteValue(SectionKey, "Value", newValue);
            _onValueChanged?.Invoke(SectionKey, "Value", newValue);
        }
        catch (Exception ex)
        {
            _rawValue = previousValue;
            OnPropertyChanged(propertyName);
            
            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "配置保存失败",
                $"无法应用当前设置修改\n详细信息: {ex.Message}",
                NotificationType.Error,
                6000
            ));
        }
    }

    private static bool EnsureKeyOption(int keyCode)
    {
        if (keyCode <= 0) return false;
        if (_availableKeys.Any(k => k.KeyCode == keyCode)) return false;

        _availableKeys.Add(new VirtualKeyOption
        {
            KeyCode = keyCode,
            KeyName = $"Custom({keyCode})"
        });
        
        return true;
    }
}