/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models.GameServer;
using FufuLauncher.Services.GameServer;
using ProtoBuf;
using ZstdSharp;

namespace FufuLauncher.Services
{
    [ProtoContract]
    public class Manifest
    {
        [ProtoMember(1)]
        public List<FileEntry> Files
        {
            get;
            set;
        } = new List<FileEntry>();
    }

    [ProtoContract]
    public class FileEntry
    {
        [ProtoMember(1)]
        public string Path
        {
            get;
            set;
        } = string.Empty;

        [ProtoMember(2)]
        public List<Chunk> Chunks
        {
            get;
            set;
        } = new List<Chunk>();

        [ProtoMember(3)]
        public bool IsFolder
        {
            get;
            set;
        }

        [ProtoMember(4)]
        public long Size
        {
            get;
            set;
        }

        [ProtoMember(5)]
        public string Checksum
        {
            get;
            set;
        } = string.Empty;
    }

    [ProtoContract]
    public class Chunk
    {
        [ProtoMember(1)]
        public string Id
        {
            get;
            set;
        } = string.Empty;

        [ProtoMember(2)]
        public string Checksum
        {
            get;
            set;
        } = string.Empty;

        [ProtoMember(3)]
        public long Offset
        {
            get;
            set;
        }

        [ProtoMember(4)]
        public int CompressedSize
        {
            get;
            set;
        }

        [ProtoMember(5)]
        public int UncompressedSize
        {
            get;
            set;
        }
    }

    public class GenshinDownloader
    {
        private readonly SophonBuildClient _sophonBuildClient;
        private readonly ChunkDownloader _chunkDownloader;
        private readonly GameServerScheme _scheme;
        private long _lastReportTicks = 0;

        public event Action<string>? Log;
        public event Action<long, long, int, int>? ProgressChanged;
        public event Action<string>? ErrorOccurred;

        public GenshinDownloader(SophonBuildClient sophonBuildClient, ChunkDownloader chunkDownloader,
            GameServerScheme scheme)
        {
            _sophonBuildClient = sophonBuildClient;
            _chunkDownloader = chunkDownloader;
            _scheme = scheme;
        }

        public async Task StartDownloadAsync(string installPath, string lang, bool downloadBaseGame, int maxThreads,
            CancellationToken token, GameServerDownloadMonitor? downloadMonitor = null)
        {
            try
            {
                using var operation = await GameServerOperation.EnterAsync(installPath, token, requireClosed: false)
                    .ConfigureAwait(false);
                Log?.Invoke("Download_Connecting".GetLocalized());

                using JsonDocument buildDoc = await _sophonBuildClient.GetBuildDocumentAsync(_scheme, false, token)
                    .ConfigureAwait(false);
                var dataProp = buildDoc.RootElement.GetProperty("data");
                var manifestsProp = dataProp.GetProperty("manifests");
                string versionTag = dataProp.GetProperty("tag").GetString()!;

                var targetAssets = new List<string>();
                if (downloadBaseGame) targetAssets.Add("game");
                else Log?.Invoke("Download_VoiceOnly".GetLocalized());

                targetAssets.Add(lang);

                var filesToProcess = new ConcurrentBag<(FileEntry File, string UrlPrefix)>();

                foreach (var asset in targetAssets)
                {
                    JsonElement config = default;
                    foreach (var manifestElement in manifestsProp.EnumerateArray())
                    {
                        if (manifestElement.GetProperty("matching_field").GetString() == asset)
                        {
                            config = manifestElement;
                            break;
                        }
                    }

                    if (config.ValueKind == JsonValueKind.Undefined) continue;

                    string mId = config.GetProperty("manifest").GetProperty("id").GetString()!;
                    string mChecksum =
                        config.GetProperty("manifest").TryGetProperty("checksum", out var checksumProp) &&
                        checksumProp.ValueKind == JsonValueKind.String
                            ? checksumProp.GetString()!
                            : string.Empty;
                    string mDownloadPrefix =
                        config.GetProperty("manifest_download").GetProperty("url_prefix").GetString()!;
                    string chunkDownloadPrefix =
                        config.GetProperty("chunk_download").GetProperty("url_prefix").GetString()!;

                    Log?.Invoke(string.Format("Download_FetchingManifest".GetLocalized(), asset));

                    byte[] manifestBytes = await _sophonBuildClient
                        .DownloadAndDecompressAsync($"{mDownloadPrefix}/{mId}", mChecksum, token).ConfigureAwait(false);

                    using var ms = new MemoryStream(manifestBytes);
                    var protoManifest = Serializer.Deserialize<Manifest>(ms);
                    foreach (var f in protoManifest.Files) filesToProcess.Add((f, chunkDownloadPrefix));
                }

                int totalFiles = filesToProcess.Count;
                long totalBytes = filesToProcess.Sum(f => f.File.Size);
                int processedFiles = 0;
                long processedBytes = 0;
                var failedFiles = new ConcurrentBag<string>();

                Log?.Invoke(string.Format("Download_TaskStart".GetLocalized(), totalFiles, FormatSize(totalBytes)));

                string stagingPath = Path.Combine(installPath, "staging");
                if (!Directory.Exists(stagingPath)) Directory.CreateDirectory(stagingPath);

                var parallelOptions = new ParallelOptions
                    { MaxDegreeOfParallelism = maxThreads, CancellationToken = token };
                Action<long>? onBytesTransferred = downloadMonitor is null ? null : downloadMonitor.AddBytes;

                await Parallel.ForEachAsync(filesToProcess, parallelOptions, async (item, ct) =>
                {
                    if (item.File.IsFolder) return;

                    string localPath = Path.Combine(stagingPath, item.File.Path);

                    Action<int> onChunkWritten = (size) =>
                    {
                        long current = Interlocked.Add(ref processedBytes, size);
                        ReportProgress(current, totalBytes, processedFiles, totalFiles);
                    };

                    bool success = await ProcessFileAsync(item.File, item.UrlPrefix, localPath, onChunkWritten,
                        onBytesTransferred, ct);

                    if (!success)
                    {
                        failedFiles.Add(item.File.Path);
                        Log?.Invoke(string.Format("Download_FileUnfixable".GetLocalized(), item.File.Path));
                    }

                    Interlocked.Increment(ref processedFiles);
                    ReportProgress(Interlocked.Read(ref processedBytes), totalBytes, processedFiles, totalFiles,
                        force: true);
                });

                if (!failedFiles.IsEmpty)
                {
                    throw new InvalidOperationException(string.Format("Download_FileFailed".GetLocalized(),
                        failedFiles.Count));
                }

                Log?.Invoke("Download_MovingFiles".GetLocalized());
                MoveFilesRecursively(new DirectoryInfo(stagingPath), new DirectoryInfo(installPath));
                try
                {
                    Directory.Delete(stagingPath, true);
                }
                catch
                {
                }

                string gidVerPath = Path.Combine(installPath, "gid_ver");
                string configPath = Path.Combine(installPath, "config.ini");

                await File.WriteAllTextAsync(gidVerPath, versionTag, token);

                if (File.Exists(configPath))
                {
                    var iniFile = new Helpers.IniFile(configPath);
                    iniFile.WriteValue("General", "game_version", versionTag);
                }
                else
                {
                    string configContent =
                        $"[General]\ngame_version={versionTag}\nchannel={(int)_scheme.Channel}\nsub_channel={(int)_scheme.SubChannel}\ncps={_scheme.Cps}\n";
                    await File.WriteAllTextAsync(configPath, configContent, token);
                }

                Log?.Invoke("Download_AllDone".GetLocalized());
            }
            catch (OperationCanceledException)
            {
                Log?.Invoke("Download_UserCancelled".GetLocalized());
                throw;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
                throw;
            }
        }

