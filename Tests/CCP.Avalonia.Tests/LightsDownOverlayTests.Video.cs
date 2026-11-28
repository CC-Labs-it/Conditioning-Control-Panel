using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Super;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Video;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Lane L6 of the Linux Super port: Lights Down driven by Core <see cref="LightsDownRun"/> on the
/// mandatory video (WPF LightsDownService, 7.4.1): the free-account gate, the full-screen layer and its
/// edge dim, the floating dim window with a hole per video, Follow off by default, the refusals, panic
/// and the --render-all sample.</summary>
public partial class LightsDownOverlayTests
{
    private sealed class StillTime : TimeProvider, ITimer
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => this;
        public bool Change(TimeSpan due, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 120000;
        public bool IsPlaying => true;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public void SetPause(bool paused) { }
        public void Stop() { }
        public void Dispose() { }
    }

    private static readonly PixelRect Desk = new(0, 0, 1920, 1080);

    private static void Video(bool floating, Action<MandatoryVideoOverlay, AppSettings> body, bool gate = true)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var provider = CoreSettings.ServiceProvider;
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = service.Current;
            (s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.LightsDownFollow, s.GentleVisuals,
                s.AudioDuckingEnabled, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled)
                = (floating, 3, false, false, false, false, false, 0, false);
            var seams = (LightsDownOverlay.EffectOn, LightsDownOverlay.Cursor, LightsDownOverlay.OpenDim, LightsDownOverlay.Stack, LightsDownOverlay.FloatScreens);
            if (gate) LightsDownOverlay.EffectOn = () => true;
            LightsDownOverlay.Cursor = () => null;
            LightsDownOverlay.OpenDim = (v, b, k, c) => LightsDownOverlay.TryOpen(v, b, k, _ => true, _ => true, _ => true, (_, _) => true, c);
            LightsDownOverlay.Stack = (_, _) => LightsDownStack.Below;
            LightsDownOverlay.FloatScreens = (_, _) => new[] { (Desk, 1.0) };
            var o = new MandatoryVideoOverlay(() => new Decoder());
            o.Scheduler = new MandatoryVideoScheduler(o, new StillTime(), () => new[] { "/v/a.mp4", "/v/b.mp4", "/v/c.mp4" });
            try
            {
                Assert.True(o.Scheduler.Trigger(userInitiated: false));
                o.Show("/v/first.mp4", strict: false);
                if (!floating)
                    ((List<MandatoryVideoOverlay.Surface>)o.Surfaces).Add(o.Build(MultiMonitorResultTests.MakeScreen(1, Desk, Desk, true), false));
                Dispatcher.UIThread.RunJobs();   // the posted start (WPF VideoStarted)
                body(o, s);
            }
            finally
            {
                o.Scheduler.ForceCleanup();
                foreach (var w in o.Windows.ToList()) w.Close();
                o.CloseAll();
                LightsDownOverlay.CloseAll();
                (LightsDownOverlay.EffectOn, LightsDownOverlay.Cursor, LightsDownOverlay.OpenDim, LightsDownOverlay.Stack, LightsDownOverlay.FloatScreens) = seams;
                CoreSettings.ServiceProvider = provider;
            }
        });
    }

    private static void Frames(int n, double dt = 0.1) { for (int i = 0; i < n; i++) LightsDownOverlay.Frame(dt); }

    [Fact]
    public void A_free_account_sees_nothing_without_the_debug_override() => Video(false, (o, s) =>
    {
        var (premium, trying) = (CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider);
        var all = Environment.GetEnvironmentVariable("CCP_SUPER_ALL");
        try
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", null);
            s.MandatoryVideosEnabled = true;
            s.SuperModes = SuperModeRule.With(s.SuperModes, SuperEffect.LightsDown, SuperMode.Both);
            SuperAccess.TryingProvider = () => null;
            CoreEntitlement.HasPremiumProvider = () => false;
            LightsDownOverlay.Start(o);
            Assert.False(LightsDownOverlay.Running);
            Assert.Empty(LightsDownOverlay.Layers);
            CoreEntitlement.HasPremiumProvider = () => true;
            SuperAccess.RaiseChanged(SuperEffect.LightsDown);
            Dispatcher.UIThread.RunJobs();
            Assert.True(LightsDownOverlay.Running);
            Assert.Single(LightsDownOverlay.Layers);
            Assert.Contains(SuperEffect.LightsDown, SuperBox.HostedOnThisHead);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CCP_SUPER_ALL", all);
            (CoreEntitlement.HasPremiumProvider, SuperAccess.TryingProvider) = (premium, trying);
        }
    }, gate: false);

    [Fact]
    public void Full_screen_dims_the_edges_inside_the_video_window_and_pulls_back_when_the_mouse_moves() => Video(false, (o, s) =>
    {
        var surface = o.Surfaces.Single();
        var host = (Panel)surface.Window.Content!;
        var layer = Assert.Single(LightsDownOverlay.Layers);
        // Over the picture, under the attention targets, the grace card and the buttons.
        Assert.Equal(host.Children.IndexOf(surface.Picture) + 1, host.Children.IndexOf(layer));
        Assert.True(host.Children.IndexOf(layer) < host.Children.IndexOf(surface.Layer));
        Assert.False(layer.IsHitTestVisible);
        Assert.Equal(0, LightsDownOverlay.Count);   // no window of its own on full screen

        (int, int)? at = (900, 500);
        LightsDownOverlay.Cursor = () => at;
        Frames(30);   // 3 s at rest
        var run = LightsDownOverlay.RunForTest!;
        Assert.False(run.Floating);
        Assert.True(run.Full.Level > 0.5);
        Assert.False(layer.Blank);
        var c = layer.CanvasForTest;
        Assert.True(c.At(0, 0).A > 60, "the screen edge is dim");
        var (mi, mj) = c.CellAt(960, 540);
        Assert.Equal(0, c.At(mi, mj).A);   // the middle of the picture stays clear
        double rested = run.Full.Level;
        at = (1300, 700);
        Frames(2);
        Assert.True(run.Full.Level < rested, "a moving mouse pulls the dim back");

        o.CloseAll();   // a full-screen teardown ends it on the spot
        Assert.False(LightsDownOverlay.Running);
        Assert.DoesNotContain(layer, host.Children);
    });

    [Fact]
    public void Floating_dims_the_screen_under_the_video_with_a_haloed_hole_that_fades_out_at_the_end() => Video(true, (o, s) =>
    {
        Assert.True(LightsDownOverlay.Running);
        Assert.Equal(0, LightsDownOverlay.Count);   // no dim before a window is on screen
        Frames(1);
        Assert.Equal(1, LightsDownOverlay.Count);
        var layer = Assert.Single(LightsDownOverlay.Layers);
        var video = o.Windows.Single();
        Frames(80, 0.25);   // 20 s of growth
        var hole = Assert.Single(layer.Screen.Holes.Current);
        Assert.True(hole.Halo);
        Assert.Equal(video.Position.X, hole.Rect.X);
        Assert.Equal(video.Position.Y, hole.Rect.Y);
        Assert.True(LightsDownOverlay.RunForTest!.Float.Grow > 0.7);
        var c = layer.CanvasForTest;
        var (hi, hj) = c.CellAt(hole.Rect.X + hole.Rect.W / 2, hole.Rect.Y + hole.Rect.H / 2);
        Assert.Equal(0, c.At(hi, hj).A);   // the video is never dimmed
        Assert.True(c.At(0, 0).A > 60);

        o.CloseAll();   // the run's natural end: the dim fades (0.6 s), it does not snap
        Assert.True(LightsDownOverlay.Running);
        Assert.True(LightsDownOverlay.RunForTest!.Ending);
        Frames(7);
        Assert.False(LightsDownOverlay.Running);
        Assert.Equal(0, LightsDownOverlay.Count);
    });

    [Fact]
    public void Follow_is_off_by_default_and_when_on_an_unwatched_floating_video_never_dims() => Video(true, (o, s) =>
    {
        Assert.False(new AppSettings().LightsDownFollow);
        LightsDownOverlay.Cursor = () => (5, 5);   // nowhere near the video
        Frames(20, 0.25);
        Assert.True(LightsDownOverlay.RunForTest!.Float.Grow > 0.1, "Follow off: the dim grows whatever the mouse does");
        s.LightsDownFollow = true;
        Frames(40, 0.25);
        Assert.Equal(0, LightsDownOverlay.RunForTest!.Float.Grow);
        var v = o.Windows.Single();
        LightsDownOverlay.Cursor = () => (v.Position.X + 20, v.Position.Y + 20);
        Frames(8, 0.25);
        Assert.True(LightsDownOverlay.RunForTest!.Float.Grow > 0, "watched again: it grows");
    });

    [Theory]
    [InlineData(LightsDownStack.Wrong, 2)]
    [InlineData(LightsDownStack.Unknown, 4)]
    public void A_window_manager_that_cannot_keep_the_video_over_the_dim_ends_it(LightsDownStack order, int checks) => Video(true, (o, s) =>
    {
        LightsDownOverlay.Stack = (_, _) => order;
        Frames(1);
        for (int i = 1; i < checks; i++)
        {
            Assert.Equal(1, LightsDownOverlay.Count);
            Frames(1, 0.6);
        }
        Assert.Equal(0, LightsDownOverlay.Count);
        Assert.False(LightsDownOverlay.Running);
        Assert.NotNull(LightsDownOverlay.Refused);
    });

    [Fact]
    public void A_dim_window_that_cannot_open_safely_is_refused_and_the_video_plays_on() => Video(true, (o, s) =>
    {
        LightsDownOverlay.OpenDim = (v, b, k, c) => LightsDownOverlay.TryOpen(v, b, k, _ => true, _ => true, _ => false, (_, _) => true, c);
        Frames(1);
        Assert.Equal(0, LightsDownOverlay.Count);
        Assert.False(LightsDownOverlay.Running);
        Assert.NotNull(LightsDownOverlay.Refused);
        Assert.True(o.Scheduler.IsPlaying);
        Assert.Single(o.Windows);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Panic_closes_it_at_once_in_either_mode_even_mid_fade(bool floating) => Video(floating, (o, s) =>
    {
        Frames(5);
        Assert.True(LightsDownOverlay.Running);
        if (floating) { o.CloseAll(); Assert.True(LightsDownOverlay.RunForTest!.Ending); }
        PanicSurfaces.All.Single(x => x.Id == "lights-down").Stop(null);
        Assert.False(LightsDownOverlay.Running);
        Assert.Equal(0, LightsDownOverlay.Count);
        Assert.Empty(LightsDownOverlay.Layers);
    });

    [Fact]
    public void The_render_sample_draws_a_dim_with_a_clear_hole() => AvaloniaTestDispatcher.Run(() =>
    {
        var view = new LightsDownCanvasView();
        Assert.False(view.Blank);
        var c = view.CanvasForTest;
        var (hi, hj) = c.CellAt(240, 130);
        Assert.Equal(0, c.At(hi, hj).A);
        Assert.True(c.At(0, 0).A > 40);
        var host = new Window { Content = view, Width = 480, Height = 270 };
        host.Show();
        try
        {
            using var shot = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(480, 270));
            view.Measure(new Size(480, 270));
            view.Arrange(new Rect(0, 0, 480, 270));
            shot.Render(view);
        }
        finally { host.Close(); }
    });
}
