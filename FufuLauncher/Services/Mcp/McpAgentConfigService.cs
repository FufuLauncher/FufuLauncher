/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace FufuLauncher.Services.Mcp;

public enum McpAgentId
{
    Antigravity,
    ClaudeCode,
    Codex,
    ClaudeDesktop,
    DeepSeekHarness,
    DeepSeekHarnessMcpCenter
}

public enum McpConfigFormat
{
    Json,
    Toml,
    CordisYaml
}

public enum McpAgentInstallState
{
    FileMissing,
    NotConfigured,
    Configured,
    NeedsUpdate,
    InvalidFile
}

public sealed record McpAgentDescriptor(
    McpAgentId Id,
    string DisplayName,
    McpConfigFormat Format,
    bool SupportsDshProfile,
    string? NoteKey);

public sealed record McpAgentInspection(
    McpAgentInstallState State,
    string FilePath,
    string? ErrorMessage = null);

public sealed record McpAgentInstallResult(
    bool Succeeded,
    string FilePath,
    string? ErrorMessage = null);

/// <summary>
/// Builds and merges MCP client configuration entries for supported AI agents.
/// </summary>
public sealed class McpAgentConfigService
{
    public const string ServerName = "fufu-launcher";
    public const string DefaultDshProfile = "web";
    private const string DshPluginId = "mcp-fufu-launcher";
    private const string DshPluginPackage = "@deepseek-ai/dsh-mcp-client";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };

    private static readonly JsonDocumentOptions JsonReadOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static IReadOnlyList<McpAgentDescriptor> Agents { get; } =
    [
        new(McpAgentId.Antigravity, "Antigravity", McpConfigFormat.Json, false, null),
        new(McpAgentId.ClaudeCode, "Claude Code", McpConfigFormat.Json, false, null),
        new(McpAgentId.Codex, "OpenAI Codex", McpConfigFormat.Toml, false, null),
        new(McpAgentId.ClaudeDesktop, "Claude Desktop", McpConfigFormat.Json, false, "Mcp_ClientNote_ClaudeDesktop"),
        new(McpAgentId.DeepSeekHarness, "DeepSeek Harness (cordis.patch.yml)", McpConfigFormat.CordisYaml, true,
            "Mcp_ClientNote_DshCordis"),
        new(McpAgentId.DeepSeekHarnessMcpCenter, "DeepSeek Harness (mcp-center.json)", McpConfigFormat.Json, false,
            null)
    ];

    private readonly string _userProfileDir;
    private readonly string _appDataDir;

    public McpAgentConfigService()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
    {
    }

    public McpAgentConfigService(string userProfileDir, string appDataDir)
    {
        _userProfileDir = userProfileDir;
        _appDataDir = appDataDir;
    }

    public static McpAgentDescriptor GetDescriptor(McpAgentId agentId) =>
        Agents.First(agent => agent.Id == agentId);

    /// <summary>
    /// Discovers existing DeepSeek Harness profile names under <c>.dsh/profiles</c>.
    /// </summary>
    public IReadOnlyList<string> DiscoverDshProfiles()
    {
        var profilesRoot = Path.Combine(_userProfileDir, ".dsh", "profiles");
        var discovered = new List<string>();

        if (Directory.Exists(profilesRoot))
        {
            foreach (var dir in Directory.GetDirectories(profilesRoot))
            {
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    discovered.Add(name);
                }
            }

            discovered.Sort(StringComparer.OrdinalIgnoreCase);
        }

        if (discovered.Count == 0)
        {
            discovered.Add(DefaultDshProfile);
        }

        return discovered;
    }

    /// <summary>
    /// Resolves the target configuration file path for the specified client.
    /// </summary>
    public string ResolveConfigPath(McpAgentId agentId, string? dshProfile = null) =>
        agentId switch
        {
            McpAgentId.Antigravity => Path.Combine(_userProfileDir, ".gemini", "config", "mcp_config.json"),
            McpAgentId.ClaudeCode => Path.Combine(_userProfileDir, ".claude.json"),
            McpAgentId.Codex => Path.Combine(_userProfileDir, ".codex", "config.toml"),
            McpAgentId.ClaudeDesktop => Path.Combine(_appDataDir, "Claude", "claude_desktop_config.json"),
            McpAgentId.DeepSeekHarness => Path.Combine(_userProfileDir, ".dsh", "profiles",
                SanitizeProfileName(dshProfile), "cordis.patch.yml"),
            McpAgentId.DeepSeekHarnessMcpCenter => Path.Combine(_userProfileDir, ".dsh", "mcp-center.json"),
            _ => throw new ArgumentOutOfRangeException(nameof(agentId), agentId, null)
        };

    /// <summary>
    /// Generates a standalone snippet suitable for previewing or pasting into a client's config file.
    /// </summary>
    public string BuildSnippet(McpAgentId agentId, string endpointUrl, string? bearerToken)
    {
        var token = NormalizeToken(bearerToken);
        return agentId switch
        {
            McpAgentId.Codex => BuildCodexTomlBlock(endpointUrl, token, "\n"),
            McpAgentId.DeepSeekHarness => SerializeCordisYaml(new YamlSequenceNode(BuildCordisPatchBlock(endpointUrl, token))),
            _ => MergeJsonContent(null, GetJsonMapKey(agentId), BuildJsonServerEntry(agentId, endpointUrl, token))
        };
    }

    /// <summary>
    /// Inspects whether the target configuration file exists and matches the current endpoint and token.
    /// </summary>
    public McpAgentInspection Inspect(McpAgentId agentId, string endpointUrl, string? bearerToken,
        string? dshProfile = null)
    {
        var path = ResolveConfigPath(agentId, dshProfile);
        if (!File.Exists(path))
        {
            return new McpAgentInspection(McpAgentInstallState.FileMissing, path);
        }

        string content;
        try
        {
            content = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            return new McpAgentInspection(McpAgentInstallState.InvalidFile, path, ex.Message);
        }

        var token = NormalizeToken(bearerToken);
        try
        {
            var state = agentId switch
            {
                McpAgentId.Codex => InspectTomlState(content, endpointUrl, token),
                McpAgentId.DeepSeekHarness => InspectCordisYamlState(content, endpointUrl, token),
                _ => InspectJsonState(content, GetJsonMapKey(agentId), BuildJsonServerEntry(agentId, endpointUrl, token))
            };
            return new McpAgentInspection(state, path);
        }
        catch (Exception ex)
        {
            return new McpAgentInspection(McpAgentInstallState.InvalidFile, path, ex.Message);
        }
    }

    /// <summary>
    /// Safely merges the FufuLauncher MCP configuration into the target client's file on disk.
    /// </summary>
    public McpAgentInstallResult InstallOrUpdate(McpAgentId agentId, string endpointUrl, string? bearerToken,
        string? dshProfile = null)
    {
        var path = ResolveConfigPath(agentId, dshProfile);
        string? existing = null;

        try
        {
            if (File.Exists(path))
            {
                existing = File.ReadAllText(path, Encoding.UTF8);
            }

            var updated = MergeContent(agentId, existing, endpointUrl, bearerToken);
            WriteFileAtomically(path, updated);
            return new McpAgentInstallResult(true, path);
        }
        catch (Exception ex)
        {
            return new McpAgentInstallResult(false, path, ex.Message);
        }
    }

    internal static string MergeContent(McpAgentId agentId, string? existingContent, string endpointUrl,
        string? bearerToken)
    {
        var token = NormalizeToken(bearerToken);
        return agentId switch
        {
            McpAgentId.Codex => MergeCodexTomlContent(existingContent, endpointUrl, token),
            McpAgentId.DeepSeekHarness => MergeCordisYamlContent(existingContent, endpointUrl, token),
            _ => MergeJsonContent(existingContent, GetJsonMapKey(agentId),
                BuildJsonServerEntry(agentId, endpointUrl, token))
        };
    }

    private static string SanitizeProfileName(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return DefaultDshProfile;
        }

        var trimmed = profile.Trim();
        if (trimmed is "." or ".." ||
            trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            trimmed.Contains(Path.DirectorySeparatorChar) ||
            trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"Invalid DeepSeek Harness profile name: '{profile}'.");
        }

        return trimmed;
    }

    private static string? NormalizeToken(string? bearerToken) =>
        string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken.Trim();

    private static string GetJsonMapKey(McpAgentId agentId) =>
        agentId == McpAgentId.DeepSeekHarnessMcpCenter ? "servers" : "mcpServers";

    private static JsonObject BuildJsonServerEntry(McpAgentId agentId, string endpointUrl, string? bearerToken)
    {
        var entry = new JsonObject();

        switch (agentId)
        {
            case McpAgentId.Antigravity:
                entry["url"] = endpointUrl;
                AddHeadersIfNeeded(entry, bearerToken);
                break;

            case McpAgentId.ClaudeCode:
                entry["type"] = "http";
                entry["url"] = endpointUrl;
                AddHeadersIfNeeded(entry, bearerToken);
                break;

            case McpAgentId.DeepSeekHarnessMcpCenter:
                entry["transport"] = "streamable-http";
                entry["url"] = endpointUrl;
                AddHeadersIfNeeded(entry, bearerToken);
                break;

            case McpAgentId.ClaudeDesktop:
                entry["command"] = "npx";
                var args = new JsonArray((JsonNode)"-y", (JsonNode)"mcp-remote", (JsonNode)endpointUrl);
                if (endpointUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                {
                    // mcp-remote refuses plain http:// endpoints unless --allow-http is passed.
                    args.Add((JsonNode)"--allow-http");
                }

                if (bearerToken != null)
                {
                    args.Add((JsonNode)"--header");
                    args.Add((JsonNode)$"Authorization: Bearer {bearerToken}");
                }

                entry["args"] = args;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(agentId), agentId, null);
        }

        return entry;
    }

    private static void AddHeadersIfNeeded(JsonObject entry, string? bearerToken)
    {
        if (bearerToken == null)
        {
            return;
        }

        entry["headers"] = new JsonObject
        {
            ["Authorization"] = $"Bearer {bearerToken}"
        };
    }

    private static McpAgentInstallState InspectJsonState(string content, string mapKey, JsonObject expectedEntry)
    {
        var root = ParseJsonRoot(content);
        if (!root.TryGetPropertyValue(mapKey, out var mapNode) || mapNode == null)
        {
            return McpAgentInstallState.NotConfigured;
        }

        if (mapNode is not JsonObject servers)
        {
            throw new InvalidDataException($"Expected '{mapKey}' to be a JSON object.");
        }

        if (!servers.TryGetPropertyValue(ServerName, out var existingEntry) || existingEntry == null)
        {
            return McpAgentInstallState.NotConfigured;
        }

        return JsonNode.DeepEquals(existingEntry, expectedEntry)
            ? McpAgentInstallState.Configured
            : McpAgentInstallState.NeedsUpdate;
    }

    private static string MergeJsonContent(string? existingContent, string mapKey, JsonObject entry)
    {
        var root = string.IsNullOrWhiteSpace(existingContent)
            ? new JsonObject()
            : ParseJsonRoot(existingContent);

        JsonObject servers;
        if (!root.TryGetPropertyValue(mapKey, out var existingMap) || existingMap == null)
        {
            servers = new JsonObject();
            root[mapKey] = servers;
        }
        else if (existingMap is JsonObject obj)
        {
            servers = obj;
        }
        else
        {
            throw new InvalidDataException($"Expected '{mapKey}' to be a JSON object.");
        }

        servers[ServerName] = entry;
        var newline = DetectNewline(existingContent, "\n");
        var serialized = root.ToJsonString(JsonWriteOptions);
        return NormalizeNewline(serialized, newline) + newline;
    }

    private static JsonObject ParseJsonRoot(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new JsonObject();
        }

        var parsed = JsonNode.Parse(content, null, JsonReadOptions);
        return parsed as JsonObject
               ?? throw new InvalidDataException("Configuration root must be a JSON object.");
    }

    private static McpAgentInstallState InspectTomlState(string content, string endpointUrl, string? bearerToken)
    {
        string? foundUrl = null;
        string? foundAuth = null;
        var foundSection = false;
        var currentMatchKind = TomlMatchKind.None;
        string? multilineQuote = null;

        foreach (var rawLine in SplitLines(content))
        {
            if (multilineQuote != null)
            {
                UpdateMultilineState(rawLine, ref multilineQuote);
                continue;
            }

            var stripped = StripTomlComment(rawLine).Trim();
            if (string.IsNullOrEmpty(stripped))
            {
                continue;
            }

            if (TryParseTomlTableHeader(stripped, out var segments, out var isArray))
            {
                if (!isArray && IsFufuTomlSection(segments))
                {
                    if (segments.Count == 2)
                    {
                        foundSection = true;
                        currentMatchKind = TomlMatchKind.Server;
                    }
                    else if (segments.Count == 3 &&
                             string.Equals(segments[2], "http_headers", StringComparison.Ordinal))
                    {
                        currentMatchKind = TomlMatchKind.Headers;
                    }
                    else
                    {
                        currentMatchKind = TomlMatchKind.OtherSubtable;
                    }
                }
                else
                {
                    currentMatchKind = TomlMatchKind.None;
                }

                continue;
            }

            UpdateMultilineState(rawLine, ref multilineQuote);
            if (currentMatchKind == TomlMatchKind.None)
            {
                continue;
            }

            if (TryParseTomlAssignment(stripped, out var key, out var value))
            {
                if (currentMatchKind == TomlMatchKind.Server && key == "url")
                {
                    foundUrl = value;
                }
                else if (currentMatchKind == TomlMatchKind.Headers &&
                         string.Equals(key, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    foundAuth = value;
                }
            }
        }

        if (!foundSection)
        {
            return McpAgentInstallState.NotConfigured;
        }

        var urlMatches = string.Equals(foundUrl, endpointUrl, StringComparison.Ordinal);
        var authMatches = bearerToken == null
            ? foundAuth == null
            : string.Equals(foundAuth, $"Bearer {bearerToken}", StringComparison.Ordinal);

        return urlMatches && authMatches
            ? McpAgentInstallState.Configured
            : McpAgentInstallState.NeedsUpdate;
    }

    private static string MergeCodexTomlContent(string? existingContent, string endpointUrl, string? bearerToken)
    {
        var newline = DetectNewline(existingContent, "\n");
        var keptLines = new List<string>();

        if (!string.IsNullOrWhiteSpace(existingContent))
        {
            var skippingSection = false;
            string? multilineQuote = null;

            foreach (var line in SplitLines(existingContent))
            {
                if (multilineQuote != null)
                {
                    if (!skippingSection)
                    {
                        keptLines.Add(line);
                    }

                    UpdateMultilineState(line, ref multilineQuote);
                    continue;
                }

                var stripped = StripTomlComment(line).Trim();
                if (!string.IsNullOrEmpty(stripped) &&
                    TryParseTomlTableHeader(stripped, out var segments, out _))
                {
                    skippingSection = IsFufuTomlSection(segments);
                    if (!skippingSection)
                    {
                        keptLines.Add(line);
                    }

                    continue;
                }

                UpdateMultilineState(line, ref multilineQuote);
                if (!skippingSection)
                {
                    keptLines.Add(line);
                }
            }
        }

        while (keptLines.Count > 0 && string.IsNullOrWhiteSpace(keptLines[^1]))
        {
            keptLines.RemoveAt(keptLines.Count - 1);
        }

        var block = BuildCodexTomlBlock(endpointUrl, bearerToken, newline);
        if (keptLines.Count == 0)
        {
            return block;
        }

        return string.Join(newline, keptLines) + newline + newline + block;
    }

    private static string BuildCodexTomlBlock(string endpointUrl, string? bearerToken, string newline)
    {
        var sb = new StringBuilder();
        sb.Append("[mcp_servers.").Append(ServerName).Append(']').Append(newline);
        sb.Append("url = ").Append(FormatTomlBasicString(endpointUrl)).Append(newline);

        if (bearerToken != null)
        {
            sb.Append(newline);
            sb.Append("[mcp_servers.").Append(ServerName).Append(".http_headers]").Append(newline);
            sb.Append("Authorization = ").Append(FormatTomlBasicString($"Bearer {bearerToken}")).Append(newline);
        }

        return sb.ToString();
    }

    private static bool IsFufuTomlSection(IReadOnlyList<string> segments) =>
        segments.Count >= 2 &&
        string.Equals(segments[0], "mcp_servers", StringComparison.Ordinal) &&
        string.Equals(segments[1], ServerName, StringComparison.Ordinal);

    private static bool TryParseTomlTableHeader(string strippedLine, out List<string> segments, out bool isArray)
    {
        segments = new List<string>();
        isArray = false;

        if (!strippedLine.StartsWith('['))
        {
            return false;
        }

        string inner;
        if (strippedLine.StartsWith("[[", StringComparison.Ordinal))
        {
            if (!strippedLine.EndsWith("]]", StringComparison.Ordinal) || strippedLine.Length < 4)
            {
                return false;
            }

            isArray = true;
            inner = strippedLine[2..^2].Trim();
        }
        else
        {
            if (!strippedLine.EndsWith(']') || strippedLine.Length < 2)
            {
                return false;
            }

            inner = strippedLine[1..^1].Trim();
        }

        if (string.IsNullOrEmpty(inner))
        {
            return false;
        }

        var index = 0;
        while (index < inner.Length)
        {
            while (index < inner.Length && char.IsWhiteSpace(inner[index]))
            {
                index++;
            }

            if (index >= inner.Length)
            {
                return false;
            }

            if (inner[index] is '"' or '\'')
            {
                var quote = inner[index++];
                var sb = new StringBuilder();
                var closed = false;
                while (index < inner.Length)
                {
                    var ch = inner[index++];
                    if (quote == '"' && ch == '\\' && index < inner.Length)
                    {
                        sb.Append(UnescapeTomlChar(inner[index++]));
                        continue;
                    }

                    if (ch == quote)
                    {
                        closed = true;
                        break;
                    }

                    sb.Append(ch);
                }

                if (!closed)
                {
                    return false;
                }

                segments.Add(sb.ToString());
            }
            else
            {
                var start = index;
                while (index < inner.Length && inner[index] != '.' && !char.IsWhiteSpace(inner[index]))
                {
                    index++;
                }

                segments.Add(inner[start..index]);
            }

            while (index < inner.Length && char.IsWhiteSpace(inner[index]))
            {
                index++;
            }

            if (index < inner.Length)
            {
                if (inner[index] != '.')
                {
                    return false;
                }

                index++;
            }
        }

        return segments.Count > 0;
    }

    private static bool TryParseTomlAssignment(string strippedLine, out string key, out string? value)
    {
        key = string.Empty;
        value = null;

        var equals = strippedLine.IndexOf('=');
        if (equals <= 0)
        {
            return false;
        }

        key = strippedLine[..equals].Trim().Trim('"', '\'');
        var rawValue = strippedLine[(equals + 1)..].Trim();

        if (rawValue.Length >= 2 && rawValue[0] == '"' && rawValue[^1] == '"')
        {
            var sb = new StringBuilder();
            for (var i = 1; i < rawValue.Length - 1; i++)
            {
                var ch = rawValue[i];
                if (ch == '\\' && i + 1 < rawValue.Length - 1)
                {
                    sb.Append(UnescapeTomlChar(rawValue[++i]));
                }
                else
                {
                    sb.Append(ch);
                }
            }

            value = sb.ToString();
            return true;
        }

        if (rawValue.Length >= 2 && rawValue[0] == '\'' && rawValue[^1] == '\'')
        {
            value = rawValue[1..^1];
            return true;
        }

        return false;
    }

    private static string StripTomlComment(string line)
    {
        char? quote = null;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quote == null)
            {
                if (ch == '#')
                {
                    return line[..i];
                }

                if (ch is '"' or '\'')
                {
                    quote = ch;
                }
            }
            else
            {
                if (quote == '"' && ch == '\\')
                {
                    i++;
                    continue;
                }

                if (ch == quote)
                {
                    quote = null;
                }
            }
        }

        return line;
    }

    private static void UpdateMultilineState(string line, ref string? multilineQuote)
    {
        var i = 0;
        while (i <= line.Length - 3)
        {
            if (multilineQuote == null)
            {
                if (line.AsSpan(i, 3).SequenceEqual("\"\"\""))
                {
                    multilineQuote = "\"\"\"";
                    i += 3;
                    continue;
                }

                if (line.AsSpan(i, 3).SequenceEqual("'''"))
                {
                    multilineQuote = "'''";
                    i += 3;
                    continue;
                }

                if (line[i] == '#')
                {
                    return;
                }

                if (line[i] is '"' or '\'')
                {
                    var q = line[i++];
                    while (i < line.Length)
                    {
                        if (q == '"' && line[i] == '\\')
                        {
                            i += 2;
                            continue;
                        }

                        if (line[i] == q)
                        {
                            break;
                        }

                        i++;
                    }
                }
            }
            else if (line.AsSpan(i, 3).SequenceEqual(multilineQuote))
            {
                multilineQuote = null;
                i += 3;
                continue;
            }

            i++;
        }
    }

    private static string FormatTomlBasicString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            sb.Append(ch switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\r' => "\\r",
                '\n' => "\\n",
                '\t' => "\\t",
                _ => ch
            });
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static char UnescapeTomlChar(char escaped) =>
        escaped switch
        {
            'r' => '\r',
            'n' => '\n',
            't' => '\t',
            '\\' => '\\',
            '"' => '"',
            _ => escaped
        };

    private static McpAgentInstallState InspectCordisYamlState(string content, string endpointUrl, string? bearerToken)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return McpAgentInstallState.NotConfigured;
        }

        var root = LoadCordisRootSequence(content);
        if (root == null)
        {
            return McpAgentInstallState.NotConfigured;
        }

        var existing = FindCordisPluginEntry(root);
        if (existing == null)
        {
            return McpAgentInstallState.NotConfigured;
        }

        if (!TryGetMappingChild(existing, "name", out YamlScalarNode? pkgNode) ||
            !string.Equals(pkgNode.Value, DshPluginPackage, StringComparison.Ordinal) ||
            !TryGetMappingChild(existing, "config", out YamlMappingNode? configNode))
        {
            return McpAgentInstallState.NeedsUpdate;
        }

        var serverNameMatches = TryGetMappingChild(configNode, "serverName", out YamlScalarNode? sn) &&
                                string.Equals(sn.Value, ServerName, StringComparison.Ordinal);
        var transportMatches = TryGetMappingChild(configNode, "transport", out YamlScalarNode? tr) &&
                               string.Equals(tr.Value, "streamable-http", StringComparison.Ordinal);
        var urlMatches = TryGetMappingChild(configNode, "url", out YamlScalarNode? urlNode) &&
                         string.Equals(urlNode.Value, endpointUrl, StringComparison.Ordinal);

        string? existingAuth = null;
        if (TryGetMappingChild(configNode, "headers", out YamlMappingNode? headersNode) &&
            TryGetMappingChild(headersNode, "Authorization", out YamlScalarNode? authNode))
        {
            existingAuth = authNode.Value;
        }

        var authMatches = bearerToken == null
            ? existingAuth == null
            : string.Equals(existingAuth, $"Bearer {bearerToken}", StringComparison.Ordinal);

        return serverNameMatches && transportMatches && urlMatches && authMatches
            ? McpAgentInstallState.Configured
            : McpAgentInstallState.NeedsUpdate;
    }

    private static string MergeCordisYamlContent(string? existingContent, string endpointUrl, string? bearerToken)
    {
        var newline = DetectNewline(existingContent, "\n");
        var root = string.IsNullOrWhiteSpace(existingContent)
            ? new YamlSequenceNode()
            : LoadCordisRootSequence(existingContent) ?? new YamlSequenceNode();

        var newEntry = BuildCordisPluginEntry(endpointUrl, bearerToken);
        var updated = false;

        foreach (var item in root.Children)
        {
            if (item is not YamlMappingNode patchNode ||
                !TryGetMappingChild(patchNode, "insert", out YamlSequenceNode? insertList))
            {
                continue;
            }

            for (var i = 0; i < insertList.Children.Count; i++)
            {
                if (IsFufuCordisEntry(insertList.Children[i]))
                {
                    insertList.Children[i] = newEntry;
                    updated = true;
                    break;
                }
            }

            if (updated)
            {
                break;
            }
        }

        if (!updated)
        {
            YamlSequenceNode? firstInsertList = null;
            foreach (var item in root.Children)
            {
                if (item is YamlMappingNode patchNode &&
                    TryGetMappingChild(patchNode, "insert", out YamlSequenceNode? insertList))
                {
                    firstInsertList = insertList;
                    break;
                }
            }

            if (firstInsertList != null)
            {
                firstInsertList.Add(newEntry);
            }
            else
            {
                root.Add(BuildCordisPatchBlock(endpointUrl, bearerToken));
            }
        }

        return NormalizeNewline(SerializeCordisYaml(root), newline);
    }

    private static YamlSequenceNode? LoadCordisRootSequence(string content)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(content);
        stream.Load(reader);
        if (stream.Documents.Count == 0)
        {
            return null;
        }

        return stream.Documents[0].RootNode as YamlSequenceNode
               ?? throw new InvalidDataException("Expected cordis.patch.yml root to be a YAML sequence.");
    }

    private static YamlMappingNode BuildCordisPatchBlock(string endpointUrl, string? bearerToken) =>
        new()
        {
            { new YamlScalarNode("insert"), new YamlSequenceNode(BuildCordisPluginEntry(endpointUrl, bearerToken)) }
        };

    private static YamlMappingNode BuildCordisPluginEntry(string endpointUrl, string? bearerToken)
    {
        var config = new YamlMappingNode
        {
            { new YamlScalarNode("serverName"), new YamlScalarNode(ServerName) },
            { new YamlScalarNode("transport"), new YamlScalarNode("streamable-http") },
            { new YamlScalarNode("url"), new YamlScalarNode(endpointUrl) }
        };

        if (bearerToken != null)
        {
            config.Add(new YamlScalarNode("headers"), new YamlMappingNode
            {
                { new YamlScalarNode("Authorization"), new YamlScalarNode($"Bearer {bearerToken}") }
            });
        }

        return new YamlMappingNode
        {
            { new YamlScalarNode("id"), new YamlScalarNode(DshPluginId) },
            {
                new YamlScalarNode("name"),
                new YamlScalarNode(DshPluginPackage) { Style = ScalarStyle.SingleQuoted }
            },
            { new YamlScalarNode("config"), config }
        };
    }

    private static YamlMappingNode? FindCordisPluginEntry(YamlSequenceNode root)
    {
        foreach (var item in root.Children)
        {
            if (item is not YamlMappingNode patchNode ||
                !TryGetMappingChild(patchNode, "insert", out YamlSequenceNode? insertList))
            {
                continue;
            }

            foreach (var candidate in insertList.Children)
            {
                if (IsFufuCordisEntry(candidate))
                {
                    return (YamlMappingNode)candidate;
                }
            }
        }

        return null;
    }

    private static bool IsFufuCordisEntry(YamlNode node)
    {
        if (node is not YamlMappingNode entry)
        {
            return false;
        }

        if (TryGetMappingChild(entry, "id", out YamlScalarNode? idNode) &&
            string.Equals(idNode.Value, DshPluginId, StringComparison.Ordinal))
        {
            return true;
        }

        return TryGetMappingChild(entry, "name", out YamlScalarNode? nameNode) &&
               string.Equals(nameNode.Value, DshPluginPackage, StringComparison.Ordinal) &&
               TryGetMappingChild(entry, "config", out YamlMappingNode? configNode) &&
               TryGetMappingChild(configNode, "serverName", out YamlScalarNode? serverNode) &&
               string.Equals(serverNode.Value, ServerName, StringComparison.Ordinal);
    }

    private static bool TryGetMappingChild<TNode>(YamlMappingNode mapping, string key, [NotNullWhen(true)] out TNode? node)
        where TNode : YamlNode
    {
        foreach (var pair in mapping.Children)
        {
            if (pair.Key is YamlScalarNode scalar &&
                string.Equals(scalar.Value, key, StringComparison.Ordinal) &&
                pair.Value is TNode typed)
            {
                node = typed;
                return true;
            }
        }

        node = null;
        return false;
    }

    private static string SerializeCordisYaml(YamlNode root)
    {
        var stream = new YamlStream(new YamlDocument(root));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        var text = writer.ToString().Replace("\r\n", "\n");
        if (text.EndsWith("\n...\n", StringComparison.Ordinal))
        {
            text = text[..^4];
        }
        else if (text.EndsWith("...", StringComparison.Ordinal))
        {
            text = text[..^3];
        }

        return text.TrimEnd('\n') + "\n";
    }

    private static void WriteFileAtomically(string targetPath, string content)
    {
        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? ".", $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tempPath, content, Utf8NoBom);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n');

    private static string DetectNewline(string? text, string fallback) =>
        !string.IsNullOrEmpty(text) && text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : fallback;

    private static string NormalizeNewline(string text, string newline) =>
        newline == "\r\n" ? text.Replace("\r\n", "\n").Replace("\n", "\r\n") : text.Replace("\r\n", "\n");

    private enum TomlMatchKind
    {
        None,
        Server,
        Headers,
        OtherSubtable
    }
}
