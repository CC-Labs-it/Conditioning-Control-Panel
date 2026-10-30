using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class QuestClipboardArtTests
{
    [Fact]
    public Task ClipboardPaintsTheLoopAndReleasesItsClock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = MotionLevel.Full;
        settings.Current.PerformanceMode = false;
        var art = new QuestsArtView(new Dictionary<string, int> { ["done"] = 5, ["total"] = 7 });
        Assert.Equal(5, art.Shown);
        Assert.Equal(3, art.DoneShown);
        var window = new Window { Width = 600, Height = 340, Content = art };
        try
        {
            window.Show();
            art.Run();
            Assert.True(art.Running);
            art.Hold();
            Assert.False(art.Running);
            foreach (var size in new[] { new Size(600, 340), new Size(1000, 630) })
            {
                art.Measure(size);
                art.Arrange(new Rect(size));
                byte[]? first = null;
                bool changed = false;
                for (double t = 0; t < 13; t += 0.2)
                {
                    art.Frame(t);
                    using var bitmap = new RenderTargetBitmap(PixelSize.FromSize(size, 1));
                    bitmap.Render(art);
                    using var png = new MemoryStream();
                    bitmap.Save(png);
                    var frame = png.ToArray();
                    first ??= frame;
                    changed |= !first.AsSpan().SequenceEqual(frame);
                }
                Assert.True(changed, "The clipboard story must change across the loop.");
            }
            settings.Current.MotionLevel = MotionLevel.Off;
            art.Run();
            Assert.False(art.Running);
            settings.Current.MotionLevel = MotionLevel.Full;
            art.Release();
            art.Run();
            Assert.False(art.Running);
        }
        finally { art.Release(); window.Close(); }
        return Task.CompletedTask;
    });
    private static void EnsureUi()
    {
        if (Application.Current == null) AppBuilder.Configure<Application>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private sealed class Probe(Action<DrawingContext> draw) : Control
    {
        public override void Render(DrawingContext d) => draw(d);
    }
    private static byte[] Raster(Action<DrawingContext> draw)
    {
        var view = new Probe(draw); view.Measure(new Size(200, 240)); view.Arrange(new Rect(0, 0, 200, 240));
        using var bmp = new RenderTargetBitmap(new PixelSize(200, 240)); bmp.Render(view);
        var pixels = new byte[200 * 240 * 4]; var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bmp.CopyPixels(new PixelRect(0, 0, 200, 240), pin.AddrOfPinnedObject(), pixels.Length, 800); }
        finally { pin.Free(); }
        return pixels;
    }
    [Fact]
    public Task GlintHasWpfOffsetDutyCycleSlantAndGradient() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureUi();
        foreach (double t in new[] { 1.6, 2.49, 3.5 }) Assert.All(Raster(d => QuestsArtView.PaintSheen(d, new Rect(0, 0, 200, 60), t)), b => Assert.Equal(0, b));
        var pixels = Raster(d => QuestsArtView.PaintSheen(d, new Rect(0, 0, 200, 60), 2.6392));
        int A(int x, int y) => pixels[(y * 200 + x) * 4 + 3];
        Assert.True(A(100, 10) > 0); Assert.Equal(0, A(100, 50));
        Assert.True(A(65, 50) > 0); Assert.Equal(0, A(65, 10));
        Assert.True(A(85, 30) > A(100, 30));
        return Task.CompletedTask;
    });
    [Fact]
    public Task StampIsDrawnOnlyDuringItsBeatsAndUsesRotation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureUi();
        byte[] Shot(double p, int target = 0) => Raster(d =>
        {
            using var translate = d.PushTransform(Matrix.CreateTranslation(100, 0));
            QuestsArtView.PaintStamp(d, 18, 160, 200, 160, 8, target, p);
        });
        foreach (double p in new[] { 0.5, 3.3 }) Assert.All(Shot(p), b => Assert.Equal(0, b));
        Assert.All(Shot(1.5, -1), b => Assert.Equal(0, b));
        foreach (double p in new[] { 0.9, 1.35, 1.56, 1.62, 2.75 }) Assert.Contains(Shot(p), b => b != 0);
        var m = QuestsArtView.StampTransform(1.35, 18, 200, 0, 0);
        Assert.True(m.M12 < 0); Assert.True(m.M21 > 0);
        Assert.Equal(0.971715729, Math.Sqrt(m.M11 * m.M11 + m.M12 * m.M12), 6);
        Assert.Equal(1.042426407, Math.Sqrt(m.M21 * m.M21 + m.M22 * m.M22), 6);
        return Task.CompletedTask;
    });
    [Fact]
    public Task MotionOffKeepsThePlayedFrameAndResumeStartsThere() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureUi(); var settings = new SettingsService(); CoreSettings.ServiceProvider = () => settings;
        settings.Current.MotionLevel = MotionLevel.Off;
        var art = new QuestsArtView(null);
        try
        {
            art.Run(); Assert.Equal(1.6, art.Seconds);
            settings.Current.MotionLevel = MotionLevel.Full; settings.Current.PerformanceMode = false;
            art.Run(); art.Frame(3.25);
            settings.Current.MotionLevel = MotionLevel.Off;
            art.Run(); Assert.False(art.Running); Assert.Equal(3.25, art.Seconds);
            art.Run(); Assert.Equal(3.25, art.Seconds);
            settings.Current.MotionLevel = MotionLevel.Full;
            art.Run(); Assert.True(art.Running); Assert.Equal(3.25, art.Seconds);
        }
        finally { art.Release(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task ShellProviderFactoryRegistersFreshClipboardArt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureUi();
        // Exercise the shell's provider wiring without constructing font-dependent chrome.
        _ = MainShellWindow.BillboardProviders(new WeakReference<MainShellWindow>(null!));
        var payload = new Dictionary<string, int> { ["done"] = 5, ["total"] = 7 };
        var first = Assert.IsType<QuestsArtView>(BillboardArt.Create(BuiltInArtKeys.Quests, payload));
        var second = Assert.IsType<QuestsArtView>(BillboardArt.Create(BuiltInArtKeys.Quests, payload));
        Assert.NotSame(first, second); Assert.Equal(3, first.DoneShown); Assert.Equal(5, first.Shown);
        first.Release(); second.Release();
        return Task.CompletedTask;
    });
    [Fact]
    public Task SceneActuallyDrawsItsGlintAndStampAtTheirPhases() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureUi();
        byte[] Shot(double t, bool sheen, bool stamp) => Raster(d =>
        {
            var art = new QuestsArtView(new Dictionary<string, int> { ["done"] = 2, ["total"] = 3 }) { ShowSheen = sheen, ShowStamp = stamp };
            art.Measure(new Size(200, 240)); art.Arrange(new Rect(0, 0, 200, 240)); art.Frame(t); art.Render(d); art.Release();
        });
        foreach (double t in new[] { 1.6, 2.49, 3.5 }) Assert.Equal(Shot(t, false, false), Shot(t, true, false));
        Assert.False(Shot(2.6392, false, false).AsSpan().SequenceEqual(Shot(2.6392, true, false)));
        foreach (double t in new[] { 2.3, 2.75, 2.96, 3.02, 3.85 }) Assert.False(Shot(t, false, false).AsSpan().SequenceEqual(Shot(t, false, true)), $"stamp t={t}");
        return Task.CompletedTask;
    });
}
