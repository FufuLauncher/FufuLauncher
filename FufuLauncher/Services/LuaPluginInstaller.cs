/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Messages;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MoonSharp.Interpreter;

namespace FufuLauncher.Services;

public class LuaPluginInstaller
{
    private readonly PluginStoreService _storeService;
    private string _pluginsDir;
    private string? _expectedFileHash;
    private string? _expectedLuaHash;
    public event Action<int, string>? ProgressChanged;
    public event Action<string>? LogReceived;
    
    public static DispatcherQueue? UIDispatcher { get; set; }
    
    public static XamlRoot? MainXamlRoot { get; set; }

    public LuaPluginInstaller(PluginStoreService storeService)
    {
        _storeService = storeService;
        _pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
    }
    
    public async Task ExecuteInstallScriptAsync(string luaScriptUrl,
        string? expectedLuaHash = null, string? expectedFileHash = null,
        CancellationToken cancellationToken = default,
        string? dllFileName = null, string? pluginId = null)
    {
        _expectedLuaHash = expectedLuaHash;
        _expectedFileHash = expectedFileHash;

        ReportProgress(0, "下载安装脚本...");
        LogMessage($"Downloading Lua script from: {luaScriptUrl}");

        var luaScript = await _storeService.DownloadLuaScriptAsync(luaScriptUrl, expectedLuaHash);
        
        ReportProgress(3, "扫描中...");
        LogMessage("Running Lua security validation...");
        var securityResult = PluginVerifier.ValidateLuaSecurity(luaScript);
        if (!securityResult.IsValid)
        {
            LogMessage($"SECURITY BLOCK: {securityResult.Reason}");
            throw new SecurityViolationException(securityResult.Reason ?? "Lua脚本未通过安全验证。");
        }
        LogMessage("Lua security scan passed.");

        ReportProgress(5, "执行安装脚本...");
        LogMessage("Executing Lua install script...");

        await ExecuteScriptAsync(luaScript, cancellationToken);

        // 安装脚本执行完成后，检查并补全 config.ini 的 File 字段
        if (!string.IsNullOrEmpty(pluginId))
        {
            var pluginDir = Path.Combine(_pluginsDir, pluginId);
            EnsureConfigFileEntry(pluginDir, dllFileName);
        }
    }
    
