namespace FufuLauncher.Contracts.Services;

public interface IScreenshotService
{
    Task StartAsync(int gamePid);
    Task StopAsync();
    bool IsRunning { get; }
}
