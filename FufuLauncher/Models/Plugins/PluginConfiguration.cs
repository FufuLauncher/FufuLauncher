using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FufuLauncher.Helpers;

namespace FufuLauncher.Models;

public sealed class PluginConfigInfoItem(string key, string value)
{
    public string Key
    {
        get;
    } = key;

    public string Value
    {
        get;
    } = value;
}

public enum PluginConfigValueKind
{
    Boolean,
    Integer,
    Number,
    Text,
    Key,
    Enumeration
}

public sealed class PluginConfigOption : PinnablePluginSetting
{
    private string _value;
    private readonly IReadOnlyList<PluginConfigChoice> _staticChoices = Array.Empty<PluginConfigChoice>();
    private IReadOnlyList<PluginConfigChoice> _choices = Array.Empty<PluginConfigChoice>();
    private GamepadFamilies _gamepadFamilies;
    private bool _refreshingChoices;
    private bool _useKeyListInput = true;

    internal PluginConfigOption(string sectionName, string name, string type, string description, string value,
        string optionsSource = "", string optionsJson = "")
    {
        SectionName = sectionName;
        DisplayName = string.IsNullOrWhiteSpace(name) ? sectionName : name;
        Description = description;
        Kind = type.Trim().ToLowerInvariant() switch
        {
            "bool" or "boolean" => PluginConfigValueKind.Boolean,
            "int" or "integer" => PluginConfigValueKind.Integer,
            "number" or "float" or "double" or "decimal" => PluginConfigValueKind.Number,
            "key" => PluginConfigValueKind.Key,
            "enum" => PluginConfigValueKind.Enumeration,
            _ => PluginConfigValueKind.Text
        };
        _value = value;
        UsesGamepadChoices = Kind == PluginConfigValueKind.Enumeration &&
                             optionsSource.Trim().Equals("gamepad", StringComparison.OrdinalIgnoreCase);
        if (Kind == PluginConfigValueKind.Enumeration && !UsesGamepadChoices)
        {
            _staticChoices = ParseChoices(optionsJson);
            if (!string.IsNullOrWhiteSpace(optionsSource) || _staticChoices.Count == 0)
                Kind = PluginConfigValueKind.Text;
        }

        if (Kind is PluginConfigValueKind.Key or PluginConfigValueKind.Enumeration) RefreshChoices();
    }

    public string SectionName
    {
        get;
    }

    public string DisplayName
    {
        get;
    }

    public string Description
    {
        get;
    }

    public string ToolTipText => DisplayName == SectionName ? DisplayName : $"{DisplayName}\n{SectionName}";

    public PluginConfigValueKind Kind
    {
        get;
    }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public double HeaderTextMaxWidth => HasDescription ? 192 : 216;

    public bool UsesGamepadChoices
    {
        get;
    }

    public IReadOnlyList<PluginConfigChoice> Choices => _choices;
    public bool ShowChoiceList => Kind != PluginConfigValueKind.Key || _useKeyListInput;
    public bool ShowKeyNumber => Kind == PluginConfigValueKind.Key && !_useKeyListInput;

    public PluginConfigChoice? SelectedChoice
    {
        get => _choices.FirstOrDefault(choice => Identity(choice.Value) == Identity(Value));
        set
        {
            if (_refreshingChoices || value == null || !_choices.Contains(value) ||
                Identity(value.Value) == Identity(Value)) return;
            Value = value.Value;
        }
    }

