using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FufuLauncher.Messages;

public class CloudCredentialUpdatedMessage : ValueChangedMessage<string>
{
    public CloudCredentialUpdatedMessage(string uid) : base(uid) { }
}
