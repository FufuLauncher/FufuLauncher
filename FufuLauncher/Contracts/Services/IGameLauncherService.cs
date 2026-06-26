/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Services;

namespace FufuLauncher.Contracts.Services
{
    public interface IGameLauncherService
    {
        bool IsGamePathSelected();
        string GetGamePath();
        Task SaveGamePathAsync(string path);
        Task<bool> GetUseInjectionAsync();
        Task SetUseInjectionAsync(bool useInjection);

        Task<string> GetCustomLaunchParametersAsync();
        Task SetCustomLaunchParametersAsync(string parameters);

        Task<LaunchResult> LaunchGameAsync();
        Task StopBetterGIAsync();
    }
}
