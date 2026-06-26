/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Models;

namespace FufuLauncher.Contracts.Services
{
    public interface IAccountService
    {
        Task<List<GameAccount>> GetAccountsAsync();
        Task AddAccountAsync(GameAccount account);
        Task RemoveAccountAsync(Guid id);
        Task SetCurrentAccountAsync(GameAccount account);
        Task<GameAccount?> GetCurrentAccountAsync();
        Task<bool> TestRegistryAccessAsync();
        Task SetHDREnabledAsync(bool enabled);
        Task<bool> GetHDREnabledAsync();
    }
}
