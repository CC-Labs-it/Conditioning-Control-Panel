using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Descent;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralFirstLightVisualTests
{
    [Fact]
    public Task RendererIsDeterministic_ArmBloomAndHandoffAreDrawn() => Run(() =>
    {
        var first = new SpiralFirstLightVisual(Colors.HotPink);
        var second = new SpiralFirstLightVisual(Colors.HotPink);
        var fog = Draw(first, .30, 0);
        Assert.Equal(fog, Draw(second, .30, 0));
        Assert.NotEqual(fog, Draw(first, .30, 10)); // wall clock moves only the fog
        var arm = Draw(first, 1.5, 0);
        Assert.NotEqual(fog, arm);
        Assert.Equal(arm, Draw(second, 1.5, 0));
        Assert.NotEqual(arm, Draw(first, SpiralFirstLightTimeline.HandoffStartSeconds(false), 0));
        Assert.Equal(Blank(), Draw(first, SpiralFirstLightTimeline.TotalSeconds(false), 0));
    });

    [Fact]
    public Task ReducedMotionPaintsFinishedSpiralWithoutEmbersThenFadesOut() => Run(() =>
    {
        var visual = new SpiralFirstLightVisual(Colors.HotPink);
        visual.Begin(true);
        var snapped = Draw(visual, 0, 0);
        Assert.NotEqual(Blank(), snapped);
        // Same frozen geometry at every reveal timestamp; only layer opacity changes.
        Assert.NotEqual(snapped, Draw(visual, .4, 0));
        Assert.Equal(Blank(), Draw(visual, .8, 0));
        visual.Begin(false);
        Assert.NotEqual(snapped, Draw(visual, 0, 0));
    });

    [Theory]
    [InlineData(MotionLevel.Full, true)]
    [InlineData(MotionLevel.Reduced, false)]
    [InlineData(MotionLevel.Off, false)]
    public Task FogCanvasUsesSharedBeatOnlyForFullMotionAndStopsOnExit(MotionLevel motion, bool ticks) => Run(() =>
    {
        var settings = CoreSettings.Current;
        var before = settings.MotionLevel;
        settings.MotionLevel = motion;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var tab = new SpiralTabView();
        var window = new Window { Content = tab, Width = 800, Height = 500 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            typeof(SpiralTabView).GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(tab, new object[] { SpiralRoomState.Fog });
            var host = tab.FindControl<Grid>("FogHost")!;
            var visual = Assert.IsType<SpiralFirstLightVisual>(Assert.Single(host.Children));
            Assert.False(visual.IsHitTestVisible);
            Assert.Equal(SpiralFirstLightTimeline.DrawStartSeconds, visual.ElapsedSeconds);
            Assert.Equal(0, visual.AmbientSeconds);
            Assert.Equal(ticks, BeatLoop.Running.Any(loop => loop.Owner == visual));
            // The shared beat really moves the fog clock (Full only); the reveal stays held.
            time.Advance(TimeSpan.FromSeconds(1.5));
            BeatLoop.StepAll();
            Assert.Equal(ticks ? 1.5 : 0, visual.AmbientSeconds, 3);
            Assert.Equal(SpiralFirstLightTimeline.DrawStartSeconds, visual.ElapsedSeconds);
            var frame = Draw(visual, visual.ElapsedSeconds, 0);
            visual.Begin(false); // reduced fog must never snap to the revealed spiral
            Assert.Equal(frame, Draw(visual, SpiralFirstLightTimeline.DrawStartSeconds, 0));
            tab.IsVisible = false;
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == visual);
            tab.IsVisible = true;
            typeof(SpiralTabView).GetMethod("ApplyState", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(tab, new object[] { SpiralRoomState.Fog });
            window.Content = null;
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == visual);
        }
        finally { window.Close(); settings.MotionLevel = before; BeatLoop.Time = TimeProvider.System; }
    });

    // WPF SpiralTabView.xaml.cs:785 snapshots MotionFx.Level; MotionFx.cs:35-54 applies both caps.
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    public Task RevealSnapshotsEffectiveMotion(bool gentle, bool osAnimations, bool reduced) => Run(() =>
    {
        var settings = CoreSettings.Current;
        var before = (settings.MotionLevel, settings.GentleVisuals);
        var oldProbe = UiMotionPreference.OsProbe;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        settings.MotionLevel = MotionLevel.Full;
        settings.GentleVisuals = gentle;
        UiMotionPreference.OsProbe = () => osAnimations;
        UiMotionPreference.CacheStamp = long.MinValue;
        var tab = new SpiralTabView();
        var window = new Window { Content = tab, Width = 800, Height = 500 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            tab.BeginFirstLight();
            var visual = Assert.IsType<SpiralFirstLightVisual>(Assert.Single(tab.FindControl<Grid>("FogHost")!.Children));
            time.Advance(TimeSpan.FromSeconds(.5));
            BeatLoop.StepAll();
            Assert.Equal(reduced ? SpiralFirstLightTimeline.HandoffStartSeconds(true) : .5, visual.ElapsedSeconds, 3);
            Assert.Equal(MotionLevel.Full, settings.MotionLevel);

            // The reveal keeps its entry snapshot if the preference changes mid-reveal.
            settings.GentleVisuals = false;
            UiMotionPreference.OsProbe = () => true;
            UiMotionPreference.CacheStamp = long.MinValue;
            time.Advance(TimeSpan.FromSeconds(.5));
            BeatLoop.StepAll();
            Assert.Equal(reduced ? SpiralFirstLightTimeline.HandoffStartSeconds(true) : 1, visual.ElapsedSeconds, 3);
        }
        finally
        {
            window.Close();
            (settings.MotionLevel, settings.GentleVisuals) = before;
            UiMotionPreference.OsProbe = oldProbe;
            UiMotionPreference.CacheStamp = long.MinValue;
            BeatLoop.Time = TimeProvider.System;
        }
    });

    private static byte[] Draw(SpiralFirstLightVisual control, double elapsed, double ambient)
    {
        control.SetFrame(elapsed, ambient);
        return Pixels(control);
    }

    private static byte[] Blank() => Pixels(new Control());

    private static byte[] Pixels(Control control)
    {
        control.Measure(new Size(800, 500));
        control.Arrange(new Rect(0, 0, 800, 500));
        using var image = new RenderTargetBitmap(new PixelSize(800, 500), new Vector(96, 96));
        image.Render(control);
        using var bytes = new MemoryStream();
        image.Save(bytes);
        return bytes.ToArray();
    }

    private static Task Run(Action action) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        action();
        return Task.CompletedTask;
    });
}
