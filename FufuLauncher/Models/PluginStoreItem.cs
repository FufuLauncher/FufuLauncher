/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public class PluginStoreItem : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _name = string.Empty;
    private string _developer = string.Empty;
    private string _description = string.Empty;
    private string _longDescription = string.Empty;
    private string _version = string.Empty;
    private string _iconUrl = string.Empty;
    private List<string> _screenshots = new();
    private string _category = string.Empty;
    private List<string> _tags = new();
    private long _downloads;
    private long _sizeBytes;
    private string _minAppVersion = string.Empty;
    private DateTime _updatedAt;
    private string _luaInstallUrl = string.Empty;
    private string _luaUninstallUrl = string.Empty;
    private string _downloadUrl = string.Empty;
    private string _fileHash = string.Empty;
    private string _luaHash = string.Empty;
    private StorePluginState _state = StorePluginState.Available;
    private int _installProgress;
    private string _installStatusText = string.Empty;
    private bool _isInstallInProgress;

    [JsonPropertyName("id")]
    public string Id
    {
        get => _id;
        set { _id = value; OnPropertyChanged(); }
    }

    [JsonPropertyName("name")]
    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(); }
    }

    [JsonPropertyName("developer")]
    public string Developer
    {
        get => _developer;
        set
        {
            _developer = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDeveloper));
        }
    }

    [JsonPropertyName("description")]
    public string Description
    {
        get => _description;
        set { _description = value; OnPropertyChanged(); }
    }

    [JsonPropertyName("long_description")]
    public string LongDescription
    {
        get => _longDescription;
        set { _longDescription = value; OnPropertyChanged(); }
    }

    [JsonPropertyName("version")]
    public string Version
    {
        get => _version;
        set { _version = value; OnPropertyChanged(); OnPropertyChanged(nameof(VersionDisplay)); }
    }

    [JsonPropertyName("icon_url")]
    public string IconUrl
    {
        get => _iconUrl;
        set { _iconUrl = value; OnPropertyChanged(); }
    }

    [JsonPropertyName("screenshots")]
    public List<string> Screenshots
    {
        get => _screenshots;
        set { _screenshots = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasScreenshots)); }
    }

    [JsonPropertyName("category")]
    public string Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(); OnPropertyChanged(nameof(CategoryDisplay)); }
    }

    private static readonly Dictionary<string, string> CategoryResourceKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["utility"] = "PluginStoreCategoryUtility",
        ["gameplay"] = "PluginStoreCategoryGameplay",
        ["visuals"] = "PluginStoreCategoryVisuals",
    };

    [JsonIgnore]
    public string CategoryDisplay =>
        string.IsNullOrEmpty(Category) ? "" :
        CategoryResourceKeys.TryGetValue(Category, out var key) ? key.GetLocalized() : Category;

    [JsonPropertyName("tags")]
    public List<string> Tags
    {
        get => _tags;
        set { _tags = value; OnPropertyChanged(); OnPropertyChanged(nameof(TagsDisplay)); }
    }

    [JsonPropertyName("downloads")]
    public long Downloads
    {
        get => _downloads;
        set { _downloads = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownloadsDisplay)); }
    }

    [JsonPropertyName("size_bytes")]
    public long SizeBytes
    {
        get => _sizeBytes;
        set { _sizeBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); }
    }

    [JsonPropertyName("min_app_version")]
    public string MinAppVersion
    {
        get => _minAppVersion;
        set { _minAppVersion = value; OnPropertyChanged(); }
    }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set { _updatedAt = value; OnPropertyChanged(); }
    }

    [JsonPropertyName("lua_install_url")]
    public string LuaInstallUrl
    {
        get => _luaInstallUrl;
        set { _luaInstallUrl = value; OnPropertyChanged(); }
    }

    [JsonPropertyName("lua_uninstall_url")]
    public string LuaUninstallUrl
    {
        get => _luaUninstallUrl;
        set { _luaUninstallUrl = value; OnPropertyChanged(); }
    }

    [JsonPropertyName("download_url")]
    public string DownloadUrl
    {
        get => _downloadUrl;
        set { _downloadUrl = value; OnPropertyChanged(); }
    }
    
    [JsonPropertyName("file_hash")]
    public string FileHash
    {
        get => _fileHash;
        set { _fileHash = value; OnPropertyChanged(); }
    }
    
    [JsonPropertyName("lua_hash")]
    public string LuaHash
    {
        get => _luaHash;
        set { _luaHash = value; OnPropertyChanged(); }
    }

    private string _dllFileName = string.Empty;
    
    [JsonPropertyName("dll_file_name")]
    public string DllFileName
    {
        get => _dllFileName;
        set { _dllFileName = value; OnPropertyChanged(); }
    }

    private string _visibility = "public";
    
    [JsonPropertyName("visibility")]
    public string Visibility
    {
        get => _visibility;
        set { _visibility = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPrivate)); }
    }

    private string _updateType = string.Empty;
    
    [JsonPropertyName("update_type")]
    public string UpdateType
    {
        get => _updateType;
        set { _updateType = value; OnPropertyChanged(); OnPropertyChanged(nameof(UpdateTypeDisplay)); }
    }

    private List<PluginDependency> _dependencies = new();
    
    [JsonPropertyName("dependencies")]
    public List<PluginDependency> Dependencies
    {
        get => _dependencies;
        set { _dependencies = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDependencies)); OnPropertyChanged(nameof(DependenciesDisplay)); }
    }
    
    [JsonIgnore]
    public string AccessToken { get; set; } = string.Empty;
    
    [JsonIgnore]
    public string DlToken { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsPrivate => Visibility == "private";

    [JsonIgnore]
    public bool HasDependencies => Dependencies?.Any(d => !d.IsEmpty) ?? false;

    [JsonIgnore]
    public string DependenciesDisplay
    {
        get
        {
            if (Dependencies == null || Dependencies.Count == 0) return string.Empty;
            var real = Dependencies.Where(d => !d.IsEmpty).Select(d => d.ToString()).Where(s => !string.IsNullOrEmpty(s));
            return string.Join("; ", real);
        }
    }

    [JsonIgnore]
    public string UpdateTypeDisplay => string.IsNullOrEmpty(UpdateType) ? "" : UpdateType;

    [JsonIgnore]
    public bool HasUpdateType => !string.IsNullOrEmpty(UpdateType);

    [JsonIgnore]
    public StorePluginState State
    {
        get => _state;
        set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanInstall));
            OnPropertyChanged(nameof(CanUninstall));
            OnPropertyChanged(nameof(StateIsInstalled));
            OnPropertyChanged(nameof(StateIsInProgress));
            OnPropertyChanged(nameof(ButtonText));
        }
    }

    [JsonIgnore]
    public int InstallProgress
    {
        get => _installProgress;
        set { _installProgress = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string InstallStatusText
    {
        get => _installStatusText;
        set { _installStatusText = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public bool IsInstallInProgress
    {
        get => _isInstallInProgress;
        set { _isInstallInProgress = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanInstall)); }
    }

    public bool HasDeveloper => !string.IsNullOrWhiteSpace(Developer);
    public bool HasScreenshots => Screenshots.Count > 0;

    public string VersionDisplay => string.IsNullOrEmpty(Version) ? "" : $"v{Version}";

    public string DownloadsDisplay => FormatDownloadCount(Downloads);

    public string SizeDisplay => FormatFileSize(SizeBytes);

    public string TagsDisplay => Tags.Count > 0 ? string.Join(" · ", Tags) : "";

    public bool CanInstall => State == StorePluginState.Available && !IsInstallInProgress;
    public bool CanUninstall => (State == StorePluginState.Installed || State == StorePluginState.UpdateAvailable) && !IsInstallInProgress;
    public bool StateIsInstalled => State == StorePluginState.Installed;
    public bool StateIsInstalledOrUpdate => State == StorePluginState.Installed || State == StorePluginState.UpdateAvailable;
    public bool StateIsUpdateAvailable => State == StorePluginState.UpdateAvailable;
    public bool StateIsInProgress => State == StorePluginState.Installing;

    public string ButtonText => State switch
    {
        StorePluginState.Available => "PluginStoreInstall".GetLocalized(),
        StorePluginState.Installed => "PluginStoreInstalled".GetLocalized(),
        StorePluginState.Installing => "PluginStoreInstalling".GetLocalized(),
        StorePluginState.UpdateAvailable => "PluginStoreUpdate".GetLocalized(),
        _ => "PluginStoreInstall".GetLocalized()
    };

    private static string FormatDownloadCount(long count)
    {
        var culture = ResourceExtensions.CurrentCulture ?? "";
        var isChinese = culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

        if (isChinese)
        {
            if (count >= 10000)
                return string.Format("PluginStoreDownloadCountWan".GetLocalized(), count / 10000.0);
            if (count >= 1000)
                return string.Format("PluginStoreDownloadCountK".GetLocalized(), count / 1000.0);
        }
        else
        {
            if (count >= 1000000)
                return string.Format("PluginStoreDownloadCountWan".GetLocalized(), count / 1000000.0);
            if (count >= 1000)
                return string.Format("PluginStoreDownloadCountK".GetLocalized(), count / 1000.0);
        }

        return count.ToString();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public enum StorePluginState
{
    Available,
    Installing,
    Installed,
    UpdateAvailable
}
