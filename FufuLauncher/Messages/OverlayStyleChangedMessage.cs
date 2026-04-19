using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FufuLauncher.Messages
{
    public class OverlayStyleChangedMessage : ValueChangedMessage<bool>
    {
        public OverlayStyleChangedMessage(bool isAcrylic) : base(isAcrylic) { }
    }
}