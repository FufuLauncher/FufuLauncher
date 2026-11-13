/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FufuLauncher.Helpers;

public static class EndpointImage
{
    public static ImageSource Create(string url) => new BitmapImage(new Uri(url));
}