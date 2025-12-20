namespace FufuLauncher.Contracts.Services;

public interface IHoyoverseCheckinService
{
    Task<(string status, string summary)> GetCheckinStatusAsync();
    Task<(bool success, string message)> ExecuteCheckinAsync();
}