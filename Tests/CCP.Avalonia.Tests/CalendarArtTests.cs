using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using SkiaSharp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Billboard;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class CalendarArtTests
{
    [Fact]
    public void HomeCardUsesCalendarArtAndPanicParksItsClock() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldSettings = CoreSettings.ServiceProvider;
        var oldMake = MainShellWindow.MakeShowcase;
        var oldMotion = CalendarArtView.MotionAllowed;
        CoreSettings.ServiceProvider = () => settings;
        MainShellWindow? shell = null;
        try
        {
            CoreSettings.Current.DashboardBrowserCollapsed = true;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            var card = EventCards.Locktober(true, new DateTime(2026, 10, 25),
                new RaffleReading(2, 3600, new[] { 2, 9 }), key => key)!;
            MainShellWindow.MakeShowcase = () => new Provider(card);
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var host = shell.BillboardHost!;
            for (int i = 0; i < host.Deck.Cards.Count && host.CurrentCard?.Spec.Id != card.Id; i++) host.Advance();
            var art = Assert.Single(host.GetLogicalDescendants().OfType<CalendarArtView>());
            Assert.True(art.Data.IsLit(9));
            Assert.False(art.Data.IsLit(1));
            Assert.False(art.Running);
            using var bitmap = new RenderTargetBitmap(new PixelSize(800, 300));
            art.Measure(new Size(800, 300));
            art.Arrange(new Rect(0, 0, 800, 300));
            bitmap.Render(art); // exercises the actual drawing code, including text and progress
            // Landed frames reuse their number layouts instead of building ~31 per tick.
            int built = art.TextLayoutsBuilt;
            Assert.True(built > 0);
            bitmap.Render(art);
            Assert.Equal(built, art.TextLayoutsBuilt);
            using var pixels = new MemoryStream();
            bitmap.Save(pixels);
            pixels.Position = 0;
            using var decoded = SKBitmap.Decode(pixels);
            // Day 2 is counted, day 1 is not; compare inside their faces, above the numbers.
            Assert.True(decoded.GetPixel(443, 49).Red > decoded.GetPixel(405, 49).Red + 50);
            // Progress follows Counted / Needed, independently of the sparse mask.
            Assert.True(decoded.GetPixel(410, 234).Red > decoded.GetPixel(600, 234).Red + 50);
            var frames = new List<long>();
            foreach (double t in new[] { 0, .2, .6, 1.6, 2.9, 3.7 })
            {
                art.SecondsForTests = t;
                using var frame = Draw(art.Render);
                frames.Add(frame.Pixels.Select((c, i) => (long)c.Red * (i + 1)).Sum());
            }
            Assert.Equal(frames.Count, frames.Distinct().Count());
            // WPF BuiltInArt.cs:313-327: today's shadow and glint cover the next cell.
            foreach (double t in new[] { 64.48, 1.6 })
            {
                var front = new CalendarArtView(new BillboardCalendarInfo(0, 0, 31, 1, 0)) { SecondsForTests = t };
                var control = new CalendarArtView(new BillboardCalendarInfo(0, 0, 31, 31, 0)) { SecondsForTests = t };
                foreach (var view in new[] { front, control }) { view.Measure(new Size(800, 300)); view.Arrange(new Rect(0, 0, 800, 300)); }
                using var top = Draw(front.Render); using var behind = Draw(control.Render);
                int delta = t > 2 ? top.GetPixel(442, 43).Red - behind.GetPixel(442, 43).Red : behind.GetPixel(439, 60).Red - top.GetPixel(439, 60).Red;
                Assert.True(delta > 5, $"Today must overlay its neighbour at {t}s: pixel difference {delta}");
                front.Release(); control.Release();
            }
            art.SecondsForTests = null;
            CalendarArtView.MotionAllowed = () => true;
            art.Run();
            Assert.True(art.Running);
            art.Hold();
            Assert.False(art.Running);
            art.Run();
            Assert.True(art.Running);
            var retired = new CalendarArtView(art.Data);
            retired.Run();
            retired.Release();
            retired.Run();
            Assert.False(retired.Running);
            PanicSurfaces.All.Single(s => s.Id == "billboard-calendar").Stop(shell);
            Assert.False(art.Running);
            art.Run(); // panic parks, it does not freeze a single-card deck for the session
            Assert.True(art.Running);
            art.Release();
            // A hold before the first Run (Begin runs before the shell shows) keeps the counted-day pop.
            var early = new CalendarArtView(art.Data);
            early.Hold();
            Assert.Equal(0, early.Offset);
            early.Release();
        }
        finally
        {
            shell?.Close();
            MainShellWindow.MakeShowcase = oldMake;
            CalendarArtView.MotionAllowed = oldMotion;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    // Isolated rendered layers catch each missing effect, independently of the other layers.
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void WpfLayersRenderAtSampledTimes(int layer) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var art = new CalendarArtView(new BillboardCalendarInfo(2, 25, 31, 25, 3));
        SKBitmap Frame(double t) => Draw(dc =>
        {
            if (layer == 0) art.PaintGround(dc, 800, 300);
            if (layer == 1 || layer == 2) art.PaintParticles(dc, 800, 300, t, layer == 2);
            if (layer == 3) art.Spark(dc, new Point(100, 100), 20, Colors.White, .95, t);
            if (layer == 4) CalendarArtView.Sheen(dc, new Rect(100, 100, 300, 20), 10, t);
            if (layer == 6) art.TodayGlint(dc, new Rect(100, 100, 80, 80), 80, t);
            if (layer == 7) art.PaintProgress(dc, new Rect(100, 100, 300, 20), 20, t);
            if (layer == 5) art.Plate(dc, new Rect(100, 100, 80, 60), 8, Brushes.Gray, 10, t > 1);
        });
        var sums = new List<long>();
        foreach (double t in new[] { 0, .2, .6, 1.6, 2.9, 3.7 })
        {
            using var frame = Frame(t);
            sums.Add(frame.Pixels.Select((c, i) => (long)(c.Alpha + c.Red + c.Green + c.Blue) * (i + 1)).Sum());
            if (layer == 6 && (t == 0 || t == 1.6)) Assert.All(frame.Pixels, c => Assert.Equal(0, c.Alpha));
            if (layer == 7) Assert.True(frame.GetPixel(150, 102).Green > frame.GetPixel(150, 117).Green);
            if (layer == 0) Assert.True(frame.GetPixel(576, 135).Red > frame.GetPixel(10, 10).Red);
            if (layer == 5) Assert.Equal(t <= 1, frame.GetPixel(140, 100).Red > frame.GetPixel(140, 159).Red);
            if (layer == 5) Assert.Equal(t <= 1, frame.GetPixel(187, 171).Alpha > 0); // outer shadow extends down and right
        }
        Assert.Contains(sums, n => n > 0);
        if (layer != 0) Assert.True(sums.Distinct().Count() > 1);
        if (layer == 1 || layer == 2)
        {
            var motion = CalendarArtView.MotionAllowed; var particles = CalendarArtView.ParticlesAllowed;
            try { CalendarArtView.MotionAllowed = () => true; CalendarArtView.ParticlesAllowed = () => false; art.Run(); using var off = Frame(.2); Assert.All(off.Pixels, c => Assert.Equal(0, c.Alpha)); }
            finally { art.Release(); CalendarArtView.MotionAllowed = motion; CalendarArtView.ParticlesAllowed = particles; }
        }
        art.Release();
    });
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1.176226968862)] [InlineData(3, -.450217217285)]
    public void BobUsesBothWpfFrequencies(double t, double expected) => Assert.Equal(expected, CalendarArtView.Bob(t, 1), 10);
    private static SKBitmap Draw(Action<DrawingContext> paint)
    {
        var probe = new Probe(paint); probe.Measure(new Size(800, 300)); probe.Arrange(new Rect(0, 0, 800, 300));
        using var bitmap = new RenderTargetBitmap(new PixelSize(800, 300)); bitmap.Render(probe);
        using var stream = new MemoryStream(); bitmap.Save(stream); stream.Position = 0; return SKBitmap.Decode(stream);
    }
    private sealed class Probe(Action<DrawingContext> paint) : Control { public override void Render(DrawingContext dc) => paint(dc); }

    private readonly SettingsService settings = new();
    private sealed class Provider(BillboardCardSpec card) : IBillboardProvider
    {
        public string Id => "calendar-test";
        public IEnumerable<BillboardCardSpec> Current(BillboardContext context) => new[] { card };
        public void Invoke(string target) { }
        public event EventHandler? Changed { add { } remove { } }
    }
}
