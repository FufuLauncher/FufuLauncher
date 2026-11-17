/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Text;

namespace FufuLauncher.Services.GameServer;

internal sealed record CutsceneAudioTrack(int Index, string? Language, string? Name);
internal sealed record CutsceneSubtitleTrack(int Index, string? Language, string? Name, string? CodecId);
internal sealed record CutsceneMediaTracks(
    IReadOnlyList<CutsceneAudioTrack> AudioTracks,
    IReadOnlyList<CutsceneSubtitleTrack> SubtitleTracks);

internal static class CutsceneAudioTracks
{
    private const uint Segment = 0x18538067;
    private const uint Tracks = 0x1654AE6B;
    private const uint TrackEntry = 0xAE;
    private const uint TrackType = 0x83;
    private const uint TrackName = 0x536E;
    private const uint CodecId = 0x86;
    private const uint TrackLanguage = 0x22B59C;
    private const uint TrackLanguageBcp47 = 0x22B59D;

    // Read the Matroska track table produced by Charlotte without loading the video frames or requiring codecs.
    public static IReadOnlyList<CutsceneAudioTrack> Read(string video) => ReadMediaTracks(video).AudioTracks;

    public static CutsceneMediaTracks ReadMediaTracks(string video)
    {
        using var stream = File.OpenRead(video);
        foreach (var element in ReadChildren(stream, stream.Length))
        {
            if (element.Id != Segment)
                continue;

            foreach (var child in ReadChildren(stream, element.End))
            {
                if (child.Id == Tracks)
                    return ReadTracks(stream, child.End);
            }
        }

        throw new InvalidDataException("The video has no Matroska track table.");
    }

    private static CutsceneMediaTracks ReadTracks(Stream stream, long end)
    {
        var audio = new List<CutsceneAudioTrack>();
        var subtitles = new List<CutsceneSubtitleTrack>();
        foreach (var entry in ReadChildren(stream, end))
        {
            if (entry.Id != TrackEntry)
                continue;

            ulong type = 0;
            string? name = null;
            string? codecId = null;
            string? language = null;
            string? languageBcp47 = null;
            foreach (var field in ReadChildren(stream, entry.End))
            {
                switch (field.Id)
                {
                    case TrackType:
                        if (field.End - stream.Position > sizeof(ulong))
                            throw new InvalidDataException("Invalid Matroska track type.");
                        while (stream.Position < field.End)
                            type = (type << 8) | ReadByte(stream);
                        break;
                    case TrackName:
                        name = ReadText(stream, field.End);
                        break;
                    case CodecId:
                        codecId = ReadText(stream, field.End);
                        break;
                    case TrackLanguage:
                        language = ReadText(stream, field.End);
                        break;
                    case TrackLanguageBcp47:
                        languageBcp47 = ReadText(stream, field.End);
                        break;
                }
            }

            if (type == 2)
                audio.Add(new CutsceneAudioTrack(audio.Count, languageBcp47 ?? language, name));
            else if (type == 0x11)
                subtitles.Add(new CutsceneSubtitleTrack(subtitles.Count, languageBcp47 ?? language, name, codecId));
        }
        return new CutsceneMediaTracks(audio, subtitles);
    }

    private static IEnumerable<(uint Id, long End)> ReadChildren(Stream stream, long end)
    {
        while (stream.Position < end)
        {
            uint id = (uint)ReadVariableInteger(stream, keepMarker: true, out _);
            ulong size = ReadVariableInteger(stream, keepMarker: false, out int width);
            long remaining = end - stream.Position;
            bool unknownSize = size == (1UL << (7 * width)) - 1;
            if (remaining < 0 || (unknownSize ? id != Segment : size > (ulong)remaining))
                throw new InvalidDataException("Invalid Matroska element size.");

            long elementEnd = unknownSize ? end : stream.Position + (long)size;
            yield return (id, elementEnd);
            stream.Position = elementEnd;
        }
    }

    private static ulong ReadVariableInteger(Stream stream, bool keepMarker, out int width)
    {
        uint first = ReadByte(stream);
        uint marker = 0x80;
        width = 1;
        while (marker != 0 && (first & marker) == 0)
        {
            marker >>= 1;
            width++;
        }
        if (marker == 0 || (keepMarker && width > sizeof(uint)))
            throw new InvalidDataException("Invalid Matroska element header.");

        ulong value = keepMarker ? first : first & (marker - 1);
        for (int i = 1; i < width; i++)
            value = (value << 8) | ReadByte(stream);
        return value;
    }

    private static string? ReadText(Stream stream, long end)
    {
        long length = end - stream.Position;
        if (length is <= 0 or > 256)
            return null;

        Span<byte> bytes = stackalloc byte[(int)length];
        stream.ReadExactly(bytes);
        string text = Encoding.UTF8.GetString(bytes).TrimEnd('\0').Trim();
        return text.Length > 0 ? text : null;
    }

    private static uint ReadByte(Stream stream)
    {
        int value = stream.ReadByte();
        return value >= 0 ? (uint)value : throw new EndOfStreamException();
    }
}
