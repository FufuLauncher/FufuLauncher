/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
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

    public McpSettingsViewModel(McpHostService host, DispatcherQueue? dispatcher)
    {
        _host = host;
        _dispatcher = dispatcher;
        _hostStatusChangedHandler = (_, _) => DispatchRefreshStatus();
        _auditChangedHandler = (_, _) => DispatchRefreshAuditSummary();
    }

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
        }
        finally
        {
            _suppressUpdates = false;
        }

        _initialized = true;
        RaiseLayout();
        RefreshStatus();
        RefreshAuditSummary();
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