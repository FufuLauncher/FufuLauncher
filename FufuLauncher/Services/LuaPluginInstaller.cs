/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
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

    public LuaPluginInstaller(PluginStoreService storeService)
    {
        _storeService = storeService;
        _pluginsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
    }
    
    public async Task ExecuteInstallScriptAsync(string luaScriptUrl,
        string? expectedLuaHash = null, string? expectedFileHash = null,
        CancellationToken cancellationToken = default)
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
