/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Mcp;

public sealed class McpHostService
{
    public const int DefaultPort = 8790;
    public const int MinPort = 1024;
    public const int MaxPort = 65535;
    public const int MinTokenLength = 16;
    public const int MaxTokenLength = 128;

    private const string EnabledSettingKey = "Mcp_Enabled";
    private const string PortSettingKey = "Mcp_Port";
    private const string TokenSettingKey = "Mcp_Token";
    private const string RequireAuthSettingKey = "Mcp_RequireAuth";
    private const string TokenModeSettingKey = "Mcp_TokenMode";
    private const string CustomTokenSettingKey = "Mcp_CustomToken";
    private const string IpFilterEnabledSettingKey = "Mcp_IpFilterEnabled";
    private const string IpFilterModeSettingKey = "Mcp_IpFilterMode";
    private const string IpRulesSettingKey = "Mcp_IpRules";
    private const string AuditEnabledSettingKey = "Mcp_AuditEnabled";
    private const string ToolSettingPrefix = "Mcp_Tool_";

    private const string TokenModeAuto = "auto";
    private const string TokenModeCustom = "custom";
    private const string IpModeAllow = "allow";
    private const string IpModeDeny = "deny";

    private readonly ILocalSettingsService _settings;
    private readonly McpHttpServer _server = new();
    private readonly McpProtocolHandler _protocol;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly Dictionary<string, bool> _toolSwitches = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    private bool _initialized;
    private bool _enabled;
    private int _port = DefaultPort;
    private string _token = string.Empty;
    private bool _requireAuth = true;
    private bool _customTokenMode;
    private string _customToken = string.Empty;
    private bool _ipFilterEnabled;
    private bool _ipAllowMode = true;
    private string _ipRulesText = string.Empty;
    private List<McpIpRule> _ipRules = new();
    private string? _lastError;

    public McpHostService(ILocalSettingsService settings, McpToolInvoker invoker)
    {
        _settings = settings;
        _protocol = new McpProtocolHandler(this, invoker);
        _server.Diagnostic += message => DebugLog.Info("McpHost", $"[Mcp] {message}");
    }

    public event EventHandler? StatusChanged;

    public McpAuditLog Audit
    {
        get;
    } = new();

    public bool IsEnabled
    {
        get
        {
            lock (_stateLock)
            {
                return _enabled;
            }
        }
    }

    public bool IsRunning => _server.IsRunning;

    public int Port
    {
        get
        {
            lock (_stateLock)
            {
                return _port;
            }
        }
    }

    public bool RequireAuth
    {
        get
        {
            lock (_stateLock)
            {
                return _requireAuth;
            }
        }
    }

    public bool IsCustomTokenMode
    {
        get
        {
            lock (_stateLock)
            {
                return _customTokenMode;
            }
        }
    }

    public string AccessToken
    {
        get
        {
            lock (_stateLock)
            {
                return _customTokenMode ? _customToken : _token;
            }
        }
    }

    public string CustomToken
    {
        get
        {
            lock (_stateLock)
            {
                return _customToken;
            }
        }
    }

    public bool IpFilterEnabled
    {
        get
        {
            lock (_stateLock)
            {
                return _ipFilterEnabled;
            }
        }
    }

    public bool IpAllowMode
    {
        get
        {
            lock (_stateLock)
            {
                return _ipAllowMode;
            }
        }
    }

    public string IpRulesText
    {
        get
        {
            lock (_stateLock)
            {
                return _ipRulesText;
            }
        }
    }

    public bool AuditEnabled => Audit.IsEnabled;

