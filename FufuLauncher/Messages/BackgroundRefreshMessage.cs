/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FufuLauncher.Messages
{
    public class BackgroundRefreshMessage : ValueChangedMessage<bool>
    {
        public BackgroundRefreshMessage(bool forceRefresh = true) : base(forceRefresh)
        {
        }
    }
}

