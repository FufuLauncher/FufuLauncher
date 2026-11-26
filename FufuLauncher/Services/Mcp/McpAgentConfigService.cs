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
    private readonly Func<string, string?> _getEnvironmentVariable;

    public McpAgentConfigService()
        : this(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetEnvironmentVariable)
    {
    }

    public McpAgentConfigService(
        string userProfileDir,
        string appDataDir,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        _userProfileDir = userProfileDir;
        _appDataDir = appDataDir;
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public static McpAgentDescriptor GetDescriptor(McpAgentId agentId) =>
        Agents.First(agent => agent.Id == agentId);

    /// <summary>
    /// Discovers existing DeepSeek Harness profile names under <c>$DSH_HOME/profiles</c> or <c>~/.dsh/profiles</c>.
    /// </summary>
    public IReadOnlyList<string> DiscoverDshProfiles()
    {
        var profilesRoot = Path.Combine(ResolveDshHome(), "profiles");
        var discovered = new List<string>();

        try
        {
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
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall back to the default profile so manual entry remains available.
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
            McpAgentId.Codex => Path.Combine(ResolveCodexHome(), "config.toml"),
            McpAgentId.ClaudeDesktop => Path.Combine(_appDataDir, "Claude", "claude_desktop_config.json"),
            McpAgentId.DeepSeekHarness => Path.Combine(ResolveDshHome(), "profiles",
                SanitizeProfileName(dshProfile), "cordis.patch.yml"),
            McpAgentId.DeepSeekHarnessMcpCenter => Path.Combine(ResolveDshHome(), "mcp-center.json"),
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
            McpAgentId.DeepSeekHarness => SerializeCordisYamlStream(
                new YamlStream(new YamlDocument(new YamlSequenceNode(BuildCordisPatchBlock(endpointUrl, token))))),
            McpAgentId.DeepSeekHarnessMcpCenter => MergeMcpCenterJsonContent(null, endpointUrl, token),
            _ => MergeJsonMapContent(null, "mcpServers", BuildJsonServerEntry(agentId, endpointUrl, token))
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
                McpAgentId.DeepSeekHarnessMcpCenter => InspectMcpCenterJsonState(content, endpointUrl, token),
                _ => InspectJsonMapState(content, "mcpServers", BuildJsonServerEntry(agentId, endpointUrl, token))
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
        var path = string.Empty;

        try
        {
            path = ResolveConfigPath(agentId, dshProfile);
            string? existing = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;

            for (var attempt = 0; attempt < 2; attempt++)
            {
                var updated = MergeContent(agentId, existing, endpointUrl, bearerToken);

                var currentOnDisk = File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
                if (!string.Equals(currentOnDisk, existing, StringComparison.Ordinal))
                {
                    if (attempt == 0)
                    {
                        existing = currentOnDisk;
                        continue;
                    }

                    throw new IOException("Configuration file changed concurrently while saving.");
                }

                WriteFileAtomically(path, updated, hadExistingFile: existing != null);
                return new McpAgentInstallResult(true, path);
            }

            throw new IOException("Configuration file changed concurrently while saving.");
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
            McpAgentId.DeepSeekHarnessMcpCenter => MergeMcpCenterJsonContent(existingContent, endpointUrl, token),
            _ => MergeJsonMapContent(existingContent, "mcpServers",
                BuildJsonServerEntry(agentId, endpointUrl, token))
        };
    }

    private string ResolveCodexHome() =>
        ResolveDirectoryOverride("CODEX_HOME") ?? Path.Combine(_userProfileDir, ".codex");

    private string ResolveDshHome() =>
        ResolveDirectoryOverride("DSH_HOME") ?? Path.Combine(_userProfileDir, ".dsh");

    private string? ResolveDirectoryOverride(string variableName)
    {
        var value = _getEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Path.GetFullPath(value.Trim());
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

    private static McpAgentInstallState InspectJsonMapState(string content, string mapKey, JsonObject expectedEntry)
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

    private static string MergeJsonMapContent(string? existingContent, string mapKey, JsonObject entry)
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
        return SerializeJsonRoot(root, existingContent);
    }

    private static McpAgentInstallState InspectMcpCenterJsonState(string content, string endpointUrl,
        string? bearerToken)
    {
        var root = ParseJsonRoot(content);
        if (!root.TryGetPropertyValue("servers", out var serversNode) || serversNode == null)
        {
            return McpAgentInstallState.NotConfigured;
        }

        if (serversNode is not JsonArray servers)
        {
            throw new InvalidDataException("Expected 'servers' to be a JSON array.");
        }

        var match = FindMcpCenterEntry(servers, out _);
        if (match == null)
        {
            return McpAgentInstallState.NotConfigured;
        }

        var existingId = match["id"]?.GetValue<string?>();
        var existingEnabled = match["enabled"]?.GetValue<bool?>() ?? true;
        var expected = BuildMcpCenterServerEntry(endpointUrl, bearerToken, existingId, existingEnabled);
        return JsonNode.DeepEquals(match, expected)
            ? McpAgentInstallState.Configured
            : McpAgentInstallState.NeedsUpdate;
    }

    private static string MergeMcpCenterJsonContent(string? existingContent, string endpointUrl, string? bearerToken)
    {
        var root = string.IsNullOrWhiteSpace(existingContent)
            ? new JsonObject()
            : ParseJsonRoot(existingContent);

        JsonArray servers;
        if (!root.TryGetPropertyValue("servers", out var existingServers) || existingServers == null)
        {
            servers = new JsonArray();
            root["servers"] = servers;
        }
        else if (existingServers is JsonArray arr)
        {
            servers = arr;
        }
        else
        {
            throw new InvalidDataException("Expected 'servers' to be a JSON array.");
        }

        var existing = FindMcpCenterEntry(servers, out var existingIndex);
        var preservedId = existing?["id"]?.GetValue<string?>();
        var preservedEnabled = existing?["enabled"]?.GetValue<bool?>() ?? true;
        var updatedEntry = BuildMcpCenterServerEntry(endpointUrl, bearerToken, preservedId, preservedEnabled);

        if (existingIndex >= 0)
        {
            servers[existingIndex] = updatedEntry;
        }
        else
        {
            servers.Add((JsonNode)updatedEntry);
        }

        return SerializeJsonRoot(root, existingContent);
    }

    private static JsonObject? FindMcpCenterEntry(JsonArray servers, out int index)
    {
        for (var i = 0; i < servers.Count; i++)
        {
            if (servers[i] is not JsonObject obj)
            {
                continue;
            }

            var id = obj["id"]?.GetValue<string?>();
            var name = obj["name"]?.GetValue<string?>();
            if (string.Equals(id, ServerName, StringComparison.Ordinal) ||
                string.Equals(name, ServerName, StringComparison.Ordinal))
            {
                index = i;
                return obj;
            }
        }

        index = -1;
        return null;
    }

    private static JsonObject BuildMcpCenterServerEntry(string endpointUrl, string? bearerToken,
        string? existingId = null, bool enabled = true)
    {
        var entry = new JsonObject
        {
            ["id"] = string.IsNullOrWhiteSpace(existingId) ? ServerName : existingId,
            ["name"] = ServerName,
            ["type"] = "http",
            ["url"] = endpointUrl,
            ["authMode"] = bearerToken != null ? "bearer" : "none"
        };

        if (bearerToken != null)
        {
            entry["token"] = bearerToken;
        }

        entry["enabled"] = enabled;
        return entry;
    }

    private static string SerializeJsonRoot(JsonObject root, string? existingContent)
    {
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
        var scan = ScanToml(content);

        if (!scan.FoundFufuEntry)
        {
            return McpAgentInstallState.NotConfigured;
        }

        var urlMatches = string.Equals(scan.FoundUrl, endpointUrl, StringComparison.Ordinal);
        var authMatches = bearerToken == null
            ? scan.FoundAuth == null
            : string.Equals(scan.FoundAuth, $"Bearer {bearerToken}", StringComparison.Ordinal);

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
            var scan = ScanToml(existingContent);
            var lines = SplitLines(existingContent);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!scan.SkippedLineIndices.Contains(i))
                {
                    keptLines.Add(lines[i]);
                }
            }
        }

        while (keptLines.Count > 0 && string.IsNullOrWhiteSpace(keptLines[^1]))
        {
            keptLines.RemoveAt(keptLines.Count - 1);
        }

        var block = BuildCodexTomlBlock(endpointUrl, bearerToken, newline);
        var merged = keptLines.Count == 0
            ? block
            : string.Join(newline, keptLines) + newline + newline + block;

        ScanToml(merged);
        return merged;
    }

    private static TomlScanResult ScanToml(string content)
    {
        var skippedLines = new HashSet<int>();
        var definedTables = new HashSet<string>(StringComparer.Ordinal);
        var currentTable = new List<string>();
        var currentMatchKind = TomlMatchKind.None;
        var skippingSection = false;
        var skippingContinuation = false;
        string? multilineQuote = null;
        var bracketDepth = 0;
        var braceDepth = 0;

        var foundFufuEntry = false;
        string? foundUrl = null;
        string? foundAuth = null;

        var lines = SplitLines(content);
        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var rawLine = lines[lineIndex];
            if (multilineQuote != null || bracketDepth > 0 || braceDepth > 0)
            {
                if (skippingSection || skippingContinuation)
                {
                    skippedLines.Add(lineIndex);
                }

                if (!UpdateTomlDelimiters(rawLine, ref multilineQuote, ref bracketDepth, ref braceDepth))
                {
                    throw new InvalidDataException($"Malformed TOML string at line {lineIndex + 1}.");
                }

                if (multilineQuote == null && bracketDepth == 0 && braceDepth == 0)
                {
                    skippingContinuation = false;
                }

                continue;
            }

            var stripped = StripTomlComment(rawLine).Trim();
            if (string.IsNullOrEmpty(stripped))
            {
                continue;
            }

            if (stripped.StartsWith('['))
            {
                if (!TryParseTomlTableHeader(stripped, out var segments, out var isArray))
                {
                    throw new InvalidDataException($"Malformed TOML table header at line {lineIndex + 1}.");
                }

                if (!isArray)
                {
                    var key = string.Join('\0', segments);
                    if (!definedTables.Add(key))
                    {
                        throw new InvalidDataException($"Duplicate TOML table header at line {lineIndex + 1}.");
                    }
                }

                currentTable = segments;
                skippingSection = !isArray && IsFufuTomlSection(segments);
                if (skippingSection)
                {
                    skippedLines.Add(lineIndex);
                    foundFufuEntry = true;
                    if (segments.Count == 2)
                    {
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

            if (!TryParseTomlAssignmentLine(stripped, out var keySegments, out var rawValue))
            {
                throw new InvalidDataException($"Malformed TOML assignment at line {lineIndex + 1}.");
            }

            if (!UpdateTomlDelimiters(rawValue, ref multilineQuote, ref bracketDepth, ref braceDepth))
            {
                throw new InvalidDataException($"Malformed TOML value at line {lineIndex + 1}.");
            }

            var fullPath = new List<string>(currentTable.Count + keySegments.Count);
            fullPath.AddRange(currentTable);
            fullPath.AddRange(keySegments);

            var isFufuLine = skippingSection || IsFufuTomlSection(fullPath);
            if (isFufuLine)
            {
                skippedLines.Add(lineIndex);
                skippingContinuation = !skippingSection &&
                                       (multilineQuote != null || bracketDepth > 0 || braceDepth > 0);
                foundFufuEntry = true;

                if (TryDecodeTomlScalarString(rawValue, out var scalarValue))
                {
                    if (fullPath.Count == 3 && string.Equals(fullPath[2], "url", StringComparison.Ordinal))
                    {
                        foundUrl = scalarValue;
                    }
                    else if (fullPath.Count == 4 &&
                             string.Equals(fullPath[2], "http_headers", StringComparison.Ordinal) &&
                             string.Equals(fullPath[3], "Authorization", StringComparison.OrdinalIgnoreCase))
                    {
                        foundAuth = scalarValue;
                    }
                }
            }
        }

        if (multilineQuote != null || bracketDepth != 0 || braceDepth != 0)
        {
            throw new InvalidDataException("Malformed TOML: unclosed multiline string, array, or inline table.");
        }

        return new TomlScanResult(foundFufuEntry, foundUrl, foundAuth, skippedLines);
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

        return TryParseTomlKeyPath(inner, out segments);
    }

    private static bool TryParseTomlAssignmentLine(string strippedLine, out List<string> keySegments,
        out string rawValue)
    {
        keySegments = new List<string>();
        rawValue = string.Empty;

        var equals = FindUnquotedEquals(strippedLine);
        if (equals <= 0)
        {
            return false;
        }

        var lhs = strippedLine[..equals].Trim();
        rawValue = strippedLine[(equals + 1)..].Trim();
        if (string.IsNullOrEmpty(rawValue))
        {
            return false;
        }

        return TryParseTomlKeyPath(lhs, out keySegments);
    }

    private static int FindUnquotedEquals(string line)
    {
        char? quote = null;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quote == null)
            {
                if (ch == '=')
                {
                    return i;
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

        return -1;
    }

    private static bool TryParseTomlKeyPath(string keyText, out List<string> segments)
    {
        segments = new List<string>();
        if (string.IsNullOrWhiteSpace(keyText))
        {
            return false;
        }

        var index = 0;
        while (index < keyText.Length)
        {
            while (index < keyText.Length && char.IsWhiteSpace(keyText[index]))
            {
                index++;
            }

            if (index >= keyText.Length)
            {
                return false;
            }

            if (keyText[index] is '"' or '\'')
            {
                var quote = keyText[index++];
                var sb = new StringBuilder();
                var closed = false;
                while (index < keyText.Length)
                {
                    var ch = keyText[index++];
                    if (quote == '"' && ch == '\\' && index < keyText.Length)
                    {
                        sb.Append(UnescapeTomlChar(keyText[index++]));
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
                while (index < keyText.Length && IsTomlBareKeyChar(keyText[index]))
                {
                    index++;
                }

                if (index == start)
                {
                    return false;
                }

                segments.Add(keyText[start..index]);
            }

            while (index < keyText.Length && char.IsWhiteSpace(keyText[index]))
            {
                index++;
            }

            if (index < keyText.Length)
            {
                if (keyText[index] != '.')
                {
                    return false;
                }

                index++;
                if (index >= keyText.Length)
                {
                    return false;
                }
            }
        }

        return segments.Count > 0;
    }

    private static bool IsTomlBareKeyChar(char ch) =>
        char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-';

    private static bool TryDecodeTomlScalarString(string rawValue, out string? value)
    {
        value = null;

        if (rawValue.Length >= 2 && rawValue[0] == '"' && rawValue[^1] == '"' &&
            !rawValue.StartsWith("\"\"\"", StringComparison.Ordinal))
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

        if (rawValue.Length >= 2 && rawValue[0] == '\'' && rawValue[^1] == '\'' &&
            !rawValue.StartsWith("'''", StringComparison.Ordinal))
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

    private static bool UpdateTomlDelimiters(string line, ref string? multilineQuote, ref int bracketDepth,
        ref int braceDepth)
    {
        var i = 0;
        while (i < line.Length)
        {
            if (multilineQuote != null)
            {
                if (multilineQuote == "\"\"\"" && line[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (i <= line.Length - 3 && line.AsSpan(i, 3).SequenceEqual(multilineQuote))
                {
                    multilineQuote = null;
                    i += 3;
                    continue;
                }

                i++;
                continue;
            }

            if (i <= line.Length - 3 && line.AsSpan(i, 3).SequenceEqual("\"\"\""))
            {
                multilineQuote = "\"\"\"";
                i += 3;
                continue;
            }

            if (i <= line.Length - 3 && line.AsSpan(i, 3).SequenceEqual("'''"))
            {
                multilineQuote = "'''";
                i += 3;
                continue;
            }

            var ch = line[i];
            if (ch == '#')
            {
                return true;
            }

            if (ch is '"' or '\'')
            {
                var q = ch;
                i++;
                var closed = false;
                while (i < line.Length)
                {
                    if (q == '"' && line[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (line[i] == q)
                    {
                        closed = true;
                        i++;
                        break;
                    }

                    i++;
                }

                if (!closed)
                {
                    return false;
                }

                continue;
            }

            switch (ch)
            {
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    bracketDepth--;
                    if (bracketDepth < 0)
                    {
                        return false;
                    }

                    break;
                case '{':
                    braceDepth++;
                    break;
                case '}':
                    braceDepth--;
                    if (braceDepth < 0)
                    {
                        return false;
                    }

                    break;
            }

            i++;
        }

        return true;
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

        var stream = LoadCordisStream(content);
        if (stream.Documents.Count == 0)
        {
            return McpAgentInstallState.NotConfigured;
        }

        var root = stream.Documents[0].RootNode as YamlSequenceNode
                   ?? throw new InvalidDataException("Expected cordis.patch.yml root to be a YAML sequence.");

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
        var stream = string.IsNullOrWhiteSpace(existingContent)
            ? new YamlStream()
            : LoadCordisStream(existingContent);

        YamlSequenceNode root;
        if (stream.Documents.Count == 0)
        {
            root = new YamlSequenceNode();
            stream.Add(new YamlDocument(root));
        }
        else
        {
            root = stream.Documents[0].RootNode as YamlSequenceNode
                   ?? throw new InvalidDataException("Expected cordis.patch.yml root to be a YAML sequence.");
        }

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

        return NormalizeNewline(SerializeCordisYamlStream(stream), newline);
    }

    private static YamlStream LoadCordisStream(string content)
    {
        var stream = new YamlStream();
        using var reader = new StringReader(content);
        stream.Load(reader);
        return stream;
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

    private static bool TryGetMappingChild<TNode>(YamlMappingNode mapping, string key,
        [NotNullWhen(true)] out TNode? node)
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

    private static string SerializeCordisYamlStream(YamlStream stream)
    {
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        var text = writer.ToString().Replace("\r\n", "\n");

        // Keep explicit document separators for multi-document streams; trim single-doc trailer.
        if (stream.Documents.Count <= 1)
        {
            if (text.EndsWith("\n...\n", StringComparison.Ordinal))
            {
                text = text[..^4];
            }
            else if (text.EndsWith("...", StringComparison.Ordinal))
            {
                text = text[..^3];
            }
        }

        return text.TrimEnd('\n') + "\n";
    }

    private static void WriteFileAtomically(string targetPath, string content, bool hadExistingFile)
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

            if (hadExistingFile && File.Exists(targetPath))
            {
                // File.Replace preserves the existing target file's NTFS ACL on Windows.
                File.Replace(tempPath, targetPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, targetPath, overwrite: false);
            }
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

    private sealed record TomlScanResult(
        bool FoundFufuEntry,
        string? FoundUrl,
        string? FoundAuth,
        HashSet<int> SkippedLineIndices);
}
