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
        { 1, ("Element_Pyro".GetLocalized(), "https://act.mihoyo.com/act/gt-ui/assets/icons/6a4f0b7ab73fe4d3.png") },
        { 2, ("Element_Anemo".GetLocalized(), "https://act.mihoyo.com/act/gt-ui/assets/icons/48d1aac6ecc56b33.png") },
        { 3, ("Element_Geo".GetLocalized(), "https://act.mihoyo.com/act/gt-ui/assets/icons/829a6b86fb23d8bb.png") },
        { 4, ("Element_Dendro".GetLocalized(), "https://act.mihoyo.com/act/gt-ui/assets/icons/247f14512efc8325.png") },
        { 5, ("Element_Electro".GetLocalized(), "https://act.mihoyo.com/act/gt-ui/assets/icons/e18d224ec1047cae.png") },
        { 6, ("Element_Hydro".GetLocalized(), "https://act.mihoyo.com/act/gt-ui/assets/icons/b162f5384487d283.png") },
        { 7, ("Element_Cryo".GetLocalized(), "https://act.mihoyo.com/act/gt-ui/assets/icons/bf2f65ee0d7f6243.png") }
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

