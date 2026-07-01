/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using ProtoBuf;
using ZstdSharp;

namespace FufuLauncher.Services
{

    [ProtoContract]
    public class Manifest
    {
        [ProtoMember(1)]
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
    }

    [ProtoContract]
    public class FileEntry
    {
        [ProtoMember(1)]
        public string Path { get; set; }
        [ProtoMember(2)]
        public List<Chunk> Chunks { get; set; } = new List<Chunk>();
        [ProtoMember(3)]
        public bool IsFolder { get; set; }
        [ProtoMember(4)]
        public long Size { get; set; }
        [ProtoMember(5)]
        public string Checksum { get; set; }
    }

    [ProtoContract]
    public class Chunk
    {
        [ProtoMember(1)]
        public string Id { get; set; }
        [ProtoMember(2)]
        public string Checksum { get; set; }
        [ProtoMember(3)]
        public long Offset { get; set; }
        [ProtoMember(4)]
        public int CompressedSize { get; set; }
        [ProtoMember(5)]
        public int UncompressedSize { get; set; }
    }

    public class GenshinDownloader
    {
        private const string BuildApiUrl = "https://api-takumi.mihoyo.com/downloader/sophon_chunk/api/getBuild?branch=main&package_id=8xfMve0uwQ&password=CW8GbLNU8f&plat_app=ddxf5qt290cg";
        private readonly HttpClient _httpClient;
        private long _lastReportTicks = 0;

        public event Action<string> Log;
        public event Action<long, long, int, int> ProgressChanged;
        public event Action<string> ErrorOccurred;

        public GenshinDownloader()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        }

        public async Task StartDownloadAsync(string installPath, string lang, bool downloadBaseGame, int maxThreads, CancellationToken token)
        {
            try
            {
                Log?.Invoke("正在连接米哈游服务器...");
                var buildJson = await GetJsonAsync(BuildApiUrl, token);
                var manifests = buildJson["data"]["manifests"];
                string versionTag = buildJson["data"]["tag"].ToString();

                var targetAssets = new List<string>();
                if (downloadBaseGame) targetAssets.Add("game");
                else Log?.Invoke(">> 跳过游戏本体，仅下载语音包模式");

                targetAssets.Add(lang);

                var filesToProcess = new ConcurrentBag<(FileEntry File, string UrlPrefix)>();

                foreach (var asset in targetAssets)
                {
                    var config = manifests.FirstOrDefault(m => m["matching_field"]?.ToString() == asset);
                    if (config == null) continue;

                    string mId = config["manifest"]["id"].ToString();
                    string mChecksum = config["manifest"]["checksum"].ToString();
                    string mDownloadPrefix = config["manifest_download"]["url_prefix"].ToString();
                    string chunkDownloadPrefix = config["chunk_download"]["url_prefix"].ToString();

                    Log?.Invoke($"正在获取清单文件: {asset}...");

                    byte[] manifestBytes = await DownloadAndDecompressManifestAsync(mDownloadPrefix, mId, mChecksum, token);
                    if (manifestBytes == null) throw new Exception($"清单下载失败: {asset}");

                    using var ms = new MemoryStream(manifestBytes);
                    var protoManifest = Serializer.Deserialize<Manifest>(ms);
                    foreach (var f in protoManifest.Files) filesToProcess.Add((f, chunkDownloadPrefix));
                }

                int totalFiles = filesToProcess.Count;
                long totalBytes = filesToProcess.Sum(f => f.File.Size);
                int processedFiles = 0;
                long processedBytes = 0;

                Log?.Invoke($"校验/下载任务开始。总计: {totalFiles} 文件, {FormatSize(totalBytes)}");

                string stagingPath = Path.Combine(installPath, "staging");
                if (!Directory.Exists(stagingPath)) Directory.CreateDirectory(stagingPath);

                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = maxThreads, CancellationToken = token };

                await Parallel.ForEachAsync(filesToProcess, parallelOptions, async (item, ct) =>
                {
                    if (item.File.IsFolder) return;

                    string localPath = Path.Combine(stagingPath, item.File.Path);

                    Action<int> onChunkWritten = (size) =>
                    {
                        long current = Interlocked.Add(ref processedBytes, size);
                        ReportProgress(current, totalBytes, processedFiles, totalFiles);
                    };

                    bool success = await ProcessFileAsync(item.File, item.UrlPrefix, localPath, onChunkWritten, ct);

                    if (!success)
                    {
                        Log?.Invoke($"文件无法修复或用户取消操作: {item.File.Path}");
                    }

                    Interlocked.Increment(ref processedFiles);
                    ReportProgress(Interlocked.Read(ref processedBytes), totalBytes, processedFiles, totalFiles, force: true);
                });

                Log?.Invoke("下载校验完毕，正在移动文件...");
                MoveFilesRecursively(new DirectoryInfo(stagingPath), new DirectoryInfo(installPath));
                try { Directory.Delete(stagingPath, true); } catch { }

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
                    string configContent = $"[General]\ngame_version={versionTag}\nchannel=1\nsub_channel=1\ncps=mihoyo\n";
                    await File.WriteAllTextAsync(configPath, configContent, token);
                }

