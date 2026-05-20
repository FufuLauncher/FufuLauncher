using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FufuLauncher.Messages;

public class MinWindowSizeLimitChangedMessage : ValueChangedMessage<bool>
{
    public MinWindowSizeLimitChangedMessage(bool value) : base(value)
    {
    }
}