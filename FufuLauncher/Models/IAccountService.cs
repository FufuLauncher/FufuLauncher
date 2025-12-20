using FufuLauncher.Models;

namespace FufuLauncher.Core.Contracts.Services
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