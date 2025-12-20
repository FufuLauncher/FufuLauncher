using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Contracts.Services;

namespace FufuLauncher.Services;

public interface IGameConfigService
{
    Task<GameConfig?> LoadGameConfigAsync(string gamePath);
    Task SaveGamePathAsync(string gamePath);
    Task<string?> GetSavedGamePathAsync();
}

public class GameConfig : ObservableObject
{
    private string _gamePath = string.Empty;
    private string _version = string.Empty;
    private string _serverType = string.Empty;
    private string _directorySize = "0 MB";

    public string GamePath
    {
        get => _gamePath;
        set => SetProperty(ref _gamePath, value);
    }

    public string Version
    {
        get => _version;
        set => SetProperty(ref _version, value);
    }

    public string ServerType
    {
        get => _serverType;
        set => SetProperty(ref _serverType, value);
    }

    public string DirectorySize
    {
        get => _directorySize;
        set => SetProperty(ref _directorySize, value);
    }
}

public class GameConfigService : IGameConfigService
{
    private const string SettingsKey = "GameInstallationPath";
    private readonly ILocalSettingsService _localSettingsService;

    public GameConfigService(ILocalSettingsService localSettingsService)
    {
        _localSettingsService = localSettingsService;
    }

    public async Task<GameConfig?> LoadGameConfigAsync(string gamePath)
    {
        if (string.IsNullOrEmpty(gamePath) || !Directory.Exists(gamePath))
            return null;

        try
        {
            var config = new GameConfig { GamePath = gamePath };

            var configPath = Path.Combine(gamePath, "config.ini");
            if (!File.Exists(configPath))
            {

                configPath = Directory.GetFiles(gamePath, "config.ini", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }

            if (configPath != null && File.Exists(configPath))
            {
                var content = await File.ReadAllTextAsync(configPath);

                var versionLine = content.Split('\n')
                    .FirstOrDefault(line => line.StartsWith("game_version=", StringComparison.OrdinalIgnoreCase));
                if (versionLine != null)
                {
                    var parts = versionLine.Split('=', 2);
                    if (parts.Length > 1)
                        config.Version = parts[1].Trim();
                }

                config.ServerType = DetectServerType(content);
            }
            else
            {
                config.Version = "未找到版本信息";
                config.ServerType = "未知";
            }

            config.DirectorySize = CalculateDirectorySize(gamePath);

            return config;
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveGamePathAsync(string gamePath)
    {
        await _localSettingsService.SaveSettingAsync(SettingsKey, gamePath);
    }

    public async Task<string?> GetSavedGamePathAsync()
    {

        var result = await _localSettingsService.ReadSettingAsync(SettingsKey);
        return result?.ToString();
    }

    private string DetectServerType(string configContent)
    {

        if (configContent.Contains("pcadbdpz") || configContent.Contains("channel=1"))
            return "中国大陆服务器（官服）";

        if (configContent.Contains("channel=14") || configContent.Contains("cps=bilibili"))
            return "中国大陆服务器（B服）";

        if (configContent.Contains("os_usa") || configContent.Contains("os_euro") ||
            configContent.Contains("os_asia") || configContent.Contains("channel=0"))
            return "国际服务器";

        return "未知服务器";
    }

    private string CalculateDirectorySize(string path)
    {
        try
        {
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            long sizeInBytes = files.Sum(file => new FileInfo(file).Length);

            return sizeInBytes switch
            {
                >= 1073741824 => $"{sizeInBytes / 1073741824.0:F2} GB",
                >= 1048576 => $"{sizeInBytes / 1048576.0:F2} MB",
                >= 1024 => $"{sizeInBytes / 1024.0:F2} KB",
                _ => $"{sizeInBytes} Bytes"
            };
        }
        catch
        {
            return "无法计算";
        }
    }
}