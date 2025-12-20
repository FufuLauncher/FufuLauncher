using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Messages;

namespace FufuLauncher.Services
{
    public class NotificationService : INotificationService
    {
        public Task ShowAsync(string title, string message, NotificationType type = NotificationType.Information, int duration = 5000)
        {
            return Task.Run(() => Show(title, message, type, duration));
        }

        public void Show(string title, string message, NotificationType type = NotificationType.Information, int duration = 5000)
        {
            WeakReferenceMessenger.Default.Send(new NotificationMessage(title, message, type, duration));
        }
    }
}