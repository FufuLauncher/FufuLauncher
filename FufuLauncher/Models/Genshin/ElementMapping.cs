/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public static class ElementMapping
{
    private static readonly Dictionary<int, (string Name, string IconUrl)> Elements = new()
    {
        { 1, ("Element_Pyro".GetLocalized(), ApiEndpoints.ElementPyroIconUrl) },
        { 2, ("Element_Anemo".GetLocalized(), ApiEndpoints.ElementAnemoIconUrl) },
        { 3, ("Element_Geo".GetLocalized(), ApiEndpoints.ElementGeoIconUrl) },
        { 4, ("Element_Dendro".GetLocalized(), ApiEndpoints.ElementDendroIconUrl) },
        { 5, ("Element_Electro".GetLocalized(), ApiEndpoints.ElementElectroIconUrl) },
        { 6, ("Element_Hydro".GetLocalized(), ApiEndpoints.ElementHydroIconUrl) },
        { 7, ("Element_Cryo".GetLocalized(), ApiEndpoints.ElementCryoIconUrl) }
    };

    public static string? GetElementName(int elementId)
    {
        return Elements.TryGetValue(elementId, out var element) ? element.Name : null;
    }

    public static string? GetElementIconUrl(int elementId)
    {
        return Elements.TryGetValue(elementId, out var element) ? element.IconUrl : null;
    }

    public static (string? Name, string? IconUrl) GetElement(int elementId)
    {
        return Elements.TryGetValue(elementId, out var element) ? element : (null, null);
    }
}