/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace FufuLauncher.Messages;

public class BackgroundDownloadStateMessage : ValueChangedMessage<bool>
{
    public BackgroundDownloadStateMessage(bool isDownloading) : base(isDownloading)
    {
    }
}
