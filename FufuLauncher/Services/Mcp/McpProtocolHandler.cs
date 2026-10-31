/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Mcp;

internal sealed class McpProtocolHandler
{
    private const string LatestProtocolVersion = "2025-06-18";

    private static readonly string[] SupportedProtocolVersions =
    [
        "2024-11-05",
        "2025-03-26",
        "2025-06-18"
    ];

    private readonly McpHostService _host;
    private readonly McpToolInvoker _invoker;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _concurrency = new(8, 8);

    public McpProtocolHandler(McpHostService host, McpToolInvoker invoker)
    {
        _host = host;
        _invoker = invoker;
    }

    public async Task<McpHttpResponse> HandleAsync(McpHttpRequest request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Path, "/mcp", StringComparison.OrdinalIgnoreCase))
        {
            return McpHttpResponse.Json(404,
                PlainError(-32601, "Not Found. The MCP endpoint is POST /mcp."));
        }

        var remoteAddress = McpIpFilter.Describe(request.RemoteAddress);

        if (!_host.IsConnectionAllowed(request.RemoteAddress))
        {
            _host.Audit.Record(McpAuditKind.Connection, remoteAddress, McpAuditActions.IpBlocked, false, 0);
            return McpHttpResponse.Json(403, PlainError(-32003, "Forbidden. This address is not allowed."));
        }

        if (!_host.ValidateToken(ExtractToken(request)))
        {
            _host.Audit.Record(McpAuditKind.Connection, remoteAddress, McpAuditActions.AuthFailed, false, 0);
            return McpHttpResponse.Json(401,
                PlainError(-32001, "Unauthorized. Provide the launcher MCP access token."),
                new Dictionary<string, string> { ["WWW-Authenticate"] = "Bearer" });
        }

        switch (request.Method)
        {
            case "POST":
                return await HandlePostAsync(request, remoteAddress, cancellationToken).ConfigureAwait(false);

            case "DELETE":
            {
                var sessionId = request.Header("Mcp-Session-Id");
                if (!string.IsNullOrEmpty(sessionId))
                {
                    _sessions.TryRemove(sessionId, out _);
                }

                return McpHttpResponse.Empty(204);
            }

            case "GET":
            case "OPTIONS":
                return McpHttpResponse.Empty(405, new Dictionary<string, string> { ["Allow"] = "POST, DELETE" });

            default:
                return McpHttpResponse.Empty(405, new Dictionary<string, string> { ["Allow"] = "POST, DELETE" });
        }
    }

    private async Task<McpHttpResponse> HandlePostAsync(McpHttpRequest request, string remoteAddress,
        CancellationToken cancellationToken)
    {
        string bodyText;
        try
        {
            bodyText = Encoding.UTF8.GetString(request.Body);
        }
        catch (Exception ex)
        {
            return McpHttpResponse.Json(400, PlainError(-32700, $"Parse error: {ex.Message}"));
        }

        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return McpHttpResponse.Empty(202);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bodyText);
        }
        catch (JsonException ex)
        {
            return McpHttpResponse.Json(400, PlainError(-32700, $"Parse error: {ex.Message}"));
        }

        using (document)
        {
            var isBatch = document.RootElement.ValueKind == JsonValueKind.Array;
            var messages = isBatch
                ? document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList()
                : new List<JsonElement> { document.RootElement.Clone() };

            var sessionId = request.Header("Mcp-Session-Id");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                sessionId = Guid.NewGuid().ToString("N");
            }

            var responses = new List<JsonElement>();
            await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var message in messages)
                {
                    var response = await ProcessMessageAsync(message, sessionId, remoteAddress, cancellationToken)
                        .ConfigureAwait(false);
                    if (response.HasValue)
                    {
                        responses.Add(response.Value);
                    }
                }
            }
            finally
            {
                _concurrency.Release();
            }

            PruneSessions();

            var headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Mcp-Session-Id"] = sessionId
            };

            if (responses.Count == 0)
            {
                return McpHttpResponse.Empty(202, headers);
            }

            var payload = isBatch ? McpJson.Serialize(responses) : responses[0].GetRawText();

            var accept = request.Header("Accept") ?? string.Empty;
            var useEventStream = accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) &&
                                 !accept.Contains("application/json", StringComparison.OrdinalIgnoreCase);

            return useEventStream
                ? McpHttpResponse.EventStream(200, $"event: message\ndata: {payload}\n\n", headers)
                : McpHttpResponse.Json(200, payload, headers);
        }
    }

    private async Task<JsonElement?> ProcessMessageAsync(JsonElement message, string? sessionId,
        string remoteAddress, CancellationToken cancellationToken)
    {
        if (message.ValueKind != JsonValueKind.Object)
        {
            return BuildError(null, -32600, "Invalid Request");
        }

        if (!message.TryGetProperty("method", out var methodElement) ||
            methodElement.ValueKind != JsonValueKind.String)
        {
            return BuildError(null, -32600, "Invalid Request: missing method");
        }

        var method = methodElement.GetString() ?? string.Empty;
        JsonElement? id = message.TryGetProperty("id", out var idElement) &&
                          idElement.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? idElement.Clone()
            : null;
        var isNotification = id == null;

        var parameters = message.TryGetProperty("params", out var paramsElement)
            ? paramsElement.Clone()
            : JsonSerializer.SerializeToElement(new Dictionary<string, object?>(), McpJson.Options);

        try
        {
            switch (method)
            {
                case "initialize":
                    _sessions[sessionId!] = DateTimeOffset.UtcNow;
                    _host.Audit.Record(McpAuditKind.Connection, remoteAddress, McpAuditActions.Connect, true, 0);
                    return BuildResult(id, BuildInitializeResult(parameters));

                case "notifications/initialized":
                case "notifications/cancelled":
                case "notifications/roots/list_changed":
                    return null;

                case "ping":
                    return BuildResult(id, new Dictionary<string, object?>());

                case "logging/setLevel":
                    return BuildResult(id, new Dictionary<string, object?>());

                case "tools/list":
                    return BuildResult(id, BuildToolList());

                case "tools/call":
                    return await CallToolAsync(id, parameters, remoteAddress, cancellationToken).ConfigureAwait(false);

                default:
                    return isNotification ? null : BuildError(id, -32601, $"Method not found: {method}");
            }
        }
        catch (McpToolException ex)
        {
            return isNotification ? null : BuildError(id, -32603, ex.Message);
        }
        catch (Exception ex)
        {
            return isNotification ? null : BuildError(id, -32603, $"Internal error: {ex.Message}");
        }
    }

    private object BuildInitializeResult(JsonElement parameters)
    {
        var requestedVersion = parameters.ValueKind == JsonValueKind.Object &&
                               parameters.TryGetProperty("protocolVersion", out var versionElement) &&
                               versionElement.ValueKind == JsonValueKind.String
            ? versionElement.GetString()
            : null;

        var negotiatedVersion = requestedVersion != null && SupportedProtocolVersions.Contains(requestedVersion)
            ? requestedVersion
            : LatestProtocolVersion;

        return new Dictionary<string, object?>
        {
            ["protocolVersion"] = negotiatedVersion,
            ["capabilities"] = new Dictionary<string, object?>
            {
                ["tools"] = new Dictionary<string, object?> { ["listChanged"] = false }
            },
            ["serverInfo"] = new Dictionary<string, object?>
            {
                ["name"] = "fufu-launcher",
                ["title"] = "FufuLauncher MCP",
                ["version"] = AppVersionHelper.FullVersion
            },
            ["instructions"] =
                "FufuLauncher MCP endpoint. Only tools enabled in the launcher settings are listed and callable. " +
                "Plugin configuration can only be read with get_plugin_config and changed with set_plugin_config " +
                "for options that already exist; the config file must not be edited directly. " +
                "Tools such as start_game, stop_game, switch_server and install_plugin change the user environment; " +
                "confirm the user intent before calling them. " +
                "The player data tools (get_gacha_records, get_gacha_overview, search_gacha_pools, " +
                "get_gacha_pool_records, get_achievement_overview, get_achievement_categories, search_achievements) " +
                "only read the launcher local wish history and achievement save; they never contact the game servers " +
                "and never change the data."
        };
    }

    private object BuildToolList()
    {
        var tools = McpToolCatalog.All
            .Where(definition => _host.IsToolEnabled(definition.Id))
            .Select(definition => new Dictionary<string, object?>
            {
                ["name"] = definition.Id,
                ["title"] = definition.TitleKey.GetLocalized(),
                ["description"] = definition.Description,
                ["inputSchema"] = JsonSerializer.Deserialize<JsonElement>(definition.InputSchemaJson, McpJson.Options)
            })
            .ToList();

        return new Dictionary<string, object?> { ["tools"] = tools };
    }

    private async Task<JsonElement?> CallToolAsync(JsonElement? id, JsonElement parameters, string remoteAddress,
        CancellationToken cancellationToken)
    {
        if (parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            return BuildError(id, -32602, "Invalid params: missing tool name");
        }

        var toolId = nameElement.GetString() ?? string.Empty;
        var definition = McpToolCatalog.Find(toolId);
        if (definition == null)
        {
            _host.Audit.Record(McpAuditKind.ToolCall, remoteAddress, toolId, false, 0);
            return BuildError(id, -32602, $"Unknown tool: {toolId}");
        }

        if (!_host.IsToolEnabled(definition.Id))
        {
            _host.Audit.Record(McpAuditKind.ToolCall, remoteAddress, definition.Id, false, 0);
            return BuildError(id, -32602, $"Tool is disabled in the launcher settings: {definition.Id}");
        }

        var arguments = parameters.TryGetProperty("arguments", out var argumentsElement) &&
                        argumentsElement.ValueKind == JsonValueKind.Object
            ? argumentsElement.Clone()
            : JsonSerializer.SerializeToElement(new Dictionary<string, object?>(), McpJson.Options);

        var startedAt = Environment.TickCount64;

        try
        {
            var result = await _invoker.InvokeAsync(definition.Id, arguments, cancellationToken)
                .ConfigureAwait(false);
            _host.Audit.Record(McpAuditKind.ToolCall, remoteAddress, definition.Id, !result.IsError,
                Elapsed(startedAt), result.IsError ? Truncate(result.Text, 300) : null);
            return BuildResult(id, BuildContent(result.Text, result.IsError));
        }
        catch (McpToolException ex)
        {
            _host.Audit.Record(McpAuditKind.ToolCall, remoteAddress, definition.Id, false, Elapsed(startedAt),
                Truncate(ex.Message, 300));
            return BuildResult(id, BuildContent(ex.Message, true));
        }
        catch (OperationCanceledException)
        {
            _host.Audit.Record(McpAuditKind.ToolCall, remoteAddress, definition.Id, false, Elapsed(startedAt),
                "Cancelled");
            return BuildResult(id, BuildContent("Tool invocation was cancelled.", true));
        }
        catch (Exception ex)
        {
            _host.Audit.Record(McpAuditKind.ToolCall, remoteAddress, definition.Id, false, Elapsed(startedAt),
                Truncate(ex.Message, 300));
            return BuildResult(id, BuildContent($"Tool execution failed: {ex.Message}", true));
        }
    }

    private static int Elapsed(long startedAt) => (int)Math.Clamp(Environment.TickCount64 - startedAt, 0, int.MaxValue);

    private static string? Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength];
    }

    private static object BuildContent(string text, bool isError) => new Dictionary<string, object?>
    {
        ["content"] = new[]
        {
            new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = text
            }
        },
        ["isError"] = isError
    };

    private static JsonElement BuildResult(JsonElement? id, object result)
    {
        object? identifier = id.HasValue ? (object)id.Value : null;
        return McpJson.ToElement(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = identifier,
            ["result"] = result
        });
    }

    private static JsonElement BuildError(JsonElement? id, int code, string message)
    {
        object? identifier = id.HasValue ? (object)id.Value : null;
        return McpJson.ToElement(new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = identifier,
            ["error"] = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["message"] = message
            }
        });
    }

    private static string PlainError(int code, string message) => McpJson.Serialize(new Dictionary<string, object?>
    {
        ["jsonrpc"] = "2.0",
        ["id"] = null,
        ["error"] = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message
        }
    });

    private static string? ExtractToken(McpHttpRequest request)
    {
        var authorization = request.Header("Authorization");
        if (!string.IsNullOrWhiteSpace(authorization))
        {
            return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? authorization[7..].Trim()
                : authorization.Trim();
        }

        var headerToken = request.Header("X-Mcp-Token");
        if (!string.IsNullOrWhiteSpace(headerToken))
        {
            return headerToken.Trim();
        }

        return request.QueryValue("token");
    }

    private void PruneSessions()
    {
        if (_sessions.Count <= 64)
        {
            return;
        }

        var threshold = DateTimeOffset.UtcNow.AddHours(-2);
        foreach (var session in _sessions)
        {
            if (session.Value < threshold)
            {
                _sessions.TryRemove(session.Key, out _);
            }
        }
    }
}