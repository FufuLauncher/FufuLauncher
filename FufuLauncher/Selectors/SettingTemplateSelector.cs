using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using FufuLauncher.ViewModels;

namespace FufuLauncher.Selectors;

public class SettingTemplateSelector : DataTemplateSelector
{
    public DataTemplate BoolTemplate { get; set; }
    public DataTemplate NumberTemplate { get; set; }
    public DataTemplate StringTemplate { get; set; }
    public DataTemplate KeyTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        return GetTemplate(item) ?? base.SelectTemplateCore(item);
    }

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        return GetTemplate(item) ?? base.SelectTemplateCore(item, container);
    }

    private DataTemplate GetTemplate(object item)
    {
        if (item is PluginSettingItem settingItem)
        {
            var type = settingItem.Type?.ToLower() ?? "";
            return type switch
            {
                "bool" => BoolTemplate,
                "int" => NumberTemplate,
                "float" => NumberTemplate,
                "key" => KeyTemplate,
                _ => StringTemplate
            };
        }
        return null;
    }
}