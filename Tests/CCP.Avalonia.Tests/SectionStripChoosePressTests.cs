using System.Linq;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SectionStripChoosePressTests : IDisposable
{
    // xUnit disposes each test's instance: put the process-wide glow clock back (PLAYBOOK P02).
    public void Dispose() => NavGlow.Time = TimeProvider.System;

    private sealed class SteppedGlowClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static void SetUp()
    {
        // As in NavRailPaintTests, a slow renderer must not expire a real-time cue between asserts.
        // Holding its clock also proves removal is caused by detachment, rather than elapsed time.
        NavGlow.Time = new SteppedGlowClock();
        if (Application.Current is not null) return;
        // The same bare builder the sibling shell tests use, so whichever class starts the app first
        // the host is identical and the result does not depend on run order.
        AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public async Task ClickShowsOneRingAndChangingSectionRemovesIt(MotionLevel level)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetUp();
            var w = new MainShellWindow();
            try
            {
                w.Show();
                w.ShowTab("presets");
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;
                strip.MotionOverride = level;
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                var pill = strip.PillFor("ramp")!;
                pill.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("ramp", w.CurrentTab);
                var layer = AdornerLayer.GetAdornerLayer(pill)!;
                var rings = layer.Children.OfType<NavGlowRing>().Where(r => AdornerLayer.GetAdornedElement(r) == pill).ToArray();
                Assert.Equal(level == MotionLevel.Off ? 0 : 1, rings.Length);
                if (level == MotionLevel.Full)
                {
                    ((SteppedGlowClock)NavGlow.Time).Now += TimeSpan.FromMilliseconds(NavGlow.SheenMs / 2).Ticks;
                    rings[0].Tick();
                    Assert.Equal(1, rings[0].Opacity);
                }
                if (level == MotionLevel.Reduced)
                {
                    Assert.Equal(0.85, rings[0].Opacity);
                    if (Environment.GetEnvironmentVariable("CCP_STRIP_PROOF_DIR") is { Length: > 0 } dir)
                    {
                        Dispatcher.UIThread.RunJobs();
                        using var frame = w.CaptureRenderedFrame()!;
                        Directory.CreateDirectory(dir);
                        frame.Save(Path.Combine(dir, "section-strip-ramp-choose.png"));
                    }
                }
                w.ShowTab("assets");
                Assert.DoesNotContain(layer.Children.OfType<NavGlowRing>(), r => AdornerLayer.GetAdornedElement(r) == pill);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public async Task PointerPressSquishesAndReleaseChoosesWithMotionGatedRing(MotionLevel level)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetUp();
            var w = new MainShellWindow();
            try
            {
                w.Show();
                w.ShowTab("presets");
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;
                strip.MotionOverride = level;
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                var p = strip.Part("ramp")!;
                var at = p.Pill.TranslatePoint(new Point(p.Pill.Bounds.Width / 2, p.Pill.Bounds.Height / 2), w)!.Value;
                w.MouseMove(at);
                w.MouseDown(at, MouseButton.Left);
                Assert.Equal(0.97, ((TransformGroup)p.Ring.RenderTransform!).Children.OfType<ScaleTransform>().First().GetBaseValue(ScaleTransform.ScaleXProperty).GetValueOrDefault());
                w.MouseUp(at, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("ramp", w.CurrentTab);
                Assert.Equal(1, ((TransformGroup)p.Ring.RenderTransform!).Children.OfType<ScaleTransform>().First().GetBaseValue(ScaleTransform.ScaleXProperty).GetValueOrDefault());
                var layer = AdornerLayer.GetAdornerLayer(p.Pill)!;
                var rings = layer.Children.OfType<NavGlowRing>().Where(r => AdornerLayer.GetAdornedElement(r) == p.Pill).ToArray();
                Assert.Equal(level == MotionLevel.Off ? 0 : 1, rings.Length);
                if (level == MotionLevel.Full)
                {
                    ((SteppedGlowClock)NavGlow.Time).Now += TimeSpan.FromMilliseconds(NavGlow.SheenMs / 2).Ticks;
                    rings[0].Tick();
                    Assert.Equal(1, rings[0].Opacity);
                }
                if (level == MotionLevel.Reduced) Assert.Equal(0.85, rings[0].Opacity);
                // Removing the section removes its one-shot adorners with the pill.
                w.ShowTab("assets");
                Assert.DoesNotContain(layer.Children.OfType<NavGlowRing>(), r => AdornerLayer.GetAdornedElement(r) == p.Pill);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task ReleasingOutsideCancelsThePressWithoutNavigating()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetUp();
            var w = new MainShellWindow();
            try
            {
                w.Show();
                w.ShowTab("presets");
                var strip = w.Named<SectionTabStrip>("SectionStrip")!;
                strip.MotionOverride = MotionLevel.Off;
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                var p = strip.Part("ramp")!;
                var at = p.Pill.TranslatePoint(new Point(p.Pill.Bounds.Width / 2, p.Pill.Bounds.Height / 2), w)!.Value;
                w.MouseMove(at);
                w.MouseDown(at, MouseButton.Left);
                var scale = ((TransformGroup)p.Ring.RenderTransform!).Children.OfType<ScaleTransform>().First();
                Assert.Equal(0.97, scale.ScaleX);
                w.MouseMove(new Point(1, 1));
                w.MouseUp(new Point(1, 1), MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, scale.ScaleX);
                Assert.Equal("presets", w.CurrentTab);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task RightButtonDoesNotPressOrSquishAPill()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            SetUp();
            var strip = new SectionTabStrip { MotionOverride = MotionLevel.Off };
            strip.Show("studio", "presets");
            var w = new Window { Width = 1000, Height = 200, Content = strip };
            try
            {
                w.Show();
                Dispatcher.UIThread.RunJobs();
                w.UpdateLayout();
                var p = strip.Part("ramp")!;
                var at = p.Pill.TranslatePoint(new Point(p.Pill.Bounds.Width / 2, p.Pill.Bounds.Height / 2), w)!.Value;
                w.MouseDown(at, MouseButton.Right);
                var scale = ((TransformGroup)p.Ring.RenderTransform!).Children.OfType<ScaleTransform>().First();
                Assert.False(p.Pressed);
                Assert.Equal(1, scale.ScaleX);
                w.MouseUp(at, MouseButton.Right);
                Assert.Equal("presets", strip.ActivePillKey);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

}
