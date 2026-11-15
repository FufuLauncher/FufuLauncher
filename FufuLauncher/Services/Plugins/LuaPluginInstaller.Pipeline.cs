/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Runtime.ExceptionServices;
using FufuLauncher.Helpers;
using MoonSharp.Interpreter;

namespace FufuLauncher.Services;

public partial class LuaPluginInstaller
{
    #region Script Execution Pipeline

    public async Task ExecuteUserScriptAsync(string luaScript,
        CancellationToken cancellationToken = default)
    {
        ClearCollectedLogs();

        LogMessage("Start");
        LogMessage($"Script length: {luaScript.Length} chars");
        LogMessage($"Sandbox directory: {_pluginsDir}");

        ReportProgress(0, "PluginInstaller_ExecutingScript".GetLocalized());

        try
        {
            await ExecuteScriptAsync(luaScript, cancellationToken);
            LogMessage("No exception");
            ReportProgress(100, "PluginInstaller_ScriptCompleted".GetLocalized());
        }
        catch (Exception ex)
        {
            LogMessage($"Script test aborted: {ex.Message}");
            ReportProgress(100, "PluginInstaller_ScriptFailed".GetLocalized());
            throw;
        }
    }

    public async Task ExecuteInstallScriptAsync(string luaScriptUrl,
        string? expectedLuaHash = null, string? expectedFileHash = null,
        CancellationToken cancellationToken = default,
        string? dllFileName = null, string? pluginId = null,
        string? dlToken = null, string? accessToken = null,
        string? downloadUrl = null, string? archivePassword = null, string? archiveFileName = null)
    {
        await _scriptGate.WaitAsync(cancellationToken);
        try
        {
            _expectedLuaHash = expectedLuaHash;
            _expectedFileHash = expectedFileHash;
            _dlToken = dlToken;
            _accessToken = accessToken;
            _pluginId = pluginId;
            _downloadUrl = downloadUrl;
            _installFailure = null;
            _archiveExtractor = new PluginArchiveExtractor(archivePassword, (fileName, retry, token) =>
                RequestArchivePasswordAsync(
                    string.IsNullOrWhiteSpace(archiveFileName) ? fileName : Path.GetFileName(archiveFileName), retry,
                    token));

            ReportProgress(0, "PluginStoreScriptDownloading".GetLocalized());
            LogMessage($"Downloading Lua script from: {luaScriptUrl}");

            var luaScript =
                await _storeService.DownloadLuaScriptAsync(luaScriptUrl, expectedLuaHash, dlToken, accessToken);

            ReportProgress(3, "PluginStoreScriptScanning".GetLocalized());
            LogMessage("Running Lua security validation...");
            var securityResult = PluginVerifier.ValidateLuaSecurity(luaScript);
            if (!securityResult.IsValid)
            {
                LogMessage($"SECURITY BLOCK: {securityResult.Reason}");
                throw new SecurityViolationException(securityResult.Reason ??
                                                     "PluginStoreLuaSecurityFailed".GetLocalized());
            }

            LogMessage("Lua security scan passed.");

            ReportProgress(5, "PluginStoreScriptExecuting".GetLocalized());
            LogMessage("Executing Lua install script...");

            await ExecuteScriptCoreAsync(luaScript, cancellationToken);

            if (!string.IsNullOrEmpty(pluginId))
            {
                var pluginDir = Path.Combine(_pluginsDir, pluginId);
                EnsureConfigFileEntry(pluginDir, dllFileName);
            }
        }
        finally
        {
            ResetInstallContext();
            _scriptGate.Release();
        }
    }

    public async Task ExecuteScriptAsync(string luaScript, CancellationToken cancellationToken = default)
    {
        await _scriptGate.WaitAsync(cancellationToken);
        try
        {
            ResetInstallContext();
            _archiveExtractor = new PluginArchiveExtractor(requestPassword: RequestArchivePasswordAsync);
            await ExecuteScriptCoreAsync(luaScript, cancellationToken);
        }
        finally
        {
            ResetInstallContext();
            _scriptGate.Release();
        }
    }

    private void ResetInstallContext()
    {
        foreach (var archive in _temporaryArchives)
        {
            try
            {
                if (File.Exists(archive)) File.Delete(archive);
            }
            catch (Exception ex)
            {
                DebugLog.Info("LuaPluginInstaller.Pipeline",
                    $"[LuaInstaller] Failed to clean temporary archive: {ex.Message}");
            }
        }

        _temporaryArchives.Clear();
        _downloadedFiles.Clear();
        _expectedLuaHash = null;
        _expectedFileHash = null;
        _dlToken = null;
        _accessToken = null;
        _pluginId = null;
        _downloadUrl = null;
        _installFailure = null;
        _archiveExtractor = new();
    }

    private async Task ExecuteScriptCoreAsync(string luaScript, CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            var script = new Script(CoreModules.None);

            RegisterInstallApi(script, cancellationToken);

            try
            {
                script.DoString(@"
                    ipairs = function(t)
                        local i = 0
                        return function()
                            i = i + 1
                            local v = t[i]
                            if v ~= nil then
                                return i, v
                            end
                        end, t, 0
                    end
                    pairs = function(t)
                        return next, t, nil
                    end
                ");
            }
            catch (InterpreterException ex)
            {
                DebugLog.Info("LuaPluginInstaller.Pipeline",
                    $"[LuaInstaller] Failed to inject Lua helpers: {ex.Message}");
            }

            try
            {
                script.DoString(luaScript);
                if (_installFailure != null)
                    ExceptionDispatchInfo.Capture(_installFailure).Throw();
            }
            catch (InterpreterException ex)
            {
                var cause = _installFailure ?? ex;
                while (cause.InnerException != null && cause is not (OperationCanceledException or
                           CaptchaRequiredException or PrivatePluginAccessException or HashMismatchException or
                           SecurityViolationException or PluginArchivePasswordException))
                    cause = cause.InnerException;
                if (cause is OperationCanceledException oce)
                {
                    DebugLog.Info("LuaPluginInstaller.Pipeline", $"[LuaInstaller] Lua script cancelled");
                    LogMessage("Script execution cancelled");
                    throw oce;
                }

                if (cause is CaptchaRequiredException or PrivatePluginAccessException or HashMismatchException or
                    SecurityViolationException or PluginArchivePasswordException)
                    ExceptionDispatchInfo.Capture(cause).Throw();

                DebugLog.Info("LuaPluginInstaller.Pipeline", $"[LuaInstaller] Lua error: {ex.Message}");
                LogMessage($"Lua script error: {ex.Message}");
                throw new InvalidOperationException(
                    string.Format("PluginStoreLuaScriptFailed".GetLocalized(), ex.Message), ex);
            }
        }, cancellationToken);
    }

    private void RegisterInstallApi(Script script, CancellationToken cancellationToken)
    {
        DynValue installTable = DynValue.NewTable(script);

        var table = installTable.Table;

        RegisterInstallHandlers(script, table, cancellationToken);
        RegisterFileOperationHandlers(table, cancellationToken);
        RegisterFileInfoHandlers(script, table, cancellationToken);
        RegisterUtilityHandlers(table, cancellationToken);
        RegisterUiHandlers(table);
        RegisterGlobalHandlers(script);

        script.Globals["install"] = installTable;

        DynValue systemTable = DynValue.NewTable(script);
        var sysTable = systemTable.Table;

        RegisterSystemHandlers(script, sysTable);

        script.Globals["system"] = systemTable;
    }

    #endregion
}