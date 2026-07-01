using System.ComponentModel;

namespace FufuLauncher.Models;

public class NavItemConfig : INotifyPropertyChanged
{
    public string ViewModelKey { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string IconGlyph { get; init; } = "";

    private bool _isUserVisible = true;
    public bool IsUserVisible
    {
        get => _isUserVisible;
        set
        {
            if (_isUserVisible == value) return;
            _isUserVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUserVisible)));
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsForceVisible { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? VisibilityChanged;
}
