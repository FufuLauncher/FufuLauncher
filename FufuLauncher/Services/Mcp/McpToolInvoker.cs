/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Globalization;
using System.Text.Json;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services.GameServer;

namespace FufuLauncher.Services.Mcp;

public sealed partial class McpToolInvoker
{
    private readonly IGameLauncherService _gameLauncher;
    private readonly GameSessionService _gameSession;
    private readonly GameServerConfigurationService _serverConfiguration;
    private readonly GameChannelSdkService _channelSdk;
    private readonly PluginStoreService _pluginStore;
    private readonly McpInstallJobService _installJobs;
    private readonly LightweightPluginService _lightweightPlugin;
    private readonly PluginGameVersionGuard _pluginGameVersionGuard;
    private readonly ConstraintService _constraintService;
    private readonly IUpdateService _updateService;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public McpToolInvoker(
        IGameLauncherService gameLauncher,
        GameSessionService gameSession,
        GameServerConfigurationService serverConfiguration,
        GameChannelSdkService channelSdk,
        PluginStoreService pluginStore,
        McpInstallJobService installJobs,
        LightweightPluginService lightweightPlugin,
        PluginGameVersionGuard pluginGameVersionGuard,
        ConstraintService constraintService,
        IUpdateService updateService)
    {
        _gameLauncher = gameLauncher;
        _gameSession = gameSession;
        _serverConfiguration = serverConfiguration;
        _channelSdk = channelSdk;
        _pluginStore = pluginStore;
        _installJobs = installJobs;
        _lightweightPlugin = lightweightPlugin;
        _pluginGameVersionGuard = pluginGameVersionGuard;
        _constraintService = constraintService;
        _updateService = updateService;
    }

    public Task<McpToolResult> InvokeAsync(string toolId, JsonElement arguments,
        CancellationToken cancellationToken)
    {
        return toolId switch
        {
            "list_plugins" => ListPluginsAsync(cancellationToken),
            "set_plugin_enabled" => SetPluginEnabledAsync(arguments, cancellationToken),
            "get_plugin_config" => GetPluginConfigAsync(arguments, cancellationToken),
            "set_plugin_config" => SetPluginConfigAsync(arguments, cancellationToken),
            "search_plugin_store" => SearchPluginStoreAsync(arguments, cancellationToken),
            "install_plugin" => InstallPluginAsync(arguments, cancellationToken),
            "get_install_status" => GetInstallStatusAsync(arguments, cancellationToken),
            "cancel_install" => CancelInstallAsync(arguments, cancellationToken),
            "get_game_status" => GetGameStatusAsync(cancellationToken),
            "start_game" => StartGameAsync(cancellationToken),
            "stop_game" => StopGameAsync(cancellationToken),
            "list_servers" => ListServersAsync(cancellationToken),
            "switch_server" => SwitchServerAsync(arguments, cancellationToken),
            "open_page" => OpenPageAsync(arguments, cancellationToken),
            "open_tool_window" => OpenToolWindowAsync(arguments, cancellationToken),
            "set_window_state" => SetWindowStateAsync(arguments, cancellationToken),
            "read_crash_log" => ReadCrashLogAsync(arguments, cancellationToken),
            "check_update" => CheckUpdateAsync(arguments, cancellationToken),
            _ => throw new McpToolException($"Unknown tool: {toolId}")
        };
    }

    private static JsonElement? GetProperty(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value;
    }

    private static string? OptionalString(JsonElement arguments, string name)
    {
        var element = GetProperty(arguments, name);
        if (element == null)
        {
            return null;
        }

        return element.Value.ValueKind switch
        {
            JsonValueKind.String => element.Value.GetString(),
            JsonValueKind.Number => element.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string RequiredString(JsonElement arguments, string name)
    {
        var value = OptionalString(arguments, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new McpToolException($"缺少必填参数：{name}");
        }

        return value.Trim();
    }

    private static bool OptionalBool(JsonElement arguments, string name, bool fallback)
    {
        var element = GetProperty(arguments, name);
        if (element == null)
        {
            return fallback;
        }

        return element.Value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(element.Value.GetString(), out var parsed) ? parsed : fallback,
            JsonValueKind.Number => element.Value.TryGetDouble(out var number) && Math.Abs(number) > 0.0001,
            _ => fallback
        };
    }

    private static bool RequiredBool(JsonElement arguments, string name)
    {
        if (GetProperty(arguments, name) == null)
        {
            throw new McpToolException($"缺少必填参数：{name}");
        }

        return OptionalBool(arguments, name, false);
    }

    private static int OptionalInt(JsonElement arguments, string name, int fallback, int minimum, int maximum)
    {
        var element = GetProperty(arguments, name);
        if (element == null)
        {
            return fallback;
        }

        var value = fallback;
        if (element.Value.ValueKind == JsonValueKind.Number && element.Value.TryGetInt32(out var number))
        {
            value = number;
        }
        else if (element.Value.ValueKind == JsonValueKind.String &&
                 int.TryParse(element.Value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                     out var parsed))
        {
            value = parsed;
        }

        return Math.Clamp(value, minimum, maximum);
    }

    private static string? GetScalarValue(JsonElement arguments, string name)
    {
        var element = GetProperty(arguments, name);
        if (element == null)
        {
            return null;
        }

        return element.Value.ValueKind switch
        {
            JsonValueKind.String => element.Value.GetString(),
            JsonValueKind.Number => element.Value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string PluginsDirectory => Path.Combine(AppContext.BaseDirectory, "Plugins");
}