using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Billboard;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;
namespace CCP.Avalonia.Tests;
public sealed class PosterArtViewTests
{
    // Layout/pointer tests use an installed font; the shipped font stack has its own tests.
    private static readonly FontFamily TestFont = new(OperatingSystem.IsLinux() ? "DejaVu Sans" : "Arial");
    [Fact]
    public Task MotionGatesHoldAndReleaseThePoster() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var mono = Application.Current!.Resources["Font.Mono"];
        Application.Current.Resources["Font.Mono"] = TestFont;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        var deck = new BillboardDeck(() => new[] { new PosterProvider() },
            () => new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now));
        var host = new BillboardCardHost(deck);
        var window = new Window { FontFamily = TestFont, Content = host, Width = 800, Height = 450 };
        try
        {
            window.Show();
            host.Begin();
            window.UpdateLayout();
            var view = Assert.Single(host.GetVisualDescendants().OfType<PosterArtView>());
            view.Run();
            Assert.False(view.IsAnimating);
            var still = view.CurrentMatrix;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            CoreSettings.Current.PerformanceMode = false;
            view.Run();
            Assert.True(view.IsAnimating);
            view.Step(.1, true, new Point(1, 0));
            Assert.NotEqual(still, view.CurrentMatrix);
            view.Hold();
            Assert.False(view.IsAnimating);
            view.Run();
            Assert.True(view.IsAnimating);
            host.ReleaseArt();
            view.Run();
            Assert.False(view.IsAnimating);
        }
        finally { host.ReleaseArt(); window.Close(); Application.Current.Resources["Font.Mono"] = mono; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task HoverSurvivesTheCtaAndSnoozeOverlays() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var mono = Application.Current!.Resources["Font.Mono"];
        Application.Current.Resources["Font.Mono"] = TestFont;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        CoreSettings.Current.PerformanceMode = false;
        var host = new BillboardCardHost(new BillboardDeck(() => new[] { new PosterProvider() },
            () => new BillboardContext(BillboardTier.Free, DateTime.UtcNow, DateTime.Now)));
        var window = new Window { FontFamily = TestFont, Content = host, Width = 800, Height = 450 };
        try
        {
            window.Show(); host.Begin(); window.UpdateLayout();
            var view = Assert.Single(host.GetVisualDescendants().OfType<PosterArtView>());
            window.MouseMove(view.TranslatePoint(new Point(400, 200), window)!.Value);
            Assert.True(view.Hovered);
            // The stage must see handled moves too, before the button consumes them.
            host.Cta.PointerMoved += (_, e) => e.Handled = true;
            foreach (var overlay in new[] { host.Cta, host.SnoozeButton })
            {
                Assert.True(overlay.IsVisible);
                var center = new Point(overlay.Bounds.Width / 2, overlay.Bounds.Height / 2);
                var at = overlay.TranslatePoint(center, window)!.Value;
                window.MouseMove(at);
                Assert.True(view.Hovered);
                var local = overlay.TranslatePoint(center, view)!.Value;
                Assert.Equal(local.X / view.Bounds.Width, view.Aim.X, 5);
                Assert.Equal(local.Y / view.Bounds.Height, view.Aim.Y, 5);
            }
            window.MouseMove(new Point(-10, -10));
            Assert.False(view.Hovered);
            host.ReleaseArt();
            window.MouseMove(view.TranslatePoint(new Point(400, 200), window)!.Value);
            Assert.False(view.Hovered); // released art unhooks the stage
        }
        finally { host.ReleaseArt(); window.Close(); Application.Current.Resources["Font.Mono"] = mono; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WidePosterKeepsTheWpfRightBiasedCrop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        using var source = new WriteableBitmap(new PixelSize(600, 200), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var frame = source.Lock())
        {
            int stride = frame.RowBytes / 4;
            var pixels = Enumerable.Range(0, stride * 200)
                .Select(i => unchecked((int)(i % stride < 300 ? 0xffff0000u : 0xff0000ffu))).ToArray();
            Marshal.Copy(pixels, 0, frame.Address, pixels.Length);
        }
        var cover = new PosterCoverImage { Source = source };
        cover.Measure(new Size(200, 200)); cover.Arrange(new Rect(0, 0, 200, 200));
        using var rendered = new RenderTargetBitmap(new PixelSize(200, 200));
        rendered.Render(cover);
        var pixel = Marshal.AllocHGlobal(4);
        try
        {
            // 0.62 crops source x=248..448; centred cover crops x=200..400.
            // At view x=60 WPF therefore shows blue, while centred cover shows red.
            rendered.CopyPixels(new PixelRect(60, 100, 1, 1), pixel, 4, 4);
            Assert.Equal(0xff0000ffu, unchecked((uint)Marshal.ReadInt32(pixel)));
        }
        finally { Marshal.FreeHGlobal(pixel); }
        return Task.CompletedTask;
    });

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private sealed class PosterProvider : IBillboardProvider
    {
        public string Id => "poster-test";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => new[]
        {
            new BillboardCardSpec("poster-test", BillboardCardKind.House, 0, "", "Poster", "", "#ff4fa8",
                "poster", "", new BillboardAction(BillboardActionKind.Tab, "home", "Read")),
        };
        public void Invoke(string target) { }
        public event EventHandler? Changed { add { } remove { } }
    }
}