    public double KeyNumberValue
    {
        get => int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key) ? key : double.NaN;
        set
        {
            if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue) return;
            var key = (int)Math.Round(value);
            if (key != KeyNumberValue) Value = key.ToString(CultureInfo.InvariantCulture);
        }
    }

    public void SetKeyInputMode(bool useList)
    {
        if (_useKeyListInput == useList) return;
        _useKeyListInput = useList;
        OnPropertyChanged(nameof(ShowChoiceList));
        OnPropertyChanged(nameof(ShowKeyNumber));
    }

    public void SetConnectedGamepads(GamepadFamilies families)
    {
        if (!UsesGamepadChoices || _gamepadFamilies == families) return;
        _gamepadFamilies = families;
        RefreshChoices();
    }

    private string Identity(string value) => UsesGamepadChoices
        ? GamepadButtonCatalog.Role(value).ToUpperInvariant()
        : Kind == PluginConfigValueKind.Key &&
          int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var key)
            ? key.ToString(CultureInfo.InvariantCulture)
            : value;

    private void RefreshChoices()
    {
        _refreshingChoices = true;
        try
        {
            var choices = Kind == PluginConfigValueKind.Key
                ? VirtualKeyCatalog.Keys.Select(key =>
                    new PluginConfigChoice(key.KeyCode.ToString(CultureInfo.InvariantCulture), key.KeyName)).ToList()
                : (UsesGamepadChoices ? GamepadButtonCatalog.Create(_gamepadFamilies) : _staticChoices).ToList();
            if (!choices.Any(choice => Identity(choice.Value) == Identity(Value)))
                choices.Add(new PluginConfigChoice(Value,
                    Kind == PluginConfigValueKind.Key ? $"Custom({Value})" : Value));
            _choices = choices;
            OnPropertyChanged(nameof(Choices));
            OnPropertyChanged(nameof(SelectedChoice));
        }
        finally
        {
            _refreshingChoices = false;
        }
    }

    private static IReadOnlyList<PluginConfigChoice> ParseChoices(string json)
    {
        try
        {
            var entries = JsonSerializer.Deserialize<List<ChoiceMetadata>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return entries?.Where(entry =>
                           entry?.Value != null && !entry.Value.Contains('\r') && !entry.Value.Contains('\n'))
                       .Select(entry =>
                           new PluginConfigChoice(entry.Value!,
                               string.IsNullOrEmpty(entry.Label) ? entry.Value! : entry.Label))
                       .DistinctBy(entry => entry.Value, StringComparer.Ordinal).ToArray() ??
                   Array.Empty<PluginConfigChoice>();
        }
        catch (JsonException)
        {
            return Array.Empty<PluginConfigChoice>();
        }
    }

    private sealed record ChoiceMetadata(string? Value, string? Label);

    public double Minimum => Kind == PluginConfigValueKind.Integer ? int.MinValue : double.MinValue;
    public double Maximum => Kind == PluginConfigValueKind.Integer ? int.MaxValue : double.MaxValue;
    public double SmallChange => Kind == PluginConfigValueKind.Number ? 0.1 : 1;

    public string Value
    {
        get => _value;
        set
        {
            if (SetProperty(ref _value, value))
            {
                OnPropertyChanged(nameof(BooleanValue));
                OnPropertyChanged(nameof(NumberValue));
                OnPropertyChanged(nameof(KeyNumberValue));
                if (Kind is PluginConfigValueKind.Key or PluginConfigValueKind.Enumeration)
                {
                    if (_choices.Any(choice => Identity(choice.Value) == Identity(Value)))
                        OnPropertyChanged(nameof(SelectedChoice));
                    else
                        RefreshChoices();
                }
            }
        }
    }

    public bool BooleanValue
    {
        get => Value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
        set
        {
            if (value != BooleanValue)
            {
                Value = value ? "1" : "0";
            }
        }
    }

    public double NumberValue
    {
        get
        {
            if (!double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
                !double.TryParse(Value, NumberStyles.Float, CultureInfo.CurrentCulture, out number))
            {
                return double.NaN;
            }

            return double.IsFinite(number) && number >= Minimum && number <= Maximum ? number : double.NaN;
        }
        set
        {
            if (!double.IsFinite(value) || value < Minimum || value > Maximum || value == NumberValue)
            {
                return;
            }

            Value = Kind == PluginConfigValueKind.Integer
                ? Math.Truncate(value).ToString("0", CultureInfo.InvariantCulture)
                : value.ToString("G", CultureInfo.InvariantCulture);

            if (Kind == PluginConfigValueKind.Integer && value != NumberValue)
            {
                OnPropertyChanged(nameof(NumberValue));
            }
        }
    }
}

public sealed class PluginConfiguration
{
    private readonly string[] _lines;
    private readonly string _lineEnding;
    private readonly Encoding _encoding;
    private readonly IReadOnlyList<OptionSource> _sources;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private long _version;
    private long _savedVersion;

