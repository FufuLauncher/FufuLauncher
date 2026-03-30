using System.Diagnostics;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using Microsoft.Data.Sqlite;

namespace FufuLauncher.Services
{
    public class LocalSettingsService : ILocalSettingsService
    {
        private const string _defaultApplicationDataFolder = "FufuLauncher/ApplicationData";
        private const string _defaultLocalSettingsDb = "LocalSettings.db";

        private readonly string _localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private readonly string _applicationDataFolder;
        private readonly string _dbPath;

        private Dictionary<string, string> _settings;
        private bool _isInitialized = false;

        public const string BackgroundServerKey = "BackgroundServer";
        public const string IsBackgroundEnabledKey = "IsBackgroundEnabled";
        public const string LastAnnouncedVersionKey = "LastAnnouncedVersion";
        
        public const string LastAnnouncementUrlKey = "LastAnnouncementUrl";
        
        public const string HasShownSecurityWarningKey = "HasShownSecurityWarning";

        private readonly JsonSerializerOptions _jsonOptions;

        public LocalSettingsService()
        {
            _applicationDataFolder = Path.Combine(_localApplicationData, _defaultApplicationDataFolder);
            _dbPath = Path.Combine(_applicationDataFolder, _defaultLocalSettingsDb);
            _settings = new Dictionary<string, string>();

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
        }

        public async Task InitializeAsync()
        {
            if (!_isInitialized)
            {
                Debug.WriteLine("LocalSettingsService: 开始初始化数据库");
                
                Directory.CreateDirectory(_applicationDataFolder);
                
                await InitializeDatabaseAsync();
                
                _settings = await LoadSettingsFromDbAsync();
                
                _isInitialized = true;
                Debug.WriteLine($"LocalSettingsService: 初始化完成，加载 {_settings.Count} 项");
            }
        }

        public async Task<object?> ReadSettingAsync(string key)
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            if (_settings.TryGetValue(key, out var storedValue))
            {
                Debug.WriteLine($"LocalSettingsService: 读取 '{key}' -> {storedValue}");

                try
                {
                    var deserialized = JsonSerializer.Deserialize<object>(storedValue, _jsonOptions);

                    if (deserialized is JsonElement jsonElement)
                    {
                        return jsonElement.ValueKind switch
                        {
                            JsonValueKind.String => jsonElement.GetString(),
                            JsonValueKind.Number => jsonElement.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            JsonValueKind.Array => jsonElement.EnumerateArray().ToArray(),
                            JsonValueKind.Object => jsonElement,
                            _ => storedValue
                        };
                    }

                    return deserialized;
                }
                catch (JsonException)
                {
                    return storedValue;
                }
            }

            Debug.WriteLine($"LocalSettingsService: 读取 '{key}' 未找到");
            return null;
        }

        public async Task SaveSettingAsync<T>(string key, T value)
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }
            
            var json = JsonSerializer.Serialize(value, _jsonOptions);
            
            _settings[key] = json;

            Debug.WriteLine($"LocalSettingsService: 保存 '{key}' -> {json}");
            
            await SaveSettingToDbAsync(key, json);
        }

        private async Task InitializeDatabaseAsync()
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();
                
                var command = connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS Settings (
                        [Key] TEXT PRIMARY KEY,
                        [Value] TEXT
                    )";
                
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LocalSettingsService: 数据库表初始化失败 - {ex.Message}");
            }
        }

        private async Task<Dictionary<string, string>> LoadSettingsFromDbAsync()
        {
            var settings = new Dictionary<string, string>();
            try
            {
                Debug.WriteLine($"LocalSettingsService: 尝试从数据库加载 {_dbPath}");

                if (File.Exists(_dbPath))
                {
                    using var connection = new SqliteConnection($"Data Source={_dbPath}");
                    await connection.OpenAsync();
                    
                    var command = connection.CreateCommand();
                    command.CommandText = "SELECT [Key], [Value] FROM Settings";
                    
                    using var reader = await command.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        var key = reader.GetString(0);
                        var value = reader.GetString(1);
                        settings[key] = value;
                    }

                    Debug.WriteLine($"LocalSettingsService: 成功从数据库加载 {settings.Count} 项");
                    return settings;
                }

                Debug.WriteLine("LocalSettingsService: 数据库文件尚未创建，返回空字典");
                return settings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LocalSettingsService: 数据库加载失败 - {ex.Message}");
                return settings;
            }
        }

        private async Task SaveSettingToDbAsync(string key, string value)
        {
            try
            {
                using var connection = new SqliteConnection($"Data Source={_dbPath}");
                await connection.OpenAsync();
                
                var command = connection.CreateCommand();
                command.CommandText = @"
                    INSERT INTO Settings ([Key], [Value])
                    VALUES ($key, $value)
                    ON CONFLICT([Key]) DO UPDATE SET [Value] = excluded.[Value]";
                
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$value", value);
                
                await command.ExecuteNonQueryAsync();
                Debug.WriteLine($"LocalSettingsService: 已将 '{key}' 保存至数据库");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LocalSettingsService: 数据库保存失败 - {ex.Message}");
            }
        }
    }
}