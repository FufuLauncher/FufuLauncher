/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using FufuLauncher.Helpers;
using FufuLauncher.Models;

namespace FufuLauncher.Services.Background;

internal static class BackgroundImageLoader
{
    public static Task<ImageSource> LoadFileAsync(string path) => LoadAsync(() => File.OpenRead(path));

    internal static ImageSource CreatePreviewThumbnail(ImageSource source, int maxEdge)
    {
        if (source is not WriteableBitmap bitmap) return source;

        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var longest = Math.Max(width, height);
        if (width <= 0 || height <= 0 || longest <= maxEdge) return source;

        var scale = maxEdge / (double)longest;
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));

        var pixels = new byte[checked(width * height * 4)];
        using (var stream = bitmap.PixelBuffer.AsStream())
            stream.ReadExactly(pixels);

        using var image = Image.LoadPixelData<Bgra32>(pixels, width, height);
        image.Mutate(context => context.Resize(targetWidth, targetHeight));
        var thumbnailPixels = new byte[checked(targetWidth * targetHeight * 4)];
        image.CopyPixelDataTo(thumbnailPixels);

        var thumbnail = new WriteableBitmap(targetWidth, targetHeight);
        using (var stream = thumbnail.PixelBuffer.AsStream())
            stream.Write(thumbnailPixels, 0, thumbnailPixels.Length);
        thumbnail.Invalidate();
        return thumbnail;
    }

    public static async Task<ImageSource> LoadFallbackAsync()
    {
        try
        {
            return await LoadFileAsync(Path.Combine(AppContext.BaseDirectory, "Assets", "bg.png"));
        }
        catch (Exception ex)
        {
            DebugLog.Info("BackgroundImageLoader",
                $"BackgroundImageLoader: Default background file unavailable, using embedded background: {ex.Message}");
        }

        return await LoadAsync(() => typeof(BackgroundImageLoader).Assembly.GetManifestResourceStream(
            "FufuLauncher.Assets.bg.png") ?? throw new FileNotFoundException("内嵌默认背景不存在"));
    }

    private static async Task<ImageSource> LoadAsync(Func<Stream> openStream)
    {
        var started = Environment.TickCount64;
        DebugLog.Info("BackgroundImageLoader", "Loader.Start");
        var dispatcher = App.MainWindow?.DispatcherQueue ?? DispatcherQueue.GetForCurrentThread();
        if (dispatcher == null)
            throw new InvalidOperationException("Background image loading requires the main window UI thread");


        var decoded = await Task.Run(async () =>
        {
            var quality = await BackgroundImageQualitySettings.GetEffectiveQualityAsync().ConfigureAwait(false);
            var antialias = await BackgroundImageAntialiasSettings.GetAsync().ConfigureAwait(false);
            using var stream = openStream();
            using var image = Image.Load<Bgra32>(new DecoderOptions { MaxFrames = 1 }, stream);
            image.Mutate(context => context.AutoOrient());
            ApplyDisplayQuality(image, quality);
            var pixels = new byte[checked(image.Width * image.Height * 4)];
            image.CopyPixelDataTo(pixels);
            ApplyAntialias(pixels, image.Width, image.Height, antialias);


            for (var i = 0; i < pixels.Length; i += 4)
            {
                var alpha = pixels[i + 3];
                if (alpha == 255) continue;
                pixels[i] = (byte)((pixels[i] * alpha + 127) / 255);
                pixels[i + 1] = (byte)((pixels[i + 1] * alpha + 127) / 255);
                pixels[i + 2] = (byte)((pixels[i + 2] * alpha + 127) / 255);
            }

            var palette = BackgroundColorAnalyzer.Analyze(pixels, image.Width, image.Height);
            DebugLog.Info("BackgroundImageLoader", "Loader.Decoded",
                $"{image.Width}x{image.Height} in {Environment.TickCount64 - started}ms");
            return (image.Width, image.Height, Pixels: pixels, Palette: palette);
        }).ConfigureAwait(false);

        var completion = new TaskCompletionSource<ImageSource>(TaskCreationOptions.RunContinuationsAsynchronously);

        void CreateBitmap()
        {
            var bitmapStarted = Environment.TickCount64;
            try
            {
                DebugLog.Info("BackgroundImageLoader", "Loader.Bitmap.Start", $"{decoded.Width}x{decoded.Height}");
                var bitmap = new WriteableBitmap(decoded.Width, decoded.Height);
                using var buffer = bitmap.PixelBuffer.AsStream();
                buffer.Write(decoded.Pixels, 0, decoded.Pixels.Length);
                bitmap.Invalidate();
                BackgroundColorPaletteCache.Set(bitmap, decoded.Palette);
                DebugLog.Info("BackgroundImageLoader", "Loader.Bitmap.Done",
                    $"{Environment.TickCount64 - bitmapStarted}ms");
                completion.SetResult(bitmap);
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }

        if (dispatcher.HasThreadAccess)
            CreateBitmap();
        else if (!dispatcher.TryEnqueue(CreateBitmap))
            completion.SetException(new InvalidOperationException("Background image UI dispatcher queue is closed"));

        var result = await completion.Task.ConfigureAwait(false);
        DebugLog.Info("BackgroundImageLoader", "Loader.End", $"{Environment.TickCount64 - started}ms");
        return result;
    }

    
    
    
    
    private static void ApplyAntialias(byte[] pixels, int width, int height, (bool Enabled, int Strength) settings)
    {
        if (!settings.Enabled || settings.Strength <= 0)
            return;

        var profile = BackgroundImageAntialiasSettings.GetEdgeProfile(settings.Strength);
        var started = Environment.TickCount64;
        var processed = BackgroundImageEdgeAntialiaser.Apply(pixels, width, height, profile);
        DebugLog.Info("BackgroundImageLoader",
            $"Applied edge antialias: strength={settings.Strength}, threshold={profile.EdgeThreshold:0.###}, " +
            $"subpix={profile.SubpixelQuality:0.##}, steps={profile.SearchSteps}, " +
            $"edgePixels={processed}, {Environment.TickCount64 - started}ms");
    }

    private static void ApplyDisplayQuality(Image<Bgra32> image, BackgroundImageQuality quality)
    {
        var maxEdge = BackgroundImageQualitySettings.GetMaxDecodeEdge(quality);
        if (maxEdge <= 0)
            return;

        var longestEdge = Math.Max(image.Width, image.Height);
        if (longestEdge <= maxEdge)
            return;

        var scale = (double)maxEdge / longestEdge;
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        image.Mutate(context => context.Resize(width, height));
    }
}