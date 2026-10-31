using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosHudPortraitTests
{
    // WPF ConditioningControlPanel.csproj:211-215 ships the promoted art, not a test fixture.
    [Fact]
    public void Bundled_portrait_is_copied_to_output()
    {
        var source = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(ThisFile())!, "..", "..",
            "ConditioningControlPanel", "assets", "Chaos", "portraits", "neutral.png"));
        var bundled = Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", "portraits", "neutral.png");
        Assert.True(File.Exists(bundled), $"Bundled HUD portrait was not copied: {bundled}");
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(bundled));
    }

    private static string ThisFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    // WPF ChaosArt.Resolve: prefer a decodable user override, else bundled, else hide.
    [Theory]
    [InlineData(true, true, 2)]
    [InlineData(false, true, 3)]
    [InlineData(false, false, 0)]
    public void Portrait_uses_Wpf_roots_and_decode_fallback(bool validUser, bool validBundle, int width)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var paths = new[] {
                Path.Combine(CorePaths.UserData, "assets", "assets", "Chaos", "portraits", "neutral.png"),
                Path.Combine(AppContext.BaseDirectory, "assets", "Chaos", "portraits", "neutral.png") };
            var before = Array.ConvertAll(paths, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
            ChaosHudWindow? hud = null;
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(paths[i])!);
                    if (i == 0 ? validUser : validBundle)
                    {
                        using var image = new WriteableBitmap(new PixelSize(i + 2, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                        image.Save(paths[i]);
                    }
                    else File.WriteAllText(paths[i], "unreadable image");
                }
                hud = new ChaosHudWindow(new ChaosRunState(new ChaosRunConfig()));
                var imageControl = hud.FindControl<Image>("Portrait")!;
                Assert.Equal(width != 0, hud.FindControl<Border>("PortraitHost")!.IsVisible);
                if (width == 0) Assert.Null(imageControl.Source);
                else Assert.Equal(width, Assert.IsType<Bitmap>(imageControl.Source).PixelSize.Width);
                hud.Close();
                Assert.Null(imageControl.Source); // the window owns and releases its decoded bitmap
            }
            finally
            {
                hud?.Close();
                for (int i = 0; i < paths.Length; i++)
                    if (before[i] is { } bytes) File.WriteAllBytes(paths[i], bytes);
                    else File.Delete(paths[i]);
            }
        });
    }
}
