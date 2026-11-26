using System.Text;
using System.Text.Json.Nodes;
using FufuLauncher.Services.Mcp;
using Xunit;

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
        _service = new McpAgentConfigService(_userProfileDir, _appDataDir);
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
        File.WriteAllText(path, """
            # User comment
            model = "gpt-5"
            instructions = '''
            [mcp_servers.fufu-launcher]
            url = "http://not-a-section"
            '''

            [mcp_servers.cadence]
            command = 'cadence-cli.exe'

            [mcp_servers."fufu-launcher"]
            url = "http://127.0.0.1:9999/mcp"

            [mcp_servers."fufu-launcher".http_headers]
            Authorization = "Bearer oldtoken"

            [windows]
            sandbox = "elevated"
            """);

        Assert.Equal(McpAgentInstallState.NeedsUpdate,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);

        var result = _service.InstallOrUpdate(McpAgentId.Codex, LocalUrl, Token);
        Assert.True(result.Succeeded);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.Codex, LocalUrl, Token).State);

        var text = File.ReadAllText(path);
        Assert.Contains("# User comment", text);
        Assert.Contains("url = \"http://not-a-section\"", text);
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
    public void DeepSeekHarnessCordisYamlPreservesExistingEntriesAndJsTags()
    {
        var path = _service.ResolveConfigPath(McpAgentId.DeepSeekHarness, "custom");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """
            - insert:
                - id: mcp-github
                  name: '@deepseek-ai/dsh-mcp-client'
                  config:
                    serverName: github
                    transport: stdio
                    command: npx
                    env:
                      GITHUB_TOKEN: !!js process.env.GITHUB_TOKEN
            """);

        Assert.Equal(McpAgentInstallState.NotConfigured,
            _service.Inspect(McpAgentId.DeepSeekHarness, LocalUrl, Token, "custom").State);

        var install = _service.InstallOrUpdate(McpAgentId.DeepSeekHarness, LocalUrl, Token, "custom");
        Assert.True(install.Succeeded);
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
        Assert.Contains("mcp-github", yamlText);
        Assert.Contains("!!js", yamlText);
        Assert.Contains(updatedUrl, yamlText);
        Assert.Equal(1, CountOccurrences(yamlText, "mcp-fufu-launcher"));
        Assert.DoesNotContain("Authorization", yamlText);
    }

    [Fact]
    public void DeepSeekHarnessMcpCenterUsesServersRootKey()
    {
        var install = _service.InstallOrUpdate(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, Token);
        Assert.True(install.Succeeded);
        Assert.Equal(McpAgentInstallState.Configured,
            _service.Inspect(McpAgentId.DeepSeekHarnessMcpCenter, LocalUrl, Token).State);

        var root = JsonNode.Parse(File.ReadAllText(install.FilePath))!.AsObject();
        var fufu = root["servers"]![McpAgentConfigService.ServerName]!.AsObject();
        Assert.Equal("streamable-http", fufu["transport"]!.GetValue<string>());
        Assert.Equal(LocalUrl, fufu["url"]!.GetValue<string>());
    }

    [Fact]
    public void DiscoverDshProfilesEnumeratesFoldersAndRejectsPathTraversal()
    {
        Assert.Equal(["web"], _service.DiscoverDshProfiles());

        Directory.CreateDirectory(Path.Combine(_userProfileDir, ".dsh", "profiles", "alpha"));
        Directory.CreateDirectory(Path.Combine(_userProfileDir, ".dsh", "profiles", "beta"));
        Assert.Equal(["alpha", "beta"], _service.DiscoverDshProfiles());

        Assert.Throws<ArgumentException>(() => _service.ResolveConfigPath(McpAgentId.DeepSeekHarness, "../evil"));
    }

    [Theory]
    [InlineData(McpAgentId.Antigravity, "mcpServers", "Authorization")]
    [InlineData(McpAgentId.ClaudeCode, "\"type\": \"http\"", "Authorization")]
    [InlineData(McpAgentId.Codex, "[mcp_servers.fufu-launcher]", "[mcp_servers.fufu-launcher.http_headers]")]
    [InlineData(McpAgentId.ClaudeDesktop, "\"--allow-http\"", "Authorization: Bearer")]
    [InlineData(McpAgentId.DeepSeekHarness, "@deepseek-ai/dsh-mcp-client", "Authorization")]
    [InlineData(McpAgentId.DeepSeekHarnessMcpCenter, "\"servers\"", "Authorization")]
    public void BuildSnippetProducesValidAuthAndNoAuthVariantsForEveryClient(
        McpAgentId agentId,
        string expectedMarker,
        string expectedAuthMarker)
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
        Assert.DoesNotContain("Authorization", withoutAuth);
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
