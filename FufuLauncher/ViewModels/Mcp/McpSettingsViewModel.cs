/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using FufuLauncher.Helpers;
using FufuLauncher.Models;
using FufuLauncher.Services.Mcp;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace FufuLauncher.ViewModels;

public sealed class McpSettingsViewModel : ObservableObject
{
    private readonly McpHostService _host;
    private readonly McpAgentConfigService _agentConfigService;
    private readonly DispatcherQueue? _dispatcher;
    private readonly EventHandler _hostStatusChangedHandler;
    private readonly EventHandler _auditChangedHandler;

    private bool _initialized;
    private bool _suppressUpdates;
    private bool _isEnabled;
    private double _port;
    private string _statusText = string.Empty;
    private string _endpointText = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _hasError;
    private bool _requireAuth;
    private int _tokenModeIndex;
    private string _accessToken = string.Empty;
    private string _customToken = string.Empty;
    private string _tokenError = string.Empty;
    private Visibility _tokenErrorVisibility = Visibility.Collapsed;
    private bool _ipFilterEnabled;
    private int _ipModeIndex;
    private string _ipRulesText = string.Empty;
    private string _ipError = string.Empty;
    private Visibility _ipErrorVisibility = Visibility.Collapsed;
    private string _auditSummary = string.Empty;
    private int _selectedAgentIndex;
    private int _endpointModeIndex;
    private string _dshProfileName = McpAgentConfigService.DefaultDshProfile;
    private string _dshProfilesCaption = string.Empty;
    private string _clientConfigPath = string.Empty;
    private string _clientConfigStatus = string.Empty;
    private string _clientConfigSnippet = string.Empty;
    private string _clientNoteText = string.Empty;
    private Visibility _clientNoteVisibility = Visibility.Collapsed;
    private Visibility _clientEndpointWarningVisibility = Visibility.Collapsed;
    private Visibility _dshProfileVisibility = Visibility.Collapsed;
    private string _clientInstallButtonText = string.Empty;
    private string _clientActionMessage = string.Empty;
    private Visibility _clientActionMessageVisibility = Visibility.Collapsed;
    private bool _clientActionIsError;

    public McpSettingsViewModel(McpHostService host, DispatcherQueue? dispatcher)
        : this(host, new McpAgentConfigService(), dispatcher)
    {
    }

    public McpSettingsViewModel(McpHostService host, McpAgentConfigService agentConfigService,
        DispatcherQueue? dispatcher)
    {
        _host = host;
        _agentConfigService = agentConfigService;
        _dispatcher = dispatcher;
        _hostStatusChangedHandler = (_, _) => DispatchRefreshStatus();
        _auditChangedHandler = (_, _) => DispatchRefreshAuditSummary();
    }

    public IReadOnlyList<McpAgentDescriptor> AgentOptions => McpAgentConfigService.Agents;

    public ObservableCollection<McpToolToggleItem> PluginTools
    {
        get;
    } = new();

    public ObservableCollection<McpToolToggleItem> GameTools
    {
        get;
    } = new();

    public ObservableCollection<McpToolToggleItem> InterfaceTools
    {
        get;
    } = new();

    public ObservableCollection<McpToolToggleItem> PlayerTools
    {
        get;
    } = new();

    public ObservableCollection<McpToolToggleItem> SystemTools
    {
        get;
    } = new();

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (!SetProperty(ref _isEnabled, value) || _suppressUpdates || !_initialized)
            {
                return;
            }

