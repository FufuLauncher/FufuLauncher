/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FufuLauncher.Services;

internal sealed class PluginConfigBaseline
{
    private const int FormatVersion = 1;
    private const int HashSize = 32;

    private static readonly byte[] Magic = [(byte)'F', (byte)'C', (byte)'L', (byte)'B'];

    private static readonly Regex BuildDatePattern =
        new(@"(\d{1,2})\s*月\s*(\d{1,2})\s*号\s*构建版本", RegexOptions.Compiled);

    public string SourceUrl
    {
        get;
        init;
    } = string.Empty;

    public DateTimeOffset FetchedAtUtc
    {
        get;
        init;
    }

    public string Description
    {
        get;
        init;
    } = string.Empty;

    public int BuildMonth
    {
        get;
        init;
    }

    public int BuildDay
    {
        get;
        init;
    }

    public string BuildDateText
    {
        get;
        init;
    } = string.Empty;

    public string Content
    {
        get;
        init;
    } = string.Empty;

    public static bool TryParseBuildDate(string? description, out int month, out int day, out string matched)
    {
        month = 0;
        day = 0;
        matched = string.Empty;

        if (string.IsNullOrWhiteSpace(description))
        {
            return false;
        }

        var match = BuildDatePattern.Match(description);
        if (!match.Success)
        {
            return false;
        }

        if (!int.TryParse(match.Groups[1].Value, out month) || !int.TryParse(match.Groups[2].Value, out day))
        {
            return false;
        }

        if (month is < 1 or > 12 || day is < 1 or > 31)
        {
            return false;
        }

        matched = match.Value;
        return true;
    }

    public static IReadOnlyList<string> SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    public static void Write(string path, PluginConfigBaseline baseline)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var contentBytes = Encoding.UTF8.GetBytes(baseline.Content ?? string.Empty);
        var hash = SHA256.HashData(contentBytes);

        var temporaryPath = path + ".tmp";

        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, false))
        {
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(baseline.SourceUrl ?? string.Empty);
            writer.Write(baseline.FetchedAtUtc.UtcDateTime.Ticks);
            writer.Write(baseline.Description ?? string.Empty);
            writer.Write(baseline.BuildMonth);
            writer.Write(baseline.BuildDay);
            writer.Write(baseline.BuildDateText ?? string.Empty);
            writer.Write(contentBytes.Length);
            writer.Write(contentBytes);
            writer.Write(hash);
        }

        File.Move(temporaryPath, path, true);
    }

    public static PluginConfigBaseline? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8, false);

            var magic = reader.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length || !magic.SequenceEqual(Magic))
            {
                return null;
            }

            if (reader.ReadInt32() != FormatVersion)
            {
                return null;
            }

            var sourceUrl = reader.ReadString();
            var fetchedAtUtc = new DateTimeOffset(reader.ReadInt64(), TimeSpan.Zero);
            var description = reader.ReadString();
            var buildMonth = reader.ReadInt32();
            var buildDay = reader.ReadInt32();
            var buildDateText = reader.ReadString();

            var length = reader.ReadInt32();
            if (length < 0 || length > 4 * 1024 * 1024)
            {
                return null;
            }

            var contentBytes = reader.ReadBytes(length);
            if (contentBytes.Length != length)
            {
                return null;
            }

            var hash = reader.ReadBytes(HashSize);
            if (hash.Length != HashSize || !SHA256.HashData(contentBytes).SequenceEqual(hash))
            {
                return null;
            }

            return new PluginConfigBaseline
            {
                SourceUrl = sourceUrl,
                FetchedAtUtc = fetchedAtUtc,
                Description = description,
                BuildMonth = buildMonth,
                BuildDay = buildDay,
                BuildDateText = buildDateText,
                Content = Encoding.UTF8.GetString(contentBytes)
            };
        }
        catch
        {
            return null;
        }
    }
}
