/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using CommunityToolkit.Mvvm.Messaging.Messages;
using FufuLauncher.Models;

namespace FufuLauncher.Messages;

public class QuickFpsSettingsChangedMessage : ValueChangedMessage<QuickFpsSettings>
{
    public QuickFpsSettingsChangedMessage(QuickFpsSettings settings) : base(settings)
    {
    }
}