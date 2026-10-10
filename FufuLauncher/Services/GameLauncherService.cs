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
    private readonly ControlPanelModel _controlPanelModel;
    private readonly GameServerConfigurationService _gameServerConfigurationService;
    private readonly IPluginUpdateService _pluginUpdateService;
    private readonly LightweightPluginService _lightweightPluginService;
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
        ControlPanelModel controlPanelModel,
        IPluginUpdateService pluginUpdateService,
        IScreenshotService screenshotService,
        IAuthTicketService authTicketService,
        AccountManager accountManager,
        GameServerConfigurationService gameServerConfigurationService,
        LightweightPluginService lightweightPluginService,
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
        _constraintService = constraintService;
        _modTrustGate = modTrustGate;
    }
}
