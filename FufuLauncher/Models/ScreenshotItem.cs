/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Models;

public class ScreenshotItem
{
    public string FilePath { get; set; }
    public string FileName { get; set; }
    public DateTime CreationTime { get; set; }
    public BitmapImage ImageSource { get; set; }
    public string SourceLabel { get; set; } = "";
}

public class ScreenshotGroup
{
    public string DateKey { get; set; }
    public ObservableCollection<ScreenshotItem> Items { get; set; } = new();
}