                Log?.Invoke("全部完成！");
            }
            catch (OperationCanceledException) { Log?.Invoke("用户取消操作。"); throw; }
            catch (Exception ex) { ErrorOccurred?.Invoke(ex.Message); throw; }
        }

        private async Task<bool> ProcessFileAsync(FileEntry file, string urlPrefix, string localPath, Action<int> onProgress, CancellationToken token)
        {
            try
            {
                string dir = Path.GetDirectoryName(localPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

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

                        byte[] data = await DownloadChunkWithRetryAsync($"{urlPrefix}/{chunk.Id}", token);
                        if (data == null) return false;

                        await fs.WriteAsync(data, 0, data.Length, token);
                        onProgress?.Invoke(data.Length);
                    }
                }

                string finalMd5 = await ComputeFileMd5Async(localPath, token);
                if (!finalMd5.Equals(file.Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    Log?.Invoke($"[校验不通过] {file.Path} (服务器:{file.Checksum} vs 本地:{finalMd5})");
                    if (File.Exists(localPath)) File.Delete(localPath);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[文件处理异常] {file.Path}: {ex.Message}");
                return false;
            }
        }

        private async Task<byte[]> DownloadChunkWithRetryAsync(string url, CancellationToken token)
        {
            int retry = 5;
            while (retry > 0)
            {
                try
                {
                    byte[] compressed = await _httpClient.GetByteArrayAsync(url, token);
                    using var decompressor = new Decompressor();
                    return decompressor.Unwrap(compressed).ToArray();
                }
                catch (Exception)
                {
                    retry--;
                    if (retry == 0) return null;
                    await Task.Delay(1000, token);
                }
            }
            return null;
        }

        private async Task<byte[]> DownloadAndDecompressManifestAsync(string prefix, string id, string expectedMd5, CancellationToken token)
        {
            string url = $"{prefix}/{id}";
            int retry = 5;
            while (retry > 0)
            {
                try
                {
                    byte[] compressed = await _httpClient.GetByteArrayAsync(url, token);
                    using var decompressor = new Decompressor();
                    byte[] data = decompressor.Unwrap(compressed).ToArray();
                    if (ComputeMd5(data).Equals(expectedMd5, StringComparison.OrdinalIgnoreCase))
                        return data;

                    Log?.Invoke($"Manifest MD5校验失败，重试中...");
                }
                catch { }
                retry--;
                await Task.Delay(2000, token);
            }
            return null;
        }

        private async Task<string> ComputeFileMd5Async(string filePath, CancellationToken token)
        {
            using var md5 = MD5.Create();
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
            byte[] hash = await md5.ComputeHashAsync(stream, token);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        private string ComputeMd5(byte[] data)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(data);
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

        private async Task<JObject> GetJsonAsync(string url, CancellationToken token)
        {
            int retry = 3;
            while (retry > 0)
            {
                try
                {
                    var str = await _httpClient.GetStringAsync(url, token);
                    return JObject.Parse(str);
                }
                catch { retry--; await Task.Delay(1000); }
            }
            throw new Exception("无法连接到米哈游 API");
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

