/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.Windows.ApplicationModel.Resources;

namespace FufuLauncher.Helpers;

public static class ResourceExtensions
{

    private static ResourceManager _resourceManager;

    public static string GetLocalized(this string resourceKey)
    {
        try
        {


            if (_resourceManager == null)
            {
                _resourceManager = new ResourceManager();
            }


            var candidate = _resourceManager.MainResourceMap.GetValue($"Resources/{resourceKey}");
            return candidate != null ? candidate.ValueAsString : resourceKey;
        }
        catch
        {

            return resourceKey;
        }
    }
}
