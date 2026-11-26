using System.Text;
using System.Text.Json.Nodes;
using FufuLauncher.Services.Mcp;
using Xunit;
using YamlDotNet.RepresentationModel;

namespace FufuLauncher.Tests;

public sealed class McpAgentConfigServiceTests : IDisposable
{
    private const string LocalUrl = "http://127.0.0.1:8790/mcp";
    private const string Token = "fb0b27bf1f858f13dd609504d28de657";

    private readonly string _tempRoot;
    private readonly string _userProfileDir;
    private readonly string _appDataDir;
    private readonly McpAgentConfigService _service;

    public McpAgentConfigServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"fufu-mcp-test-{Guid.NewGuid():N}");
        _userProfileDir = Path.Combine(_tempRoot, "UserProfile");
        _appDataDir = Path.Combine(_tempRoot, "AppData");
        Directory.CreateDirectory(_userProfileDir);
        Directory.CreateDirectory(_appDataDir);
        _service = new McpAgentConfigService(_userProfileDir, _appDataDir, _ => null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    [Fact]
    public void AntigravityInstallCreatesAndUpdatesWithoutClobberingExistingServers()
    {
        var path = _service.ResolveConfigPath(McpAgentId.Antigravity);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "mcpServers": {
                "other": {
                  "command": "other-cli.exe"
                }
              }
            }
            """, new UTF8Encoding(false));

        Assert.Equal(McpAgentInstallState.NotConfigured,
            _service.Inspect(McpAgentId.Antigravity, LocalUrl, Token).State);

        var result = _service.InstallOrUpdate(McpAgentId.Antigravity, LocalUrl, Token);
        Assert.True(result.Succeeded);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.Antigravity, LocalUrl, Token).State);

        // Changing port should trigger NeedsUpdate, then updating should bring back Configured.
        const string newUrl = "http://127.0.0.1:8791/mcp";
        Assert.Equal(McpAgentInstallState.NeedsUpdate,
            _service.Inspect(McpAgentId.Antigravity, newUrl, Token).State);

        Assert.True(_service.InstallOrUpdate(McpAgentId.Antigravity, newUrl, null).Succeeded);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.Antigravity, newUrl, null).State);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var servers = root["mcpServers"]!.AsObject();
        Assert.Equal("other-cli.exe", servers["other"]!["command"]!.GetValue<string>());
        var fufu = servers[McpAgentConfigService.ServerName]!.AsObject();
        Assert.Equal(newUrl, fufu["url"]!.GetValue<string>());
        Assert.False(fufu.ContainsKey("headers"));

        var rawBytes = File.ReadAllBytes(path);
        Assert.False(rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF);
    }

    [Fact]
    public void ClaudeCodePreservesTopLevelPropertiesAndAddsHttpType()
    {
        var path = _service.ResolveConfigPath(McpAgentId.ClaudeCode);
        File.WriteAllText(path, """
            {
              "numStartups": 42,
              "projects": { "demo": { "allowed": true } },
              "mcpServers": {
                "existing": { "type": "stdio", "command": "node" }
              }
            }
            """);

        var install = _service.InstallOrUpdate(McpAgentId.ClaudeCode, LocalUrl, Token);
        Assert.True(install.Succeeded);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.ClaudeCode, LocalUrl, Token).State);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(42, root["numStartups"]!.GetValue<int>());
        Assert.True(root["projects"]!["demo"]!["allowed"]!.GetValue<bool>());
        Assert.Equal("node", root["mcpServers"]!["existing"]!["command"]!.GetValue<string>());

        var fufu = root["mcpServers"]![McpAgentConfigService.ServerName]!.AsObject();
        Assert.Equal("http", fufu["type"]!.GetValue<string>());
        Assert.Equal(LocalUrl, fufu["url"]!.GetValue<string>());
        Assert.Equal($"Bearer {Token}", fufu["headers"]!["Authorization"]!.GetValue<string>());
    }

    [Fact]
    public void ClaudeDesktopUsesMcpRemoteWithAllowHttpForHttpEndpoints()
    {
        var install = _service.InstallOrUpdate(McpAgentId.ClaudeDesktop, LocalUrl, Token);
        Assert.True(install.Succeeded, install.ErrorMessage);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.ClaudeDesktop, LocalUrl, Token).State);

        var root = JsonNode.Parse(File.ReadAllText(install.FilePath))!.AsObject();
        var fufu = root["mcpServers"]![McpAgentConfigService.ServerName]!.AsObject();
        Assert.Equal("npx", fufu["command"]!.GetValue<string>());
        var args = fufu["args"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(["-y", "mcp-remote", LocalUrl, "--allow-http", "--header", $"Authorization: Bearer {Token}"],
            args);

        var httpsSnippet = _service.BuildSnippet(McpAgentId.ClaudeDesktop, "https://example.com/mcp", null);
        var httpsArgs = JsonNode.Parse(httpsSnippet)!["mcpServers"]![McpAgentConfigService.ServerName]!["args"]!
            .AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(["-y", "mcp-remote", "https://example.com/mcp"], httpsArgs);
    }

    [Fact]
    public void MalformedJsonDoesNotOverwriteExistingFile()
    {
        var path = _service.ResolveConfigPath(McpAgentId.Antigravity);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string broken = "{ broken json";
        File.WriteAllText(path, broken);

        var inspection = _service.Inspect(McpAgentId.Antigravity, LocalUrl, Token);
        Assert.Equal(McpAgentInstallState.InvalidFile, inspection.State);

        var result = _service.InstallOrUpdate(McpAgentId.Antigravity, LocalUrl, Token);
        Assert.False(result.Succeeded);
        Assert.Equal(broken, File.ReadAllText(path));
    }

    [Fact]
    public void CodexTomlMergesPreservesOtherSectionsAndCommentsAndUpdatesCleanly()
    {
        var path = _service.ResolveConfigPath(McpAgentId.Codex);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """"
            # User comment
            model = "gpt-5"
            instructions = '''
            [mcp_servers.fufu-launcher]
            url = "http://not-a-section"
            '''
            escaped_doc = """
            prefix \"""
            [mcp_servers.fufu-launcher]
            url = "http://still-inside-string"
            """

            [mcp_servers.cadence]
            command = 'cadence-cli.exe'

            [mcp_servers."fufu-launcher"]
            url = "http://127.0.0.1:9999/mcp"

            [mcp_servers."fufu-launcher".http_headers]
            Authorization = "Bearer oldtoken"

            [windows]
            sandbox = "elevated"
            """");

        Assert.Equal(McpAgentInstallState.NeedsUpdate,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);

        var result = _service.InstallOrUpdate(McpAgentId.Codex, LocalUrl, Token);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);

        var text = File.ReadAllText(path);
        Assert.Contains("# User comment", text);
        Assert.Contains("url = \"http://not-a-section\"", text);
        Assert.Contains("url = \"http://still-inside-string\"", text);
        Assert.Contains("[mcp_servers.cadence]", text);
        Assert.Contains("[windows]", text);
        Assert.DoesNotContain("oldtoken", text);
        Assert.DoesNotContain("9999", text);
        Assert.Contains("[mcp_servers.fufu-launcher]", text);
        Assert.Contains($"Authorization = \"Bearer {Token}\"", text);

        // Removing auth should remove http_headers section and still report Configured.
        Assert.True(_service.InstallOrUpdate(McpAgentId.Codex, LocalUrl, null).Succeeded);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.Codex, LocalUrl, null).State);
        Assert.DoesNotContain("http_headers", File.ReadAllText(path));
    }

    [Fact]
    public void CodexTomlHandlesDottedKeysAndRejectsMalformedInput()
    {
        var path = _service.ResolveConfigPath(McpAgentId.Codex);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            model = "gpt-5"
            mcp_servers.fufu-launcher.url = "http://127.0.0.1:9999/mcp"
            mcp_servers.fufu-launcher.http_headers.Authorization = "Bearer oldtoken"
            """);

        Assert.Equal(McpAgentInstallState.NeedsUpdate,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);

        var result = _service.InstallOrUpdate(McpAgentId.Codex, LocalUrl, Token);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);

        var updated = File.ReadAllText(path);
        Assert.DoesNotContain("mcp_servers.fufu-launcher.url", updated);
        Assert.DoesNotContain("oldtoken", updated);
        Assert.Contains("[mcp_servers.fufu-launcher]", updated);

        const string malformed = "model = [\n";
        File.WriteAllText(path, malformed);
        Assert.Equal(McpAgentInstallState.InvalidFile,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);
        var failedInstall = _service.InstallOrUpdate(McpAgentId.Codex, LocalUrl, Token);
        Assert.False(failedInstall.Succeeded);
        Assert.Equal(malformed, File.ReadAllText(path));
    }

    [Fact]
    public void CodexTomlAllowsRepeatedArrayOfTablesSubtables()
    {
        var path = _service.ResolveConfigPath(McpAgentId.Codex);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            model = "gpt-5"

            [[items]]
            name = "first"

            [items.options]
            enabled = true

            [[items]]
            name = "second"

            [items.options]
            enabled = false
            """);

        Assert.Equal(McpAgentInstallState.NotConfigured,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);

        var result = _service.InstallOrUpdate(McpAgentId.Codex, LocalUrl, Token);
        Assert.True(result.Succeeded, result.ErrorMessage);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);

        var updated = File.ReadAllText(path);
        Assert.Contains("name = \"first\"", updated);
        Assert.Contains("name = \"second\"", updated);
        Assert.Contains("[mcp_servers.fufu-launcher]", updated);
    }

    [Fact]
    public void CodexTomlRejectsInlineMcpServersTableAndHandlesInlineHttpHeaders()
    {
        var path = _service.ResolveConfigPath(McpAgentId.Codex);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        const string inlineMcpServers = """
            model = "gpt-5"
            mcp_servers = { docs = { url = "http://127.0.0.1:1234/mcp" } }
            """;
        File.WriteAllText(path, inlineMcpServers);
        Assert.Equal(McpAgentInstallState.InvalidFile,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);
        var failed = _service.InstallOrUpdate(McpAgentId.Codex, LocalUrl, Token);
        Assert.False(failed.Succeeded);
        Assert.Equal(inlineMcpServers, File.ReadAllText(path));

        File.WriteAllText(path, $$"""
            [mcp_servers.fufu-launcher]
            url = "{{LocalUrl}}"
            http_headers = { Authorization = "Bearer stale-token", X-Test = "1" }
            """);
        Assert.Equal(McpAgentInstallState.NeedsUpdate,
            _service.Inspect(McpAgentId.Codex, LocalUrl, null).State);

        var cleaned = _service.InstallOrUpdate(McpAgentId.Codex, LocalUrl, null);
        Assert.True(cleaned.Succeeded, cleaned.ErrorMessage);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.Codex, LocalUrl, null).State);
        var updated = File.ReadAllText(path);
        Assert.DoesNotContain("Authorization", updated);
        Assert.DoesNotContain("stale-token", updated);
    }

    [Fact]
    public void DeepSeekHarnessCordisYamlPreservesExistingEntriesJsTagsAndExtraDocuments()
    {
        var path = _service.ResolveConfigPath(McpAgentId.DeepSeekHarness, "custom");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            ---
            - insert:
                - id: mcp-github
                  name: '@deepseek-ai/dsh-mcp-client'
                  config:
                    serverName: github
                    transport: stdio
                    command: npx
                    env:
                      GITHUB_TOKEN: !!js process.env.GITHUB_TOKEN
            ---
            - note: second-document
            """);

        Assert.Equal(McpAgentInstallState.NotConfigured,
            _service.Inspect(McpAgentId.DeepSeekHarness, LocalUrl, Token, "custom").State);

        var install = _service.InstallOrUpdate(McpAgentId.DeepSeekHarness, LocalUrl, Token, "custom");
        Assert.True(install.Succeeded, install.ErrorMessage);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.DeepSeekHarness, LocalUrl, Token, "custom").State);

        // Update endpoint & verify in-place replacement without duplicating entry.
        const string updatedUrl = "http://127.0.0.1:8795/mcp";
        Assert.Equal(McpAgentInstallState.NeedsUpdate,
            _service.Inspect(McpAgentId.DeepSeekHarness, updatedUrl, Token, "custom").State);
        Assert.True(_service.InstallOrUpdate(McpAgentId.DeepSeekHarness, updatedUrl, null, "custom").Succeeded);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.DeepSeekHarness, updatedUrl, null, "custom").State);

        var yamlText = File.ReadAllText(path);
        using var reader = new StringReader(yamlText);
        var stream = new YamlStream();
        stream.Load(reader);

        Assert.Equal(2, stream.Documents.Count);

        var doc1Sequence = Assert.IsType<YamlSequenceNode>(stream.Documents[0].RootNode);
        var doc1RootMap = Assert.IsType<YamlMappingNode>(Assert.Single(doc1Sequence.Children));
        var insertEntries = Assert.IsType<YamlSequenceNode>(doc1RootMap[new YamlScalarNode("insert")]);
        Assert.Equal(2, insertEntries.Children.Count);

        var githubEntry = insertEntries.Children
            .OfType<YamlMappingNode>()
            .Single(m => ((YamlScalarNode)m[new YamlScalarNode("id")]).Value == "mcp-github");
        Assert.Equal("@deepseek-ai/dsh-mcp-client",
            ((YamlScalarNode)githubEntry[new YamlScalarNode("name")]).Value);
        var githubConfig = Assert.IsType<YamlMappingNode>(githubEntry[new YamlScalarNode("config")]);
        Assert.Equal("github", ((YamlScalarNode)githubConfig[new YamlScalarNode("serverName")]).Value);
        Assert.Equal("stdio", ((YamlScalarNode)githubConfig[new YamlScalarNode("transport")]).Value);
        Assert.Equal("npx", ((YamlScalarNode)githubConfig[new YamlScalarNode("command")]).Value);
        var githubEnv = Assert.IsType<YamlMappingNode>(githubConfig[new YamlScalarNode("env")]);
        var githubTokenNode = Assert.IsType<YamlScalarNode>(githubEnv[new YamlScalarNode("GITHUB_TOKEN")]);
        Assert.Equal("process.env.GITHUB_TOKEN", githubTokenNode.Value);

        var githubTokenLine = Assert.Single(
            yamlText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries),
            line => line.Contains("GITHUB_TOKEN:", StringComparison.Ordinal));
        Assert.Contains("!!js", githubTokenLine);

        var fufuEntry = insertEntries.Children
            .OfType<YamlMappingNode>()
            .Single(m => ((YamlScalarNode)m[new YamlScalarNode("id")]).Value == "mcp-fufu-launcher");
        Assert.Equal("@deepseek-ai/dsh-mcp-client",
            ((YamlScalarNode)fufuEntry[new YamlScalarNode("name")]).Value);
        var fufuConfig = Assert.IsType<YamlMappingNode>(fufuEntry[new YamlScalarNode("config")]);
        Assert.Equal("fufu-launcher", ((YamlScalarNode)fufuConfig[new YamlScalarNode("serverName")]).Value);
        Assert.Equal("streamable-http", ((YamlScalarNode)fufuConfig[new YamlScalarNode("transport")]).Value);
        Assert.Equal(updatedUrl, ((YamlScalarNode)fufuConfig[new YamlScalarNode("url")]).Value);
        Assert.False(fufuConfig.Children.ContainsKey(new YamlScalarNode("headers")));

        var doc2Sequence = Assert.IsType<YamlSequenceNode>(stream.Documents[1].RootNode);
        var doc2Map = Assert.IsType<YamlMappingNode>(Assert.Single(doc2Sequence.Children));
        Assert.Equal("second-document", ((YamlScalarNode)doc2Map[new YamlScalarNode("note")]).Value);
    }

    [Fact]
    public void DeepSeekHarnessMcpCenterUsesServersArraySchemaAndPreservesEntries()
    {
        var path = _service.ResolveConfigPath(McpAgentId.DeepSeekHarnessMcpCenter);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            {
              "servers": [
                {
                  "id": "srv_other",
                  "name": "other",
                  "type": "stdio",
                  "command": "npx",
                  "enabled": true
                },
                {
                  "id": "srv_custom_fufu",
                  "name": "fufu-launcher",
                  "type": "http",
                  "url": "http://127.0.0.1:9999/mcp",
                  "authMode": "none",
                  "enabled": false
                }
              ]
            }
            """);

        Assert.Equal(McpAgentInstallState.NeedsUpdate,
            _service.Inspect(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, Token).State);

        var install = _service.InstallOrUpdate(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, Token);
        Assert.True(install.Succeeded, install.ErrorMessage);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, Token).State);

        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var servers = root["servers"]!.AsArray();
        Assert.Equal(2, servers.Count);
        Assert.Equal("srv_other", servers[0]!["id"]!.GetValue<string>());

        var fufu = servers[1]!.AsObject();
        Assert.Equal("srv_custom_fufu", fufu["id"]!.GetValue<string>());
        Assert.Equal("http", fufu["type"]!.GetValue<string>());
        Assert.Equal(LocalUrl, fufu["url"]!.GetValue<string>());
        Assert.Equal("bearer", fufu["authMode"]!.GetValue<string>());
        Assert.Equal(Token, fufu["token"]!.GetValue<string>());
        Assert.False(fufu["enabled"]!.GetValue<bool>());

        // Auth off switches authMode to none and strips token.
        Assert.True(_service.InstallOrUpdate(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, null).Succeeded);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, null).State);
        var updatedRoot = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var updatedFufu = updatedRoot["servers"]![1]!.AsObject();
        Assert.Equal("none", updatedFufu["authMode"]!.GetValue<string>());
        Assert.False(updatedFufu.ContainsKey("token"));

        // Non-array servers and trailing commas should fail cleanly without overwriting.
        const string invalidCenter = """{ "servers": {} }""";
        File.WriteAllText(path, invalidCenter);
        Assert.Equal(McpAgentInstallState.InvalidFile,
            _service.Inspect(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, Token).State);
        Assert.False(_service.InstallOrUpdate(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, Token).Succeeded);
        Assert.Equal(invalidCenter, File.ReadAllText(path));

        const string trailingCommaCenter = """{"servers":[],}""";
        File.WriteAllText(path, trailingCommaCenter);
        Assert.Equal(McpAgentInstallState.InvalidFile,
            _service.Inspect(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, Token).State);
        Assert.False(_service.InstallOrUpdate(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, Token).Succeeded);
        Assert.Equal(trailingCommaCenter, File.ReadAllText(path));
    }

    [Fact]
    public void EnvOverridesControlCodexHomeAndDshHome()
    {
        var customCodexHome = Path.Combine(_tempRoot, "custom-codex");
        var customDshHome = Path.Combine(_tempRoot, "custom-dsh");
        Directory.CreateDirectory(Path.Combine(customDshHome, "profiles", "work"));

        var service = new McpAgentConfigService(
            _userProfileDir,
            _appDataDir,
            name => name switch
            {
                "CODEX_HOME" => customCodexHome,
                "DSH_HOME" => customDshHome,
                _ => null
            });

        Assert.Equal(["work"], service.DiscoverDshProfiles());
        Assert.Equal(Path.Combine(customCodexHome, "config.toml"),
            service.ResolveConfigPath(McpAgentId.Codex));
        Assert.Equal(Path.Combine(customDshHome, "profiles", "work", "cordis.patch.yml"),
            service.ResolveConfigPath(McpAgentId.DeepSeekHarness, "work"));
        Assert.Equal(Path.Combine(_userProfileDir, ".dsh", "mcp-center.json"),
            service.ResolveConfigPath(McpAgentId.DeepSeekHarnessMcpCenter));

        Assert.True(service.InstallOrUpdate(McpAgentId.Codex, LocalUrl, Token).Succeeded);
        Assert.True(File.Exists(Path.Combine(customCodexHome, "config.toml")));
        Assert.False(File.Exists(Path.Combine(_userProfileDir, ".codex", "config.toml")));
    }

    [Fact]
    public void DiscoverDshProfilesAndInvalidProfileInstallHandleErrorsGracefully()
    {
        Assert.Equal(["web"], _service.DiscoverDshProfiles());

        Directory.CreateDirectory(Path.Combine(_userProfileDir, ".dsh", "profiles", "alpha"));
        Directory.CreateDirectory(Path.Combine(_userProfileDir, ".dsh", "profiles", "beta"));
        Assert.Equal(["alpha", "beta"], _service.DiscoverDshProfiles());

        Assert.Throws<ArgumentException>(() => _service.ResolveConfigPath(McpAgentId.DeepSeekHarness, "../evil"));

        var dotResult = _service.InstallOrUpdate(McpAgentId.DeepSeekHarness, LocalUrl, Token, ".");
        Assert.False(dotResult.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(dotResult.ErrorMessage));
    }

    [Theory]
    [InlineData(McpAgentId.Antigravity, "mcpServers", "Authorization", "headers")]
    [InlineData(McpAgentId.ClaudeCode, "\"type\": \"http\"", "Authorization", "headers")]
    [InlineData(McpAgentId.Codex, "[mcp_servers.fufu-launcher]", "[mcp_servers.fufu-launcher.http_headers]", "Authorization")]
    [InlineData(McpAgentId.ClaudeDesktop, "\"--allow-http\"", "Authorization: Bearer", "Authorization")]
    [InlineData(McpAgentId.DeepSeekHarness, "@deepseek-ai/dsh-mcp-client", "Authorization", "Authorization")]
    [InlineData(McpAgentId.DeepSeekHarnessMcpCenter, "\"type\": \"http\"", "\"authMode\": \"bearer\"", "\"token\"")]
    public void BuildSnippetProducesValidAuthAndNoAuthVariantsForEveryClient(
        McpAgentId agentId,
        string expectedMarker,
        string expectedAuthMarker,
        string forbiddenNoAuthMarker)
    {
        var withAuth = _service.BuildSnippet(agentId, LocalUrl, Token);
        Assert.Contains(LocalUrl, withAuth);
        Assert.Contains(expectedMarker, withAuth);
        Assert.Contains(expectedAuthMarker, withAuth);
        Assert.Contains(Token, withAuth);

        var withoutAuth = _service.BuildSnippet(agentId, LocalUrl, null);
        Assert.Contains(LocalUrl, withoutAuth);
        Assert.Contains(expectedMarker, withoutAuth);
        Assert.DoesNotContain(Token, withoutAuth);
        Assert.DoesNotContain(forbiddenNoAuthMarker, withoutAuth);
    }
}