    private PluginConfiguration(string text, Encoding encoding)
    {
        _encoding = encoding;
        _lineEnding = text.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n"
            : text.Contains('\r')
                ? "\r"
                : text.Contains('\n')
                    ? "\n"
                    : Environment.NewLine;
        _lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        var general = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<OptionSource>();
        Section? section = null;

        for (var index = 0; index < _lines.Length; index++)
        {
            var line = _lines[index].Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                AddSection(section, general, sources);
                section = new Section(line[1..^1].Trim(), index);
                continue;
            }

            var separator = line.IndexOf('=');
            if (section == null || separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            section.Items[key] = line[(separator + 1)..].Trim();
            if (key.Equals("Value", StringComparison.OrdinalIgnoreCase))
            {
                section.ValueLines.Add(index);
            }
        }

        AddSection(section, general, sources);
        GeneralInfo = general;
        _sources = sources;
        Options = sources.Select(source => source.Option).ToArray();

        foreach (var option in Options)
        {
            option.PropertyChanged += OnOptionPropertyChanged;
        }
    }

    public IReadOnlyDictionary<string, string> GeneralInfo
    {
        get;
    }

    public IReadOnlyList<PluginConfigOption> Options
    {
        get;
    }

    public bool HasChanges => _version != _savedVersion;

    public static async Task<PluginConfiguration> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        (Encoding encoding, int offset) = bytes switch
        {
            [0xFF, 0xFE, 0x00, 0x00, ..] => (new UTF32Encoding(false, true, true), 4),
            [0x00, 0x00, 0xFE, 0xFF, ..] => (new UTF32Encoding(true, true, true), 4),
            [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(true, true), 3),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(false, true, true), 2),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(true, true, true), 2),
            _ => ((Encoding)new UTF8Encoding(false, true), 0)
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new PluginConfiguration(encoding.GetString(bytes, offset, bytes.Length - offset), encoding);
    }

    public async Task SaveAsync(string path)
    {
        await _saveLock.WaitAsync();
        try
        {
            while (HasChanges)
            {
                var version = _version;
                var content = BuildContent();
                await File.WriteAllTextAsync(path, content, _encoding);
                _savedVersion = version;
            }
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private void OnOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PluginConfigOption.Value))
        {
            _version++;
        }
    }

    private string BuildContent()
    {
        var replacements = new Dictionary<int, string>();
        var insertions = new Dictionary<int, string>();
        foreach (var source in _sources)
        {
            if (source.Option.Value == source.OriginalValue)
            {
                continue;
            }

            if (source.ValueLines.Count == 0)
            {
                insertions[source.HeaderLine] = $"Value = {source.Option.Value}";
                continue;
            }

            foreach (var index in source.ValueLines)
            {
                var line = _lines[index];
                var start = line.IndexOf('=') + 1;
                while (start < line.Length && char.IsWhiteSpace(line[start]))
                {
                    start++;
                }

                var end = line.Length;
                while (end > start && char.IsWhiteSpace(line[end - 1]))
                {
                    end--;
                }

                replacements[index] = line[..start] + source.Option.Value + line[end..];
            }
        }

        var lines = new List<string>(_lines.Length + insertions.Count);
        for (var index = 0; index < _lines.Length; index++)
        {
            lines.Add(replacements.GetValueOrDefault(index, _lines[index]));
            if (insertions.TryGetValue(index, out var valueLine))
            {
                lines.Add(valueLine);
            }
        }

        return string.Join(_lineEnding, lines);
    }

    private static void AddSection(Section? section, Dictionary<string, string> general, List<OptionSource> sources)
    {
        if (section == null)
        {
            return;
        }

        if (section.Name.Equals("General", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in section.Items)
            {
                general[item.Key] = item.Value;
            }

            return;
        }

        var value = section.Items.GetValueOrDefault("Value", string.Empty);
        var option = new PluginConfigOption(
            section.Name,
            section.Items.GetValueOrDefault("Name", string.Empty),
            section.Items.GetValueOrDefault("Type", string.Empty),
            section.Items.GetValueOrDefault("Description", string.Empty),
            value,
            section.Items.GetValueOrDefault("OptionsSource", string.Empty),
            section.Items.GetValueOrDefault("Options", string.Empty));
        sources.Add(new OptionSource(option, section.HeaderLine, section.ValueLines, value));
    }

    private sealed record OptionSource(
        PluginConfigOption Option,
        int HeaderLine,
        IReadOnlyList<int> ValueLines,
        string OriginalValue);

    private sealed class Section(string name, int headerLine)
    {
        public string Name
        {
            get;
        } = name;

        public int HeaderLine
        {
            get;
        } = headerLine;

        public Dictionary<string, string> Items
        {
            get;
        } = new(StringComparer.OrdinalIgnoreCase);

        public List<int> ValueLines
        {
            get;
        } = new();
    }
}
