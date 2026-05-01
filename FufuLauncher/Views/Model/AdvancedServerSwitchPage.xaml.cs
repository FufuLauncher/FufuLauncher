using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Security.Cryptography;
using System.Text.Json;
using FufuLauncher.Constants;
using FufuLauncher.Protobuf;
using ZstdSharp;

namespace FufuLauncher.Views
{
    public sealed partial class AdvancedServerSwitchPage : Page
    {
        private string _gameDir = string.Empty;
        private Window _parentWindow;
        private string _targetServer = string.Empty;
        
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
                _targetServer = param.TargetServer ?? "";
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
                
                var converter = new PackageConverter(_gameDir, cacheDir, UpdateProgressText, _targetServer);
            
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
        public const string CN_API = ApiEndpoints.HypCnApi; 
        public const string CN_SOPHON = ApiEndpoints.SophonCnApi;
        
        public const string BILI_LAUNCHER_ID = "umfgRO5gh5";
        public const string BILI_GAME_ID = "T2S0Gz4Dr2";

        public const string OS_LAUNCHER_ID = "VYTpXlbWo8";
        public const string OS_GAME_ID = "gopR6Cufr3";
        public const string OS_API = ApiEndpoints.HypOsApi; 
        public const string OS_SOPHON = ApiEndpoints.SophonOsApi; 
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
        public List<SophonAssetOperation> Assemble { get; set; } = new();
    }

    public class PackageConverter
    {
        private readonly string gameDir;
        private readonly string cacheDir;
        private readonly HttpClient httpClient;
        private readonly Action<string> print;
        private string _savedGameVersion = null;

        private readonly string chunksDir;
        private readonly string targetDir;

        private readonly bool isCurrentlyCn;
        private readonly bool isCurrentlyOs;
        private readonly bool targetIsOversea;
        private SophonManifestProto _targetManifest;
        private string _targetChunkPrefix;
        private string _targetChunkSuffix;
        
        private readonly bool isCurrentlyBili;
        private readonly bool targetIsBilibili;
        public string TargetServerName => targetIsOversea ? "国际服 (OS)" : (targetIsBilibili ? "B服 (Bilibili)" : "国服 (CN)");
        
        public PackageConverter(string gameDir, string cacheDir, Action<string> logger, string targetServer = "")
        {
            this.gameDir = gameDir;
            this.cacheDir = cacheDir;
            print = logger;
            httpClient = new HttpClient();

            chunksDir = Path.Combine(cacheDir, "Chunks");
            targetDir = Path.Combine(cacheDir, "Target");

            foreach (var d in new[] { chunksDir, targetDir }) Directory.CreateDirectory(d);
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

            string configPath = Path.Combine(gameDir, "config.ini");
            if (File.Exists(configPath))
            {
                string[] lines = File.ReadAllLines(configPath);
                foreach (var line in lines)
                {
                    string trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("game_version="))
                    {
                        _savedGameVersion = trimmedLine.Split('=', 2)[1].Trim();
                    }
                    if (trimmedLine.Contains("channel=14"))
                    {
                        isCurrentlyBili = true;
                    }
                }
            }
            isCurrentlyBili = File.Exists(configPath) && File.ReadAllText(configPath).Contains("channel=14");

            if (targetServer == "Bili") { targetIsOversea = false; targetIsBilibili = true; }
            else if (targetServer == "OS") { targetIsOversea = true; targetIsBilibili = false; }
            else if (targetServer == "CN") { targetIsOversea = false; targetIsBilibili = false; }
            else { targetIsOversea = isCurrentlyCn; targetIsBilibili = false; }
        }

        private void ClearPluginsFolder()
        {
            string localDataDirName = isCurrentlyOs ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR;
            string pluginsDir = Path.Combine(gameDir, localDataDirName, "Plugins");
            if (Directory.Exists(pluginsDir))
            {
                Directory.Delete(pluginsDir, true);
                print("已在校验前清理 Plugins 文件夹");
            }
        }

        public async Task RunVerificationAsync()
        {
            ClearPluginsFolder();

            print("正在请求网络分支");
            var localInfo = await GetBranchAndManifestUrlAsync(isCurrentlyOs, isCurrentlyBili);
            _targetChunkPrefix = localInfo.chunkPrefix;
            _targetChunkSuffix = localInfo.chunkSuffix;

            print("正在下载并解析最新清单");
            _targetManifest = await DownloadAndDecodeManifestAsync(localInfo.manifestUrl);
            
            await VerifyAndRepairAsync();
    
            print("清理临时数据");
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }

        public async Task ExecuteConversionAsync()
        {
            ClearPluginsFolder();

            print("正在请求网络分支");
            var localInfo = await GetBranchAndManifestUrlAsync(isCurrentlyOs, isCurrentlyBili);
            var targetInfo = await GetBranchAndManifestUrlAsync(targetIsOversea, targetIsBilibili);
            
            _targetChunkPrefix = targetInfo.chunkPrefix;
            _targetChunkSuffix = targetInfo.chunkSuffix;

            print("正在下载并解析清单");
            var localManifest = await DownloadAndDecodeManifestAsync(localInfo.manifestUrl);
            _targetManifest = await DownloadAndDecodeManifestAsync(targetInfo.manifestUrl);

            print("正在比对");
            var ops = GenerateOperations(_targetManifest, localManifest, _targetChunkPrefix, _targetChunkSuffix);

            print("正在下载所需的数据块");
            await DownloadDiffChunksAsync(ops.Assemble);

            print("正在组装文件");
            AssembleFiles(ops.Assemble);

            print("正在替换文件");
            ReplacePhysicalFiles(ops);

            print("清理临时数据");
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }
        
        public async Task VerifyAndRepairAsync()
        {
            if (_targetManifest == null) throw new Exception("清单数据未初始化，无法进行校验");

            print("正在校验游戏文件完整性，该过程可能需要较长时间");
            
            var brokenAssets = new System.Collections.Concurrent.ConcurrentBag<SophonAssetOperation>();
            int total = _targetManifest.Assets.Count;
            int current = 0;
            
            long totalBytes = Enumerable.Sum(_targetManifest.Assets, a => a.AssetSize);
            long processedBytes = 0;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var parallelOptions = new ParallelOptions 
            { 
                MaxDegreeOfParallelism = Environment.ProcessorCount 
            };

            await Parallel.ForEachAsync(_targetManifest.Assets, parallelOptions, async (asset, token) =>
            {
                string filePath = Path.Combine(gameDir, asset.AssetName);
                bool isBroken = false;

                if (!File.Exists(filePath))
                {
                    isBroken = true;
                }
                else
                {
                    await Task.Run(() =>
                    {
                        string expectedMd5 = (asset.AssetHashMd5 ?? "").ToLowerInvariant();
                        string actualMd5 = HashUtility.Md5File(filePath);
                        if (expectedMd5 != actualMd5)
                        {
                            isBroken = true;
                        }
                    }, token);
                }

                if (isBroken)
                {
                    var op = new SophonAssetOperation(asset, _targetChunkPrefix, _targetChunkSuffix);
                    foreach (var chunk in asset.AssetChunks)
                    {
                        op.Instructions.Add(new AssemblyInstruction("download", chunk));
                        op.DiffChunks.Add(new SophonChunk(_targetChunkPrefix, _targetChunkSuffix, chunk));
                    }
                    brokenAssets.Add(op);
                }
                
                long currentBytes = Interlocked.Add(ref processedBytes, asset.AssetSize);
                int currentCount = Interlocked.Increment(ref current);
                
                if (currentCount % 10 == 0 || currentCount == total)
                {
                    TimeSpan elapsed = stopwatch.Elapsed;
                    if (elapsed.TotalSeconds > 2 && currentBytes > 0)
                    {
                        double bytesPerSecond = currentBytes / elapsed.TotalSeconds;
                        long remainingBytes = totalBytes - currentBytes;
                        double remainingSeconds = remainingBytes / bytesPerSecond;
                        TimeSpan remainingTime = TimeSpan.FromSeconds(remainingSeconds);
                        
                        print($"校验中: {currentCount}/{total} - 剩余时间: {remainingTime:hh\\:mm\\:ss}");
                    }
                    else
                    {
                        print($"校验中: {currentCount}/{total} - 预计剩余时间: N/A");
                    }
                }
            });

            var brokenAssetsList = brokenAssets.ToList();
            if (brokenAssetsList.Count > 0)
            {
                print($"发现 {brokenAssetsList.Count} 个异常或缺失文件，正在修复");
                
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, true);
                    Directory.CreateDirectory(targetDir);
                }

                await DownloadDiffChunksAsync(brokenAssetsList);
                AssembleFiles(brokenAssetsList);
                
                foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
                {
                    string relPath = Path.GetRelativePath(targetDir, file);
                    string dstPath = Path.Combine(gameDir, relPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                    if (File.Exists(dstPath)) File.Delete(dstPath);
                    File.Move(file, dstPath);
                }
                print("异常文件修复完成");
            }
            else
            {
                print("文件校验通过，游戏数据完整");
            }
        }

        private async Task<(string manifestUrl, string chunkPrefix, string chunkSuffix)> GetBranchAndManifestUrlAsync(bool isOversea, bool isBili = false, bool isPreDownload = false)
        {
            string api = isOversea ? GameConstants.OS_API : GameConstants.CN_API;
            string launcherId = isOversea ? GameConstants.OS_LAUNCHER_ID : (isBili ? GameConstants.BILI_LAUNCHER_ID : GameConstants.CN_LAUNCHER_ID);
            string gameId = isOversea ? GameConstants.OS_GAME_ID : (isBili ? GameConstants.BILI_GAME_ID : GameConstants.CN_GAME_ID);

            string url = $"{api}/getGameBranches?launcher_id={launcherId}&game_ids[]={gameId}";
            var jsonResp = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(jsonResp);
            var branches = doc.RootElement.GetProperty("data").GetProperty("game_branches")[0];
            
            JsonElement targetBranch;
            if (isPreDownload)
            {
                if (!branches.TryGetProperty("pre_download", out targetBranch) || targetBranch.ValueKind == JsonValueKind.Null)
                {
                    throw new Exception("当前未开启预下载");
                }
            }
            else
            {
                targetBranch = branches.GetProperty("main");
            }

            string buildUrl = targetBranch.TryGetProperty("build_url", out var bUrl) && bUrl.ValueKind == JsonValueKind.String ? bUrl.GetString() : null;

            if (string.IsNullOrEmpty(buildUrl))
            {
                string sophonApi = isOversea ? GameConstants.OS_SOPHON : GameConstants.CN_SOPHON;
                string pkgId = targetBranch.GetProperty("package_id").GetString();
                string pwd = targetBranch.GetProperty("password").GetString();
                string branchName = isPreDownload ? "pre_download" : "main";
                buildUrl = $"{sophonApi}/getBuild?branch={branchName}&package_id={pkgId}&password={pwd}";
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

        public async Task ExecutePreDownloadAsync()
        {
            print("正在请求网络分支 (预下载)");
            var localInfo = await GetBranchAndManifestUrlAsync(isCurrentlyOs, isCurrentlyBili, false);
            var targetInfo = await GetBranchAndManifestUrlAsync(isCurrentlyOs, isCurrentlyBili, true);

            _targetChunkPrefix = targetInfo.chunkPrefix;
            _targetChunkSuffix = targetInfo.chunkSuffix;

            print("正在下载并解析清单");
            var localManifest = await DownloadAndDecodeManifestAsync(localInfo.manifestUrl);
            _targetManifest = await DownloadAndDecodeManifestAsync(targetInfo.manifestUrl);

            print("正在比对预下载资源");
            var ops = GenerateUpdateOperations(_targetManifest, localManifest, _targetChunkPrefix, _targetChunkSuffix);

            print("正在下载预下载数据块");
            await DownloadDiffChunksAsync(ops.Assemble);

            print("正在组装预下载文件");
            AssembleFiles(ops.Assemble);

            print("正在安装预下载文件");
            ApplyUpdatePhysicalFiles();

            print("清理临时数据");
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }

        private OperationLists GenerateUpdateOperations(SophonManifestProto targetManifest, SophonManifestProto localManifest, string urlPrefix, string urlSuffix)
        {
            var ops = new OperationLists();
            var localAssetMap = new Dictionary<string, AssetProperty>();
            
            foreach (var asset in localManifest.Assets)
            {
                localAssetMap[asset.AssetName ?? ""] = asset;
            }

            foreach (var targetAsset in targetManifest.Assets)
            {
                string targetName = targetAsset.AssetName ?? "";
                string targetMd5 = (targetAsset.AssetHashMd5 ?? "").ToLowerInvariant();

                if (localAssetMap.TryGetValue(targetName, out var localAsset) && 
                    (localAsset.AssetHashMd5 ?? "").ToLowerInvariant() == targetMd5 && 
                    File.Exists(Path.Combine(gameDir, localAsset.AssetName ?? ""))) 
                {
                    continue;
                }

                var op = new SophonAssetOperation(targetAsset, urlPrefix, urlSuffix);
                foreach (var chunk in targetAsset.AssetChunks)
                {
                    string chunkHash = (chunk.ChunkDecompressedHashMd5 ?? "").ToLowerInvariant();
                    bool reused = false;
                    
                    if (localAssetMap.TryGetValue(targetName, out var currentLocalAsset) && File.Exists(Path.Combine(gameDir, currentLocalAsset.AssetName ?? "")))
                    {
                        var oldChunk = currentLocalAsset.AssetChunks.FirstOrDefault(c => (c.ChunkDecompressedHashMd5 ?? "").ToLowerInvariant() == chunkHash);
                        if (oldChunk != null)
                        {
                            op.Instructions.Add(new AssemblyInstruction("reuse", chunk, currentLocalAsset.AssetName, oldChunk));
                            reused = true;
                        }
                    }

                    if (!reused)
                    {
                        op.Instructions.Add(new AssemblyInstruction("download", chunk));
                        op.DiffChunks.Add(new SophonChunk(urlPrefix, urlSuffix, chunk));
                    }
                }
                ops.Assemble.Add(op);
            }
            return ops;
        }

        private void ApplyUpdatePhysicalFiles()
        {
            foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
            {
                string relPath = Path.GetRelativePath(targetDir, file);
                string dstPath = Path.Combine(gameDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                if (File.Exists(dstPath)) File.Delete(dstPath);
                File.Move(file, dstPath);
            }
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
            
            foreach (var asset in localManifest.Assets)
            {
                string name = asset.AssetName ?? "";
                localAssetMap[NormalizePath(name)] = asset;
            }

            var targetAssetMap = new Dictionary<string, AssetProperty>();
            foreach (var asset in targetManifest.Assets) targetAssetMap[NormalizePath(asset.AssetName ?? "")] = asset;

            foreach (var kvp in targetAssetMap)
            {
                string normPath = kvp.Key;
                var targetAsset = kvp.Value;
                string targetMd5 = (targetAsset.AssetHashMd5 ?? "").ToLowerInvariant();
                string targetName = targetAsset.AssetName ?? "";

                if (localAssetMap.TryGetValue(normPath, out var localAsset) && (localAsset.AssetHashMd5 ?? "").ToLowerInvariant() == targetMd5 && File.Exists(Path.Combine(gameDir, localAsset.AssetName ?? ""))) continue;

                var op = new SophonAssetOperation(targetAsset, urlPrefix, urlSuffix);
                foreach (var chunk in targetAsset.AssetChunks)
                {
                    string chunkHash = (chunk.ChunkDecompressedHashMd5 ?? "").ToLowerInvariant();
                    bool reused = false;
                    
                    if (localAssetMap.TryGetValue(normPath, out var currentLocalAsset) && File.Exists(Path.Combine(gameDir, currentLocalAsset.AssetName ?? "")))
                    {
                        var oldChunk = currentLocalAsset.AssetChunks.FirstOrDefault(c => (c.ChunkDecompressedHashMd5 ?? "").ToLowerInvariant() == chunkHash);
                        if (oldChunk != null)
                        {
                            op.Instructions.Add(new AssemblyInstruction("reuse", chunk, currentLocalAsset.AssetName, oldChunk));
                            reused = true;
                        }
                    }

                    if (!reused)
                    {
                        op.Instructions.Add(new AssemblyInstruction("download", chunk));
                        op.DiffChunks.Add(new SophonChunk(urlPrefix, urlSuffix, chunk));
                    }
                }
                ops.Assemble.Add(op);
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
                int current = Interlocked.Increment(ref downloaded);
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
            
            byte[] buffer = new byte[81920];

            for (int i = 0; i < totalOps; i++)
            {
                var op = assembleOps[i];
                string targetPath = Path.Combine(targetDir, op.AssetName);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));

                using (var targetFile = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    targetFile.SetLength(op.Asset.AssetSize);

                    foreach (var inst in op.Instructions)
                    {
                        targetFile.Seek(inst.TargetChunk.ChunkOnFileOffset, SeekOrigin.Begin);
                        long remainingBytes = inst.TargetChunk.ChunkSizeDecompressed;

                        if (inst.Action == "reuse")
                        {
                            using var localFile = new FileStream(Path.Combine(gameDir, inst.LocalAssetName), FileMode.Open, FileAccess.Read, FileShare.Read);
                            localFile.Seek(inst.LocalChunk.ChunkOnFileOffset, SeekOrigin.Begin);
                            
                            while (remainingBytes > 0)
                            {
                                int bytesRead = localFile.Read(buffer, 0, (int)Math.Min(buffer.Length, remainingBytes));
                                if (bytesRead <= 0) break;
                                targetFile.Write(buffer, 0, bytesRead);
                                remainingBytes -= bytesRead;
                            }
                        }
                        else
                        {
                            using var compressedFile = new FileStream(Path.Combine(chunksDir, inst.TargetChunk.ChunkName), FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var dctx = new DecompressionStream(compressedFile);
                            
                            while (remainingBytes > 0)
                            {
                                int bytesRead = dctx.Read(buffer, 0, (int)Math.Min(buffer.Length, remainingBytes));
                                if (bytesRead <= 0) break;
                                targetFile.Write(buffer, 0, bytesRead);
                                remainingBytes -= bytesRead;
                            }
                        }
                    }
                }
                print($"合并补丁文件中: {i + 1}/{totalOps}");
            }
        }

        private void ReplacePhysicalFiles(OperationLists ops)
        {
            string localDataDirName = isCurrentlyOs ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR;
            string targetDataDirName = targetIsOversea ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR;

            string localDataDir = Path.Combine(gameDir, localDataDirName);
            string targetDataDir = Path.Combine(gameDir, targetDataDirName);
            
            if (localDataDirName != targetDataDirName && Directory.Exists(localDataDir))
            {
                if (Directory.Exists(targetDataDir)) Directory.Delete(targetDataDir, true);
                Directory.Move(localDataDir, targetDataDir);
            }

            string localExeName = isCurrentlyOs ? GameConstants.OS_EXE : GameConstants.CN_EXE;
            string targetExeName = targetIsOversea ? GameConstants.OS_EXE : GameConstants.CN_EXE;

            string localExe = Path.Combine(gameDir, localExeName);
            string targetExe = Path.Combine(gameDir, targetExeName);
            
            if (localExeName != targetExeName && File.Exists(localExe))
            {
                if (File.Exists(targetExe)) File.Delete(targetExe);
                File.Move(localExe, targetExe);
            }

            foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
            {
                string relPath = Path.GetRelativePath(targetDir, file);
                string dstPath = Path.Combine(gameDir, relPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                if (File.Exists(dstPath)) File.Delete(dstPath);
                File.Move(file, dstPath);
            }

            string sdkPkgVersionFile = Path.Combine(gameDir, "sdk_pkg_version");
            if (targetIsOversea && File.Exists(sdkPkgVersionFile))
            {
                File.Delete(sdkPkgVersionFile);
            }
            
            string configPath = Path.Combine(gameDir, "config.ini");
            string configContent = "";

            if (targetIsOversea)
                configContent = "[General]\r\nchannel=1\r\ncps=mihoyo\r\nsub_channel=0\r\n";
            else if (targetIsBilibili)
                configContent = "[General]\r\nchannel=14\r\ncps=bilibili\r\nsub_channel=0\r\n";
            else
                configContent = "[General]\r\nchannel=1\r\ncps=mihoyo\r\nsub_channel=1\r\n";
            
            if (!string.IsNullOrEmpty(_savedGameVersion))
            {
                configContent += $"game_version={_savedGameVersion}\r\n";
            }

            File.WriteAllText(configPath, configContent);
            
            if (targetIsBilibili)
            {
                string bilibiliPluginsDir = Path.Combine(gameDir, targetDataDirName, "Plugins");
                string targetSdkPath = Path.Combine(bilibiliPluginsDir, "PCGameSDK.dll");

                if (!Directory.Exists(bilibiliPluginsDir)) Directory.CreateDirectory(bilibiliPluginsDir);

                string appBaseDir = AppContext.BaseDirectory;
                string sourceSdkPath = Path.Combine(appBaseDir, "Assets", "PCGameSDK.dll");

                if (File.Exists(sourceSdkPath))
                {
                    File.Copy(sourceSdkPath, targetSdkPath, true);
                    print("B服SDK已注入");
                }
                else
                {
                    print("PCGameSDK.dll不存在");
                }
            }
        }
    }
}