    public string? LastError
    {
        get
        {
            lock (_stateLock)
            {
                return _lastError;
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _initializeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await LoadSettingsAsync().ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    public async Task StartIfEnabledAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        if (!IsEnabled)
        {
            return;
        }

        await StartAsync().ConfigureAwait(false);
    }

    public async Task<bool> StartAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_server.IsRunning)
            {
                return true;
            }

            try
            {
                await _server.StartAsync(Port, _protocol.HandleAsync).ConfigureAwait(false);
                SetLastError(null);
                DebugLog.Info("McpHost", $"[Mcp] 服务已启动，端口 {Port}");
                RaiseStatusChanged();
                return true;
            }
            catch (Exception ex)
            {
                SetLastError(ex.Message);
                DebugLog.Info("McpHost", $"[Mcp] 服务启动失败：{ex.Message}");
                RaiseStatusChanged();
                return false;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _server.StopAsync().ConfigureAwait(false);
        SetLastError(null);
        DebugLog.Info("McpHost", "[Mcp] 服务已停止");
        RaiseStatusChanged();
    }

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        await InitializeAsync().ConfigureAwait(false);

        lock (_stateLock)
        {
            _enabled = enabled;
        }

        await _settings.SaveSettingAsync(EnabledSettingKey, enabled).ConfigureAwait(false);
        RaiseStatusChanged();

        if (enabled)
        {
            return await StartAsync().ConfigureAwait(false);
        }

        await StopAsync().ConfigureAwait(false);
        return true;
    }

    public async Task<bool> SetPortAsync(int port)
    {
        await InitializeAsync().ConfigureAwait(false);

        if (port is < MinPort or > MaxPort)
        {
            SetLastError(string.Format("Mcp_PortInvalid".GetLocalized(), MinPort, MaxPort));
            RaiseStatusChanged();
            return false;
        }

        int previous;
        bool wasRunning;
        lock (_stateLock)
        {
            previous = _port;
            _port = port;
        }

        if (previous == port)
        {
            return true;
        }

        await _settings.SaveSettingAsync(PortSettingKey, port).ConfigureAwait(false);

        wasRunning = _server.IsRunning;
        if (!wasRunning)
        {
            SetLastError(null);
            RaiseStatusChanged();
            return true;
        }

        await _server.StopAsync().ConfigureAwait(false);
        var started = await StartAsync().ConfigureAwait(false);
        if (!started)
        {
            DebugLog.Info("McpHost", $"[Mcp] 端口切换后重启服务失败：{LastError}");
        }

        return started;
    }

    public async Task<bool> SetRequireAuthAsync(bool requireAuth)
    {
        await InitializeAsync().ConfigureAwait(false);

        lock (_stateLock)
        {
            _requireAuth = requireAuth;
        }

        await _settings.SaveSettingAsync(RequireAuthSettingKey, requireAuth).ConfigureAwait(false);
        RaiseStatusChanged();
        return true;
    }

    public async Task<bool> SetCustomTokenModeAsync(bool custom)
    {
        await InitializeAsync().ConfigureAwait(false);

        string autoToken;
        lock (_stateLock)
        {
            _customTokenMode = custom;
            if (!custom && string.IsNullOrWhiteSpace(_token))
            {
                _token = CreateToken();
            }

            autoToken = _token;
        }

        await _settings.SaveSettingAsync(TokenModeSettingKey, custom ? TokenModeCustom : TokenModeAuto)
            .ConfigureAwait(false);
        await _settings.SaveSettingAsync(TokenSettingKey, autoToken).ConfigureAwait(false);
        RaiseStatusChanged();
        return true;
    }

    public async Task<string?> SaveCustomTokenAsync(string? token)
    {
        await InitializeAsync().ConfigureAwait(false);
        var trimmed = (token ?? string.Empty).Trim();

        if (!IsValidToken(trimmed))
        {
            return "Mcp_TokenInvalid".GetLocalized();
        }

        lock (_stateLock)
        {
            _customToken = trimmed;
            _customTokenMode = true;
        }

        await _settings.SaveSettingAsync(CustomTokenSettingKey, trimmed).ConfigureAwait(false);
        await _settings.SaveSettingAsync(TokenModeSettingKey, TokenModeCustom).ConfigureAwait(false);
        RaiseStatusChanged();
        return null;
    }

    public async Task<string> RegenerateTokenAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        var token = CreateToken();

        lock (_stateLock)
        {
            _token = token;
        }

