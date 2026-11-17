/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using FufuLauncher.Constants;
using FufuLauncher.Helpers;
using FufuLauncher.Services.PluginMirror;

namespace FufuLauncher.Services.GameServer;

internal enum CutsceneSubtitleExportMode
{
    None,
    SoftText,
    BurnIn
}

internal sealed record CutsceneSubtitleCue(TimeSpan Start, TimeSpan End, string Text);

internal sealed record CutsceneExtractedSubtitleTrack(
    CutsceneSubtitleTrack Track,
    string FilePath,
    IReadOnlyList<CutsceneSubtitleCue> Cues);

internal static partial class CutsceneMediaService
{
    public const string DecoderFileName = "charlotte-cli.exe";
    public const string FfmpegFileName = "ffmpeg.exe";
    private const string CharlotteLatestApiUrl = "https://api.github.com/repos/The-Steambird/charlotte/releases/latest";
    private const string CharlotteLatestPageUrl = "https://github.com/The-Steambird/charlotte/releases/latest";

    private static readonly HttpClient ToolDownloadClient = CreateToolClient();

    private static HttpClient CreateToolClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "FufuLauncher-MediaSetup/1.0");
        return client;
    }

    public static string ManagedToolsDirectory => Path.Combine(AppPaths.DataDir, "Tools", "Charlotte");
    public static string PreviewCacheRoot => Path.Combine(AppPaths.CacheDir, "CutscenePreview");

    public static bool IsToolPath(string? path, string fileName) => Path.IsPathFullyQualified(path ?? string.Empty)
        && string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)
        && File.Exists(path);

    public static string? ResolveToolPath(string? configuredPath, string fileName, string? companionToolPath = null)
    {
        if (IsToolPath(configuredPath, fileName))
            return configuredPath;

        if (!string.IsNullOrEmpty(companionToolPath))
        {
            string? companionDir = Path.GetDirectoryName(companionToolPath);
            if (!string.IsNullOrEmpty(companionDir))
            {
                string sibling = Path.Combine(companionDir, fileName);
                if (IsToolPath(sibling, fileName))
                    return sibling;
            }
        }

        string? managed = FindFileInDirectory(ManagedToolsDirectory, fileName);
        if (managed is not null)
            return managed;

        string? appLocal = FindFileInDirectory(AppContext.BaseDirectory, fileName)
            ?? FindFileInDirectory(Path.Combine(AppContext.BaseDirectory, "Tools"), fileName);
        if (appLocal is not null)
            return appLocal;

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string segment in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    string candidate = Path.Combine(segment.Trim('"'), fileName);
                    if (IsToolPath(candidate, fileName))
                        return candidate;
                }
                catch
                {
                }
            }
        }

        return null;
    }

    private static string? FindFileInDirectory(string directory, string fileName)
    {
        try
        {
            if (!Directory.Exists(directory))
                return null;
            string direct = Path.Combine(directory, fileName);
            if (IsToolPath(direct, fileName))
                return direct;
            return Directory.EnumerateFiles(directory, fileName, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = 2,
                MatchCasing = MatchCasing.CaseInsensitive
            }).FirstOrDefault(p => IsToolPath(p, fileName));
        }
        catch
        {
            return null;
        }
    }

    public static async Task<(string DecoderPath, string? FfmpegPath)> DownloadAndInstallCharlotteAsync(
        IProgress<string>? status, CancellationToken token)
    {
        status?.Report("Cutscene_ToolFetchingRelease".GetLocalized());
        string downloadUrl = await ResolveCharlotteZipUrlAsync(token).ConfigureAwait(false);

        Directory.CreateDirectory(ManagedToolsDirectory);
        string tempZip = Path.Combine(Path.GetTempPath(), $"fufu-charlotte-{Guid.NewGuid():N}.zip");
        string stagingDir = Path.Combine(Path.GetDirectoryName(ManagedToolsDirectory)!, $"Charlotte-staging-{Guid.NewGuid():N}");

        try
        {
            status?.Report("Cutscene_ToolDownloading".GetLocalized());
            await DownloadWithMirrorsAsync(downloadUrl, tempZip, token).ConfigureAwait(false);

            status?.Report("Cutscene_ToolExtracting".GetLocalized());
            Directory.CreateDirectory(stagingDir);
            string stagingRoot = Path.GetFullPath(stagingDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (ZipArchive archive = ZipFile.OpenRead(tempZip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string destination = Path.GetFullPath(Path.Combine(stagingDir, entry.FullName));
                    if (!destination.StartsWith(stagingRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Invalid archive entry path.");

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    entry.ExtractToFile(destination, overwrite: true);
                }
            }

            string? decoder = FindFileInDirectory(stagingDir, DecoderFileName);
            if (decoder is null)
                throw new InvalidOperationException(string.Format("Cutscene_InvalidDecoder".GetLocalized(), DecoderFileName));

            if (Directory.Exists(ManagedToolsDirectory))
                Directory.Delete(ManagedToolsDirectory, recursive: true);
            Directory.Move(stagingDir, ManagedToolsDirectory);

            string? finalDecoder = FindFileInDirectory(ManagedToolsDirectory, DecoderFileName)
                ?? throw new InvalidOperationException(string.Format("Cutscene_InvalidDecoder".GetLocalized(), DecoderFileName));
            string? finalFfmpeg = ResolveToolPath(null, FfmpegFileName, finalDecoder);
            return (finalDecoder, finalFfmpeg);
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); } catch { }
        }
    }

    private static async Task<string> ResolveCharlotteZipUrlAsync(CancellationToken token)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await ToolDownloadClient.GetAsync(CharlotteLatestApiUrl, timeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                        string? url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url)
                            && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                            && !name.Contains("source", StringComparison.OrdinalIgnoreCase))
                        {
                            return url;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
        }

        // Fallback: follow /releases/latest redirect (works via mirrors too)
        foreach (string candidate in BuildMirrorCandidates(CharlotteLatestPageUrl))
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                using var req = new HttpRequestMessage(HttpMethod.Head, candidate);
                using var resp = await ToolDownloadClient.SendAsync(req, timeout.Token).ConfigureAwait(false);
                string finalUri = resp.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
                int tagIdx = finalUri.IndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase);
                if (tagIdx >= 0)
                {
                    string tag = finalUri[(tagIdx + "/releases/tag/".Length)..].Trim('/');
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        string ver = tag.TrimStart('v', 'V');
                        return $"https://github.com/The-Steambird/charlotte/releases/download/{tag}/charlotte-{ver}.zip";
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }

        throw new InvalidOperationException("Cutscene_ToolDownloadFailed".GetLocalized());
    }

    private static async Task DownloadWithMirrorsAsync(string originalUrl, string destinationPath, CancellationToken token)
    {
        Exception? lastError = null;
        foreach (string url in BuildMirrorCandidates(originalUrl))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(4));
                using var response = await ToolDownloadClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None,
                    128 * 1024, FileOptions.Asynchronous);
                await input.CopyToAsync(output, timeout.Token).ConfigureAwait(false);
                if (new FileInfo(destinationPath).Length > 0)
                    return;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("Cutscene_ToolDownloadFailed".GetLocalized(), lastError);
    }

    private static IReadOnlyList<string> BuildMirrorCandidates(string originalUrl)
    {
        var list = new List<string> { originalUrl };
        try
        {
            string ghProxyHost = new Uri(ApiEndpoints.GhProxyBase).Host;
            list.Add(ApiEndpoints.BuildMirrorUrl(ghProxyHost, originalUrl));
            var config = new MirrorSiteProvider().LoadConfig();
            foreach (string domain in config.Mirrors.Take(6))
            {
                if (!string.IsNullOrWhiteSpace(domain))
                {
                    string mirrored = ApiEndpoints.BuildMirrorUrl(domain.Trim(), originalUrl);
                    if (!list.Contains(mirrored, StringComparer.OrdinalIgnoreCase))
                        list.Add(mirrored);
                }
            }
        }
        catch
        {
        }
        return list;
    }

    public static string GetPreviewCacheDirectory(string assetMd5)
    {
        string key = string.IsNullOrWhiteSpace(assetMd5) ? "temp" : assetMd5.ToLowerInvariant();
        string dir = Path.Combine(PreviewCacheRoot, key);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static long ClearPreviewCache()
    {
        if (!Directory.Exists(PreviewCacheRoot))
            return 0;

        long freed = 0;
        foreach (string file in Directory.EnumerateFiles(PreviewCacheRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                freed += new FileInfo(file).Length;
                File.Delete(file);
            }
            catch
            {
            }
        }
        try { Directory.Delete(PreviewCacheRoot, recursive: true); } catch { }
        return freed;
    }

    public static async Task<string> DecodeAsync(string decoder, string input, string workspace,
        CancellationToken token)
    {
        string expectedOutput = Path.Combine(workspace, Path.GetFileNameWithoutExtension(input) + ".mkv");
        var existing = new FileInfo(expectedOutput);
        if (existing.Exists && existing.Length > 0 && !existing.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return expectedOutput;

        string language = ResourceExtensions.CurrentCulture?.Split('-')[0] ?? "zh";
        string audio = language is "zh" or "ja" or "ko" ? language : "en";
        string subtitles = audio switch
        {
            "zh" => ResourceExtensions.CurrentCulture == "zh-TW" ? "cht" : "chs",
            "ja" => "jp",
            "ko" => "kr",
            _ => "en"
        };
        int exitCode = await RunToolAsync(decoder, workspace,
            [input, "--output", workspace, "--flat", "--audio-codec", "flac",
                "--default-audio", audio, "--default-sub", subtitles], token).ConfigureAwait(false);

        var file = new FileInfo(expectedOutput);
        if (exitCode != 0 || !file.Exists || file.Length == 0
            || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Cutscene_DecodeFailed".GetLocalized());
        return expectedOutput;
    }

    public static async Task<IReadOnlyList<CutsceneExtractedSubtitleTrack>> ExtractSubtitlesAsync(
        string? ffmpeg, string mkvPath, IReadOnlyList<CutsceneSubtitleTrack> tracks, string workspace,
        CancellationToken token)
    {
        if (ffmpeg is null || tracks.Count == 0)
            return [];

        string subsDir = Path.Combine(workspace, "subs");
        Directory.CreateDirectory(subsDir);

        var extracted = new List<CutsceneExtractedSubtitleTrack>();
        var pendingTracks = new List<(CutsceneSubtitleTrack Track, string Path)>();
        var args = new List<string> { "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", mkvPath };

        foreach (CutsceneSubtitleTrack track in tracks)
        {
            string ext = (track.CodecId?.Contains("UTF8", StringComparison.OrdinalIgnoreCase) == true
                || track.CodecId?.Contains("SRT", StringComparison.OrdinalIgnoreCase) == true) ? ".srt" : ".ass";
            string outPath = Path.Combine(subsDir, $"sub_{track.Index}{ext}");
            if (File.Exists(outPath) && new FileInfo(outPath).Length > 0)
            {
                extracted.Add(new CutsceneExtractedSubtitleTrack(track, outPath, ParseSubtitleFile(outPath)));
                continue;
            }
            args.AddRange(["-map", $"0:s:{track.Index}", "-c:s", "copy", outPath]);
            pendingTracks.Add((track, outPath));
        }

        if (pendingTracks.Count > 0)
        {
            int exitCode = await RunToolAsync(ffmpeg, subsDir, args, token).ConfigureAwait(false);
            if (exitCode == 0)
            {
                foreach (var (track, path) in pendingTracks)
                {
                    if (File.Exists(path) && new FileInfo(path).Length > 0)
                        extracted.Add(new CutsceneExtractedSubtitleTrack(track, path, ParseSubtitleFile(path)));
                }
            }
        }

        return extracted.OrderBy(item => item.Track.Index).ToArray();
    }

    private static IReadOnlyList<CutsceneSubtitleCue> ParseSubtitleFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string[] lines = File.ReadAllLines(path);
        var cues = ext == ".srt" ? ParseSrtLines(lines) : ParseAssLines(lines);
        return cues.OrderBy(c => c.Start).ThenBy(c => c.End).ToArray();
    }

    private static List<CutsceneSubtitleCue> ParseAssLines(string[] lines)
    {
        var cues = new List<CutsceneSubtitleCue>();
        int startField = 1;
        int endField = 2;
        int textField = 9;
        bool inEvents = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith('['))
            {
                inEvents = line.Equals("[Events]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inEvents)
                continue;

            if (line.StartsWith("Format:", StringComparison.OrdinalIgnoreCase))
            {
                string[] cols = line["Format:".Length..].Split(',', StringSplitOptions.TrimEntries);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i].Equals("Start", StringComparison.OrdinalIgnoreCase)) startField = i;
                    else if (cols[i].Equals("End", StringComparison.OrdinalIgnoreCase)) endField = i;
                    else if (cols[i].Equals("Text", StringComparison.OrdinalIgnoreCase)) textField = i;
                }
                continue;
            }

            if (!line.StartsWith("Dialogue:", StringComparison.OrdinalIgnoreCase))
                continue;

            string body = line["Dialogue:".Length..].TrimStart();
            string[] parts = body.Split(',', textField + 1);
            if (parts.Length <= textField)
                continue;

            if (!TryParseAssTime(parts[startField].Trim(), out TimeSpan start)
                || !TryParseAssTime(parts[endField].Trim(), out TimeSpan end))
                continue;

            string text = CleanAssText(parts[textField]);
            if (text.Length > 0)
                cues.Add(new CutsceneSubtitleCue(start, end, text));
        }
        return cues;
    }

    private static List<CutsceneSubtitleCue> ParseSrtLines(string[] lines)
    {
        var cues = new List<CutsceneSubtitleCue>();
        int i = 0;
        while (i < lines.Length)
        {
            string line = lines[i].Trim();
            if (line.Contains("-->", StringComparison.Ordinal))
            {
                string[] bounds = line.Split("-->", StringSplitOptions.TrimEntries);
                if (bounds.Length == 2
                    && TimeSpan.TryParse(bounds[0].Replace(',', '.'), CultureInfo.InvariantCulture, out TimeSpan start)
                    && TimeSpan.TryParse(bounds[1].Replace(',', '.'), CultureInfo.InvariantCulture, out TimeSpan end))
                {
                    var textLines = new List<string>();
                    i++;
                    while (i < lines.Length && !string.IsNullOrWhiteSpace(lines[i]))
                    {
                        textLines.Add(HtmlTagRegex().Replace(lines[i].Trim(), string.Empty));
                        i++;
                    }
                    string combined = string.Join("\n", textLines).Trim();
                    if (combined.Length > 0)
                        cues.Add(new CutsceneSubtitleCue(start, end, combined));
                }
            }
            i++;
        }
        return cues;
    }

    private static bool TryParseAssTime(string value, out TimeSpan result)
    {
        // ASS time format: H:MM:SS.cc (centiseconds)
        string[] parts = value.Split(':');
        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes)
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
        {
            result = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
            return true;
        }
        result = TimeSpan.Zero;
        return false;
    }

    private static string CleanAssText(string raw)
    {
        string stripped = AssTagRegex().Replace(raw, string.Empty);
        stripped = stripped.Replace("\\N", "\n", StringComparison.Ordinal)
                           .Replace("\\n", "\n", StringComparison.Ordinal)
                           .Replace("\\h", " ", StringComparison.Ordinal);
        return stripped.Trim();
    }

    public static async Task ExportRawUsmAsync(string usmSourcePath, string destination, CancellationToken token)
    {
        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $".fufu-cutscene-{Guid.NewGuid():N}.usm");
        try
        {
            await using (var input = new FileStream(usmSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous))
            {
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            if (new FileInfo(temporary).Length == 0)
                throw new IOException("Cutscene_ExportFailed".GetLocalized());
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public static Task ExportAsync(string video, string destination, string? ffmpeg,
        IReadOnlyList<int>? audioTracks, CancellationToken token) =>
        ExportAsync(video, destination, ffmpeg, audioTracks, CutsceneSubtitleExportMode.None, null, token);

    public static async Task ExportAsync(string video, string destination, string? ffmpeg,
        IReadOnlyList<int>? audioTracks, CutsceneSubtitleExportMode subtitleMode, int? subtitleTrackIndex,
        CancellationToken token)
    {
        string extension = Path.GetExtension(destination).ToLowerInvariant();
        if (extension is not (".mkv" or ".mp4"))
            throw new InvalidOperationException("Cutscene_InvalidExportFormat".GetLocalized());

        string directory = Path.GetDirectoryName(destination)!;
        Directory.CreateDirectory(directory);
        string name = $".fufu-cutscene-{Guid.NewGuid():N}{extension}";
        string temporary = Path.Combine(directory, name);
        try
        {
            if (extension == ".mkv" && audioTracks is null)
            {
                await using var input = File.OpenRead(video);
                await using var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    128 * 1024, FileOptions.Asynchronous);
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            }
            else
            {
                if (ffmpeg is null)
                    throw new InvalidOperationException("Cutscene_Mp4Unavailable".GetLocalized());
                await ConvertAsync(ffmpeg, video, temporary, extension == ".mp4", audioTracks, subtitleMode,
                        subtitleTrackIndex, token)
                    .ConfigureAwait(false);
            }

            token.ThrowIfCancellationRequested();
            if (new FileInfo(temporary).Length == 0)
                throw new IOException("Cutscene_ExportFailed".GetLocalized());
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }

    public static Task ConvertToMp4Async(string ffmpeg, string video, string destination, CancellationToken token) =>
        ConvertAsync(ffmpeg, video, destination, mp4: true, audioTracks: null, CutsceneSubtitleExportMode.None, null, token);

    private static async Task ConvertAsync(string ffmpeg, string video, string destination, bool mp4,
        IReadOnlyList<int>? audioTracks, CutsceneSubtitleExportMode subtitleMode, int? subtitleTrackIndex,
        CancellationToken token)
    {
        string workingDir = Path.GetDirectoryName(video)!;
        string? tempBurnSubPath = null;
        try
        {
            List<string> arguments = ["-nostdin", "-hide_banner", "-loglevel", "error", "-xerror", "-y", "-i", video,
                "-map", mp4 ? "0:v:0" : "0:v"];
            if (audioTracks is null)
            {
                arguments.AddRange(["-map", "0:a?"]);
            }
            else
            {
                foreach (int index in audioTracks)
                    arguments.AddRange(["-map", "0:a:" + index.ToString(CultureInfo.InvariantCulture)]);
            }

            if (mp4)
            {
                if (subtitleMode == CutsceneSubtitleExportMode.BurnIn && subtitleTrackIndex is int subIdx)
                {
                    string burnFile = $"burn_{Guid.NewGuid():N}.ass";
                    tempBurnSubPath = Path.Combine(workingDir, burnFile);
                    int subExit = await RunToolAsync(ffmpeg, workingDir,
                        ["-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", video,
                            "-map", $"0:s:{subIdx}", "-c:s", "copy", tempBurnSubPath], token).ConfigureAwait(false);
                    if (subExit == 0 && File.Exists(tempBurnSubPath) && new FileInfo(tempBurnSubPath).Length > 0)
                    {
                        // Run inside workingDir using a relative file name to avoid Windows drive colon escaping issues in libass.
                        arguments.AddRange(["-vf", $"ass={burnFile}"]);
                    }
                }
                else if (subtitleMode == CutsceneSubtitleExportMode.SoftText && subtitleTrackIndex is int softIdx)
                {
                    arguments.AddRange(["-map", $"0:s:{softIdx}", "-c:s", "mov_text"]);
                }

                arguments.AddRange(["-c:v", "libx264", "-preset", "veryfast", "-crf", "20", "-pix_fmt", "yuv420p",
                    "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]);
            }
            else
            {
                arguments.AddRange(["-map", "0:s?", "-map", "0:t?", "-c", "copy"]);
            }
            arguments.Add(destination);

            int exitCode = await RunToolAsync(ffmpeg, workingDir, arguments, token).ConfigureAwait(false);
            if (exitCode != 0)
                throw new InvalidOperationException((mp4 ? "Cutscene_Mp4Unavailable" : "Cutscene_ExportFailed").GetLocalized());
        }
        finally
        {
            if (tempBurnSubPath is not null)
            {
                try { if (File.Exists(tempBurnSubPath)) File.Delete(tempBurnSubPath); } catch { }
            }
        }
    }

    private static async Task<int> RunToolAsync(string executable, string workspace, IReadOnlyList<string> arguments,
        CancellationToken token)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workspace,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        token.ThrowIfCancellationRequested();
        using Process process = Process.Start(startInfo) ?? throw new IOException("Could not start media converter.");
        process.StandardInput.Close();
        Task output = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        Task errors = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) when (process.HasExited)
            {
            }
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            await Task.WhenAll(output, errors).ConfigureAwait(false);
        }
        return process.ExitCode;
    }

    [GeneratedRegex(@"\{[^}]*\}", RegexOptions.CultureInvariant)]
    private static partial Regex AssTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex HtmlTagRegex();
}
