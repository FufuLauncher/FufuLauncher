/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Compression;
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
    if (sender is Button btn) btn.IsEnabled = false;
    StartBtn.IsEnabled = false;

    string cnPlugins = Path.Combine(_gameDir, GameConstants.CN_DATA_DIR, "Plugins");
    string osPlugins = Path.Combine(_gameDir, GameConstants.OS_DATA_DIR, "Plugins");
    
    bool retryConversion;
    do
    {
        retryConversion = false;
        string pluginDeleteError = null;

        try
        {
            if (Directory.Exists(cnPlugins)) Directory.Delete(cnPlugins, true);
            if (Directory.Exists(osPlugins)) Directory.Delete(osPlugins, true);
        }
        catch (Exception ex)
        {
            pluginDeleteError = ex.Message;
        }

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

        if (pluginDeleteError != null)
        {
            UpdateProgressText($"清理插件文件夹失败: {pluginDeleteError}");
        }

        string cacheDir = Path.Combine(Helpers.AppPaths.ServerCacheDir, Guid.NewGuid().ToString("N"));
        var converter = new PackageConverter(_gameDir, cacheDir, UpdateProgressText, _targetServer);

        try
        {
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
                Content = $"切换失败，文件可能被占用\n错误信息: {ex.Message}\n\n请问是放弃切换并修复游戏，还是尝试补救方案？",
                PrimaryButtonText = "继续尝试",
                CloseButtonText = "修复游戏",
                XamlRoot = XamlRoot
            };
            
            var result = await errDialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                string targetPluginDir = Directory.Exists(Path.Combine(_gameDir, GameConstants.OS_DATA_DIR)) 
                    ? Path.Combine(_gameDir, GameConstants.OS_DATA_DIR, "Plugins") 
                    : Path.Combine(_gameDir, GameConstants.CN_DATA_DIR, "Plugins");

                if (!Directory.Exists(targetPluginDir)) Directory.CreateDirectory(targetPluginDir);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = targetPluginDir,
                    UseShellExecute = true,
                    Verb = "open"
                });

                var monitorTextBlock = new TextBlock 
                { 
                    Text = "请在弹出的文件夹中，手动删除所有文件(可使用快捷键Ctrl+A全选，再按Del键，或自己手动勾选右键删除)\n我们会实时监测，当你清理完毕后可点击确认继续", 
                    TextWrapping = TextWrapping.Wrap 
                };

                var manualDeleteDialog = new ContentDialog
                {
                    Title = "手动清理插件",
                    Content = monitorTextBlock,
                    PrimaryButtonText = "确认并重试",
                    CloseButtonText = "取消",
                    XamlRoot = XamlRoot
                };

                using var cts = new CancellationTokenSource();
                var monitorTask = Task.Run(async () =>
                {
                    try
                    {
                        while (!cts.Token.IsCancellationRequested)
                        {
                            bool isClean = true;
                            if (Directory.Exists(cnPlugins) && Directory.GetFileSystemEntries(cnPlugins).Length > 0) isClean = false;
                            if (Directory.Exists(osPlugins) && Directory.GetFileSystemEntries(osPlugins).Length > 0) isClean = false;

                            if (isClean)
                            {
                                DispatcherQueue.TryEnqueue(() =>
                                {
                                    monitorTextBlock.Text = "你似乎已经成功了，可以直接确认了";
                                });
                                break;
                            }
                            await Task.Delay(500, cts.Token);
                        }
                    }
                    catch { }
                }, cts.Token);

                var manualResult = await manualDeleteDialog.ShowAsync();
                cts.Cancel();

                if (manualResult == ContentDialogResult.Primary)
                {
                    if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
                    retryConversion = true;
                    continue; 
                }
            }

            _progressDialog.Title = "正在修复游戏";
            _statusText.Text = "准备修复...";
            _ = _progressDialog.ShowAsync();

            try
            {
                await Task.Run(() => converter.RunVerificationAsync());
                
                _progressDialog.Hide();
                var repairSuccessDialog = new ContentDialog
                {
                    Title = "修复完成",
                    Content = "游戏文件修复完成，您可以尝试重新切换服务器或直接游玩！",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                await repairSuccessDialog.ShowAsync();
            }
            catch (Exception repairEx)
            {
                _progressDialog.Hide();
                var repairErrDialog = new ContentDialog
                {
                    Title = "修复失败",
                    Content = $"游戏修复失败，错误信息: {repairEx.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                await repairErrDialog.ShowAsync();
            }
        }
        finally
        {
            if (!retryConversion)
            {
                if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
                
                StartBtn.IsEnabled = true;
                if (sender is Button buttonSender) buttonSender.IsEnabled = true;
            }
        }

    } while (retryConversion);
}
        
        private async void AlternativeStartBtn_Click(object sender, RoutedEventArgs e)
        {
            StartBtn.IsEnabled = false;
            if (sender is Button btn) btn.IsEnabled = false;

            var dialog = new ContentDialog
            {
                Title = "提示",
                Content = "此方法将在开始时提前清空游戏的组件文件夹，适用于文件被占用导致切换失败的情况，清空后将重新开始一遍完整的切换服务器，如果切换服务器失败，你就得修复游戏才能继续游玩！确认继续？",
                PrimaryButtonText = "确认",
                CloseButtonText = "取消",
                XamlRoot = XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                StartBtn.IsEnabled = true;
                if (sender is Button b) b.IsEnabled = true;
                return;
            }

            string cnPlugins = Path.Combine(_gameDir, GameConstants.CN_DATA_DIR, "Plugins");
            string osPlugins = Path.Combine(_gameDir, GameConstants.OS_DATA_DIR, "Plugins");

            try
            {
                if (Directory.Exists(cnPlugins)) Directory.Delete(cnPlugins, true);
                if (Directory.Exists(osPlugins)) Directory.Delete(osPlugins, true);
            }
            catch (Exception ex)
            {
                var errDialog = new ContentDialog
                {
                    Title = "清理失败",
                    Content = $"提前清理插件文件夹失败: {ex.Message}",
                    CloseButtonText = "确定",
                    XamlRoot = XamlRoot
                };
                await errDialog.ShowAsync();
                StartBtn.IsEnabled = true;
                if (sender is Button b) b.IsEnabled = true;
                return;
            }
            
            StartBtn_Click(sender, e);
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
            using var stream = new FileStream(filepath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var hashBytes = md5.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }
    }

    public static class BilibiliSdkManager
    {
        public static async Task EnsureSdkAndDeprecatedFilesAsync(string gameDir, bool toBilibili, Action<string> print = null)
        {
            print?.Invoke("清理旧版SDK文件...");
            string cnDataDir = Path.Combine(gameDir, GameConstants.CN_DATA_DIR);
            string osDataDir = Path.Combine(gameDir, GameConstants.OS_DATA_DIR);
            
            string[] deprecatedFiles = {
                Path.Combine(cnDataDir, "Plugins", "PCGameSDK.dll"),
                Path.Combine(cnDataDir, "Plugins", "EOSSDK-Win64-Shipping.dll"),
                Path.Combine(osDataDir, "Plugins", "PCGameSDK.dll"),
                Path.Combine(osDataDir, "Plugins", "EOSSDK-Win64-Shipping.dll"),
                Path.Combine(gameDir, "sdk_pkg_version")
            };

            foreach (var file in deprecatedFiles)
            {
                if (File.Exists(file))
                {
                    try 
                    { 
                        File.SetAttributes(file, FileAttributes.Normal);
                        File.Delete(file); 
                    } 
                    catch { /* ignore */ }
                }
            }

            if (!toBilibili) return;

            print?.Invoke("获取B服SDK配置...");
            string url = $"{GameConstants.CN_API}/getGameChannelSDKs?channel=14&game_ids[]={GameConstants.BILI_GAME_ID}&launcher_id={GameConstants.BILI_LAUNCHER_ID}&sub_channel=0";
            using var httpClient = new HttpClient();
            var jsonResp = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(jsonResp);
            
            var dataProp = doc.RootElement.GetProperty("data");
            var sdks = dataProp.GetProperty("game_channel_sdks");
            if (sdks.GetArrayLength() == 0) throw new Exception("获取B服SDK配置失败");
            
            var sdkPkgUrl = sdks[0].GetProperty("channel_sdk_pkg").GetProperty("url").GetString();
            
            print?.Invoke("正在下载B服SDK...");
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                using (var response = await httpClient.GetAsync(sdkPkgUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await contentStream.CopyToAsync(fileStream);
                }
                
                print?.Invoke("正在解压B服SDK...");
                await Task.Run(() => 
                {
                    using var archive = ZipFile.OpenRead(tempFile);
                    foreach (var entry in archive.Entries)
                    {
                        string destPath = Path.GetFullPath(Path.Combine(gameDir, entry.FullName));
                        if (string.IsNullOrEmpty(entry.Name))
                        {
                            Directory.CreateDirectory(destPath);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        if (File.Exists(destPath))
                        {
                            File.SetAttributes(destPath, FileAttributes.Normal);
                        }
                        entry.ExtractToFile(destPath, true);
                    }
                });
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { /* ignore */ }
                }
            }
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
            print("正在请求网络分支");
            var localInfo = await GetBranchAndManifestUrlAsync(isCurrentlyOs, isCurrentlyBili);
            _targetChunkPrefix = localInfo.chunkPrefix;
            _targetChunkSuffix = localInfo.chunkSuffix;

            print("正在下载并解析最新清单");
            _targetManifest = await DownloadAndDecodeManifestAsync(localInfo.manifestUrl);
            
            ClearPluginsFolder();
            
            await VerifyAndRepairAsync();
    
            print("清理临时数据");
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
        }

        public async Task ExecuteConversionAsync()
        {
            print("正在请求网络分支");
            var localInfo = await GetBranchAndManifestUrlAsync(isCurrentlyOs, isCurrentlyBili);
            var targetInfo = await GetBranchAndManifestUrlAsync(targetIsOversea, targetIsBilibili);
            
            _targetChunkPrefix = targetInfo.chunkPrefix;
            _targetChunkSuffix = targetInfo.chunkSuffix;

            print("正在下载并解析清单");
            var localManifest = await DownloadAndDecodeManifestAsync(localInfo.manifestUrl);
            _targetManifest = await DownloadAndDecodeManifestAsync(targetInfo.manifestUrl);

            ClearPluginsFolder();

            print("正在比对");

            string localDataDirName = isCurrentlyOs ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR;
            string pluginsDir = Path.Combine(gameDir, localDataDirName, "Plugins");
            if (Directory.Exists(pluginsDir))
            {
                Directory.Delete(pluginsDir, true);
            }

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

                if (currentCount % 100 == 0 || currentCount == total)
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
                    if (File.Exists(dstPath)) 
                    {
                        File.SetAttributes(dstPath, FileAttributes.Normal);
                        File.Delete(dstPath);
                    }
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
    
    var dataProp = doc.RootElement.GetProperty("data");
    if (dataProp.ValueKind == JsonValueKind.Null)
    {
        throw new Exception("接口返回data为空，获取分支数据失败");
    }
    
    var branches = dataProp.GetProperty("game_branches")[0];
    
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
        if (!branches.TryGetProperty("main", out targetBranch) || targetBranch.ValueKind == JsonValueKind.Null)
        {
            throw new Exception("无法获取 main 分支数据");
        }
    }

    string buildUrl = targetBranch.TryGetProperty("build_url", out var bUrl) && bUrl.ValueKind == JsonValueKind.String ? bUrl.GetString() : null;

    if (string.IsNullOrEmpty(buildUrl))
    {
        string sophonApi = isOversea ? GameConstants.OS_SOPHON : GameConstants.CN_SOPHON;
        
        if (!targetBranch.TryGetProperty("package_id", out var pkgIdProp) || pkgIdProp.ValueKind == JsonValueKind.Null)
        {
            throw new Exception("分支数据缺少package_id");
        }
        string pkgId = pkgIdProp.GetString();
        
        if (!targetBranch.TryGetProperty("password", out var pwdProp) || pwdProp.ValueKind == JsonValueKind.Null)
        {
            throw new Exception("分支数据缺少password");
        }
        string pwd = pwdProp.GetString();
        
        string branchName = isPreDownload ? "pre_download" : "main";
        buildUrl = $"{sophonApi}/getBuild?branch={branchName}&package_id={pkgId}&password={pwd}";
    }

    var buildJson = await httpClient.GetStringAsync(buildUrl);
    using var buildDoc = JsonDocument.Parse(buildJson);
    
    var buildDataProp = buildDoc.RootElement.GetProperty("data");
    if (buildDataProp.ValueKind == JsonValueKind.Null)
    {
        throw new Exception(isPreDownload ? "获取预下载构建数据失败" : "失败");
    }

    var manifestsProp = buildDataProp.GetProperty("manifests");
    if (manifestsProp.ValueKind == JsonValueKind.Null || manifestsProp.GetArrayLength() == 0)
    {
        throw new Exception("获取清单(manifests)失败。");
    }
    
    var manifestData = manifestsProp[0];

    string manifestId = manifestData.GetProperty("manifest").GetProperty("id").GetString();
    string urlPrefix = manifestData.GetProperty("manifest_download").GetProperty("url_prefix").GetString();
    string urlSuffix = manifestData.GetProperty("manifest_download").TryGetProperty("url_suffix", out var sfx) && sfx.ValueKind == JsonValueKind.String ? sfx.GetString() : "";

    string chunkPrefix = manifestData.GetProperty("chunk_download").GetProperty("url_prefix").GetString();
    string chunkSuffix = manifestData.GetProperty("chunk_download").TryGetProperty("url_suffix", out var csfx) && csfx.ValueKind == JsonValueKind.String ? csfx.GetString() : "";

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

    string localDataDirName = isCurrentlyOs ? GameConstants.OS_DATA_DIR : GameConstants.CN_DATA_DIR;
    string pluginsDir = Path.Combine(gameDir, localDataDirName, "Plugins");
    if (Directory.Exists(pluginsDir))
    {
        Directory.Delete(pluginsDir, true);
    }

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
                if (File.Exists(dstPath)) 
                {
                    File.SetAttributes(dstPath, FileAttributes.Normal);
                    File.Delete(dstPath);
                }
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

            string tempDir = Path.Combine(chunksDir, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string tempPath = Path.Combine(tempDir, chunk.ChunkName + ".tmp");
            int maxRetries = 3;

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using var response = await httpClient.GetAsync(chunk.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
            
                    await contentStream.CopyToAsync(fileStream);
                    fileStream.Close();

                    if (new FileInfo(tempPath).Length == chunk.ChunkSize)
                    {
                        if (File.Exists(chunkPath)) File.Delete(chunkPath);
                        File.Move(tempPath, chunkPath);
                        Directory.Delete(tempDir, true);
                        return;
                    }
                }
                catch (Exception)
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    if (i == maxRetries - 1)
                    {
                        if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                        throw;
                    }
                    await Task.Delay(1000 * (i + 1));
                }
            }
            
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            throw new Exception($"数据块 {chunk.ChunkName} 下载失败或大小不匹配。");
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
                            using var localFile = new FileStream(Path.Combine(gameDir, inst.LocalAssetName), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
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
                            string chunkPath = Path.Combine(chunksDir, inst.TargetChunk.ChunkName);
                            try
                            {
                                using var compressedFile = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                using var dctx = new DecompressionStream(compressedFile);
        
                                while (remainingBytes > 0)
                                {
                                    int bytesRead = dctx.Read(buffer, 0, (int)Math.Min(buffer.Length, remainingBytes));
                                    if (bytesRead <= 0) break;
                                    targetFile.Write(buffer, 0, bytesRead);
                                    remainingBytes -= bytesRead;
                                }
                            }
                            catch (Exception ex)
                            {
                                if (File.Exists(chunkPath))
                                {
                                    File.Delete(chunkPath);
                                }
                                throw new Exception($"数据块 {inst.TargetChunk.ChunkName} 已损坏并被自动清理，请重新执行操作。异常明细: {ex.Message}");
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
                if (File.Exists(dstPath)) 
                {
                    File.SetAttributes(dstPath, FileAttributes.Normal);
                    File.Delete(dstPath);
                }
                File.Move(file, dstPath);
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
            
            BilibiliSdkManager.EnsureSdkAndDeprecatedFilesAsync(gameDir, targetIsBilibili, print).GetAwaiter().GetResult();
        }
    }
}
