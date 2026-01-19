using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Models;

public partial class GachaDisplayItem : ObservableObject
{
    public string Name { get; set; }
    public string Type { get; set; }
    public int Rank { get; set; }
    
    public int Count { get; set; }   
    
    public string Time { get; set; }
    
    public string LastGetTime { get; set; }

    [ObservableProperty] private string _imageUrl;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ElementImageSource))] private string _elementUrl;
    
    public ImageSource ElementImageSource
    {
        get
        {
            if (string.IsNullOrEmpty(ElementUrl)) return null;
            try
            {
                return new BitmapImage(new Uri(ElementUrl));
            }
            catch
            {
                return null;
            }
        }
    }
    
    public SolidColorBrush RarityBackground => Rank switch
    {
        5 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 198, 160, 96)),
        4 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 149, 118, 193)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 168, 209))
    };
    
    public SolidColorBrush RarityColorHex => Rank switch
    {
        5 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 198, 160, 96)),
        4 => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 149, 118, 193)),
        _ => new SolidColorBrush(Windows.UI.Color.FromArgb(255, 102, 168, 209))
    };
}

public class ScrapedMetadata
{
    public string Name { get; set; }
    public string ImgSrc { get; set; }
    public string ElementSrc { get; set; }
    public string Type { get; set; } 
}