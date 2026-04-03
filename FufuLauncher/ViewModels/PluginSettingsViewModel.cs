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
    private readonly string _iniPath;
    private readonly string _pluginDir;
    private readonly string _presetsDir;
    private readonly string _dllPath;
    private readonly IniFile _iniFile;

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

    public ObservableCollection<PluginSettingItem> Settings { get; } = new();

    public PluginSettingsViewModel()
    {
        _pluginDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "FuFuPlugin");
        _iniPath = Path.Combine(_pluginDir, "config.ini");
        _dllPath = Path.Combine(_pluginDir, "FufuLauncher.UnlockerIsland.dll");
        _presetsDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "Presets");
        
        _iniFile = new IniFile(_iniPath);
        
        if (!Directory.Exists(_presetsDir))
        {
            Directory.CreateDirectory(_presetsDir);
        }

        LoadConfiguration();
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

        if (!File.Exists(_iniPath))
        {
            PluginName = "未安装";
            return;
        }

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

        foreach (var section in _iniFile.ReadAll())
        {
            if (section.Key.Equals("General", StringComparison.OrdinalIgnoreCase)) continue;

            var dic = section.Value;
            var name = dic.GetValueOrDefault("Name", section.Key);
            var type = dic.GetValueOrDefault("Type", "string");
            var value = dic.GetValueOrDefault("Value", "");

            var settingItem = new PluginSettingItem(_iniFile, section.Key, name, type, value, OnSettingValueChanged);
            Settings.Add(settingItem);
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
            catch { }
        }

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
                    preset.IsLocked = preset.DllHash != currentHash;
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
                "检测到插件变更",
                "当前预设与最新插件版本不匹配，已自动生成新预设",
                NotificationType.Warning,
                5000
            ));
            activeModel = null;
        }

        if (activeModel == null)
        {
            activeModel = CreateNewPreset($"默认预设_{DateTime.Now:yyyyMMdd_HHmmss}", currentIniData, currentHash);
        }

        CurrentPreset = activeModel;
        SaveActiveState();

        _iniFile.UpdateMultiple(CurrentPreset.ConfigData);
    }

    public PresetModel CreateNewPreset(string name, Dictionary<string, Dictionary<string, string>> data, string hash)
    {
        var preset = new PresetModel
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            DllHash = hash,
            ConfigData = data
        };

        preset.FilePath = Path.Combine(_presetsDir, $"{preset.Id}.json");
        SavePresetToFile(preset);
        AvailablePresets.Add(preset);
        return preset;
    }

    private void SavePresetToFile(PresetModel preset)
    {
        if (string.IsNullOrEmpty(preset.FilePath)) return;
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(preset.FilePath, JsonSerializer.Serialize(preset, options));
    }

    private void SaveActiveState()
    {
        if (CurrentPreset == null) return;
        var stateFile = Path.Combine(_presetsDir, "active_state.json");
        var stateDict = new Dictionary<string, string> { { "ActiveId", CurrentPreset.Id } };
        File.WriteAllText(stateFile, JsonSerializer.Serialize(stateDict));
    }

    private void OnSettingValueChanged(string section, string key, string value)
    {
        if (CurrentPreset == null) return;

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
        _iniFile.UpdateMultiple(CurrentPreset.ConfigData);
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
}

public class PluginSettingItem : ObservableObject
{
    private readonly IniFile _iniFile;
    private readonly Action<string, string, string> _onValueChanged;
    public string SectionKey { get; }
    public string DisplayName { get; }
    public string Type { get; }

    private string _rawValue;

    public PluginSettingItem(IniFile iniFile, string sectionKey, string displayName, string type, string value, Action<string, string, string> onValueChanged)
    {
        _iniFile = iniFile;
        SectionKey = sectionKey;
        DisplayName = displayName;
        Type = type;
        _rawValue = value;
        _onValueChanged = onValueChanged;
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
                ex.Message,
                NotificationType.Error,
                6000
            ));
        }
    }
}