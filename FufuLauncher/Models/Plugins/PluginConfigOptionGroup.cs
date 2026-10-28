using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FufuLauncher.Models;

public sealed class PluginConfigOptionGroup : ObservableObject
{
    private readonly ObservableCollection<PluginConfigOption>? _precedingOptions;

    public PluginConfigOptionGroup(ObservableCollection<PluginConfigOption> options,
        ObservableCollection<PluginConfigOption>? precedingOptions = null)
    {
        Items = options;
        _precedingOptions = precedingOptions;
        if (_precedingOptions != null) _precedingOptions.CollectionChanged += OnPrecedingOptionsChanged;
    }

    public ObservableCollection<PluginConfigOption> Items { get; }
    public bool ShowSeparator => _precedingOptions?.Count > 0;

    private void OnPrecedingOptionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(ShowSeparator));
}