    public async Task ExecuteScriptAsync(string luaScript, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            var script = new Script(CoreModules.None);
            
            RegisterInstallApi(script, cancellationToken);

            try
            {
                script.DoString(luaScript);
            }
            catch (InterpreterException ex)
            {
                Debug.WriteLine($"[LuaInstaller] Lua error: {ex.Message}");
                LogMessage($"Lua脚本错误: {ex.Message}");
                throw new InvalidOperationException($"Lua脚本执行失败: {ex.Message}", ex);
            }
        }, cancellationToken);
    }

    private void RegisterInstallApi(Script script, CancellationToken cancellationToken)
    {
        DynValue installTable = DynValue.NewTable(script);

        var table = installTable.Table;
        
        table["download"] = (Action<string, string>)((url, path) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var safePath = SanitizePath(path, "download");
            LogMessage($"下载: {url} -> {safePath}");
            
            _storeService.DownloadFileAsync(url, safePath,
                new Progress<(int percent, string status)>(p =>
                {
                    ReportProgress(5 + p.percent * 70 / 100, p.status);
                }),
                _expectedFileHash).GetAwaiter().GetResult();
        });
        
        table["extract"] = (Action<string, string>)((zipPath, destDir) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var safeZipPath = SanitizePath(zipPath, "extract source");
            var safeDestDir = SanitizePath(destDir, "extract destination");
            LogMessage($"解压: {safeZipPath} -> {safeDestDir}");

            if (!Directory.Exists(safeDestDir))
                Directory.CreateDirectory(safeDestDir);

            ZipFile.ExtractToDirectory(safeZipPath, safeDestDir, true);
            LogMessage("解压完成");
        });
        
        table["create_dir"] = (Action<string>)(path =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safePath = SanitizePath(path, "create_dir");
            LogMessage($"创建目录: {safePath}");
            if (!Directory.Exists(safePath))
                Directory.CreateDirectory(safePath);
        });
        
        table["delete"] = (Action<string>)(path =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safePath = SanitizePath(path, "delete");
            LogMessage($"删除: {safePath}");

            if (File.Exists(safePath))
                File.Delete(safePath);
            else if (Directory.Exists(safePath))
                Directory.Delete(safePath, true);
        });
        
        table["get_plugins_dir"] = (Func<string>)(() =>
        {
            return _pluginsDir;
        });
        
        table["log"] = (Action<string>)(msg =>
        {
            LogMessage(msg);
        });
        
        table["set_progress"] = (Action<int, string>)((percent, status) =>
        {
            ReportProgress(Math.Clamp(percent, 0, 100), status);
        });
        
        table["write_config"] = (Action<string, DynValue>)((dir, value) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safeDir = SanitizePath(dir, "write_config");
            LogMessage($"写入配置: {safeDir}");

            var configPath = Path.Combine(safeDir, "config.ini");

            var iniLines = new StringBuilder();
            if (value.Type == DataType.Table)
            {
                foreach (var sectionPair in value.Table.Pairs)
                {
                    var sectionName = sectionPair.Key.String;
                    var sectionTable = sectionPair.Value.Table;

                    iniLines.AppendLine($"[{sectionName}]");
                    foreach (var kvp in sectionTable.Pairs)
                    {
                        var key = kvp.Key.String;
                        var val = kvp.Value.String;
                        iniLines.AppendLine($"{key} = {val}");
                    }
                    iniLines.AppendLine();
                }
            }

            File.WriteAllText(configPath, iniLines.ToString(), Encoding.UTF8);
            LogMessage("配置写入完成");
        });
        
        table["verify_file_hash"] = (Func<string, string, bool>)((path, expectedHash) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var safePath = SanitizePath(path, "verify_file_hash");
            LogMessage($"验证文件哈希: {safePath}");

            try
            {
                PluginVerifier.VerifyFileHash(safePath, expectedHash, Path.GetFileName(safePath));
                LogMessage("文件哈希验证通过");
                return true;
            }
            catch (HashMismatchException ex)
            {
                LogMessage($"文件哈希验证失败: {ex.Message}");
                return false;
            }
        });
        
        table["show_notification"] = (Action<string, string, string, int>)((title, message, typeStr, duration) =>
        {
            LogMessage($"通知: [{typeStr}] {title} - {message}");

            var type = typeStr?.ToLowerInvariant() switch
            {
                "success" => NotificationType.Success,
                "warning" => NotificationType.Warning,
                "error" => NotificationType.Error,
                _ => NotificationType.Information
            };

            if (duration <= 0) duration = 5000;

            WeakReferenceMessenger.Default.Send(new NotificationMessage(title, message, type, duration));
        });
        
        table["show_dialog"] = (Func<string, string, string, string, string, string>)((title, content, primaryText, secondaryText, closeText) =>
        {
            LogMessage($"弹窗: {title}");

            var dispatcher = UIDispatcher;
            var xamlRoot = MainXamlRoot;

            if (dispatcher == null)
            {
                LogMessage("弹窗失败: UI 调度器未初始化");
                return "none";
            }

            var tcs = new TaskCompletionSource<string>();

            dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    var dialog = new ContentDialog
                    {
                        Title = title,
                        Content = content,
                        XamlRoot = xamlRoot,
                        DefaultButton = ContentDialogButton.Primary
                    };

                    if (!string.IsNullOrEmpty(primaryText))
                        dialog.PrimaryButtonText = primaryText;
                    if (!string.IsNullOrEmpty(secondaryText))
                        dialog.SecondaryButtonText = secondaryText;
                    if (!string.IsNullOrEmpty(closeText))
                        dialog.CloseButtonText = closeText;
                    else
                        dialog.CloseButtonText = "关闭";

                    var result = await dialog.ShowAsync();
                    tcs.TrySetResult(result.ToString().ToLowerInvariant());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[LuaInstaller] Dialog error: {ex.Message}");
                    tcs.TrySetResult("error");
                }
            });

            try
            {
                return tcs.Task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LuaInstaller] Dialog wait error: {ex.Message}");
                return "error";
            }
        });
        
        script.Globals["install"] = installTable;
    }
    
    private string SanitizePath(string rawPath, string operation)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            throw new SecurityViolationException($"安全: {operation} 操作传入了空路径。");
        }
        
        if (rawPath.Contains(".."))
        {
            Debug.WriteLine($"[LuaInstaller] SECURITY: Path traversal attempt blocked in {operation}: {rawPath}");
            throw new SecurityViolationException(
                $"安全: 已阻止路径穿越尝试 ({operation})。");
        }
        
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rawPath);
        }
        catch (Exception ex)
        {
            throw new SecurityViolationException(
                $"安全: 无效路径 ({operation}): {ex.Message}");
        }
        
        var pluginsDirFull = Path.GetFullPath(_pluginsDir);
        
        if (!fullPath.StartsWith(pluginsDirFull, StringComparison.OrdinalIgnoreCase))
        {
            Debug.WriteLine($"[LuaInstaller] SECURITY: Path outside plugins dir blocked in {operation}: {fullPath}");
            throw new SecurityViolationException(
                $"安全: 路径必须在插件目录内 ({operation})。");
        }

        return fullPath;
    }
    
    public void EnsureConfigFileEntry(string pluginDir, string? dllFileName = null)
    {
        if (string.IsNullOrWhiteSpace(pluginDir) || !Directory.Exists(pluginDir))
            return;

        var configPath = Path.Combine(pluginDir, "config.ini");
        
        if (!File.Exists(configPath))
        {
            var resolvedDll = ResolveDllFileName(pluginDir, dllFileName);
            if (string.IsNullOrEmpty(resolvedDll)) return;

            var content = $"[General]\nName = {Path.GetFileName(pluginDir)}\nFile = {resolvedDll}\n";
            File.WriteAllText(configPath, content, Encoding.UTF8);
            LogMessage($"已创建 config.ini 并写入 File = {resolvedDll}");
            return;
        }
        
        var lines = File.ReadAllLines(configPath, Encoding.UTF8);
        bool inGeneral = false;
        bool hasFileEntry = false;
        int generalEndIndex = -1;

        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                if (inGeneral)
                {
                    generalEndIndex = i;
                    break;
                }
                inGeneral = trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inGeneral)
            {
                var separatorIndex = trimmed.IndexOf('=');
                if (separatorIndex > 0)
                {
                    var key = trimmed.Substring(0, separatorIndex).Trim();
                    if (key.Equals("File", StringComparison.OrdinalIgnoreCase))
                    {
                        var val = trimmed.Substring(separatorIndex + 1).Trim();
                        if (!string.IsNullOrWhiteSpace(val))
                        {
                            hasFileEntry = true;
                            break;
                        }
                    }
                }
            }
        }

        if (hasFileEntry) return;
        
        var dllName = ResolveDllFileName(pluginDir, dllFileName);
        if (string.IsNullOrEmpty(dllName)) return;

        var lineList = new List<string>(lines);
        var insertLine = $"File = {dllName}";

        if (generalEndIndex > 0)
        {
            lineList.Insert(generalEndIndex, insertLine);
        }
        else if (inGeneral)
        {
            lineList.Add(insertLine);
        }
        else
        {
            lineList.Insert(0, "[General]");
            lineList.Insert(1, insertLine);
            lineList.Insert(2, "");
        }

        File.WriteAllLines(configPath, lineList, Encoding.UTF8);
        LogMessage($"已补全 config.ini File = {dllName}");
    }

    private static string? ResolveDllFileName(string pluginDir, string? dllFileName)
    {
        if (!string.IsNullOrWhiteSpace(dllFileName))
            return dllFileName;

        var dllFile = Directory.GetFiles(pluginDir, "*.dll", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => !f.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase));

        return dllFile != null ? Path.GetFileName(dllFile) : null;
    }

    private void ReportProgress(int percent, string status)
    {
        Debug.WriteLine($"[LuaInstaller] Progress {percent}%: {status}");
        ProgressChanged?.Invoke(percent, status);
    }

    private void LogMessage(string message)
    {
        Debug.WriteLine($"[LuaInstaller] {message}");
        LogReceived?.Invoke(message);
    }
}
