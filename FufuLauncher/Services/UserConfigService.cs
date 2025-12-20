using System.Text.Json;
using FufuLauncher.Models;

namespace FufuLauncher.Services;

public interface IUserConfigService
{
    Task<UserDisplayConfig> LoadDisplayConfigAsync();
    Task SaveDisplayConfigAsync(UserDisplayConfig config);
    Task<bool> DisplayConfigExistsAsync();
}

public class UserConfigService : IUserConfigService
{
    private readonly string _configPath;

    public UserConfigService()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "user.config.json");
    }

    public async Task<UserDisplayConfig> LoadDisplayConfigAsync()
    {
        try
        {
            if (!File.Exists(_configPath))
            {
                return new UserDisplayConfig();
            }

            var json = await File.ReadAllTextAsync(_configPath);
            return JsonSerializer.Deserialize<UserDisplayConfig>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new UserDisplayConfig();
        }
        catch
        {
            return new UserDisplayConfig();
        }
    }

    public async Task SaveDisplayConfigAsync(UserDisplayConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(_configPath, json);
        }
        catch (Exception ex)
        {
            throw new IOException($"保存用户显示配置失败: {ex.Message}");
        }
    }

    public Task<bool> DisplayConfigExistsAsync()
    {
        return Task.FromResult(File.Exists(_configPath));
    }
}