        await _settings.SaveSettingAsync(TokenSettingKey, token).ConfigureAwait(false);
        RaiseStatusChanged();
        return token;
    }

    public async Task<(bool Ok, string? Error)> SaveIpFilterAsync(bool enabled, bool allowMode, string? rulesText)
    {
        await InitializeAsync().ConfigureAwait(false);

        var text = (rulesText ?? string.Empty).Trim();
        if (!McpIpFilter.TryParse(text, out var rules, out var invalid))
        {
            return (false, string.Format("Mcp_IpRulesInvalid".GetLocalized(),
                string.Join(", ", invalid)));
        }

        lock (_stateLock)
        {
            _ipFilterEnabled = enabled;
            _ipAllowMode = allowMode;
            _ipRulesText = text;
            _ipRules = rules;
        }

        await _settings.SaveSettingAsync(IpFilterEnabledSettingKey, enabled).ConfigureAwait(false);
        await _settings.SaveSettingAsync(IpFilterModeSettingKey, allowMode ? IpModeAllow : IpModeDeny)
            .ConfigureAwait(false);
        await _settings.SaveSettingAsync(IpRulesSettingKey, text).ConfigureAwait(false);
        RaiseStatusChanged();
        return (true, null);
    }

    public async Task SetAuditEnabledAsync(bool enabled)
    {
        await InitializeAsync().ConfigureAwait(false);
        Audit.SetEnabled(enabled);
        await _settings.SaveSettingAsync(AuditEnabledSettingKey, enabled).ConfigureAwait(false);
        RaiseStatusChanged();
    }

    public bool IsConnectionAllowed(IPAddress? address)
    {
        bool enabled;
        bool allowMode;
        List<McpIpRule> rules;

        lock (_stateLock)
        {
            enabled = _ipFilterEnabled;
            allowMode = _ipAllowMode;
            rules = _ipRules;
        }

        if (!enabled)
        {
            return true;
        }

        if (address == null)
        {
            return false;
        }

        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        var matched = rules.Any(rule => rule.Matches(normalized));

        return allowMode ? matched || IPAddress.IsLoopback(normalized) : !matched;
    }

    public bool IsToolEnabled(string toolId)
    {
        lock (_stateLock)
        {
            return _toolSwitches.TryGetValue(toolId, out var enabled) && enabled;
        }
    }

    public async Task SaveToolEnabledAsync(string toolId, bool enabled)
    {
        if (McpToolCatalog.Find(toolId) == null)
        {
            return;
        }

        lock (_stateLock)
        {
            _toolSwitches[toolId] = enabled;
        }

        await _settings.SaveSettingAsync(ToolSettingPrefix + toolId, enabled).ConfigureAwait(false);
        DebugLog.Info("McpHost", $"[Mcp] 工具 {toolId} {(enabled ? "已启用" : "已禁用")}");
    }

    public bool ValidateToken(string? candidate)
    {
        bool requireAuth;
        string expected;

        lock (_stateLock)
        {
            requireAuth = _requireAuth;
            expected = _customTokenMode ? _customToken : _token;
        }

        if (!requireAuth)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrEmpty(expected))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var candidateBytes = Encoding.UTF8.GetBytes(candidate.Trim());
        return expectedBytes.Length == candidateBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, candidateBytes);
    }

    public static bool IsValidToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        var trimmed = token.Trim();
        if (trimmed.Length is < MinTokenLength or > MaxTokenLength)
        {
            return false;
        }

        foreach (var character in trimmed)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    public string GetPrimaryEndpoint()
    {
        var lanAddresses = GetLanAddresses();
        return lanAddresses.Count > 0
            ? ApiEndpoints.GetMcpEndpointUrl(lanAddresses[0], Port)
            : ApiEndpoints.GetMcpLocalEndpointUrl(Port);
    }

    private async Task LoadSettingsAsync()
    {
        var enabled = false;
        var port = DefaultPort;
        var token = string.Empty;
        var requireAuth = true;
        var customTokenMode = false;
        var customToken = string.Empty;
        var ipFilterEnabled = false;
        var ipAllowMode = true;
        var ipRulesText = string.Empty;
        var auditEnabled = true;

        try
        {
            var enabledValue = await _settings.ReadSettingAsync(EnabledSettingKey).ConfigureAwait(false);
            enabled = enabledValue != null && Convert.ToBoolean(enabledValue);

            var portValue = await _settings.ReadSettingAsync(PortSettingKey).ConfigureAwait(false);
            if (portValue != null && int.TryParse(portValue.ToString(), out var parsedPort) &&
                parsedPort is >= MinPort and <= MaxPort)
            {
                port = parsedPort;
            }

            var tokenValue = await _settings.ReadSettingAsync(TokenSettingKey).ConfigureAwait(false);
            token = tokenValue?.ToString() ?? string.Empty;

            var requireAuthValue = await _settings.ReadSettingAsync(RequireAuthSettingKey).ConfigureAwait(false);
            requireAuth = requireAuthValue == null || Convert.ToBoolean(requireAuthValue);

            var tokenModeValue = await _settings.ReadSettingAsync(TokenModeSettingKey).ConfigureAwait(false);
            customTokenMode = string.Equals(tokenModeValue?.ToString(), TokenModeCustom,
                StringComparison.OrdinalIgnoreCase);

            var customTokenValue = await _settings.ReadSettingAsync(CustomTokenSettingKey).ConfigureAwait(false);
            customToken = customTokenValue?.ToString() ?? string.Empty;

            var ipFilterEnabledValue = await _settings.ReadSettingAsync(IpFilterEnabledSettingKey)
                .ConfigureAwait(false);
            ipFilterEnabled = ipFilterEnabledValue != null && Convert.ToBoolean(ipFilterEnabledValue);

            var ipModeValue = await _settings.ReadSettingAsync(IpFilterModeSettingKey).ConfigureAwait(false);
            ipAllowMode = !string.Equals(ipModeValue?.ToString(), IpModeDeny, StringComparison.OrdinalIgnoreCase);

            var ipRulesValue = await _settings.ReadSettingAsync(IpRulesSettingKey).ConfigureAwait(false);
            ipRulesText = ipRulesValue?.ToString() ?? string.Empty;

            var auditEnabledValue = await _settings.ReadSettingAsync(AuditEnabledSettingKey).ConfigureAwait(false);
            auditEnabled = auditEnabledValue == null || Convert.ToBoolean(auditEnabledValue);
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpHost", $"[Mcp] 读取配置失败：{ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            token = CreateToken();
            try
            {
                await _settings.SaveSettingAsync(TokenSettingKey, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                DebugLog.Info("McpHost", $"[Mcp] 保存访问令牌失败：{ex.Message}");
            }
        }

        if (customTokenMode && !IsValidToken(customToken))
        {
            customTokenMode = false;
        }

        McpIpFilter.TryParse(ipRulesText, out var rules, out _);

        foreach (var definition in McpToolCatalog.All)
        {
            var toolEnabled = true;
            try
            {
                var stored = await _settings.ReadSettingAsync(ToolSettingPrefix + definition.Id)
                    .ConfigureAwait(false);
                toolEnabled = stored == null || Convert.ToBoolean(stored);
            }
            catch (Exception ex)
            {
                DebugLog.Info("McpHost", $"[Mcp] 读取工具开关失败：{definition.Id} - {ex.Message}");
            }

            lock (_stateLock)
            {
                _toolSwitches[definition.Id] = toolEnabled;
            }
        }

        lock (_stateLock)
        {
            _enabled = enabled;
            _port = port;
            _token = token;
            _requireAuth = requireAuth;
            _customTokenMode = customTokenMode;
            _customToken = customToken;
            _ipFilterEnabled = ipFilterEnabled;
            _ipAllowMode = ipAllowMode;
            _ipRulesText = ipRulesText;
            _ipRules = rules;
        }

        Audit.SetEnabled(auditEnabled);

        DebugLog.Info("McpHost",
            $"[Mcp] 配置已加载：enabled={enabled}, port={port}, auth={requireAuth}, ipFilter={ipFilterEnabled}");
    }

    private void SetLastError(string? message)
    {
        lock (_stateLock)
        {
            _lastError = message;
        }
    }

    private void RaiseStatusChanged()
    {
        try
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpHost", $"[Mcp] 状态通知失败：{ex.Message}");
        }
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static List<string> GetLanAddresses()
    {
        var addresses = new List<string>();
        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var text = unicast.Address.ToString();
                    if (text.StartsWith("127.", StringComparison.Ordinal) ||
                        text.StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!addresses.Contains(text))
                    {
                        addresses.Add(text);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpHost", $"[Mcp] 读取局域网地址失败：{ex.Message}");
        }

        return addresses;
    }
}