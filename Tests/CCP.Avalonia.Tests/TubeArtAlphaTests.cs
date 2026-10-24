using System;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TubeArtAlphaTests
{
    [Fact]
    public void ScrubsModArtBeforePaintingInBothTubeStyles() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont()
                .With(ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();

        using var source = new WriteableBitmap(new PixelSize(4, 1), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);
        byte[] pixels = { 0, 0, 0, 0, 1, 1, 1, 8, 0, 0, 0, 9, 0, 0, 0, 255 };
        using (var frame = source.Lock()) Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        var clean = Assert.IsAssignableFrom<Bitmap>(TubeArtAlpha.Scrub(source));
        Assert.NotSame(source, clean);
        Assert.Same(clean, TubeArtAlpha.Scrub(source));
        Assert.Same(clean, TubeArtAlpha.Scrub(clean));
        Assert.Null(TubeArtAlpha.Scrub(null));
        Assert.Equal(pixels, Read(source));
        byte[] expected = { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 9, 0, 0, 0, 255 };
        Assert.Equal(expected, Read(clean));

        var dir = Directory.CreateTempSubdirectory("ccp-tube-alpha-");
        var path = Path.Combine(dir.FullName, "tube.png");
        source.Save(path);
        var previous = CoreModArt.OverridePathProvider;
        var previousSettings = CoreSettings.ServiceProvider;
        CoreModArt.OverridePathProvider = name => name is "tube.png" or "tube2.png" ? path : null;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        AvatarTubeWindow? tube = null;
        try
        {
            tube = new AvatarTubeWindow();
            foreach (bool detached in new[] { false, true })
            {
                tube.SetTubeStyle(detached);
                var painted = Assert.IsAssignableFrom<Bitmap>(tube.FindControl<Image>("ImgTubeFrame")!.Source);
                Assert.Equal(expected, Read(painted));
            }
        }
        finally
        {
            tube?.Close();
            CoreModArt.OverridePathProvider = previous;
            CoreSettings.ServiceProvider = previousSettings;
            dir.Delete(true);
        }
    });

    private static byte[] Read(Bitmap bitmap)
    {
        using var copy = new WriteableBitmap(bitmap.PixelSize, bitmap.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frame = copy.Lock();
        bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), frame.Address, frame.RowBytes * bitmap.PixelSize.Height, frame.RowBytes);
        var pixels = new byte[bitmap.PixelSize.Width * 4];
        Marshal.Copy(frame.Address, pixels, 0, pixels.Length);
        return pixels;
    }
}
