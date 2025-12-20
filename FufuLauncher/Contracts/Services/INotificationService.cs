using FufuLauncher.Messages;

namespace FufuLauncher.Contracts.Services
{
    public interface INotificationService
    {
        Task ShowAsync(string title, string message, NotificationType type = NotificationType.Information, int duration = 5000);
        void Show(string title, string message, NotificationType type = NotificationType.Information, int duration = 5000);
    }
}