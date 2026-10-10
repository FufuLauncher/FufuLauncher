using FufuLauncher.Helpers;

namespace FufuLauncher.Services;

public class LaunchResult
{
    public bool Success
    {
        get;
        set;
    }

    public bool Cancelled
    {
        get;
        set;
    }

    public string ErrorMessage
    {
        get;
        set;
    } = string.Empty;

    public string DetailLog
    {
        get;
        set;
    } = string.Empty;

    public IReadOnlyList<PluginDllConflict> PluginDllConflicts
    {
        get;
        set;
    } = Array.Empty<PluginDllConflict>();
}
