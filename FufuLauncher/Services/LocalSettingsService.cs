/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Data.Repositories;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;

namespace FufuLauncher.Services
{
    public class LocalSettingsService : ILocalSettingsService
    {
        private const string _defaultApplicationDataFolder = "FufuLauncher/ApplicationData";
        private const string _defaultLocalSettingsDb = "LocalSettings.db";

        private readonly LocalSettingsRepository _repository;

        private Dictionary<string, string> _settings;
        private bool _isInitialized = false;

        public const string BackgroundServerKey = "BackgroundServer";
        public const string IsBackgroundEnabledKey = "IsBackgroundEnabled";
        public const string LastAnnouncedVersionKey = "LastAnnouncedVersion";

        public const string LastAnnouncementUrlKey = "LastAnnouncementUrl";

        public const string HasShownSecurityWarningKey = "HasShownSecurityWarning";

        public const string HasDismissedFpsWarningKey = "HasDismissedFpsWarning";

        private readonly JsonSerializerOptions _jsonOptions;

        public LocalSettingsService(LocalSettingsRepository repository)
        {
            _repository = repository;
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
                if (Helpers.AppPaths.IsFirstRun)
                {
                    Debug.WriteLine("LocalSettingsService: 首次运行，跳过数据库初始化");
                    return;
                }

                Debug.WriteLine("LocalSettingsService: 开始初始化数据库");

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Helpers.AppPaths.LocalSettingsDb)!);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LocalSettingsService: 创建配置目录失败 - {ex.Message}");
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "Settings_DirCreateFailed".GetLocalized(),
                        string.Format("Settings_DirCreateFailedMsg".GetLocalized(), ex.Message),
                        NotificationType.Error,
                        4000
                    ));
                }

                _settings = await _repository.GetAllSettingsAsync();

                try
                {
                    bool isAutoDisableFpsOff = false;
                    if (_settings.TryGetValue("IsAutoDisableFpsOff", out var offStr))
                    {
                        isAutoDisableFpsOff = offStr.Contains("true", StringComparison.OrdinalIgnoreCase);
                    }

                    if (!isAutoDisableFpsOff)
                    {
                        string fpsDir = Path.Combine(AppContext.BaseDirectory, "Plugins", "FPS");
                        string fpsEnabledPath = Path.Combine(fpsDir, "FPS.dll");
                        string fpsDisabledPath = Path.Combine(fpsDir, "FPS.disabled");

                        if (File.Exists(fpsEnabledPath))
                        {
                            if (File.Exists(fpsDisabledPath))
                            {
                                File.Delete(fpsDisabledPath);
                            }
                            File.Move(fpsEnabledPath, fpsDisabledPath);
                            Debug.WriteLine("LocalSettingsService: 已在启动时自动禁用FPS插件");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LocalSettingsService: 自动禁用FPS插件失败 - {ex.Message}");
                }

                _isInitialized = true;
                Debug.WriteLine($"LocalSettingsService: 初始化完成，加载 {_settings.Count} 项");
            }
        }

        public async Task ReInitializeAsync()
        {
            _isInitialized = false;
            _settings = new Dictionary<string, string>();
            await InitializeAsync();
        }

        public async Task<object?> ReadSettingAsync(string key)
        {
            if (Helpers.AppPaths.IsFirstRun) return null;

            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            if (_settings.TryGetValue(key, out var storedValue))
            {
                Debug.WriteLine($"LocalSettingsService: 读取 {key}");

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
            if (Helpers.AppPaths.IsFirstRun) return;

            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            var json = JsonSerializer.Serialize(value, _jsonOptions);

            _settings[key] = json;

            Debug.WriteLine($"LocalSettingsService: 保存{key}");

            await _repository.UpsertSettingAsync(key, json);
        }

        public async Task RemoveSettingAsync(string key)
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            if (_settings.ContainsKey(key))
            {
                _settings.Remove(key);
                await _repository.DeleteSettingAsync(key);
            }
        }
    }
}
