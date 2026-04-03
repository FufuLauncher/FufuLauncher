using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Security.Cryptography;
using System.Text.Json;
using FufuLauncher.Protobuf;
using ZstdSharp;

namespace FufuLauncher.Views
{
    public sealed partial class AdvancedServerSwitchPage : Page
    {
        private string _gameDir = string.Empty;
        private Window _parentWindow;
        
        private ContentDialog _progressDialog;
        private TextBlock _statusText;

        public AdvancedServerSwitchPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is BlankPage.SwitchPageParams param)
            {
                _gameDir = param.GameDir;
                _parentWindow = param.ParentWindow;
            }
        }

        private async void StartBtn_Click(object sender, RoutedEventArgs e)
        {
            _statusText = new TextBlock { Text = "准备中...", TextWrapping = TextWrapping.Wrap };
            var sp = new StackPanel { Spacing = 16, Margin = new Thickness(0, 16, 0, 0) };
            sp.Children.Add(new ProgressBar { IsIndeterminate = true, HorizontalAlignment = HorizontalAlignment.Stretch });
            sp.Children.Add(_statusText);

            _progressDialog = new ContentDialog
            {
                Title = "正在切换服务器",
                Content = sp,
                XamlRoot = XamlRoot
            };
            
            _ = _progressDialog.ShowAsync();

            try
            {
                string cacheDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FufuLauncher", "ServerCache");
                var converter = new PackageConverter(_gameDir, cacheDir, UpdateProgressText);
                
                await Task.Run(() => converter.ExecuteConversionAsync());

                _progressDialog.Hide();
                
                var successDialog = new ContentDialog
                {
                    Title = "完成",
                    Content = $"当前已切换至：{converter.TargetServerName}", 
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                
                await successDialog.ShowAsync();
                
                _parentWindow?.Close();
            }
            catch (Exception ex)
            {
                _progressDialog.Hide();
                var errDialog = new ContentDialog
                {
                    Title = "转换失败",
                    Content = ex.Message,
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                await errDialog.ShowAsync();
            }
        }
        
        private void UpdateProgressText(string msg)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_statusText != null)
                {
                    _statusText.Text = msg; 
                }
            });
        }
    }
    
    public static class GameConstants
    {
        public const string CN_EXE = "YuanShen.exe";
        public const string OS_EXE = "GenshinImpact.exe";
        public const string CN_DATA_DIR = "YuanShen_Data";
        public const string OS_DATA_DIR = "GenshinImpact_Data";

        public const string CN_LAUNCHER_ID = "jGHBHlcOq1";
        public const string CN_GAME_ID = "1Z8W5NHUQb";
        public const string CN_API = "https://hyp-api.mihoyo.com/hyp/hyp-connect/api";
        public const string CN_SOPHON = "https://downloader-api.mihoyo.com/downloader/sophon_chunk/api";

        public const string OS_LAUNCHER_ID = "VYTpXlbWo8";
        public const string OS_GAME_ID = "gopR6Cufr3";
        public const string OS_API = "https://sg-hyp-api.hoyoverse.com/hyp/hyp-connect/api";
        public const string OS_SOPHON = "https://sg-downloader-api.hoyoverse.com/downloader/sophon_chunk/api";
    }

    public static class HashUtility
    {
        public static string Md5File(string filepath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filepath);
            var hashBytes = md5.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }

    public class SophonChunk
    {
        public string ChunkName { get; set; }
        public long ChunkSize { get; set; }
        public long ChunkDecompressedSize { get; set; }
        public long ChunkOffset { get; set; }
        public string DecompressedMd5 { get; set; }
        public string DownloadUrl { get; set; }

        public SophonChunk(string urlPrefix, string urlSuffix, AssetChunk assetChunk)
        {
            ChunkName = assetChunk.ChunkName ?? "";
            ChunkSize = assetChunk.ChunkSize;
            ChunkDecompressedSize = assetChunk.ChunkSizeDecompressed;
            ChunkOffset = assetChunk.ChunkOnFileOffset;
            DecompressedMd5 = (assetChunk.ChunkDecompressedHashMd5 ?? "").ToLowerInvariant();
            DownloadUrl = $"{urlPrefix}/{ChunkName}{urlSuffix}";
        }
    }

    public class AssemblyInstruction
    {
        public string Action { get; set; } 
        public AssetChunk TargetChunk { get; set; }
        public string LocalAssetName { get; set; }
        public AssetChunk LocalChunk { get; set; }

        public AssemblyInstruction(string action, AssetChunk targetChunk, string localAssetName = null, AssetChunk localChunk = null)
        {
            Action = action;
            TargetChunk = targetChunk;
            LocalAssetName = localAssetName;
            LocalChunk = localChunk;
        }
    }

    public class SophonAssetOperation
    {
        public AssetProperty Asset { get; set; }
        public string AssetName { get; set; }
        public string AssetMd5 { get; set; }
        public string UrlPrefix { get; set; }
        public string UrlSuffix { get; set; }
        public List<AssemblyInstruction> Instructions { get; set; } = new();
        public List<SophonChunk> DiffChunks { get; set; } = new();

        public SophonAssetOperation(AssetProperty asset, string urlPrefix, string urlSuffix)
        {
            Asset = asset;
            AssetName = asset.AssetName ?? "";
            AssetMd5 = (asset.AssetHashMd5 ?? "").ToLowerInvariant();
            UrlPrefix = urlPrefix;
            UrlSuffix = urlSuffix;
        }
    }

    public class OperationLists
    {
        public List<(string src, string dst)> Backup { get; set; } = new();
        public List<(string src, string dst)> Restore { get; set; } = new();
        public List<SophonAssetOperation> Assemble { get; set; } = new();
    }

    public class PackageConverter
    {
        private readonly string gameDir;
        private readonly string cacheDir;
        private readonly HttpClient httpClient;
        private readonly Action<string> print;

        private readonly string chunksDir;
        private readonly string targetDir;
        private readonly string backupCnDir;
        private readonly string backupOsDir;

        private readonly bool isCurrentlyCn;
        private readonly bool isCurrentlyOs;
        private readonly bool targetIsOversea;
        private readonly string backupLocalDir;
        private readonly string backupTargetDir;
        
        public string TargetServerName => targetIsOversea ? "国际服 (OS)" : "国服 (CN)";

        public PackageConverter(string gameDir, string cacheDir, Action<string> logger)
        {
            this.gameDir = gameDir;
            this.cacheDir = cacheDir;
            print = logger;
            httpClient = new HttpClient();

            chunksDir = Path.Combine(cacheDir, "Chunks");
            targetDir = Path.Combine(cacheDir, "Target");
            backupCnDir = Path.Combine(cacheDir, "Backup", "CN");
            backupOsDir = Path.Combine(cacheDir, "Backup", "OS");

            foreach (var d in new[] { chunksDir, targetDir, backupCnDir, backupOsDir }) Directory.CreateDirectory(d);
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
                Directory.CreateDirectory(targetDir);
            }

            isCurrentlyCn = File.Exists(Path.Combine(gameDir, GameConstants.CN_EXE));
            isCurrentlyOs = File.Exists(Path.Combine(gameDir, GameConstants.OS_EXE));

            if (isCurrentlyCn && isCurrentlyOs)
            {
                if (Directory.Exists(Path.Combine(gameDir, GameConstants.CN_DATA_DIR))) isCurrentlyOs = false;
                else isCurrentlyCn = false;
            }

            if (!isCurrentlyCn && !isCurrentlyOs) throw new Exception("找不到核心文件，请确认游戏路径！");

            targetIsOversea = isCurrentlyCn;
            backupLocalDir = isCurrentlyCn ? backupCnDir : backupOsDir;
            backupTargetDir = isCurrentlyCn ? backupOsDir : backupCnDir;
        }

        public async Task ExecuteConversionAsync()
        {
            print("正在请求网络分支");
            var localInfo = await GetBranchAndManifestUrlAsync(isCurrentlyOs);
            var targetInfo = await GetBranchAndManifestUrlAsync(targetIsOversea);

            print("正在下载并解析清单");
            var localManifest = await DownloadAndDecodeManifestAsync(localInfo.manifestUrl);
            var targetManifest = await DownloadAndDecodeManifestAsync(targetInfo.manifestUrl);

            print("正在比对");
            var ops = GenerateOperations(targetManifest, localManifest, targetInfo.chunkPrefix, targetInfo.chunkSuffix);

            print("正在下载所需的数据块");
            await DownloadDiffChunksAsync(ops.Assemble);

            print("正在组装文件");
            AssembleFiles(ops.Assemble);

            print("正在替换与备份回滚");
            ReplacePhysicalFiles(ops);

            print("清理临时数据");
        }

        private async Task<(string manifestUrl, string chunkPrefix, string chunkSuffix)> GetBranchAndManifestUrlAsync(bool isOversea)
        {
            string api = isOversea ? GameConstants.OS_API : GameConstants.CN_API;
            string launcherId = isOversea ? GameConstants.OS_LAUNCHER_ID : GameConstants.CN_LAUNCHER_ID;
            string gameId = isOversea ? GameConstants.OS_GAME_ID : GameConstants.CN_GAME_ID;

            string url = $"{api}/getGameBranches?launcher_id={launcherId}&game_ids[]={gameId}";
            var jsonResp = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(jsonResp);
            var mainBranch = doc.RootElement.GetProperty("data").GetProperty("game_branches")[0].GetProperty("main");

            string buildUrl = mainBranch.TryGetProperty("build_url", out var bUrl) && bUrl.ValueKind == JsonValueKind.String ? bUrl.GetString() : null;

            if (string.IsNullOrEmpty(buildUrl))
            {
                string sophonApi = isOversea ? GameConstants.OS_SOPHON : GameConstants.CN_SOPHON;
                string pkgId = mainBranch.GetProperty("package_id").GetString();
                string pwd = mainBranch.GetProperty("password").GetString();
                buildUrl = $"{sophonApi}/getBuild?branch=main&package_id={pkgId}&password={pwd}";
            }

            var buildJson = await httpClient.GetStringAsync(buildUrl);
            using var buildDoc = JsonDocument.Parse(buildJson);
            var manifestData = buildDoc.RootElement.GetProperty("data").GetProperty("manifests")[0];

            string manifestId = manifestData.GetProperty("manifest").GetProperty("id").GetString();
            string urlPrefix = manifestData.GetProperty("manifest_download").GetProperty("url_prefix").GetString();
            string urlSuffix = manifestData.GetProperty("manifest_download").TryGetProperty("url_suffix", out var sfx) ? sfx.GetString() : "";

            string chunkPrefix = manifestData.GetProperty("chunk_download").GetProperty("url_prefix").GetString();
            string chunkSuffix = manifestData.GetProperty("chunk_download").TryGetProperty("url_suffix", out var csfx) ? csfx.GetString() : "";

            return ($"{urlPrefix}/{manifestId}{urlSuffix}", chunkPrefix, chunkSuffix);
        }

        private async Task<SophonManifestProto> DownloadAndDecodeManifestAsync(string manifestUrl)
        {
            var bytes = await httpClient.GetByteArrayAsync(manifestUrl);
            using var compressedStream = new MemoryStream(bytes);
            using var decompressionStream = new DecompressionStream(compressedStream);
            using var ms = new MemoryStream();
            await decompressionStream.CopyToAsync(ms);
            ms.Position = 0;
            return SophonManifestProto.Parser.ParseFrom(ms);
        }

        private string NormalizePath(string path)
        {
            if (path.StartsWith(GameConstants.CN_DATA_DIR + "/")) return path.Replace(GameConstants.CN_DATA_DIR, "_Data");
            if (path.StartsWith(GameConstants.OS_DATA_DIR + "/")) return path.Replace(GameConstants.OS_DATA_DIR, "_Data");
            return path;
        }

        private OperationLists GenerateOperations(SophonManifestProto targetManifest, SophonManifestProto localManifest, string urlPrefix, string urlSuffix)
        {
            var ops = new OperationLists();
            var localAssetMap = new Dictionary<string, AssetProperty>();
            var localChunkMap = new Dictionary<string, (string name, AssetChunk chunk)>();

            foreach (var asset in localManifest.Assets)
            {
                string name = asset.AssetName ?? "";
                localAssetMap[NormalizePath(name)] = asset;
                foreach (var chunk in asset.AssetChunks)
                {
                    string h = (chunk.ChunkDecompressedHashMd5 ?? "").ToLowerInvariant();
                    if (!string.IsNullOrEmpty(h)) localChunkMap[h] = (name, chunk);
                }
            }

            var targetAssetMap = new Dictionary<string, AssetProperty>();
            foreach (var asset in targetManifest.Assets) targetAssetMap[NormalizePath(asset.AssetName ?? "")] = asset;

            foreach (var kvp in localAssetMap)
            {
                string normPath = kvp.Key;
                var localAsset = kvp.Value;
                string localMd5 = (localAsset.AssetHashMd5 ?? "").ToLowerInvariant();
                string localName = localAsset.AssetName ?? "";

                bool needsBackup = !targetAssetMap.TryGetValue(normPath, out var targetAsset) || ((targetAsset.AssetHashMd5 ?? "").ToLowerInvariant() != localMd5);
                if (needsBackup)
                {
                    string srcPath = Path.Combine(gameDir, localName);
                    string dstPath = Path.Combine(backupLocalDir, localName);
                    if (File.Exists(srcPath)) ops.Backup.Add((srcPath, dstPath));
                }
            }

            var cnSdks = new[] { Path.Combine(GameConstants.CN_DATA_DIR, "Plugins", "PCGameSDK.dll"), "sdk_pkg_version" };
            var osSdks = new[] { Path.Combine(GameConstants.OS_DATA_DIR, "Plugins", "PluginEOSSDK.dll"), Path.Combine(GameConstants.OS_DATA_DIR, "Plugins", "EOSSDK-Win64-Shipping.dll") };
            var localSdks = isCurrentlyCn ? cnSdks : osSdks;
            var targetSdks = isCurrentlyCn ? osSdks : cnSdks;

            foreach (var sdk in localSdks)
            {
                string src = Path.Combine(gameDir, sdk);
                if (File.Exists(src)) ops.Backup.Add((src, Path.Combine(backupLocalDir, sdk)));
            }

            foreach (var kvp in targetAssetMap)
            {
                string normPath = kvp.Key;
                var targetAsset = kvp.Value;
                string targetMd5 = (targetAsset.AssetHashMd5 ?? "").ToLowerInvariant();
                string targetName = targetAsset.AssetName ?? "";

                if (localAssetMap.TryGetValue(normPath, out var localAsset) && (localAsset.AssetHashMd5 ?? "").ToLowerInvariant() == targetMd5 && File.Exists(Path.Combine(gameDir, localAsset.AssetName ?? ""))) continue;

                string backupFilePath = Path.Combine(backupTargetDir, targetName);
                if (File.Exists(backupFilePath))
                {
                    if (HashUtility.Md5File(backupFilePath) == targetMd5)
                    {
                        ops.Restore.Add((backupFilePath, Path.Combine(gameDir, targetName)));
                        continue;
                    }
                    else File.Delete(backupFilePath);
                }

                var op = new SophonAssetOperation(targetAsset, urlPrefix, urlSuffix);
                foreach (var chunk in targetAsset.AssetChunks)
                {
                    string chunkHash = (chunk.ChunkDecompressedHashMd5 ?? "").ToLowerInvariant();
                    bool reused = false;

                    if (localChunkMap.TryGetValue(chunkHash, out var localMatch) && File.Exists(Path.Combine(gameDir, localMatch.name)))
                    {
                        op.Instructions.Add(new AssemblyInstruction("reuse", chunk, localMatch.name, localMatch.chunk));
                        reused = true;
                    }

                    if (!reused)
                    {
                        op.Instructions.Add(new AssemblyInstruction("download", chunk));
                        op.DiffChunks.Add(new SophonChunk(urlPrefix, urlSuffix, chunk));
                    }
                }
                ops.Assemble.Add(op);
            }

            foreach (var sdk in targetSdks)
            {
                string src = Path.Combine(backupTargetDir, sdk);
                if (File.Exists(src)) ops.Restore.Add((src, Path.Combine(gameDir, sdk)));
            }
            return ops;
        }

        private async Task DownloadDiffChunksAsync(List<SophonAssetOperation> assembleOps)
        {
            var chunksMap = new Dictionary<string, SophonChunk>();
            foreach (var op in assembleOps)
            foreach (var chunk in op.DiffChunks)
                chunksMap[chunk.ChunkName] = chunk;

            int totalChunks = chunksMap.Count;
            if (totalChunks == 0) return;

            int downloaded = 0;
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 16 };
            await Parallel.ForEachAsync(chunksMap.Values, parallelOptions, async (chunk, token) =>
            {
                await DownloadSingleChunkAsync(chunk);
                int current = System.Threading.Interlocked.Increment(ref downloaded);
                print($"{current}/{totalChunks} - {chunk.ChunkName}");
            });
        }

        private async Task DownloadSingleChunkAsync(SophonChunk chunk)
        {
            string chunkPath = Path.Combine(chunksDir, chunk.ChunkName);
            if (File.Exists(chunkPath) && new FileInfo(chunkPath).Length == chunk.ChunkSize) return;

            var bytes = await httpClient.GetByteArrayAsync(chunk.DownloadUrl);
            await File.WriteAllBytesAsync(chunkPath, bytes);
        }

        private void AssembleFiles(List<SophonAssetOperation> assembleOps)
        {
            int totalOps = assembleOps.Count;
            if (totalOps == 0) return;

            for (int i = 0; i < totalOps; i++)
            {
                var op = assembleOps[i];
                string targetPath = Path.Combine(targetDir, op.AssetName);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                using (var targetFile = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    foreach (var inst in op.Instructions)
                    {
                        targetFile.Seek(inst.TargetChunk.ChunkOnFileOffset, SeekOrigin.Begin);

                        if (inst.Action == "reuse")
                        {
                            using var localFile = new FileStream(Path.Combine(gameDir, inst.LocalAssetName), FileMode.Open, FileAccess.Read, FileShare.Read);
                            localFile.Seek(inst.LocalChunk.ChunkOnFileOffset, SeekOrigin.Begin);
                            byte[] buffer = new byte[inst.TargetChunk.ChunkSizeDecompressed];
                            localFile.ReadExactly(buffer, 0, buffer.Length);
                            targetFile.Write(buffer, 0, buffer.Length);
                        }
                        else
                        {
                            using var compressedFile = new FileStream(Path.Combine(chunksDir, inst.TargetChunk.ChunkName), FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var dctx = new DecompressionStream(compressedFile);
                            byte[] buffer = new byte[inst.TargetChunk.ChunkSizeDecompressed];
                            dctx.ReadExactly(buffer, 0, buffer.Length);
                            targetFile.Write(buffer, 0, buffer.Length);
                        }
                    }
                }
                print($"合并补丁文件中: {i + 1}/{totalOps}");
            }
        }

        private void ReplacePhysicalFiles(OperationLists ops)
        {
            foreach (var (src, dst) in ops.Backup)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (File.Exists(dst)) File.Delete(dst);
                if (File.Exists(src)) File.Move(src, dst);
            }

            string localDataDir = Path.Combine(gameDir, isCurrentlyCn ? GameConstants.CN_DATA_DIR : GameConstants.OS_DATA_DIR);
            string targetDataDir = Path.Combine(gameDir, isCurrentlyCn ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR);
            
            if (Directory.Exists(localDataDir))
            {
                if (Directory.Exists(targetDataDir)) Directory.Delete(targetDataDir, true);
                Directory.Move(localDataDir, targetDataDir);
            }

            string localExe = Path.Combine(gameDir, isCurrentlyCn ? GameConstants.CN_EXE : GameConstants.OS_EXE);
            string targetExe = Path.Combine(gameDir, isCurrentlyCn ? GameConstants.OS_EXE : GameConstants.CN_EXE);
            
            if (File.Exists(localExe))
            {
                if (File.Exists(targetExe)) File.Delete(targetExe);
                File.Move(localExe, targetExe);
            }

            foreach (var (src, dst) in ops.Restore)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                if (File.Exists(dst)) File.Delete(dst);
                if (File.Exists(src)) File.Move(src, dst);
            }

            foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
            {
                string relPath = Path.GetRelativePath(targetDir, file);
                string dstPath = Path.Combine(gameDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                if (File.Exists(dstPath)) File.Delete(dstPath);
                File.Move(file, dstPath);
            }

            var obsoleteFiles = targetIsOversea 
                ? new[] { Path.Combine(targetDataDir, "Plugins", "PCGameSDK.dll"), Path.Combine(gameDir, "sdk_pkg_version") }
                : new[] { Path.Combine(targetDataDir, "Plugins", "PluginEOSSDK.dll"), Path.Combine(targetDataDir, "Plugins", "EOSSDK-Win64-Shipping.dll") };

            foreach (var f in obsoleteFiles) if (File.Exists(f)) File.Delete(f);
        }
    }
}