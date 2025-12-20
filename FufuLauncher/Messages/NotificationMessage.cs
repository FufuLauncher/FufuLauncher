using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FufuLauncher.Messages
{
    public enum NotificationType
    {
        Information,
        Success,
        Warning,
        Error
    }

    public class NotificationMessage : ValueChangedMessage<NotificationMessage>
    {
        public string Title
        {
            get;
        }
        public string Message
        {
            get;
        }
        public NotificationType Type
        {
            get;
        }
        public int Duration
        {
            get;
        }
        public bool IsPersistent => Duration == 0;

        public NotificationMessage(string title, string message, NotificationType type, int duration = 5000)
            : base(null)
        {
            Title = title;
            Message = message;
            Type = type;
            Duration = duration;
        }
    }
}