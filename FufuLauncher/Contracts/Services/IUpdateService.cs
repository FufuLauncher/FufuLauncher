namespace FufuLauncher.Contracts.Services
{
    public interface IUpdateService
    {
        Task<UpdateCheckResult> CheckUpdateAsync();
    }

    public class UpdateCheckResult
    {
        public bool ShouldShowUpdate
        {
            get; set;
        }
        public string ServerVersion { get; set; } = string.Empty;
        public string UpdateInfoUrl { get; set; } = string.Empty;
    }
}