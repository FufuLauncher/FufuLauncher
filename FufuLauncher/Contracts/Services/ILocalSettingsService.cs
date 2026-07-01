/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
namespace FufuLauncher.Contracts.Services
{
    public interface ILocalSettingsService
    {
        Task<object?> ReadSettingAsync(string key);
        Task SaveSettingAsync<T>(string key, T value);
        Task ReInitializeAsync();
    }
}
