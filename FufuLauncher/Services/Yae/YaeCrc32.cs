/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services.Yae;

public static class YaeCrc32
{
    private const uint Polynomial = 0xEDB88320;
    private static readonly uint[] Table = new uint[256];

    static YaeCrc32()
    {
        for (uint i = 0; i < Table.Length; i++)
        {
            var value = i;
            for (var j = 0; j < 8; j++)
            {
                value = (value >> 1) ^ ((value & 1) * Polynomial);
            }

            Table[i] = value;
        }
    }

    public static uint Compute(ReadOnlySpan<byte> buffer)
    {
        var checksum = 0xFFFFFFFFu;
        foreach (var b in buffer)
        {
            checksum = (checksum >> 8) ^ Table[(b ^ checksum) & 0xFF];
        }

        return ~checksum;
    }
}