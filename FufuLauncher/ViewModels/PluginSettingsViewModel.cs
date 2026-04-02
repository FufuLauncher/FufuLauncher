using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.ViewModels;

public partial class PluginSettingsViewModel : ObservableObject
{
    private readonly string _iniPath;
    private readonly IniFile _iniFile;

    [ObservableProperty]
    private string pluginName;

    [ObservableProperty]
    private string pluginDescription;

    [ObservableProperty]
    private string pluginDeveloper;

    [ObservableProperty]
    private string lastModifiedDate;

    public ObservableCollection<PluginSettingItem> Settings { get; } = new();

    public PluginSettingsViewModel()
    {
        _iniPath = Path.Combine(AppContext.BaseDirectory, "Plugins", "FuFuPlugin", "config.ini");
        _iniFile = new IniFile(_iniPath);
        LoadConfiguration();
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

        foreach (var section in configData)
        {
            if (section.Key.Equals("General", StringComparison.OrdinalIgnoreCase)) continue;

            var dic = section.Value;
            var name = dic.GetValueOrDefault("Name", section.Key);
            var type = dic.GetValueOrDefault("Type", "string");
            var value = dic.GetValueOrDefault("Value", "");

            var settingItem = new PluginSettingItem(_iniFile, section.Key, name, type, value);
            Settings.Add(settingItem);
        }
    }
}

public class PluginSettingItem : ObservableObject
{
    private readonly IniFile _iniFile;
    public string SectionKey { get; }
    public string DisplayName { get; }
    public string Type { get; }

    private string _rawValue;

    public PluginSettingItem(IniFile iniFile, string sectionKey, string displayName, string type, string value)
    {
        _iniFile = iniFile;
        SectionKey = sectionKey;
        DisplayName = displayName;
        Type = type;
        _rawValue = value;
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