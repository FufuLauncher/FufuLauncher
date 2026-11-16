/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Helpers;

namespace FufuLauncher.Models.GameServer;

internal enum CutsceneTraveler { Aether, Lumine }

internal static class CutsceneTravelers
{
    private static readonly (string Aether, string Lumine)[] SuffixPairs =
        [("_Boy", "_Girl"), ("_PlayerBoy", "_PlayerGirl")];

    public static CutsceneTraveler? FindVariant(string filename, IReadOnlySet<string> shippedFiles)
    {
        string stem = Path.GetFileNameWithoutExtension(filename);
        foreach (var pair in SuffixPairs)
        {
            if (stem.EndsWith(pair.Aether, StringComparison.OrdinalIgnoreCase)
                && shippedFiles.Contains(stem[..^pair.Aether.Length] + pair.Lumine + ".usm"))
                return CutsceneTraveler.Aether;
            if (stem.EndsWith(pair.Lumine, StringComparison.OrdinalIgnoreCase)
                && shippedFiles.Contains(stem[..^pair.Lumine.Length] + pair.Aether + ".usm"))
                return CutsceneTraveler.Lumine;
        }

        return null;
    }

    public static string DisplayName(this CutsceneTraveler traveler) => $"Cutscene_Traveler_{traveler}".GetLocalized();
}
