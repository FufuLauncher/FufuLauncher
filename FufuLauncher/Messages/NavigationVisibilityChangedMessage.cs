using CommunityToolkit.Mvvm.Messaging.Messages;
using FufuLauncher.Models;

namespace FufuLauncher.Messages;

public class NavigationVisibilityChangedMessage : ValueChangedMessage<NavItemConfig>
{
    public NavigationVisibilityChangedMessage(NavItemConfig value) : base(value)
    {
    }
}
