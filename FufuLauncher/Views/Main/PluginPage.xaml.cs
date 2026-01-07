using FufuLauncher.Models;
using FufuLauncher.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text;

namespace FufuLauncher.Views;

public sealed partial class PluginPage : Page
{
    public PluginViewModel ViewModel { get; }

    public PluginPage()
    {
        ViewModel = App.GetService<PluginViewModel>();
        this.InitializeComponent();
    }
    private class ConfigOption
    {
        public string SectionHeader { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public Control EditControl { get; set; }
    }
    
    private class GeneralInfo
    {
        public Dictionary<string, string> Items { get; set; } = new();
    }
    

    private void OnPluginToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggleSwitch && toggleSwitch.Tag is PluginItem item)
        {
            if (toggleSwitch.IsOn != item.IsEnabled)
            {
                if (ViewModel.TogglePluginCommand.CanExecute(item))
                {
                    ViewModel.TogglePluginCommand.Execute(item);
                }
                else
                {
                    toggleSwitch.IsOn = item.IsEnabled;
                }
            }
        }
    }
    
    private void OnConfigClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PluginItem item && item.HasConfig)
        {
            this.Frame.Navigate(typeof(PluginConfigPage), item);
        }
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
                if (currentOption != null)
                {
                    options.Add(currentOption);
                    currentOption = null;
                }

                currentSection = trimmed;

                if (!currentSection.Equals("[General]", StringComparison.OrdinalIgnoreCase))
                {
                    currentOption = new ConfigOption { SectionHeader = currentSection };
                }
                continue;
            }
            
            var parts = trimmed.Split(new[] { '=' }, 2);
            if (parts.Length != 2) continue;

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
        
        if (currentOption != null)
        {
            options.Add(currentOption);
        }

        return (general, options);
    }
    
    private FrameworkElement CreateControlForOption(ConfigOption opt)
    {
        var stack = new StackPanel { Spacing = 8 };
        
        var labelText = !string.IsNullOrEmpty(opt.Name) ? opt.Name : opt.SectionHeader;
        
        var headerBlock = new TextBlock 
        { 
            Text = labelText, 
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] 
        };
        stack.Children.Add(headerBlock);

        Control inputControl;
        string type = opt.Type?.ToLower() ?? "string";

        switch (type)
        {
            case "bool":
            case "boolean":
                bool.TryParse(opt.Value, out var boolVal);
                var ts = new ToggleSwitch 
                { 
                    IsOn = boolVal,
                    OnContent = "开启",
                    OffContent = "关闭"
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
                    LargeChange = 10
                };
                inputControl = nb;
                break;

            default: // string
                var tb = new TextBox 
                { 
                    Text = opt.Value ?? "" 
                };
                inputControl = tb;
                break;
        }

        opt.EditControl = inputControl;
        stack.Children.Add(inputControl);

        return stack;
    }
    
    private void UpdateValueFromControl(ConfigOption opt)
    {
        if (opt.EditControl is ToggleSwitch ts)
        {
            opt.Value = ts.IsOn.ToString(); // True/False
        }
        else if (opt.EditControl is NumberBox nb)
        {
            if (string.Equals(opt.Type, "int", StringComparison.OrdinalIgnoreCase))
                opt.Value = ((int)nb.Value).ToString();
            else
                opt.Value = nb.Value.ToString();
        }
        else if (opt.EditControl is TextBox tb)
        {
            opt.Value = tb.Text;
        }
    }

    private string BuildIniContent(GeneralInfo general, List<ConfigOption> options)
    {
        var sb = new StringBuilder();
        
        if (general.Items.Count > 0)
        {
            sb.AppendLine("[General]");
            foreach (var kvp in general.Items)
            {
                sb.AppendLine($"{kvp.Key} = {kvp.Value}");
            }
            sb.AppendLine();
        }
        
        foreach (var opt in options)
        {
            sb.AppendLine(opt.SectionHeader); // [Opt-1]
            if (!string.IsNullOrEmpty(opt.Name)) sb.AppendLine($"Name = {opt.Name}");
            if (!string.IsNullOrEmpty(opt.Type)) sb.AppendLine($"Type = {opt.Type}");
            sb.AppendLine($"Value = {opt.Value}");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
    

    private async void OnRenameClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is PluginItem pluginItem)
        {
            var currentFolderName = new DirectoryInfo(pluginItem.DirectoryPath).Name;
            
            var dialog = new ContentDialog
            {
                Title = "重命名插件文件夹",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var inputTextBox = new TextBox { Text = currentFolderName, AcceptsReturn = false };
            dialog.Content = inputTextBox;

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var newName = inputTextBox.Text.Trim();
                if (!string.IsNullOrEmpty(newName) && newName != currentFolderName)
                {
                    ViewModel.PerformRename(pluginItem, newName);
                }
            }
        }
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem item && item.Tag is PluginItem pluginItem)
        {
            if (ViewModel.DeletePluginCommand.CanExecute(pluginItem))
            {
                ViewModel.DeletePluginCommand.Execute(pluginItem);
            }
        }
    }
}