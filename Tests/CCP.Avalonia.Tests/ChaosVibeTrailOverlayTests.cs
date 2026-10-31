using System.Linq;
using ConditioningControlPanel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The VibePopping trail window on a stepped clock and a played pointer: glow follows,
/// dots drop and fade like WPF, nothing ticks once hidden, panic closes it.</summary>
public sealed class ChaosVibeTrailOverlayTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task Delayed_video_restack_posts_once_to_the_ui_thread()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            int calls = 0;
            var fired = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MandatoryVideoOverlay.ScheduleTrailRaise(() =>
            {
                calls++;
                fired.TrySetResult(Dispatcher.UIThread.CheckAccess());
            }, System.TimeSpan.FromMilliseconds(1));
            Assert.Equal(0, calls);
            Assert.True(await fired.Task.WaitAsync(System.TimeSpan.FromSeconds(5)));
            Assert.Equal(1, calls);
        });
    }

    // WPF ChaosModeService.cs:455,2696-2709,2925-2946,3017-3028.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Toy_path_starts_buzz_lingers_once_and_stops_then_teardown_closes(bool pinAtStart)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            ChaosVibeTrailOverlay.SkipPlatformChecksForTests = true;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
            var meta = ChaosMeta.State;
            bool pinned = CoreSettings.Current.ChaosPinOnTop;
            var raise = ChaosVibeTrailOverlay.RaiseWindow;
            var schedule = MandatoryVideoOverlay.ScheduleTrailRaise;
            var kicks = new System.Collections.Generic.List<(System.Action Run, System.TimeSpan Delay)>();
            MandatoryVideoOverlay.ScheduleTrailRaise = (a, delay) => kicks.Add((a, delay));
            int raised = 0;
            ChaosVibeTrailOverlay.RaiseWindow = _ => raised++;
            bool equipped = meta.ActiveLifetimeBoons.Contains("vibe_popping");
            bool hadLevel = meta.LifetimeBoonLevels.TryGetValue("vibe_popping", out int level);
            try
            {
                meta.ActiveLifetimeBoons.Add("vibe_popping");
                meta.LifetimeBoonLevels["vibe_popping"] = 1;
                Assert.True(ChaosMeta.IsBoonActive("vibe_popping"));
                CoreSettings.Current.ChaosPinOnTop = pinAtStart;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                CoreSettings.Current.ChaosPinOnTop = !pinAtStart;   // change during countdown, WPF keeps the snapshot
                ChaosRunHost.Overlay!.FinishCountdown();
                CoreSettings.Current.ChaosPinOnTop = pinAtStart;
                ChaosRunHost.StopTimerForTests();
                Dispatcher.UIThread.RunJobs();
                var trail = ChaosVibeTrailOverlay.Active;
                Assert.NotNull(trail);
                Assert.Equal(pinAtStart, trail!.Topmost);
                Assert.False(trail.IsVisible);
                var state = ChaosRunHost.State!;
                state.ActiveToys.Clear();
                var toy = new ChaosToyState { Id = "vibe_popping", KeyLabel = "Q", CooldownSec = 20 };
                state.ActiveToys.Add(toy);
                state.ToyPower[toy.Id] = 1;
                state.AfterglowSec = 0.5;
                ChaosRunHost.OnToyKey("Q");
                Dispatcher.UIThread.RunJobs();
                Assert.Same(trail, ChaosVibeTrailOverlay.Active);
                Assert.True(trail.IsFollowing);
                CoreSettings.Current.ChaosPinOnTop = !pinAtStart;   // change again during the active run
                var video = new Window { Width = 200, Height = 200 };
                try
                {
                    MandatoryVideoOverlay.WireVibeTrailRestacking(video);
                    video.Show();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(pinAtStart ? 1 : 0, raised);
                    Assert.Equal(System.TimeSpan.FromMilliseconds(60), Assert.Single(kicks).Delay);
                    kicks[0].Run();
                    kicks.Clear();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(pinAtStart ? 2 : 0, raised);
                    video.RaiseEvent(new PointerPressedEventArgs(video,
                        new Pointer(1, PointerType.Mouse, true), video, new Point(50, 50), 0,
                        new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, 1));
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(System.TimeSpan.FromMilliseconds(50), Assert.Single(kicks).Delay);
                    kicks[0].Run();
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(pinAtStart ? 3 : 0, raised);
                    CoreSettings.Current.ChaosPinOnTop = pinAtStart;
                    ChaosRunHost.RaiseVibeTrailAboveVideo();
                    Assert.Equal(pinAtStart ? 4 : 0, raised);
                }
                finally { video.Close(); }
                Assert.True(BubbleOverlay.VibePopOn);
                Assert.False(BubbleOverlay.VibeHoverPops);
                for (int i = 0; i < 4; i++) ChaosRunHost.Tick();
                Assert.True(BubbleOverlay.VibeHoverPops);
                Assert.True(toy.IsEffectActive);
                ChaosRunHost.Tick();
                Assert.True(BubbleOverlay.VibePopOn);
                ChaosRunHost.Tick();
                Dispatcher.UIThread.RunJobs();
                Assert.False(BubbleOverlay.VibePopOn);
                Assert.False(trail.IsFollowing);
                Assert.False(trail.IsVisible);
                Assert.False(toy.IsEffectActive);
                toy.CooldownRemainingSec = 0;
                state.MaxedBoons.Add(toy.Id);
                ChaosRunHost.UseToyById(toy.Id);
                Dispatcher.UIThread.RunJobs();
                Assert.True(BubbleOverlay.VibeHoverPops);
                Assert.True(trail.IsFollowing);
                toy.CooldownRemainingSec = 0;
                ChaosRunHost.UseToyById(toy.Id);   // panic before any queued Start could run
                ChaosRunHost.ForceShutdown();
                Dispatcher.UIThread.RunJobs();
                Assert.Null(ChaosVibeTrailOverlay.Active);
                Assert.False(BubbleOverlay.VibePopOn);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ChaosVibeTrailOverlay.SkipPlatformChecksForTests = false;
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = false;
                CoreSettings.Current.ChaosPinOnTop = pinned;
                ChaosVibeTrailOverlay.RaiseWindow = raise;
                MandatoryVideoOverlay.ScheduleTrailRaise = schedule;
                if (!equipped) meta.ActiveLifetimeBoons.Remove("vibe_popping");
                if (hadLevel) meta.LifetimeBoonLevels["vibe_popping"] = level;
                else meta.LifetimeBoonLevels.Remove("vibe_popping");
            }
            return Task.CompletedTask;
        });
    }

    // WPF BubbleService.cs:263-270,4329-4341: held sweep, hover, freeze exclusion and pause guard.
    [Fact]
    public async Task Sweep_pops_treats_snaps_lives_and_ignores_freeze_ambient_and_paused()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var host = new Window();
            int popped = 0, snapped = 0;
            try
            {
                BubbleOverlay.BeginChaos(host, _ => popped++, _ => snapped++);
                var treat = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { SizePx = 100 });
                var live = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { SizePx = 100, IsLive = true, FuseMs = 5000 });
                var freeze = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { SizePx = 100, IsFreeze = true });
                var ambient = new AmbientBubble { Size = 100 };
                BubbleOverlay.Field.Bubbles.Add(ambient);
                foreach (var b in BubbleOverlay.Field.Bubbles) { b.X = 100; b.Y = 100; }
                var at = new PixelPoint(150, 150);
                BubbleOverlay.SetVibePop(true);
                BubbleOverlay.SweepVibe(at, false);
                Assert.False(treat.Popping);
                BubbleOverlay.HoldChaos(true);
                BubbleOverlay.SweepVibe(at, true);
                Assert.False(live.Popping);
                BubbleOverlay.HoldChaos(false);
                BubbleOverlay.SweepVibe(new PixelPoint(201, 150), true);
                Assert.False(treat.Popping);
                BubbleOverlay.SweepVibe(at, true);
                Assert.Equal((1, 1), (popped, snapped));
                Assert.False(live.Deflating);
                double before = live.Scale;
                live.Step();
                Assert.True(live.Scale > before);
                Assert.False(live.DefusedViaChannel);
                Assert.False(freeze.Popping);
                Assert.False(ambient.Popping);
                BubbleOverlay.SweepVibe(at, true);
                Assert.Equal((1, 1), (popped, snapped));
                var hover = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { SizePx = 100 });
                hover.X = hover.Y = 100;
                BubbleOverlay.SetVibePop(true, hoverPops: true);
                BubbleOverlay.SweepVibe(at, false);
                Assert.True(hover.Popping);
                Assert.Equal(2, popped);
            }
            finally { BubbleOverlay.EndChaos(); BubbleOverlay.Field.Bubbles.Clear(); host.Close(); }
            return Task.CompletedTask;
        });
    }

    // WPF BubbleService.cs:1616-1617,3286,4338: Silk Touch reaches beyond the sprite.
    [Fact]
    public async Task Sweep_uses_spawned_hit_radius_for_live_magnet_and_treats()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var host = new Window();
            int popped = 0, snapped = 0;
            double hitScale = 1.25;
            bool magnet = true;
            try
            {
                BubbleOverlay.BeginChaos(host, _ => popped++, _ => snapped++,
                    hitboxScale: () => hitScale, liveMagnet: () => magnet);
                var treat = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { SizePx = 100 });
                var live = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { SizePx = 100, IsLive = true, FuseMs = 5000 });
                treat.X = treat.Y = live.X = live.Y = 100;
                hitScale = 1; magnet = false;   // WPF samples modifiers once at birth.
                Assert.Equal(62.5, treat.HitRadius);
                Assert.Equal(87.5, live.HitRadius);
                var region = new BubbleOverlayWindow(0, new PixelRect(0, 0, 1280, 720), 1);
                try
                {
                    region.Sync();
                    Assert.Equal(new PixelRect(62, 62, 176, 176), region.Rects[1]);
                }
                finally { region.Close(); }
                BubbleOverlay.SetVibePop(true);
                BubbleOverlay.SweepVibe(new PixelPoint(238, 150), true);
                Assert.False(live.Popping);
                BubbleOverlay.SweepVibe(new PixelPoint(225, 150), true);
                Assert.Equal((0, 1), (popped, snapped));
                Assert.False(treat.Popping);
                BubbleOverlay.SweepVibe(new PixelPoint(210, 150), true);
                Assert.Equal((1, 1), (popped, snapped));
            }
            finally { BubbleOverlay.EndChaos(); BubbleOverlay.Field.Bubbles.Clear(); host.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Glow_follows_the_pointer_dots_fade_and_hidden_stops_the_tick()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var w = new ChaosVibeTrailOverlay();
            try
            {
                long now = 5000;
                var at = new PixelPoint(200, 150);
                w.NowMs = () => now;
                w.ReadPointer = () => at;
                w.Position = new PixelPoint(0, 0);
                w.Show();
                w.BeginFollow();
                Assert.True(w.IsFollowing);

                var shapes = ((Canvas)w.Content!).Children.OfType<Ellipse>().ToList();
                var glow = shapes[^1];
                var dots = shapes.Take(ChaosVibeTrail.Dots).ToList();
                double s = w.RenderScaling;
                Assert.Equal(1, glow.Opacity);
                Assert.Equal(200 / s - 29, Canvas.GetLeft(glow), 3);
                Assert.Single(dots, d => d.Opacity > 0);   // the render sample was cleared; one dot at the snap

                now += 170; at = new PixelPoint(260, 150);
                w.Tick();
                Assert.Equal(260 / s - 29, Canvas.GetLeft(glow), 3);
                var lit = dots.Select(d => d.Opacity).Where(o => o > 0).OrderBy(o => o).ToList();
                Assert.Equal(2, lit.Count);
                Assert.Equal(0.25, lit[0], 3);             // the snap dot, half its 340 ms life
                Assert.Equal(0.5, lit[1], 3);              // the new one, 60 DIPs on

                now += 1000;
                w.Tick();
                Assert.All(dots, d => Assert.Equal(0, d.Opacity));

                w.EndFollow();
                Assert.False(w.IsVisible);
                Assert.False(w.IsFollowing);
                Assert.Equal(0, glow.Opacity);
                at = new PixelPoint(900, 900);
                w.Tick();                                  // hidden: no work (P01)
                Assert.Equal(260 / s - 29, Canvas.GetLeft(glow), 3);
            }
            finally { w.Close(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Panic_closes_the_trail_window()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosVibeTrailOverlay.EnsureCreated();
                Dispatcher.UIThread.RunJobs();
                var w = ChaosVibeTrailOverlay.Active;
                Assert.NotNull(w);
                w!.Show();
                PanicSurfaces.All.Single(x => x.Id == "chaos-vibe-trail").Stop(null);
                Assert.Null(ChaosVibeTrailOverlay.Active);
                Assert.False(w.IsVisible);
            }
            finally { ChaosVibeTrailOverlay.CloseActive(); }
            return Task.CompletedTask;
        });
    }
}