            _ = ApplyEnabledAsync(value);
        }
    }

    public double Port
    {
        get => _port;
        set
        {
            if (!double.IsFinite(value) || !SetProperty(ref _port, value) || _suppressUpdates || !_initialized)
            {
                return;
            }

            _ = ApplyPortAsync((int)Math.Round(value));
        }
    }

    public bool RequireAuth
    {
        get => _requireAuth;
        set
        {
            if (!SetProperty(ref _requireAuth, value) || _suppressUpdates || !_initialized)
            {
                return;
            }

            RaiseLayout();
            _ = ApplyRequireAuthAsync(value);
        }
    }

    public int TokenModeIndex
    {
        get => _tokenModeIndex;
        set
        {
            if (!SetProperty(ref _tokenModeIndex, value) || _suppressUpdates || !_initialized)
            {
                return;
            }

            RaiseLayout();
            _ = ApplyTokenModeAsync(value);
        }
    }

    public string AccessToken
    {
        get => _accessToken;
        private set => SetProperty(ref _accessToken, value);
    }

    public string CustomToken
    {
        get => _customToken;
        set => SetProperty(ref _customToken, value);
    }

    public string TokenError
    {
        get => _tokenError;
        private set => SetProperty(ref _tokenError, value);
    }

    public Visibility TokenErrorVisibility
    {
        get => _tokenErrorVisibility;
        private set => SetProperty(ref _tokenErrorVisibility, value);
    }

    public bool IpFilterEnabled
    {
        get => _ipFilterEnabled;
        set
        {
            if (!SetProperty(ref _ipFilterEnabled, value) || _suppressUpdates || !_initialized)
            {
                return;
            }

            RaiseLayout();
            _ = ApplyIpFilterAsync();
        }
    }

    public int IpModeIndex
    {
        get => _ipModeIndex;
        set
        {
            if (!SetProperty(ref _ipModeIndex, value) || _suppressUpdates || !_initialized)
            {
                return;
            }

            RaiseLayout();
            _ = ApplyIpFilterAsync();
        }
    }

    public string IpRulesText
    {
        get => _ipRulesText;
        set
        {
            if (SetProperty(ref _ipRulesText, value))
            {
                OnPropertyChanged(nameof(IpRulesHintVisibility));
            }
        }
    }

    public string IpError
    {
        get => _ipError;
        private set => SetProperty(ref _ipError, value);
    }

    public Visibility IpErrorVisibility
    {
        get => _ipErrorVisibility;
        private set
        {
            if (SetProperty(ref _ipErrorVisibility, value))
            {
                OnPropertyChanged(nameof(IpEditorVisibility));
            }
        }
    }

    public string AuditSummary
    {
        get => _auditSummary;
        private set => SetProperty(ref _auditSummary, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string EndpointText
    {
        get => _endpointText;
        private set => SetProperty(ref _endpointText, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public Visibility AuthSettingsVisibility => _requireAuth ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AutoTokenVisibility =>
        _requireAuth && _tokenModeIndex == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CustomTokenVisibility =>
        _requireAuth && _tokenModeIndex == 1 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AuthOffWarningVisibility => _requireAuth ? Visibility.Collapsed : Visibility.Visible;

    public Visibility IpEditorVisibility =>
        _ipFilterEnabled || _ipErrorVisibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility IpRulesHintVisibility =>
        _ipFilterEnabled && _ipModeIndex == 0 && string.IsNullOrWhiteSpace(_ipRulesText)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public int SelectedAgentIndex
    {
        get => _selectedAgentIndex;
        set
        {
            if (value < 0 || value >= AgentOptions.Count || !SetProperty(ref _selectedAgentIndex, value))
            {
                return;
            }

            ClearClientActionMessage();
            RefreshClientIntegration();
        }
    }

    public int EndpointModeIndex
    {
        get => _endpointModeIndex;
        set
        {
            if (value < 0 || value > 1 || !SetProperty(ref _endpointModeIndex, value))
            {
                return;
            }

            ClearClientActionMessage();
            RefreshClientIntegration();
        }
    }

    public string DshProfileName
    {
        get => _dshProfileName;
        set
        {
            if (!SetProperty(ref _dshProfileName, value))
            {
                return;
            }

            ClearClientActionMessage();
            RefreshClientIntegration();
        }
    }

    public string DshProfilesCaption
    {
        get => _dshProfilesCaption;
        private set => SetProperty(ref _dshProfilesCaption, value);
    }

    public string ClientConfigPath
    {
        get => _clientConfigPath;
        private set => SetProperty(ref _clientConfigPath, value);
    }

    public string ClientConfigStatus
    {
        get => _clientConfigStatus;
        private set => SetProperty(ref _clientConfigStatus, value);
    }

    public string ClientConfigSnippet
    {
        get => _clientConfigSnippet;
        private set => SetProperty(ref _clientConfigSnippet, value);
    }

    public string ClientNoteText
    {
        get => _clientNoteText;
        private set => SetProperty(ref _clientNoteText, value);
    }

    public Visibility ClientNoteVisibility
    {
        get => _clientNoteVisibility;
        private set => SetProperty(ref _clientNoteVisibility, value);
    }

    public Visibility ClientEndpointWarningVisibility
    {
        get => _clientEndpointWarningVisibility;
        private set => SetProperty(ref _clientEndpointWarningVisibility, value);
    }

    public Visibility DshProfileVisibility
    {
        get => _dshProfileVisibility;
        private set => SetProperty(ref _dshProfileVisibility, value);
    }

    public string ClientInstallButtonText
    {
        get => _clientInstallButtonText;
        private set => SetProperty(ref _clientInstallButtonText, value);
    }

    public string ClientActionMessage
    {
        get => _clientActionMessage;
        private set => SetProperty(ref _clientActionMessage, value);
    }

    public Visibility ClientActionSuccessVisibility =>
        _clientActionMessageVisibility == Visibility.Visible && !_clientActionIsError
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility ClientActionErrorVisibility =>
        _clientActionMessageVisibility == Visibility.Visible && _clientActionIsError
            ? Visibility.Visible
            : Visibility.Collapsed;

    public bool IsClientIntegrationReady => _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        await _host.InitializeAsync();
        _host.StatusChanged += _hostStatusChangedHandler;
        _host.Audit.Changed += _auditChangedHandler;

        BuildTools();

        _suppressUpdates = true;
        try
        {
            IsEnabled = _host.IsEnabled;
            Port = _host.Port;
            RequireAuth = _host.RequireAuth;
            TokenModeIndex = _host.IsCustomTokenMode ? 1 : 0;
            AccessToken = _host.AccessToken;
            CustomToken = _host.CustomToken;
            IpFilterEnabled = _host.IpFilterEnabled;
            IpModeIndex = _host.IpAllowMode ? 0 : 1;
            IpRulesText = _host.IpRulesText;

            var discoveredProfiles = _agentConfigService.DiscoverDshProfiles();
            _dshProfileName = discoveredProfiles[0];
            OnPropertyChanged(nameof(DshProfileName));
            DshProfilesCaption = string.Format("Mcp_ClientDshProfilesDetectedFormat".GetLocalized(),
                string.Join(", ", discoveredProfiles));
        }
        finally
        {
            _suppressUpdates = false;
        }

        _initialized = true;
        OnPropertyChanged(nameof(IsClientIntegrationReady));
        RaiseLayout();
        RefreshStatus();
        RefreshAuditSummary();
    }

    public void NotifyClientConfigCopied()
    {
        ShowClientActionMessage("Mcp_ClientCopied".GetLocalized(), isError: false);
    }

    public void NotifyClientConfigCopyFailed()
    {
        ShowClientActionMessage("Mcp_ClientCopyFailed".GetLocalized(), isError: true);
    }

    public void InstallClientConfig()
    {
        if (!_initialized)
        {
            return;
        }

        var agent = GetSelectedAgent();
        var endpointUrl = GetSelectedClientEndpoint();
        var token = _host.RequireAuth ? _host.AccessToken : null;

        var result = _agentConfigService.InstallOrUpdate(agent.Id, endpointUrl, token, DshProfileName);
        if (result.Succeeded)
        {
            ShowClientActionMessage(
                string.Format("Mcp_ClientInstallSuccessFormat".GetLocalized(), result.FilePath),
                isError: false);
        }
        else
        {
            ShowClientActionMessage(
                string.Format("Mcp_ClientInstallFailedFormat".GetLocalized(), result.ErrorMessage ?? "Unknown error"),
                isError: true);
        }

        RefreshClientIntegration();
    }

    public async Task RegenerateTokenAsync()
    {
        try
        {
            await _host.RegenerateTokenAsync();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpSettingsViewModel", $"[Mcp] 重新生成访问令牌失败：{ex.Message}");
        }
    }

    public async Task SaveCustomTokenAsync()
    {
        try
        {
            var error = await _host.SaveCustomTokenAsync(CustomToken);
            if (error == null)
            {
                TokenError = string.Empty;
                TokenErrorVisibility = Visibility.Collapsed;
                RefreshStatus();
            }
            else
            {
                TokenError = error;
                TokenErrorVisibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            TokenError = ex.Message;
            TokenErrorVisibility = Visibility.Visible;
        }
    }

    public Task ApplyIpRulesAsync() => ApplyIpFilterAsync();

    public void Detach()
    {
        _host.StatusChanged -= _hostStatusChangedHandler;
        _host.Audit.Changed -= _auditChangedHandler;
    }

    private async Task ApplyEnabledAsync(bool enabled)
    {
        try
        {
            await _host.SetEnabledAsync(enabled);
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpSettingsViewModel", $"[Mcp] 应用启用状态失败：{ex.Message}");
        }
        finally
        {
            RefreshStatus();
        }
    }

    private async Task ApplyPortAsync(int port)
    {
        try
        {
            await _host.SetPortAsync(port);
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpSettingsViewModel", $"[Mcp] 应用端口失败：{ex.Message}");
        }
        finally
        {
            RefreshStatus();
        }
    }

    private async Task ApplyRequireAuthAsync(bool requireAuth)
    {
        try
        {
            await _host.SetRequireAuthAsync(requireAuth);
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpSettingsViewModel", $"[Mcp] 应用鉴权设置失败：{ex.Message}");
        }
        finally
        {
            RefreshStatus();
        }
    }

    private async Task ApplyTokenModeAsync(int index)
    {
        try
        {
            await _host.SetCustomTokenModeAsync(index == 1);
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpSettingsViewModel", $"[Mcp] 应用令牌方式失败：{ex.Message}");
        }
        finally
        {
            RefreshStatus();
        }
    }

    private async Task ApplyIpFilterAsync()
    {
        try
        {
            var (ok, error) = await _host.SaveIpFilterAsync(IpFilterEnabled, IpModeIndex == 0, IpRulesText);
            if (ok)
            {
                IpError = string.Empty;
                IpErrorVisibility = Visibility.Collapsed;
            }
            else
            {
                IpError = error ?? string.Empty;
                IpErrorVisibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            IpError = ex.Message;
            IpErrorVisibility = Visibility.Visible;
        }
        finally
        {
            RefreshStatus();
        }
    }

    private void RefreshStatus()
    {
        var running = _host.IsRunning;
        var enabled = _host.IsEnabled;

        StatusText = running
            ? string.Format("Mcp_StatusRunning".GetLocalized(), _host.Port)
            : enabled
                ? "Mcp_StatusEnabledNotRunning".GetLocalized()
                : "Mcp_StatusStopped".GetLocalized();

        EndpointText = running
            ? _host.GetPrimaryEndpoint()
            : string.Format("Mcp_EndpointPending".GetLocalized(), _host.Port);

        var error = _host.LastError;
        ErrorMessage = error ?? string.Empty;
        HasError = !string.IsNullOrWhiteSpace(error);

        _suppressUpdates = true;
        try
        {
            if (_isEnabled != enabled)
            {
                IsEnabled = enabled;
            }

            if (Math.Abs(_port - _host.Port) > 0.01)
            {
                Port = _host.Port;
            }

            if (_requireAuth != _host.RequireAuth)
            {
                RequireAuth = _host.RequireAuth;
            }

            var modeIndex = _host.IsCustomTokenMode ? 1 : 0;
            if (_tokenModeIndex != modeIndex)
            {
                TokenModeIndex = modeIndex;
            }

            if (!string.Equals(_accessToken, _host.AccessToken, StringComparison.Ordinal))
            {
                AccessToken = _host.AccessToken;
            }

            if (!string.Equals(_customToken, _host.CustomToken, StringComparison.Ordinal))
            {
                CustomToken = _host.CustomToken;
            }

            if (_ipFilterEnabled != _host.IpFilterEnabled)
            {
                IpFilterEnabled = _host.IpFilterEnabled;
            }

            var ipMode = _host.IpAllowMode ? 0 : 1;
            if (_ipModeIndex != ipMode)
            {
                IpModeIndex = ipMode;
            }

            if (!string.Equals(_ipRulesText, _host.IpRulesText, StringComparison.Ordinal))
            {
                IpRulesText = _host.IpRulesText;
            }
        }
        finally
        {
            _suppressUpdates = false;
        }

        RaiseLayout();
        RefreshClientIntegration();
    }

    private McpAgentDescriptor GetSelectedAgent()
    {
        var index = Math.Clamp(_selectedAgentIndex, 0, AgentOptions.Count - 1);
        return AgentOptions[index];
    }

    private string GetSelectedClientEndpoint() =>
        _endpointModeIndex == 0 ? _host.GetLocalEndpoint() : _host.GetPrimaryEndpoint();

    private void RefreshClientIntegration()
    {
        var agent = GetSelectedAgent();
        var endpointUrl = GetSelectedClientEndpoint();
        var token = _host.RequireAuth ? _host.AccessToken : null;

        DshProfileVisibility = agent.SupportsDshProfile ? Visibility.Visible : Visibility.Collapsed;
        ClientEndpointWarningVisibility =
            _host.IsLocalhostOnly && IsNonLoopbackEndpoint(endpointUrl)
                ? Visibility.Visible
                : Visibility.Collapsed;

        if (!string.IsNullOrEmpty(agent.NoteKey))
        {
            ClientNoteText = agent.NoteKey.GetLocalized();
            ClientNoteVisibility = Visibility.Visible;
        }
        else
        {
            ClientNoteText = string.Empty;
            ClientNoteVisibility = Visibility.Collapsed;
        }

        ClientConfigSnippet = _agentConfigService.BuildSnippet(agent.Id, endpointUrl, token);

        try
        {
            var inspection = _agentConfigService.Inspect(agent.Id, endpointUrl, token, DshProfileName);
            ClientConfigPath = inspection.FilePath;
            ClientConfigStatus = inspection.State switch
            {
                McpAgentInstallState.FileMissing => "Mcp_ClientState_Missing".GetLocalized(),
                McpAgentInstallState.NotConfigured => "Mcp_ClientState_NotConfigured".GetLocalized(),
                McpAgentInstallState.Configured => "Mcp_ClientState_Configured".GetLocalized(),
                McpAgentInstallState.NeedsUpdate => "Mcp_ClientState_NeedsUpdate".GetLocalized(),
                McpAgentInstallState.InvalidFile => string.Format(
                    "Mcp_ClientState_InvalidFormat".GetLocalized(), inspection.ErrorMessage ?? string.Empty),
                _ => string.Empty
            };

            ClientInstallButtonText = inspection.State is McpAgentInstallState.Configured or McpAgentInstallState.NeedsUpdate
                ? "Mcp_ClientUpdateConfig".GetLocalized()
                : "Mcp_ClientInstallConfig".GetLocalized();
        }
        catch (Exception ex)
        {
            ClientConfigPath = string.Empty;
            ClientConfigStatus = string.Format("Mcp_ClientState_InvalidFormat".GetLocalized(), ex.Message);
            ClientInstallButtonText = "Mcp_ClientInstallConfig".GetLocalized();
        }
    }

    private static bool IsNonLoopbackEndpoint(string endpointUrl)
    {
        if (!Uri.TryCreate(endpointUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (IPAddress.TryParse(uri.Host, out var address))
        {
            return !IPAddress.IsLoopback(address);
        }

        return !string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowClientActionMessage(string message, bool isError)
    {
        _clientActionIsError = isError;
        _clientActionMessageVisibility = Visibility.Visible;
        ClientActionMessage = message;
        OnPropertyChanged(nameof(ClientActionSuccessVisibility));
        OnPropertyChanged(nameof(ClientActionErrorVisibility));
    }

    private void ClearClientActionMessage()
    {
        _clientActionMessageVisibility = Visibility.Collapsed;
        ClientActionMessage = string.Empty;
        OnPropertyChanged(nameof(ClientActionSuccessVisibility));
        OnPropertyChanged(nameof(ClientActionErrorVisibility));
    }

    private void RefreshAuditSummary()
    {
        var counters = _host.Audit.GetCounters();
        var summary = string.Format("Mcp_AuditSummaryFormat".GetLocalized(), counters.Connections,
            counters.Calls, counters.Failures);

        var archived = _host.Audit.ArchivedCount;
        if (archived > 0)
        {
            summary += " · " + string.Format("Mcp_AuditArchivedCount".GetLocalized(), archived);
        }

        AuditSummary = summary;
    }

    private void RaiseLayout()
    {
        OnPropertyChanged(nameof(AuthSettingsVisibility));
        OnPropertyChanged(nameof(AutoTokenVisibility));
        OnPropertyChanged(nameof(CustomTokenVisibility));
        OnPropertyChanged(nameof(AuthOffWarningVisibility));
        OnPropertyChanged(nameof(IpEditorVisibility));
        OnPropertyChanged(nameof(IpRulesHintVisibility));
    }

    private void DispatchRefreshStatus()
    {
        if (_dispatcher == null || _dispatcher.HasThreadAccess)
        {
            RefreshStatus();
            return;
        }

        _dispatcher.TryEnqueue(RefreshStatus);
    }

    private void DispatchRefreshAuditSummary()
    {
        if (_dispatcher == null || _dispatcher.HasThreadAccess)
        {
            RefreshAuditSummary();
            return;
        }

        _dispatcher.TryEnqueue(RefreshAuditSummary);
    }

    private void BuildTools()
    {
        if (PluginTools.Count > 0 || GameTools.Count > 0 || InterfaceTools.Count > 0 ||
            PlayerTools.Count > 0 || SystemTools.Count > 0)
        {
            return;
        }

        foreach (var definition in McpToolCatalog.All)
        {
            var item = new McpToolToggleItem(
                definition.Id,
                definition.TitleKey.GetLocalized(),
                definition.DescriptionKey.GetLocalized(),
                _host.IsToolEnabled(definition.Id),
                OnToolToggled);

            switch (definition.Category)
            {
                case McpToolCategories.Plugins:
                    PluginTools.Add(item);
                    break;
                case McpToolCategories.Game:
                    GameTools.Add(item);
                    break;
                case McpToolCategories.Interface:
                    InterfaceTools.Add(item);
                    break;
                case McpToolCategories.Player:
                    PlayerTools.Add(item);
                    break;
                default:
                    SystemTools.Add(item);
                    break;
            }
        }
    }

    private void OnToolToggled(string toolId, bool enabled)
    {
        _ = SaveToolStateAsync(toolId, enabled);
    }

    private async Task SaveToolStateAsync(string toolId, bool enabled)
    {
        try
        {
            await _host.SaveToolEnabledAsync(toolId, enabled);
        }
        catch (Exception ex)
        {
            DebugLog.Info("McpSettingsViewModel", $"[Mcp] 保存工具开关失败：{toolId} - {ex.Message}");
        }
    }
}