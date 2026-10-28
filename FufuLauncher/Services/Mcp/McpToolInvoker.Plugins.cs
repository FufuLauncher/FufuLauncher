/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text;
using System.Text.Json;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services.Mcp;

public sealed partial class McpToolInvoker
{
    private sealed record PluginSnapshot(
        string Id,
        string FolderPath,
        string Name,
        string Developer,
        string Description,
        string Version,
        string FileName,
        string FullPath,
        bool Enabled,
        bool Official,
        string? ConfigPath,
        long FileSize,
        DateTime Modified);

    private Task<McpToolResult> ListPluginsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plugins = ScanPlugins();

        return Task.FromResult(McpToolResult.Success(new
        {
            pluginsDirectory = PluginsDirectory,
            count = plugins.Count,
            plugins = plugins.Select(plugin => new
            {
                id = plugin.Id,
                name = plugin.Name,
                enabled = plugin.Enabled,
                official = plugin.Official,
                fileName = plugin.FileName,
                folderPath = plugin.FolderPath,
                fullPath = plugin.FullPath,
                version = plugin.Version,
                gameVersionSupport = _pluginGameVersionGuard
                    .Inspect(plugin.FolderPath, plugin.ConfigPath).Support.ToString(),
                developer = plugin.Developer,
                description = plugin.Description,
                hasConfig = plugin.ConfigPath != null,
                configPath = plugin.ConfigPath,
                fileSizeBytes = plugin.FileSize,
                modifiedAt = plugin.Modified.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            })
        }));
    }

    private Task<McpToolResult> SetPluginEnabledAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selector = RequiredString(arguments, "plugin");
        var enabled = RequiredBool(arguments, "enabled");

        var plugin = ResolvePlugin(ScanPlugins(), selector) ??
                     throw new McpToolException(
                         $"Plugin not found: {selector}. Call list_plugins to see installed plugins.");

        if (plugin.Enabled == enabled)
        {
            return Task.FromResult(McpToolResult.Success(new
            {
                changed = false,
                plugin = plugin.Id,
                name = plugin.Name,
                enabled = plugin.Enabled,
                message = "Plugin is already in the requested state."
            }));
        }

        if (enabled)
        {
            var folderName = Path.GetFileName(plugin.FolderPath);
            if (_constraintService.IsRestricted &&
                LightweightPluginService.IsMainPluginPackage(folderName, plugin.FileName))
            {
                throw new McpToolException(
                    $"Enabling this plugin is blocked by the launcher policy: {_constraintService.BlockMessage}");
            }

            var blockReason = _lightweightPlugin.GetInstallBlockReason(folderName, plugin.FileName);
            if (blockReason != null)
            {
                throw new McpToolException($"Plugin operation is blocked by the launcher settings: {blockReason}");
            }

            var versionStatus = _pluginGameVersionGuard.Inspect(plugin.FolderPath, plugin.ConfigPath);
            if (versionStatus.IsBlocked)
            {
                throw new McpToolException(versionStatus.Support == PluginGameVersionSupport.Malformed
                    ? $"Enabling this plugin is blocked: its game version requirement cannot be parsed " +
                      $"('{versionStatus.DeclaredRule}', unrecognized part: '{versionStatus.InvalidSegment}')."
                    : $"Enabling this plugin is blocked: installed game version '{versionStatus.GameVersion}' " +
                      $"is outside the plugin requirement '{versionStatus.DeclaredRule}'.");
            }
        }

        var currentPath = plugin.FullPath;
        if (!enabled && !currentPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException("This file is not a toggleable plugin assembly.");
        }

        if (enabled && !currentPath.EndsWith(".dll.disabled", StringComparison.OrdinalIgnoreCase))
        {
            throw new McpToolException("This file is not a toggleable plugin assembly.");
        }

        var targetPath = enabled
            ? currentPath[..^".disabled".Length]
            : currentPath + ".disabled";

        if (File.Exists(targetPath))
        {
            throw new McpToolException("The target file already exists; plugin state cannot be changed.");
        }

        try
        {
            File.Move(currentPath, targetPath);
        }
        catch (Exception ex)
        {
            var lockedFile = FileLockHelper.FindLockedFile(currentPath, targetPath);
            throw lockedFile != null
                ? new McpToolException($"The plugin file is in use: {FileLockHelper.GetLockedFileMessage(lockedFile)}")
                : new McpToolException($"Failed to change plugin state: {ex.Message}");
        }

        var refreshed = ScanPlugins()
            .FirstOrDefault(item => string.Equals(item.Id, plugin.Id, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(McpToolResult.Success(new
        {
            changed = true,
            plugin = plugin.Id,
            name = plugin.Name,
            enabled = refreshed?.Enabled ?? enabled,
            fileName = refreshed?.FileName ?? Path.GetFileName(targetPath)
        }));
    }

    private async Task<McpToolResult> GetPluginConfigAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var plugin = ResolveRequiredPlugin(arguments);
        var configPath = RequireConfigPath(plugin);

        var configuration = await PluginConfiguration.LoadAsync(configPath, cancellationToken)
            .ConfigureAwait(false);

        var options = configuration.Options.Select(option => new
        {
            section = option.SectionName,
            name = option.DisplayName,
            type = option.Kind.ToString().ToLowerInvariant(),
            value = option.Value,
            description = option.Description,
            choices = option.Kind == PluginConfigValueKind.Enumeration && option.Choices.Count > 0
                ? option.Choices.Select(choice => new { value = choice.Value, label = choice.Label }).ToList()
                : null
        }).ToList();

        return McpToolResult.Success(new
        {
            plugin = plugin.Id,
            name = plugin.Name,
            configPath,
            info = configuration.GeneralInfo,
            optionCount = options.Count,
            options
        });
    }

    private async Task<McpToolResult> SetPluginConfigAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var plugin = ResolveRequiredPlugin(arguments);
        var configPath = RequireConfigPath(plugin);
        var section = RequiredString(arguments, "section");
        var rawValue = GetScalarValue(arguments, "value") ??
                       throw new McpToolException("Missing required argument: value");

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var configuration = await PluginConfiguration.LoadAsync(configPath, cancellationToken)
                .ConfigureAwait(false);

            var option = configuration.Options.FirstOrDefault(item =>
                             string.Equals(item.SectionName, section, StringComparison.OrdinalIgnoreCase))
                         ?? throw new McpToolException(
                             $"Plugin configuration has no option named \"{section}\". Only existing options can be changed; adding options or editing the config file directly is not supported.");

            var oldValue = option.Value;
            var normalized = NormalizeOptionValue(option, rawValue);

            if (string.Equals(normalized, oldValue, StringComparison.Ordinal))
            {
                return McpToolResult.Success(new
                {
                    changed = false,
                    plugin = plugin.Id,
                    section = option.SectionName,
                    value = oldValue
                });
            }

            option.Value = normalized;
            await configuration.SaveAsync(configPath).ConfigureAwait(false);

            return McpToolResult.Success(new
            {
                changed = true,
                plugin = plugin.Id,
                section = option.SectionName,
                oldValue,
                newValue = option.Value
            });
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private async Task<McpToolResult> SearchPluginStoreAsync(JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var query = OptionalString(arguments, "query");
        var category = OptionalString(arguments, "category");
        var page = OptionalInt(arguments, "page", 1, 1, 1000);
        var pageSize = OptionalInt(arguments, "pageSize", 20, 1, 50);

        PluginListData data;
        try
        {
            data = await _pluginStore
                .GetPluginListAsync(category, query, "popular", page, pageSize)
                .WaitAsync(TimeSpan.FromSeconds(45), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (McpToolException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new McpToolException($"Plugin store request failed: {ex.Message}");
        }

        var installedIds = ScanPlugins().Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return McpToolResult.Success(new
        {
            total = data.Total,
            page = data.Page,
            plugins = data.Plugins.Select(item => new
            {
                id = item.Id,
                name = item.Name,
                developer = item.Developer,
                version = item.Version,
                description = item.Description,
                category = item.Category,
                downloads = item.Downloads,
                sizeBytes = item.SizeBytes,
                minAppVersion = item.MinAppVersion,
                isPrivate = item.IsPrivate,
                installed = installedIds.Contains(item.Id)
            })
        });
    }

    private Task<McpToolResult> InstallPluginAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var pluginId = OptionalString(arguments, "pluginId");
        var localPath = OptionalString(arguments, "path");
        var url = OptionalString(arguments, "url");
        var accessKey = OptionalString(arguments, "accessKey");
        var overwrite = OptionalBool(arguments, "overwrite", false);

        var sources = new[] { pluginId, localPath, url }.Count(value => !string.IsNullOrWhiteSpace(value));
        if (sources != 1)
        {
            throw new McpToolException("Provide exactly one install source: pluginId, path or url.");
        }

        var snapshot = !string.IsNullOrWhiteSpace(pluginId)
            ? _installJobs.StartStoreInstall(pluginId.Trim(), accessKey)
            : !string.IsNullOrWhiteSpace(localPath)
                ? _installJobs.StartPathInstall(localPath.Trim(), overwrite)
                : _installJobs.StartUrlInstall(url!.Trim(), overwrite);

        return Task.FromResult(McpToolResult.Success(DescribeInstallJob(snapshot)));
    }

    private Task<McpToolResult> GetInstallStatusAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobId = OptionalString(arguments, "jobId");

        if (!string.IsNullOrWhiteSpace(jobId))
        {
            var job = _installJobs.GetJob(jobId.Trim()) ??
                      throw new McpToolException($"Unknown installation job: {jobId}");
            return Task.FromResult(McpToolResult.Success(DescribeInstallJob(job)));
        }

        var jobs = _installJobs.GetJobs(10).Select(DescribeInstallJob).ToList();
        return Task.FromResult(McpToolResult.Success(new { jobs }));
    }

    private Task<McpToolResult> CancelInstallAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobId = RequiredString(arguments, "jobId");
        var cancelRequested = _installJobs.Cancel(jobId);
        var job = _installJobs.GetJob(jobId);

        return Task.FromResult(McpToolResult.Success(new
        {
            jobId,
            cancelRequested,
            status = job?.Status,
            message = cancelRequested
                ? "Cancellation requested."
                : "The job was not found or has already finished."
        }));
    }

    private static object DescribeInstallJob(McpInstallJobSnapshot job) => new
    {
        jobId = job.JobId,
        status = job.Status,
        progressPercent = job.ProgressPercent,
        message = job.Message,
        pluginId = job.PluginId,
        pluginName = job.PluginName,
        captchaUrl = job.CaptchaUrl,
        error = job.Error,
        startedAt = job.StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
        updatedAt = job.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss")
    };

    private PluginSnapshot ResolveRequiredPlugin(JsonElement arguments)
    {
        var selector = RequiredString(arguments, "plugin");
        return ResolvePlugin(ScanPlugins(), selector) ??
               throw new McpToolException($"Plugin not found: {selector}. Call list_plugins to see installed plugins.");
    }

    private static string RequireConfigPath(PluginSnapshot plugin)
    {
        if (string.IsNullOrWhiteSpace(plugin.ConfigPath) || !File.Exists(plugin.ConfigPath))
        {
            throw new McpToolException(
                $"Plugin \"{plugin.Id}\" has no config file; configuration cannot be read or changed.");
        }

        return plugin.ConfigPath;
    }

    private static string NormalizeOptionValue(PluginConfigOption option, string value)
    {
        var trimmed = value.Trim();
        switch (option.Kind)
        {
            case PluginConfigValueKind.Boolean:
                if (bool.TryParse(trimmed, out var boolean))
                {
                    return boolean ? "1" : "0";
                }

                if (trimmed is "1" or "0")
                {
                    return trimmed;
                }

                throw new McpToolException($"Option \"{option.SectionName}\" is boolean; use true or false.");

            case PluginConfigValueKind.Integer:
                if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                {
                    throw new McpToolException($"Option \"{option.SectionName}\" requires an integer.");
                }

                return integer.ToString(CultureInfo.InvariantCulture);

            case PluginConfigValueKind.Number:
                if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
                    !double.IsFinite(number))
                {
                    throw new McpToolException($"Option \"{option.SectionName}\" requires a number.");
                }

                return number.ToString("G", CultureInfo.InvariantCulture);

            case PluginConfigValueKind.Key:
                if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var keyCode))
                {
                    throw new McpToolException(
                        $"Option \"{option.SectionName}\" requires an integer virtual-key code.");
                }

                return keyCode.ToString(CultureInfo.InvariantCulture);

            case PluginConfigValueKind.Enumeration:
                var byValue = option.Choices.FirstOrDefault(choice =>
                    string.Equals(choice.Value, trimmed, StringComparison.OrdinalIgnoreCase));
                if (byValue != null)
                {
                    return byValue.Value;
                }

                var byLabel = option.Choices.FirstOrDefault(choice =>
                    string.Equals(choice.Label, trimmed, StringComparison.OrdinalIgnoreCase));
                if (byLabel != null)
                {
                    return byLabel.Value;
                }

                var acceptedValues = string.Join(", ", option.Choices.Select(choice => choice.Value));
                throw new McpToolException($"Option \"{option.SectionName}\" accepts only: {acceptedValues}");

            default:
                if (trimmed.Length > 2048)
                {
                    throw new McpToolException(
                        $"Option \"{option.SectionName}\" text is too long (max 2048 characters).");
                }

                if (trimmed.Contains('\r') || trimmed.Contains('\n') || trimmed.Contains('\0'))
                {
                    throw new McpToolException($"Option \"{option.SectionName}\" does not support multi-line text.");
                }

                return trimmed;
        }
    }

    private static PluginSnapshot? ResolvePlugin(IReadOnlyList<PluginSnapshot> plugins, string selector)
    {
        var value = selector.Trim();
        return plugins.FirstOrDefault(item => string.Equals(item.Id, value, StringComparison.OrdinalIgnoreCase))
               ?? plugins.FirstOrDefault(item => string.Equals(item.Name, value, StringComparison.OrdinalIgnoreCase))
               ?? plugins.FirstOrDefault(item =>
                   string.Equals(item.FileName, value, StringComparison.OrdinalIgnoreCase))
               ?? plugins.FirstOrDefault(item =>
                   string.Equals(Path.GetFileNameWithoutExtension(item.FileName), value,
                       StringComparison.OrdinalIgnoreCase));
    }

    private static List<PluginSnapshot> ScanPlugins()
    {
        var plugins = new List<PluginSnapshot>();
        var root = PluginsDirectory;
        if (!Directory.Exists(root))
        {
            return plugins;
        }

        foreach (var directory in Directory.GetDirectories(root))
        {
            try
            {
                var id = Path.GetFileName(directory);
                var enabledFile = Directory.GetFiles(directory, "*.dll").FirstOrDefault();
                var disabledFile = Directory.GetFiles(directory, "*.dll.disabled").FirstOrDefault();
                var targetFile = enabledFile ?? disabledFile;
                if (targetFile == null)
                {
                    continue;
                }

                var configPath = Directory.GetFiles(directory, "config.ini").FirstOrDefault()
                                 ?? Directory.GetFiles(directory, "*.ini").FirstOrDefault();
                var info = ReadGeneralInfo(configPath);
                var file = new FileInfo(targetFile);

                plugins.Add(new PluginSnapshot(
                    id,
                    directory,
                    GetInfo(info, "Name", id),
                    GetInfo(info, "Developer", string.Empty),
                    GetInfo(info, "Description", string.Empty),
                    GetInfo(info, "Version", string.Empty),
                    Path.GetFileName(targetFile),
                    targetFile,
                    enabledFile != null,
                    IsOfficialFolder(id),
                    configPath,
                    file.Exists ? file.Length : 0,
                    file.Exists ? file.LastWriteTime : DateTime.MinValue));
            }
            catch (Exception ex)
            {
                DebugLog.Info("McpToolInvoker", $"[Mcp] 读取插件目录失败：{directory} - {ex.Message}");
            }
        }

        return plugins
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool IsOfficialFolder(string folderName) =>
        string.Equals(folderName, LightweightPluginService.MainPluginFolderName,
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(folderName, LightweightPluginService.LitePluginFolderName,
            StringComparison.OrdinalIgnoreCase);

    private static string GetInfo(IReadOnlyDictionary<string, string> info, string key, string fallback) =>
        info.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static Dictionary<string, string> ReadGeneralInfo(string? configPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return result;
        }

        try
        {
            var inGeneral = false;
            foreach (var line in File.ReadAllLines(configPath, Encoding.UTF8))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('#'))
                {
                    continue;
                }

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inGeneral = trimmed.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inGeneral)
                {
                    continue;
                }

                var separator = trimmed.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                result[trimmed[..separator].Trim()] = trimmed[(separator + 1)..].Trim();
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpToolInvoker", $"[Mcp] 读取插件信息失败：{configPath} - {ex.Message}");
        }

        return result;
    }
}