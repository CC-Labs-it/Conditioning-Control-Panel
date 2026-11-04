using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>chaos-skia-fx: the stage-wide sparkle window (WPF ChaosSkiaFxOverlay). Refused without a
/// compositor; created and shown on the first effect; draws it; steps the Core field only while it is live
/// and parks the frame loop after 3 idle frames, hiding after the 8 s grace; the switch off creates nothing;
/// the panic surface and the run host's shutdown close it.</summary>
public sealed class ChaosSkiaFxOverlayTests
{
    private sealed class SteppedFrames : IFrameSource
    {
        public readonly List<Action<TimeSpan>> Subs = new();
        public TimeSpan Now;
        public void Subscribe(Action<TimeSpan> f) { if (!Subs.Contains(f)) Subs.Add(f); }
        public void Unsubscribe(Action<TimeSpan> f) => Subs.Remove(f);
        public void Step(int frames = 1)
        {
            for (int i = 0; i < frames; i++)
            {
                Now += TimeSpan.FromSeconds(1 / 60.0);
                foreach (var s in Subs.ToArray()) s(Now);
            }
        }
    }

    [Fact]
    public Task VideoRestackRaisesOnlyVisibleSparklesDuringPinnedRun() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var pin = settings.ChaosPinOnTop;
        var enabled = settings.ChaosSkiaFxEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        var skip = ChaosSkiaFxOverlay.SkipPlatformChecksForTests;
        var raise = ChaosSkiaFxOverlay.RaiseWindow;
        var calls = 0;
        try
        {
            settings.ChaosPinOnTop = true;
            settings.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.RaiseWindow = _ => calls++;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.RaiseSparklesAboveVideo(); // idle prewarm is hidden
            Assert.Equal(0, calls);
            ChaosSkiaFxOverlay.Burst(new Point(400, 300), Colors.Cyan);
            ChaosRunHost.RaiseSparklesAboveVideo(); // video shown
            ChaosRunHost.RaiseSparklesAboveVideo(); // video clicked
            Assert.Equal(2, calls);
            settings.ChaosPinOnTop = false;
            ChaosRunHost.RaiseSparklesAboveVideo();
            Assert.Equal(2, calls);
            ChaosRunHost.ForceShutdown();
            ChaosRunHost.RaiseSparklesAboveVideo();
            Assert.Equal(2, calls);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.RaiseWindow = raise;
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = skip;
            settings.ChaosPinOnTop = pin;
            settings.ChaosSkiaFxEnabled = enabled;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task VideoShowAndPointerPressScheduleRestacks() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var settings = CoreSettings.Current;
        var pin = settings.ChaosPinOnTop;
        var enabled = settings.ChaosSkiaFxEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        var skip = ChaosSkiaFxOverlay.SkipPlatformChecksForTests;
        var raise = ChaosSkiaFxOverlay.RaiseWindow;
        var schedule = MandatoryVideoOverlay.ScheduleRestack;
        var video = MandatoryVideoOverlay.Instance;
        var scheduler = video.Scheduler;
        // The show path creates its windows before checking LibVLC. Avoid a real decoder in this test.
        var shared = typeof(ConditioningControlPanel.Avalonia.Platform.LibVlcAudio).GetField("<Shared>k__BackingField", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var vlc = shared.GetValue(null);
        var pending = new List<(Action Work, TimeSpan Delay)>();
        var calls = 0;
        try
        {
            shared.SetValue(null, null);
            video.Scheduler = new ConditioningControlPanel.Services.MandatoryVideoScheduler(video);
            settings.ChaosPinOnTop = true;
            settings.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.RaiseWindow = _ => calls++;
            MandatoryVideoOverlay.ScheduleRestack = (work, delay) => pending.Add((work, delay));
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosSkiaFxOverlay.Burst(new Point(400, 300), Colors.Cyan);
            video.Show("unused.mp4", false);
            var window = Assert.Single(video.Windows);
            Assert.True(window.IsVisible);
            Assert.Equal(1, calls); // show raises immediately
            Assert.Single(pending);
            Assert.Equal(TimeSpan.FromMilliseconds(50), pending[0].Delay);
            pending[0].Work();
            pending.Clear();
            Assert.Equal(2, calls);

            window.MouseDown(new Point(20, 20), MouseButton.Left);
            window.MouseUp(new Point(20, 20), MouseButton.Left);
            Assert.Equal(2, calls); // activation must settle first
            Assert.Single(pending);
            Assert.Equal(TimeSpan.FromMilliseconds(50), pending[0].Delay);
            pending[0].Work();
            pending.Clear();
            Assert.Equal(3, calls);

            settings.ChaosPinOnTop = false;
            window.MouseDown(new Point(20, 20), MouseButton.Left);
            window.MouseUp(new Point(20, 20), MouseButton.Left);
            Assert.Single(pending);
            pending[0].Work();
            Assert.Equal(3, calls);
        }
        finally
        {
            video.CloseAll();
            video.Scheduler = scheduler;
            shared.SetValue(null, vlc);
            MandatoryVideoOverlay.ScheduleRestack = schedule;
            ChaosRunHost.ForceShutdown();
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.RaiseWindow = raise;
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = skip;
            settings.ChaosPinOnTop = pin;
            settings.ChaosSkiaFxEnabled = enabled;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task NativeRefusalDoesNotLeakAcrossSnapshots() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        var skip = ChaosSkiaFxOverlay.SkipPlatformChecksForTests;
        try
        {
            ChaosSkiaFxOverlay.CloseActive();
            CoreSettings.Current.ChaosSkiaFxEnabled = true;
            // Windows reports DWM composition even for headless windows. Reproduce that on every OS.
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            var snapshot = ProcessStateSnapshot.Take();
            ChaosSkiaFxOverlay.EnsureCreated(); // headless has no native click-through window
            Assert.Null(ChaosSkiaFxOverlay.Active);
            snapshot.Restore(); // equivalent to the next test's starting state
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.EnsureCreated();
            Assert.NotNull(ChaosSkiaFxOverlay.Active);
        }
        finally
        {
            ChaosSkiaFxOverlay.CloseActive();
            CoreSettings.Current.ChaosSkiaFxEnabled = enabled;
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = skip;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SparkleWindowLivesOnlyWhileEffectsAreLiveAndPanicClosesIt() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var savedEnabled = s.ChaosSkiaFxEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        var frames = new SteppedFrames();
        try
        {
            ChaosSkiaFxOverlay.CloseActive();
            s.ChaosSkiaFxEnabled = true;

            // No compositor: refused, nothing created (a stage-wide opaque window would lock the desktop).
            ChaosSkiaFxOverlay.Composited = () => false;
            ChaosSkiaFxOverlay.Burst(new Point(400, 300), Colors.Cyan);
            Assert.Null(ChaosSkiaFxOverlay.Active);

            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.FramesForTests = frames;

            // The switch off: nothing either.
            s.ChaosSkiaFxEnabled = false;
            ChaosSkiaFxOverlay.Burst(new Point(400, 300), Colors.Cyan);
            Assert.Null(ChaosSkiaFxOverlay.Active);
            s.ChaosSkiaFxEnabled = true;

            ChaosSkiaFxOverlay.Burst(new Point(400, 300), Colors.Cyan);
            var w = ChaosSkiaFxOverlay.Active!;
            Assert.NotNull(w);
            Assert.True(w.Topmost && !w.ShowInTaskbar && !w.ShowActivated && !w.IsHitTestVisible && !w.Focusable);
            Assert.True(w.IsVisible && w.IsRendering);
            Assert.Single(frames.Subs);
            Assert.InRange(w.Fx.ParticleCount, 20, 24);      // WPF burst at scale 1: flash + 12 ring + 7..11 shards

            // It draws: the burst's white-hot core lands where the px point maps into the window.
            frames.Step();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var frame = w.CaptureRenderedFrame()!;
            var k = frame.PixelSize.Width / w.Bounds.Width;   // frame px per DIP
            int x = (int)((400 - w.Position.X) / w.RenderScaling * k), y = (int)((300 - w.Position.Y) / w.RenderScaling * k);
            var px = new byte[4];
            using (var fb = frame.Lock())
                System.Runtime.InteropServices.Marshal.Copy(fb.Address + y * fb.RowBytes + x * 4, px, 0, 4);
            Assert.True(px[3] > 200, $"no sparkle drawn at the burst: alpha {px[3]}");

            // The field dies (shard life is random, 0.30-0.65 s, so a fixed frame count is a flake); the loop
            // renders every frame while particles live, then three idle frames later it parks, the grace is armed.
            for (int i = 0; i < 60 && w.Fx.ParticleCount > 0; i++)
            {
                Assert.True(w.IsRendering);
                frames.Step();
            }
            frames.Step(5);
            Assert.Equal(0, w.Fx.ParticleCount);
            Assert.False(w.IsRendering);
            Assert.Empty(frames.Subs);
            Assert.True(w.IsVisible && w.HidePending);
            w.HideTick();
            Assert.False(w.IsVisible);

            // The next effect shows the same window again and restarts the loop; an armed glow keeps it live.
            ChaosSkiaFxOverlay.ArmCursorGlow();
            ChaosSkiaFxOverlay.MoveCursorGlowToPx(100, 100);
            Assert.Same(w, ChaosSkiaFxOverlay.Active);
            Assert.True(w.IsVisible && w.IsRendering && !w.HidePending);
            frames.Step(300);
            Assert.True(w.IsRendering);
            ChaosSkiaFxOverlay.DisarmCursorGlow();
            frames.Step(60);
            Assert.False(w.IsRendering);

            // Panic: the registered surface closes it.
            ChaosSkiaFxOverlay.Ripple(new Point(200, 200), 300, 800, strong: true);
            Assert.True(w.IsRendering);
            PanicSurfaces.All.Single(x => x.Id == "chaos-skia-fx").Stop(null);
            Assert.Null(ChaosSkiaFxOverlay.Active);
            Assert.False(w.IsVisible);
            Assert.Empty(frames.Subs);

            // The run host's shutdown (panic "chaos", tray, exit) closes it even with no run.
            ChaosSkiaFxOverlay.TrailDot(new Point(200, 200), 0.4);
            Assert.NotNull(ChaosSkiaFxOverlay.Active);
            ChaosRunHost.ForceShutdown();
            Assert.Null(ChaosSkiaFxOverlay.Active);
        }
        finally
        {
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            ChaosSkiaFxOverlay.FramesForTests = null;
            s.ChaosSkiaFxEnabled = savedEnabled;
        }
        return Task.CompletedTask;
    });

    /// <summary>WPF ChaosModeService EndRun and CleanupAfterRun each close the field: the run's end (recap)
    /// and the recap's dismissal.</summary>
    [Fact]
    public Task RunEndAndRecapDismissCloseTheField() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var savedEnabled = s.ChaosSkiaFxEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        try
        {
            s.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.FramesForTests = new SteppedFrames();

            ChaosRunHost.StartRun(new ConditioningControlPanel.Services.Chaos.ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            var overlay = ChaosRunHost.Overlay!;
            overlay.FinishCountdown();
            var prewarmed = ChaosSkiaFxOverlay.Active;
            Assert.NotNull(prewarmed);
            Assert.False(prewarmed.IsVisible);
            Assert.False(prewarmed.IsRendering);
            Assert.Equal(0, prewarmed.Fx.ParticleCount);
            ChaosSkiaFxOverlay.TrailDot(new Point(200, 200), 0.4);
            Assert.Same(prewarmed, ChaosSkiaFxOverlay.Active);
            ChaosRunHost.RequestStop();                       // EndRun: the recap
            Assert.True(ChaosRunHost.IsActive);
            Assert.Null(ChaosSkiaFxOverlay.Active);

            ChaosSkiaFxOverlay.TrailDot(new Point(200, 200), 0.4);
            Assert.NotNull(ChaosSkiaFxOverlay.Active);
            overlay.Close();                                  // dismiss: OnOverlayClosed -> CleanupAfterRun
            Assert.False(ChaosRunHost.IsActive);
            Assert.Null(ChaosSkiaFxOverlay.Active);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            ChaosSkiaFxOverlay.FramesForTests = null;
            s.ChaosSkiaFxEnabled = savedEnabled;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ChaosPopEmitsOnceInPayloadOrSnapColour(bool live) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var enabled = CoreSettings.Current.ChaosSkiaFxEnabled;
        var composited = ChaosSkiaFxOverlay.Composited;
        var field = BubbleOverlay.Field;
        var held = field.HoldChaos;
        try
        {
            ChaosSkiaFxOverlay.CloseActive();
            CoreSettings.Current.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosSkiaFxOverlay.FramesForTests = new SteppedFrames();
            var b = new AmbientBubble
            {
                X = 150, Y = 150, Size = 100,
                Spec = new EffectBubbleSpec
                    { Tint = 0xFFFF0000, IsLive = live }
            };
            field.HoldChaos = true;
            BubbleOverlay.PopChaos(b);
            Assert.Null(ChaosSkiaFxOverlay.Active);
            Assert.False(b.Popping);
            field.HoldChaos = false;
            CoreSettings.Current.ChaosSkiaFxEnabled = false;
            BubbleOverlay.PopChaos(b);
            Assert.Null(ChaosSkiaFxOverlay.Active);
            b.Popping = false;
            CoreSettings.Current.ChaosSkiaFxEnabled = true;
            BubbleOverlay.PopChaos(b);
            var w = ChaosSkiaFxOverlay.Active;
            Assert.NotNull(w);
            var count = w.Fx.ParticleCount;
            Assert.InRange(count, 20, 24);
            using var surface = SkiaSharp.SKSurface.Create(new SkiaSharp.SKImageInfo(400, 400));
            w.Fx.Render(surface.Canvas, new SkiaSharp.SKRectI(0, 0, 400, 400), 1, TimeSpan.Zero);
            using var image = surface.Snapshot();
            using var bitmap = SkiaSharp.SKBitmap.FromImage(image);
            var center = bitmap.GetPixel(200, 200);
            Assert.True(center.Alpha > 200);
            var body = bitmap.GetPixel(225, 200);
            Assert.True(live ? body.Blue > body.Red : body.Red > body.Blue);
            BubbleOverlay.PopChaos(b);
            Assert.Equal(count, w.Fx.ParticleCount); // a duplicate pop must not emit twice
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.EnsureCreated();
            var idle = ChaosSkiaFxOverlay.Active;
            Assert.NotNull(idle);
            Assert.False(idle.IsVisible);
            Assert.False(idle.IsRendering);
            Assert.Equal(0, idle.Fx.ParticleCount);
        }
        finally
        {
            field.HoldChaos = held;
            ChaosSkiaFxOverlay.CloseActive();
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            ChaosSkiaFxOverlay.FramesForTests = null;
            CoreSettings.Current.ChaosSkiaFxEnabled = enabled;
        }
        return Task.CompletedTask;
    });
}
