/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.IO.Compression;
using System.Text;
using FufuLauncher.Constants;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services
{
    public interface IPluginUpdateService
    {
        Task ExecuteAutoUpdateAsync(StringBuilder logBuilder);

        Task<bool> InstallOrUpdateMainPluginAsync(StringBuilder? logBuilder = null,
            CancellationToken cancellationToken = default);
    }

    public class PluginUpdateService : IPluginUpdateService
    {
        private readonly ILocalSettingsService _localSettingsService;
        private static readonly SemaphoreSlim InstallGate = new(1, 1);
        public const string AutoUpdatePluginKey = "IsAutoUpdatePluginEnabled";

        public PluginUpdateService(ILocalSettingsService localSettingsService)
        {
            _localSettingsService = localSettingsService;
        }

        public async Task ExecuteAutoUpdateAsync(StringBuilder logBuilder)
        {
            try
            {
                var enabledObj = await _localSettingsService.ReadSettingAsync(AutoUpdatePluginKey);
                if (enabledObj == null || !Convert.ToBoolean(enabledObj)) return;

                var lightweightObj = await _localSettingsService.ReadSettingAsync(LightweightPluginService.SettingKey);
                if (lightweightObj != null && Convert.ToBoolean(lightweightObj))
                {
                    logBuilder.AppendLine("[插件更新] 当前处于轻量模式，跳过主插件自动更新");
                    return;
                }

                logBuilder.AppendLine("[插件更新] 自动更新已启用，开始获取最新普通版插件...");

                if (!await InstallOrUpdateMainPluginAsync(logBuilder))
                {
                    logBuilder.AppendLine("[插件更新] 自动更新失败，将降级使用本地已有插件启动");
                }
            }
            catch (Exception ex)
            {
                logBuilder.AppendLine($"[插件更新] 自动更新失败，将降级使用本地已有插件启动。错误信息: {ex.Message}");
            }
        }

        public async Task<bool> InstallOrUpdateMainPluginAsync(StringBuilder? logBuilder = null,
            CancellationToken cancellationToken = default)
        {
            await InstallGate.WaitAsync(cancellationToken);

            try
            {
                return await InstallOrUpdateMainPluginCoreAsync(logBuilder, cancellationToken);
            }
            finally
            {
                InstallGate.Release();
            }
        }

        private async Task<bool> InstallOrUpdateMainPluginCoreAsync(StringBuilder? logBuilder,
            CancellationToken cancellationToken)
        {
            string tempPath = Path.Combine(Path.GetTempPath(), $"FuFuPlugin_Install_{Guid.NewGuid():N}.zip");
            string extractPath = Path.Combine(Path.GetTempPath(), $"FuFuPlugin_Install_Extract_{Guid.NewGuid():N}");
            string backupConfigPath = Path.Combine(Path.GetTempPath(), $"FuFuPlugin_Config_{Guid.NewGuid():N}.ini");

            try
            {
                string targetDir = LightweightPluginService.MainPluginDir;
                string configPath = Path.Combine(targetDir, "config.ini");

                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(configPath))
                {
                    File.Copy(configPath, backupConfigPath, true);
                    logBuilder?.AppendLine("[插件更新] 已备份现有预设配置");
                }

                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
                {
                    HttpResponseMessage response;
                    try
                    {
                        response = await client.GetAsync(ApiEndpoints.PluginProxyUrl,
                            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        logBuilder?.AppendLine("[插件更新] 主线路请求失败，正在尝试备用线路...");
                        response = await client.GetAsync(ApiEndpoints.PluginRawUrl,
                            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                    }

                    using (response)
                    using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                               8192, true))
                    {
                        await stream.CopyToAsync(fileStream, cancellationToken);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();

                if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                Directory.CreateDirectory(extractPath);
                await Task.Run(() => ZipFile.ExtractToDirectory(tempPath, extractPath));

                var subDirs = Directory.GetDirectories(extractPath);
                string sourceDir = (subDirs.Length == 1 && Directory.GetFiles(extractPath).Length == 0)
                    ? subDirs[0]
                    : extractPath;

                cancellationToken.ThrowIfCancellationRequested();

                await Task.Run(() =>
                {
                    if (Directory.Exists(targetDir)) Directory.Delete(targetDir, true);
                    Directory.CreateDirectory(targetDir);

                    foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                    {
                        Directory.CreateDirectory(dirPath.Replace(sourceDir, targetDir));
                    }

                    foreach (var newPath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
                    {
                        File.Copy(newPath, newPath.Replace(sourceDir, targetDir), true);
                    }
                });

                if (File.Exists(backupConfigPath))
                {
                    File.Copy(backupConfigPath, configPath, true);
                    logBuilder?.AppendLine("[插件更新] 已将插件默认配置替换为预设配置");
                }

                var installedDllPath = LightweightPluginService.MainPluginDllPath;
                if (File.Exists(installedDllPath) && !PluginGameVersionGuard.IsPluginUsable(installedDllPath))
                {
                    try
                    {
                        File.Move(installedDllPath, LightweightPluginService.MainPluginDisabledPath, true);
                        logBuilder?.AppendLine("[插件更新] 主插件不支持当前游戏版本，已自动禁用");
                    }
                    catch (Exception ex)
                    {
                        logBuilder?.AppendLine($"[插件更新] 主插件自动禁用失败: {ex.Message}");
                    }
                }

                logBuilder?.AppendLine("[插件更新] 主插件安装完成");
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logBuilder?.AppendLine($"[插件更新] 主插件安装失败: {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                    if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                    if (File.Exists(backupConfigPath)) File.Delete(backupConfigPath);
                }
                catch (Exception ex)
                {
                    DebugLog.Info("PluginUpdateService",
                        $"[PluginUpdate] Failed to clean temporary files: {ex.Message}");
                }
            }
        }
    }
}