/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
namespace FufuLauncher.Models;

public static class ElementMapping
{
    private static readonly Dictionary<int, (string Name, string IconUrl)> Elements = new()
    {
        { 1, ("火", "https://act.mihoyo.com/act/gt-ui/assets/icons/6a4f0b7ab73fe4d3.png") },
        { 2, ("风", "https://act.mihoyo.com/act/gt-ui/assets/icons/48d1aac6ecc56b33.png") },
        { 3, ("岩", "https://act.mihoyo.com/act/gt-ui/assets/icons/829a6b86fb23d8bb.png") },
        { 4, ("草", "https://act.mihoyo.com/act/gt-ui/assets/icons/247f14512efc8325.png") },
        { 5, ("雷", "https://act.mihoyo.com/act/gt-ui/assets/icons/e18d224ec1047cae.png") },
        { 6, ("水", "https://act.mihoyo.com/act/gt-ui/assets/icons/b162f5384487d283.png") },
        { 7, ("冰", "https://act.mihoyo.com/act/gt-ui/assets/icons/bf2f65ee0d7f6243.png") }
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

