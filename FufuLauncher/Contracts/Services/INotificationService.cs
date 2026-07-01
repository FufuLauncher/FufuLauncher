/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using FufuLauncher.Messages;

namespace FufuLauncher.Contracts.Services
{
    public interface INotificationService
    {
        Task ShowAsync(string title, string message, NotificationType type = NotificationType.Information, int duration = 5000);
        void Show(string title, string message, NotificationType type = NotificationType.Information, int duration = 5000);
    }
}
