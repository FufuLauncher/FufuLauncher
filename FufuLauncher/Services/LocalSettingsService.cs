using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FufuLauncher.Contracts.Services;

namespace FufuLauncher.Services
{
    public class LocalSettingsService : ILocalSettingsService
    {
        private const string _defaultApplicationDataFolder = "FufuLauncher/ApplicationData";
        private const string _defaultLocalSettingsFile = "LocalSettings.json";

        private readonly string _localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        private readonly string _applicationDataFolder;
        private readonly string _localsettingsFile;

        private Dictionary<string, string> _settings;
        private bool _isInitialized = false;

        public const string BackgroundServerKey = "BackgroundServer";
        public const string IsBackgroundEnabledKey = "IsBackgroundEnabled";

        public const string LastAnnouncedVersionKey = "LastAnnouncedVersion";

        private readonly JsonSerializerOptions _jsonOptions;

        public LocalSettingsService()
        {
            _applicationDataFolder = Path.Combine(_localApplicationData, _defaultApplicationDataFolder);
            _localsettingsFile = _defaultLocalSettingsFile;
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
                Debug.WriteLine("LocalSettingsService: 开始初始化");
                _settings = await LoadSettingsAsync();
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
            await SaveSettingsAsync();
        }

        private async Task<Dictionary<string, string>> LoadSettingsAsync()
        {
            try
            {
                var filePath = Path.Combine(_applicationDataFolder, _localsettingsFile);
                Debug.WriteLine($"LocalSettingsService: 尝试加载 {filePath}");

                if (File.Exists(filePath))
                {

                    var data = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

                    var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(data, _jsonOptions) ?? new();
                    Debug.WriteLine($"LocalSettingsService: 成功加载 {settings.Count} 项");
                    Debug.WriteLine($"LocalSettingsService: 文件内容: {data}");
                    return settings;
                }

                Debug.WriteLine("LocalSettingsService: 文件不存在");
                return new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LocalSettingsService: 加载失败 - {ex.Message}");
                return new Dictionary<string, string>();
            }
        }

        private async Task SaveSettingsAsync()
        {
            try
            {
                Directory.CreateDirectory(_applicationDataFolder);
                var filePath = Path.Combine(_applicationDataFolder, _localsettingsFile);

                var data = JsonSerializer.Serialize(_settings, _jsonOptions);

                await File.WriteAllTextAsync(filePath, data, Encoding.UTF8);

                Debug.WriteLine($"LocalSettingsService: 保存到 {filePath}");
                Debug.WriteLine($"LocalSettingsService: 保存内容: {data}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LocalSettingsService: 保存失败 - {ex.Message}");
            }
        }
    }
}