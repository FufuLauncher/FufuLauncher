/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using FufuLauncher.Models;

namespace FufuLauncher.ViewModels;

public class PluginSettingItem : PinnablePluginSetting
{
    private readonly IniFile _iniFile;
    private readonly Action<string, string, string> _onValueChanged;
    private readonly Func<bool>? _canChangeValue;

    public string SectionKey
    {
        get;
    }

    public string DisplayName
    {
        get;
    }

    public string Type
    {
        get;
    }

    public string HelpUrl
    {
        get;
    }

    public Microsoft.UI.Xaml.Visibility HelpVisibility => !string.IsNullOrEmpty(HelpUrl)
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility GifImageVisibility => !string.IsNullOrEmpty(HelpUrl)
        ? Microsoft.UI.Xaml.Visibility.Visible
        : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility GifErrorVisibility => Microsoft.UI.Xaml.Visibility.Collapsed;

    private bool _isSelected;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SelectionMarkVisibility));
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility SelectionMarkVisibility =>
        IsSelected ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private string _rawValue;

    private static readonly ObservableCollection<VirtualKeyOption> _availableKeys = VirtualKeyCatalog.Keys;

    private bool _useKeyListInput;

    public ObservableCollection<VirtualKeyOption> AvailableKeys => _availableKeys;

    public Microsoft.UI.Xaml.Media.ImageSource HelpImageSource
    {
        get
        {
            if (string.IsNullOrEmpty(HelpUrl)) return null;
            try
            {
                var resolvedPath = HelpUrl;
                if (!Uri.IsWellFormedUriString(HelpUrl, UriKind.Absolute) && !Path.IsPathRooted(HelpUrl))
                {
                    resolvedPath = Path.Combine(AppContext.BaseDirectory, HelpUrl);
                }

                return new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(resolvedPath));
            }
            catch
            {
                return null;
            }
        }
    }

    public PluginSettingItem(IniFile iniFile, string sectionKey, string displayName, string type, string value,
        string helpUrl, Action<string, string, string> onValueChanged, bool useKeyListInput,
        Func<bool>? canChangeValue = null)
    {
        _iniFile = iniFile;
        SectionKey = sectionKey;
        DisplayName = displayName;
        Type = type;
        _rawValue = value;
        HelpUrl = helpUrl;
        _onValueChanged = onValueChanged;
        _canChangeValue = canChangeValue;
        _useKeyListInput = useKeyListInput;
        if (string.Equals(Type, "key", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(_rawValue, out var currentKey))
        {
            EnsureKeyOption(currentKey);
        }
    }

    public int? KeyValue
    {
        get => int.TryParse(_rawValue, out var result) ? result : 0;
        set
        {
            if (value == null || _canChangeValue?.Invoke() == false || value == KeyValue) return;

            var targetValue = value.Value.ToString();
            if (_rawValue != targetValue)
            {
                var previousValue = _rawValue;
                _rawValue = targetValue;

                bool isNew = EnsureKeyOption(value.Value);

                if (isNew)
                {
                    WeakReferenceMessenger.Default.Send(new NotificationMessage(
                        "PluginSettings_InternalViewRefreshed".GetLocalized(),
                        string.Format("PluginSettings_AddedUnknownKey_Format".GetLocalized(), value),
                        NotificationType.Success,
                        3000
                    ));

                    var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                    if (dispatcher != null)
                    {
                        dispatcher.TryEnqueue(() =>
                        {
                            OnPropertyChanged();
                            OnPropertyChanged(nameof(KeyNumberValue));
                        });
                    }
                    else
                    {
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(KeyNumberValue));
                    }
                }
                else
                {
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(KeyNumberValue));
                }

                UpdatePhysicalConfig(targetValue, previousValue, nameof(KeyValue));
            }
        }
    }

    public PluginSettingItem(IniFile iniFile, string sectionKey, string displayName, string type, string value,
        Action<string, string, string> onValueChanged, bool useKeyListInput, Func<bool>? canChangeValue = null)
    {
        _iniFile = iniFile;
        SectionKey = sectionKey;
        DisplayName = displayName;
        Type = type;
        _rawValue = value;
        _onValueChanged = onValueChanged;
        _canChangeValue = canChangeValue;
        _useKeyListInput = useKeyListInput;
        if (string.Equals(Type, "key", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(_rawValue, out var currentKey))
        {
            EnsureKeyOption(currentKey);
        }
    }

    public bool UseKeyListInput
    {
        get => _useKeyListInput;
        set
        {
            if (SetProperty(ref _useKeyListInput, value))
            {
                OnPropertyChanged(nameof(KeyListVisibility));
                OnPropertyChanged(nameof(KeyNumberVisibility));
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility KeyListVisibility =>
        UseKeyListInput ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public Microsoft.UI.Xaml.Visibility KeyNumberVisibility =>
        UseKeyListInput ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;

    public double KeyNumberValue
    {
        get => KeyValue ?? 0;
        set
        {
            if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue) return;
            KeyValue = (int)Math.Round(value);
        }
    }

    public void SetKeyInputMode(bool useKeyListInput)
    {
        UseKeyListInput = useKeyListInput;
    }

    public bool BoolValue
    {
        get => _rawValue == "1" || _rawValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (_canChangeValue?.Invoke() == false || value == BoolValue) return;

            var targetValue = value ? "1" : "0";
            if (_rawValue != targetValue)
            {
                var previousValue = _rawValue;
                _rawValue = targetValue;
                OnPropertyChanged();
                UpdatePhysicalConfig(targetValue, previousValue, nameof(BoolValue));
            }
        }
    }

    public double FloatValue
    {
        get => double.TryParse(_rawValue, out var result) ? result : 0;
        set
        {
            if (_canChangeValue?.Invoke() == false || !double.IsFinite(value) || value == FloatValue) return;

            var targetValue = value.ToString("G");
            if (_rawValue != targetValue)
            {
                var previousValue = _rawValue;
                _rawValue = targetValue;
                OnPropertyChanged();
                UpdatePhysicalConfig(targetValue, previousValue, nameof(FloatValue));
            }
        }
    }

    public string StringValue
    {
        get => _rawValue;
        set
        {
            if (_canChangeValue?.Invoke() == false) return;

            if (_rawValue != value)
            {
                var previousValue = _rawValue;
                _rawValue = value;
                OnPropertyChanged();
                UpdatePhysicalConfig(value, previousValue, nameof(StringValue));
            }
        }
    }

    private void UpdatePhysicalConfig(string newValue, string previousValue, string propertyName)
    {
        try
        {
            _iniFile.WriteValue(SectionKey, "Value", newValue);
            _onValueChanged?.Invoke(SectionKey, "Value", newValue);
        }
        catch (Exception ex)
        {
            _rawValue = previousValue;
            OnPropertyChanged(propertyName);

            WeakReferenceMessenger.Default.Send(new NotificationMessage(
                "PluginSettings_SaveFailedTitle".GetLocalized(),
                string.Format("PluginSettings_ApplyFailedDetail_Format".GetLocalized(), ex.Message),
                NotificationType.Error,
                6000
            ));
        }
    }

    private static bool EnsureKeyOption(int keyCode)
    {
        if (keyCode <= 0) return false;
        if (_availableKeys.Any(k => k.KeyCode == keyCode)) return false;

        _availableKeys.Add(new VirtualKeyOption
        {
            KeyCode = keyCode,
            KeyName = $"Custom({keyCode})"
        });

        return true;
    }
}
