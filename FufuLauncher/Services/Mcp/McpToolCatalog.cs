/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

namespace FufuLauncher.Services.Mcp;

internal static class McpToolCategories
{
    public const string Plugins = "plugins";
    public const string Game = "game";
    public const string Interface = "interface";
    public const string Player = "player";
    public const string System = "system";
}

internal static class McpToolCatalog
{
    private const string EmptySchema = """{"type":"object","properties":{},"additionalProperties":false}""";

    public static IReadOnlyList<McpToolDefinition> All
    {
        get;
    } =
    [
        new("list_plugins", McpToolCategories.Plugins, "McpTool_ListPlugins", "McpTool_ListPluginsCaption",
            "List every plugin installed under the launcher Plugins directory, including official launcher plugins (FuFuPlugin / YuanShen-UnlockerLite) and third-party plugins. Returns folder id, display name, enabled state, config path and version.",
            EmptySchema),

        new("set_plugin_enabled", McpToolCategories.Plugins, "McpTool_SetPluginEnabled",
            "McpTool_SetPluginEnabledCaption",
            "Enable or disable an installed plugin by folder name, display name or dll file name. Works for both official and third-party plugins. The launcher policy restrictions (lightweight mode / constraint policy) and locked files are enforced.",
            """{"type":"object","properties":{"plugin":{"type":"string","description":"Plugin folder name, display name or dll file name."},"enabled":{"type":"boolean","description":"true to enable, false to disable."}},"required":["plugin","enabled"],"additionalProperties":false}"""),

        new("get_plugin_config", McpToolCategories.Plugins, "McpTool_GetPluginConfig",
            "McpTool_GetPluginConfigCaption",
            "Read the structured configuration of an installed plugin. Returns the [General] information and every configurable option (section, display name, type, current value, choices). This is the only supported way to inspect plugin configuration.",
            """{"type":"object","properties":{"plugin":{"type":"string","description":"Plugin folder name, display name or dll file name."}},"required":["plugin"],"additionalProperties":false}"""),

        new("set_plugin_config", McpToolCategories.Plugins, "McpTool_SetPluginConfig",
            "McpTool_SetPluginConfigCaption",
            "Change one existing option value of a plugin configuration. Only options that already exist in the config file can be modified; raw file editing and adding new sections are not supported. The launcher rewrites only the value line, keeping the rest of the file untouched.",
            """{"type":"object","properties":{"plugin":{"type":"string","description":"Plugin folder name, display name or dll file name."},"section":{"type":"string","description":"Existing section name as reported by get_plugin_config."},"value":{"type":["string","boolean","number"],"description":"New value. Booleans, integers, numbers or text, depending on the option type."}},"required":["plugin","section","value"],"additionalProperties":false}"""),

        new("search_plugin_store", McpToolCategories.Plugins, "McpTool_SearchPluginStore",
            "McpTool_SearchPluginStoreCaption",
            "Search the official plugin store. Returns plugin ids, names, versions, descriptions, download counts and whether the plugin is already installed.",
            """{"type":"object","properties":{"query":{"type":"string","description":"Optional search text."},"category":{"type":"string","description":"Optional store category id."},"page":{"type":"integer","minimum":1,"default":1},"pageSize":{"type":"integer","minimum":1,"maximum":50,"default":20}},"additionalProperties":false}"""),

        new("install_plugin", McpToolCategories.Plugins, "McpTool_InstallPlugin", "McpTool_InstallPluginCaption",
            "Start a background plugin installation and return a job id. Provide exactly one source: pluginId (official plugin store), path (a local .dll or archive on the launcher machine), or url (an http(s) archive download). When the store requires human verification, a verification window is opened for the user; use get_install_status to check progress and cancel_install to stop the installation.",
            """{"type":"object","properties":{"pluginId":{"type":"string","description":"Plugin store id."},"accessKey":{"type":"string","description":"Access key for private store plugins."},"path":{"type":"string","description":"Local .dll, .zip, .7z or .rar path on the launcher machine."},"url":{"type":"string","description":"http(s) URL of an archive to download and install."},"overwrite":{"type":"boolean","default":false,"description":"Replace an existing plugin folder when installing from path or url."}},"additionalProperties":false}"""),

        new("get_install_status", McpToolCategories.Plugins, "McpTool_GetInstallStatus",
            "McpTool_GetInstallStatusCaption",
            "Return the progress of a plugin installation started by install_plugin, including whether the user still has to complete verification. Without jobId, the most recent installation jobs are returned.",
            """{"type":"object","properties":{"jobId":{"type":"string","description":"Installation job id. Omit to list recent jobs."}},"additionalProperties":false}"""),

        new("cancel_install", McpToolCategories.Plugins, "McpTool_CancelInstall",
            "McpTool_CancelInstallCaption",
            "Cancel a running or waiting plugin installation started by install_plugin. A verification window that is still open is closed.",
            """{"type":"object","properties":{"jobId":{"type":"string","description":"Installation job id."}},"required":["jobId"],"additionalProperties":false}"""),

        new("get_game_status", McpToolCategories.Game, "McpTool_GetGameStatus", "McpTool_GetGameStatusCaption",
            "Return the launcher game state: configured game path, whether the game is running, current server scheme and installed game version.",
            EmptySchema),

        new("start_game", McpToolCategories.Game, "McpTool_StartGame", "McpTool_StartGameCaption",
            "Start the game through the launcher with the current launch configuration. Fails when the game is already running or no game path is configured.",
            EmptySchema),

        new("stop_game", McpToolCategories.Game, "McpTool_StopGame", "McpTool_StopGameCaption",
            "Terminate the running game process and any integrated helper process started by the launcher.",
            EmptySchema),

        new("list_servers", McpToolCategories.Game, "McpTool_ListServers", "McpTool_ListServersCaption",
            "List the supported game server schemes (channel / sub-channel), the currently active scheme and whether each scheme matches the installed client.",
            EmptySchema),

        new("switch_server", McpToolCategories.Game, "McpTool_SwitchServer", "McpTool_SwitchServerCaption",
            "Switch the game server scheme by writing the channel, sub-channel and cps fields into the game config.ini. Only schemes compatible with the installed client (Chinese or oversea) are accepted, and the game must be closed.",
            """{"type":"object","properties":{"serverId":{"type":"string","description":"Server id from list_servers, for example 1-1-cn."}},"required":["serverId"],"additionalProperties":false}"""),

        new("open_page", McpToolCategories.Interface, "McpTool_OpenPage", "McpTool_OpenPageCaption",
            "Navigate the launcher main window to a page.",
            """{"type":"object","properties":{"page":{"type":"string","enum":["home","control_panel","game_settings","account","other","plugins","plugin_store","plugin_settings","help","settings"],"description":"Page id."}},"required":["page"],"additionalProperties":false}"""),

        new("open_tool_window", McpToolCategories.Interface, "McpTool_OpenToolWindow",
            "McpTool_OpenToolWindowCaption",
            "Open one of the toolbox tool windows, for example gacha analysis, data center, calculator or achievements.",
            """{"type":"object","properties":{"tool":{"type":"string","enum":["gacha_analysis","data_center","calculator","cycle_challenge","achievements","inventory","player_info","daily_note","bbs","video_resources","community","miyoushe"],"description":"Tool id."}},"required":["tool"],"additionalProperties":false}"""),

        new("set_window_state", McpToolCategories.Interface, "McpTool_SetWindowState",
            "McpTool_SetWindowStateCaption",
            "Maximize, minimize or restore the launcher main window.",
            """{"type":"object","properties":{"state":{"type":"string","enum":["maximize","minimize","restore"],"description":"Target window state."}},"required":["state"],"additionalProperties":false}"""),

        new("get_gacha_records", McpToolCategories.Player, "McpTool_GetGachaRecords",
            "McpTool_GetGachaRecordsCaption",
            "Query the wish (gacha) records the launcher has stored locally for a player, newest first by default. Filters by pool category or gacha type id, rarity, item name and time range. Returns one page of records with pity counters and a summary of every matched record. Use get_gacha_overview for statistics and search_gacha_pools for banner periods.",
            """{"type":"object","properties":{"uid":{"type":"string","description":"Game UID. Omit to use the player the launcher currently has selected."},"pool":{"type":"string","enum":["character","weapon","standard","novice","chronicled","301","302","200","100","400","500"],"description":"Pool filter: category name or gacha type id."},"rank":{"type":"integer","enum":[3,4,5],"description":"Only return items of this rarity."},"item":{"type":"string","description":"Item name or item id keyword."},"from":{"type":"string","description":"Earliest record time, yyyy-MM-dd or yyyy-MM-dd HH:mm:ss."},"to":{"type":"string","description":"Latest record time, yyyy-MM-dd or yyyy-MM-dd HH:mm:ss."},"order":{"type":"string","enum":["asc","desc"],"default":"desc","description":"Sort by record time. Default desc (newest first)."},"page":{"type":"integer","minimum":1,"default":1},"pageSize":{"type":"integer","minimum":1,"maximum":100,"default":20}},"additionalProperties":false}"""),

        new("get_gacha_overview", McpToolCategories.Player, "McpTool_GetGachaOverview",
            "McpTool_GetGachaOverviewCaption",
            "Return an analysis overview of the stored wish records: total pulls, rarity distribution and rates, estimated primogem cost, pity statistics (average, best, worst, current pity per pool), per-pool statistics, monthly distribution and the most recent five-star pulls.",
            """{"type":"object","properties":{"uid":{"type":"string","description":"Game UID. Omit to use the player the launcher currently has selected."},"recentFiveStars":{"type":"integer","minimum":0,"maximum":50,"default":10,"description":"How many recent five-star pulls to list."}},"additionalProperties":false}"""),

        new("search_gacha_pools", McpToolCategories.Player, "McpTool_SearchGachaPools",
            "McpTool_SearchGachaPoolsCaption",
            "Search the banner periods (期) known to the launcher, for example by version 5.2 or by a featured character name. Returns each period with its time window, featured items and the player's pulls and five-star results inside that period. Pass the returned key to get_gacha_pool_records to read the individual records.",
            """{"type":"object","properties":{"uid":{"type":"string","description":"Game UID used for the pull statistics. Omit to use the player the launcher currently has selected."},"query":{"type":"string","description":"Keyword matched against version, pool name, featured item names and period dates."},"pool":{"type":"string","enum":["character","weapon","chronicled","301","400","302","500"],"description":"Banner type filter."},"from":{"type":"string","description":"Only periods that end on or after this date (yyyy-MM-dd)."},"to":{"type":"string","description":"Only periods that start on or before this date (yyyy-MM-dd)."},"hasPulls":{"type":"boolean","default":false,"description":"true to only return periods that contain at least one pull."},"page":{"type":"integer","minimum":1,"default":1},"pageSize":{"type":"integer","minimum":1,"maximum":100,"default":20}},"additionalProperties":false}"""),

        new("get_gacha_pool_records", McpToolCategories.Player, "McpTool_GetGachaPoolRecords",
            "McpTool_GetGachaPoolRecordsCaption",
            "Return every wish record of one banner period, identified by key from search_gacha_pools or by version plus pool type. Includes global pity counters and the obtained item totals. When several periods match, the candidates are returned instead of records.",
            """{"type":"object","properties":{"uid":{"type":"string","description":"Game UID. Omit to use the player the launcher currently has selected."},"key":{"type":"string","description":"Period key returned by search_gacha_pools."},"version":{"type":"string","description":"Version text of the period, for example 5.2 or 5.2下半."},"pool":{"type":"string","enum":["character","weapon","chronicled","301","400","302","500"],"description":"Banner type filter used together with version."},"order":{"type":"string","enum":["asc","desc"],"default":"desc","description":"Sort by record time. Default desc (newest first)."},"page":{"type":"integer","minimum":1,"default":1},"pageSize":{"type":"integer","minimum":1,"maximum":100,"default":20}},"additionalProperties":false}"""),

        new("get_achievement_overview", McpToolCategories.Player, "McpTool_GetAchievementOverview",
            "McpTool_GetAchievementOverviewCaption",
            "Return the achievement overview stored in the launcher save: the achievement save profile, total and completed achievement counts, global completion rate, primogem rewards obtained and still available, and the completion rate of every game version. Use get_achievement_categories for per-category progress and search_achievements for one specific achievement.",
            EmptySchema),

        new("get_achievement_categories", McpToolCategories.Player, "McpTool_GetAchievementCategories",
            "McpTool_GetAchievementCategoriesCaption",
            "Return the completion progress of every achievement category (分区): total and completed achievements, completion rate, primogem rewards and how many achievements are in progress. Pass category to inspect one category only.",
            """{"type":"object","properties":{"category":{"type":"string","description":"Category name filter; a substring match is enough."},"order":{"type":"string","enum":["default","progress_asc","progress_desc","name","total_desc"],"default":"default","description":"Sort order of the returned categories."}},"additionalProperties":false}"""),

        new("search_achievements", McpToolCategories.Player, "McpTool_SearchAchievements",
            "McpTool_SearchAchievementsCaption",
            "Search the achievements of the launcher save by exact id, title or description keyword, category, version or completion state. Returns the completion status, current and maximum progress, completion time and the progress of the series an achievement belongs to. This is the way to check one specific achievement.",
            """{"type":"object","properties":{"id":{"type":"integer","description":"Exact achievement id."},"query":{"type":"string","description":"Keyword matched against the achievement title and description."},"category":{"type":"string","description":"Category name filter; a substring match is enough."},"version":{"type":"string","description":"Game version filter, for example 5.2."},"status":{"type":"string","enum":["all","completed","incomplete"],"default":"all","description":"Completion state filter."},"page":{"type":"integer","minimum":1,"default":1},"pageSize":{"type":"integer","minimum":1,"maximum":100,"default":20}},"additionalProperties":false}"""),

        new("read_crash_log", McpToolCategories.System, "McpTool_ReadCrashLog", "McpTool_ReadCrashLogCaption",
            "Read the tail of the launcher crash log (CrashLog.txt) without modifying it.",
            """{"type":"object","properties":{"maxLines":{"type":"integer","minimum":1,"maximum":500,"default":120,"description":"Number of trailing lines to return."}},"additionalProperties":false}"""),

        new("check_update", McpToolCategories.System, "McpTool_CheckUpdate", "McpTool_CheckUpdateCaption",
            "Check whether a newer launcher version is available. This only queries the update server: the update program is never launched and no local state is changed.",
            EmptySchema)
    ];

    public static McpToolDefinition? Find(string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : All.FirstOrDefault(tool => string.Equals(tool.Id, id, StringComparison.OrdinalIgnoreCase));
}