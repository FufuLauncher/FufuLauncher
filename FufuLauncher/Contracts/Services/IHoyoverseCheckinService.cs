namespace FufuLauncher.Contracts.Services;

public interface IHoyoverseCheckinService
{
    Task<(string status, string summary)> GetCheckinStatusAsync(string targetUid = null);
    Task<(bool success, string message)> ExecuteCheckinAsync(string targetUid = null);
    Task<List<string>> GetBoundUidsAsync();
}