        private async Task<bool> ProcessFileAsync(FileEntry file, string urlPrefix, string localPath,
            Action<int> onProgress, Action<long>? onBytesTransferred, CancellationToken token)
        {
            try
            {
                string? dir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(localPath))
                {
                    var info = new FileInfo(localPath);
                    if (info.Length == file.Size)
                    {
                        string localMd5 = await ComputeFileMd5Async(localPath, token);
                        if (localMd5.Equals(file.Checksum, StringComparison.OrdinalIgnoreCase))
                        {
                            onProgress?.Invoke((int)file.Size);
                            return true;
                        }
                    }

                    File.Delete(localPath);
                }

                using (var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    foreach (var chunk in file.Chunks)
                    {
                        token.ThrowIfCancellationRequested();

                        long written = await DownloadAndDecompressChunkAsync($"{urlPrefix}/{chunk.Id}", fs,
                            chunk.CompressedSize, onBytesTransferred, token);
                        onProgress?.Invoke((int)written);
                    }
                }

                string finalMd5 = await ComputeFileMd5Async(localPath, token);
                if (!finalMd5.Equals(file.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    Log?.Invoke(string.Format("Download_VerifyFailed".GetLocalized(), file.Path, file.Checksum,
                        finalMd5));
                    if (File.Exists(localPath)) File.Delete(localPath);
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log?.Invoke(string.Format("Download_FileException".GetLocalized(), file.Path, ex.Message));
                return false;
            }
        }

        private async Task<long> DownloadAndDecompressChunkAsync(string url, Stream target, int compressedSize,
            Action<long>? onBytesTransferred, CancellationToken token)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.zst");
            try
            {
                await _chunkDownloader.DownloadFileAsync(url, tempPath, compressedSize, null, token, onBytesTransferred)
                    .ConfigureAwait(false);

                using var compressedStream = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None,
                    ChunkDownloader.BufferSize, true);
                using var decompressor = new DecompressionStream(compressedStream);

                byte[] buffer = new byte[ChunkDownloader.BufferSize];
                long total = 0;
                while (true)
                {
                    int read = await decompressor.ReadAsync(buffer, token).ConfigureAwait(false);
                    if (read <= 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                    total += read;
                }

                return total;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        private async Task<string> ComputeFileMd5Async(string filePath, CancellationToken token)
        {
            using var md5 = MD5.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            byte[] hash = await md5.ComputeHashAsync(stream, token);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        private void ReportProgress(long downloaded, long total, int filesDone, int filesTotal, bool force = false)
        {
            long now = DateTime.UtcNow.Ticks;
            if (force || (now - _lastReportTicks) > 1000000)
            {
                _lastReportTicks = now;
                ProgressChanged?.Invoke(downloaded, total, filesDone, filesTotal);
            }
        }

        private void MoveFilesRecursively(DirectoryInfo source, DirectoryInfo target)
        {
            if (!target.Exists) target.Create();
            foreach (var file in source.GetFiles())
            {
                string targetPath = Path.Combine(target.FullName, file.Name);
                if (File.Exists(targetPath)) File.Delete(targetPath);
                file.MoveTo(targetPath);
            }

            foreach (var dir in source.GetDirectories())
            {
                MoveFilesRecursively(dir, target.CreateSubdirectory(dir.Name));
            }
        }

        private string FormatSize(long bytes) => $"{bytes / 1024.0 / 1024.0:F2} MB";
    }
}