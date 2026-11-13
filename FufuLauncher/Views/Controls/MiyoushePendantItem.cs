using System.ComponentModel;
using FufuLauncher.Models.Miyoushe;
using Microsoft.UI.Xaml.Media;

namespace FufuLauncher.Views;

public sealed class MiyoushePendantItem : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public MiyoushePendantItem(MiyoushePendant pendant) => Pendant = pendant;

    public MiyoushePendant Pendant
    {
        get;
    }

    public string Id => Pendant.Id;

    public string Name => Pendant.Name;

    public bool Owned => Pendant.Owned;

    public bool IsUsed => Pendant.IsUsed;

    public bool Available => Pendant.Available;

    public ImageSource? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}