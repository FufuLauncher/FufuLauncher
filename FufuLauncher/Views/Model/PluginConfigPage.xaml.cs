using System.Collections.ObjectModel;
using System.Text;
using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace FufuLauncher.Views;

public class ConfigOption
{
    public string SectionHeader
    {
        get; set;
    }
    public string Name
    {
        get; set;
    }
    public string Type
    {
        get; set;
    }
    public string Value
    {
        get; set;
    }
    public Control EditControl
    {
        get; set;
    }
    
    public string NameDisplay
    {
        get
        {
            if (!string.IsNullOrEmpty(Name)) return Name;
            return SectionHeader?.Trim('[', ']', ' ') ?? "Unknown Option";
        }
    }
}

public class GeneralInfo
{
    public Dictionary<string, string> Items { get; set; } = new();
}

public sealed partial class PluginConfigPage : Page
{
    private PluginItem _pluginItem;
    private PluginViewModel _viewModel;

    public ObservableCollection<ConfigOption> Options { get; private set; } = new();
    public ObservableCollection<string> InfoList { get; private set; } = new();

    private GeneralInfo _currentGeneralInfo;
    private List<ConfigOption> _currentOptionsList;

    private bool _isInitialized = false;

    public PluginConfigPage()
    {
        this.InitializeComponent();
        _viewModel = App.GetService<PluginViewModel>();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is PluginItem item)
        {
            _pluginItem = item;
            TitleTextBlock.Text = item.DisplayName;
            _isInitialized = false;
            await LoadConfigAsync();
            _isInitialized = true;
        }
    }

    private async Task LoadConfigAsync()
    {
        try
        {
            Options.Clear();
            InfoList.Clear();

            if (!File.Exists(_pluginItem.ConfigFilePath))
            {
                return;
            }
            
            var lines = await File.ReadAllLinesAsync(_pluginItem.ConfigFilePath, Encoding.UTF8);
            var (general, opts) = ParseIniConfig(lines);

            _currentGeneralInfo = general;
            _currentOptionsList = opts;
            
            if (general.Items.Count > 0)
            {
                InfoBanner.Visibility = Visibility.Visible;
                foreach (var kvp in general.Items) InfoList.Add($"{kvp.Key}: {kvp.Value}");
                InfoItemsControl.ItemsSource = InfoList;
            }
            else
            {
                InfoBanner.Visibility = Visibility.Collapsed;
            }
            
            foreach (var opt in opts)
            {
                CreateControlForOption(opt);
                Options.Add(opt);
            }

            ConfigGridView.ItemsSource = Options;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading config: {ex.Message}");
        }
    }

    private void CreateControlForOption(ConfigOption opt)
    {
        Control inputControl;
        string type = opt.Type?.ToLower() ?? "string";

        switch (type)
        {
            case "bool":
            case "boolean":
                var valStr = opt.Value?.Trim().ToLowerInvariant();
                bool boolVal = valStr == "1" || valStr == "true" || valStr == "yes" || valStr == "on";

                var ts = new ToggleSwitch
                {
                    IsOn = boolVal,
                    OnContent = "已开启",
                    OffContent = "已关闭",
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ts.Toggled += (_, _) =>
                {
                    if (!_isInitialized) return;
                    opt.Value = ts.IsOn ? "1" : "0";
                    TriggerAutoSave();
                };
                inputControl = ts;
                break;

            case "int":
            case "integer":
            case "number":
                double.TryParse(opt.Value, out var dVal);
                var nb = new NumberBox
                {
                    Value = dVal,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
                    SmallChange = 1,
                    LargeChange = 10,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center
                };
                nb.ValueChanged += (s, e) =>
                {
                    if (!_isInitialized) return;
                    if (string.Equals(opt.Type, "int", StringComparison.OrdinalIgnoreCase))
                        opt.Value = ((int)nb.Value).ToString();
                    else
                        opt.Value = nb.Value.ToString();
                    TriggerAutoSave();
                };
                inputControl = nb;
                break;

            default:
                var tb = new TextBox
                {
                    Text = opt.Value ?? "",
                    PlaceholderText = "请输入配置值...",
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Center
                };
                tb.LostFocus += (s, e) =>
                {
                    if (!_isInitialized) return;
                    if (opt.Value != tb.Text)
                    {
                        opt.Value = tb.Text;
                        TriggerAutoSave();
                    }
                };
                tb.KeyDown += (s, e) =>
                {
                    if (e.Key == Windows.System.VirtualKey.Enter)
                    {
                        if (opt.Value != tb.Text)
                        {
                            opt.Value = tb.Text;
                            TriggerAutoSave();
                        }
                        ConfigGridView.Focus(FocusState.Programmatic);
                    }
                };
                inputControl = tb;
                break;
        }

        opt.EditControl = inputControl;
    }

    private async void TriggerAutoSave()
    {
        try
        {
            var content = BuildIniContent(_currentGeneralInfo, _currentOptionsList);
            
            await File.WriteAllTextAsync(_pluginItem.ConfigFilePath, content, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AutoSave Failed: {ex.Message}");
        }
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        ConfigGridView.Focus(FocusState.Programmatic);
        if (Frame.CanGoBack) Frame.GoBack();
    }

    private (GeneralInfo, List<ConfigOption>) ParseIniConfig(string[] lines)
    {
        var general = new GeneralInfo();
        var options = new List<ConfigOption>();
        ConfigOption currentOption = null;
        string currentSection = "";

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#") || trimmed.StartsWith(";")) continue;

            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                if (currentOption != null) options.Add(currentOption);
                currentSection = trimmed;
                
                if (!currentSection.Equals("[General]", StringComparison.OrdinalIgnoreCase))
                {
                    currentOption = new ConfigOption { SectionHeader = currentSection };
                }
                continue;
            }

            var parts = trimmed.Split(new[] { '=' }, 2);
            if (parts.Length == 2)
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (currentSection.Equals("[General]", StringComparison.OrdinalIgnoreCase))
                {
                    general.Items[key] = value;
                }
                else if (currentOption != null)
                {
                    if (key.Equals("Name", StringComparison.OrdinalIgnoreCase)) currentOption.Name = value;
                    else if (key.Equals("Type", StringComparison.OrdinalIgnoreCase)) currentOption.Type = value;
                    else if (key.Equals("Value", StringComparison.OrdinalIgnoreCase)) currentOption.Value = value;
                }
            }
        }
        if (currentOption != null) options.Add(currentOption);
        return (general, options);
    }

    private string BuildIniContent(GeneralInfo general, List<ConfigOption> options)
    {
        var sb = new StringBuilder();
        if (general.Items.Count > 0)
        {
            sb.AppendLine("[General]");
            foreach (var kvp in general.Items) sb.AppendLine($"{kvp.Key} = {kvp.Value}");
            sb.AppendLine();
        }
        foreach (var opt in options)
        {
            sb.AppendLine(opt.SectionHeader);
            if (!string.IsNullOrEmpty(opt.Name)) sb.AppendLine($"Name = {opt.Name}");
            if (!string.IsNullOrEmpty(opt.Type)) sb.AppendLine($"Type = {opt.Type}");
            sb.AppendLine($"Value = {opt.Value}");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}