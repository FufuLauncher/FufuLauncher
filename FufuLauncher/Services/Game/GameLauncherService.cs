/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Services.AuthTicket;
using FufuLauncher.Services.GameServer;
using FufuLauncher.ViewModels;

namespace FufuLauncher.Services;

public partial class GameLauncherService : IGameLauncherService
{
    private readonly ILocalSettingsService _localSettingsService;
    private readonly IGameConfigService _gameConfigService;
    private readonly ILauncherService _launcherService;
    private readonly ControlPanelViewModel _controlPanelModel;
    private readonly GameServerConfigurationService _gameServerConfigurationService;
    private readonly LightweightPluginService _lightweightPluginService;
    private readonly PluginGameVersionGuard _pluginGameVersionGuard;
    private readonly ConstraintService _constraintService;
    private readonly IScreenshotService _screenshotService;
    private readonly IAuthTicketService _authTicketService;
    private readonly AccountManager _accountManager;
    private readonly GameRegistrySnapshot _registrySnapshot = new();
    private readonly CodeSigning.ModTrustGate _modTrustGate;

    private bool _lastUseInjection;

    public GameLauncherService(
        ILocalSettingsService localSettingsService,
        IGameConfigService gameConfigService,
        ILauncherService launcherService,
        ControlPanelViewModel controlPanelModel,
        IScreenshotService screenshotService,
        IAuthTicketService authTicketService,
        AccountManager accountManager,
        GameServerConfigurationService gameServerConfigurationService,
        LightweightPluginService lightweightPluginService,
        PluginGameVersionGuard pluginGameVersionGuard,
        ConstraintService constraintService,
        CodeSigning.ModTrustGate modTrustGate)
    {
        _localSettingsService = localSettingsService;
        _gameConfigService = gameConfigService;
        _launcherService = launcherService;
        _controlPanelModel = controlPanelModel;
        _screenshotService = screenshotService;
        _authTicketService = authTicketService;
        _accountManager = accountManager;
        _gameServerConfigurationService = gameServerConfigurationService;
        _lightweightPluginService = lightweightPluginService;
        _pluginGameVersionGuard = pluginGameVersionGuard;
        _constraintService = constraintService;
        _modTrustGate = modTrustGate;
    }
}