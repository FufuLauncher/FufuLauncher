/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Tray;

internal sealed partial class InputActiveDesktopAcrylicBackdrop : Microsoft.UI.Xaml.Media.SystemBackdrop
{
    private readonly ConcurrentDictionary<ICompositionSupportsSystemBackdrop, DesktopAcrylicController> _controllers = new();

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);

        var configuration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        configuration.IsInputActive = true;

        var controller = new DesktopAcrylicController();
        controller.AddSystemBackdropTarget(target);
        controller.SetSystemBackdropConfiguration(configuration);
        _controllers.TryAdd(target, controller);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);

        if (_controllers.TryRemove(target, out var controller) && controller is not null)
        {
            controller.RemoveSystemBackdropTarget(target);
            controller.Dispose();
        }
    }
}
