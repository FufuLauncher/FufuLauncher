using System.Collections.ObjectModel;
using FufuLauncher.ViewModels;

namespace FufuLauncher.Helpers;

public static class VirtualKeyCatalog
{
    public static ObservableCollection<VirtualKeyOption> Keys
    {
        get;
    } = new(
        Enum.GetValues<Windows.System.VirtualKey>()
            .Where(key => key != Windows.System.VirtualKey.None)
            .Select(key => new VirtualKeyOption { KeyCode = (int)key, KeyName = key.ToString() })
            .GroupBy(key => key.KeyCode).Select(group => group.First()).OrderBy(key => key.KeyCode));

    public static ObservableCollection<VirtualKeyOption> CreateQuickFpsOptions()
    {
        var options = new ObservableCollection<VirtualKeyOption>
        {
            new()
            {
                KeyCode = 0,
                KeyName = "QuickFpsUnbound".GetLocalized()
            }
        };

        foreach (var key in Keys)
        {
            options.Add(key);
        }

        return options;
    }
}