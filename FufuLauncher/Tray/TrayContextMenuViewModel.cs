using CommunityToolkit.Mvvm.Input;
using FufuLauncher.Helpers;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Tray;

internal sealed class TrayContextMenuViewModel
{
    public TrayContextMenuViewModel(
        Action showWindow,
        Action launchGame,
        Action checkin,
        Action switchServer,
        Action openMcp,
        Action exit)
    {
        ShowWindowCommand = new RelayCommand(showWindow);
        LaunchGameCommand = new RelayCommand(launchGame);
        CheckinCommand = new RelayCommand(checkin);
        SwitchServerCommand = new RelayCommand(switchServer);
        McpCommand = new RelayCommand(openMcp);
        ExitCommand = new RelayCommand(exit);
    }

    public string Title => "AppDisplayName".GetLocalized();

    public FontFamily TrayIconFont { get; } = new(
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? "Segoe Fluent Icons"
            : "Segoe MDL2 Assets");

    public IRelayCommand ShowWindowCommand
    {
        get;
    }

    public IRelayCommand LaunchGameCommand
    {
        get;
    }

    public IRelayCommand CheckinCommand
    {
        get;
    }

    public IRelayCommand SwitchServerCommand
    {
        get;
    }

    public IRelayCommand McpCommand
    {
        get;
    }

    public IRelayCommand ExitCommand
    {
        get;
    }
}