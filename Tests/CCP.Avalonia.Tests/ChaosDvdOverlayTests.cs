using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using IFrameSource = ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource;

namespace CCP.Avalonia.Tests;

/// <summary>chaos-dvd-overlay: logos fly on one click-through host window, stepped by the host's frame
/// source (no timer of their own); the panic registry closes it; no compositor = no window.</summary>
public sealed class ChaosDvdOverlayTests
{
    private sealed class SteppedFrames : IFrameSource
    {
        public readonly List<Action<TimeSpan>> Subs = new();
        public void Subscribe(Action<TimeSpan> f) { if (!Subs.Contains(f)) Subs.Add(f); }
        public void Unsubscribe(Action<TimeSpan> f) => Subs.Remove(f);
        public void Frame(double sec) { foreach (var s in Subs.ToArray()) s(TimeSpan.FromSeconds(sec)); }
    }

    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public async Task LogosFlyOnTheHostFramesAndPanicClosesTheWindow()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var previousShared = CoreSettings.Current.ChaosDvdSharedHost;
            Setup();
            var (can, prep, frames, sfx) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx);
            var src = new SteppedFrames();
            var host = new Window();
            host.Show();
            try
            {
                CoreSettings.Current.ChaosDvdSharedHost = true;
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.FrameSourceFor = _ => src;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                ChaosDvdOverlay.Launch(host, 10, 1, 1, count: 2);

                var w = Assert.IsType<Window>(ChaosDvdOverlay.Window);
                Assert.False(w.IsHitTestVisible);
                var canvas = Assert.IsType<Canvas>(w.Content);
                Assert.Equal(2, canvas.Children.Count);
                Assert.Single(src.Subs);
                var label = canvas.Children[0];
                var before = (Canvas.GetLeft(label), Canvas.GetTop(label));
                src.Frame(1.000);
                src.Frame(1.050);
                Assert.NotEqual(before, (Canvas.GetLeft(label), Canvas.GetTop(label)));
                Assert.InRange(label.Opacity, 0.01, 0.85);

                PanicSurfaces.All.Single(s => s.Id == "chaos-dvd").Stop(null);
                Assert.Null(ChaosDvdOverlay.Window);
                Assert.False(w.IsVisible);
                Assert.False(ChaosDvdOverlay.AnyActive);
                Assert.Empty(src.Subs);
            }
            finally
            {
                ChaosDvdOverlay.CloseActive();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx) = (can, prep, frames, sfx);
                host.Close();
                CoreSettings.Current.ChaosDvdSharedHost = previousShared;
            }
            Assert.Equal(previousShared, CoreSettings.Current.ChaosDvdSharedHost);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastLogoRetiringDropsTheFrameSubscriptionAndARefusedWindowStaysNull(bool shared)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var previousShared = CoreSettings.Current.ChaosDvdSharedHost;
            Setup();
            var (can, prep, frames, sfx) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx);
            var src = new SteppedFrames();
            var host = new Window();
            host.Show();
            try
            {
                CoreSettings.Current.ChaosDvdSharedHost = shared;
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.FrameSourceFor = _ => src;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };

                ChaosDvdOverlay.PrepareWindow = (_, _) => false;   // the platform refuses click-through/override-redirect
                ChaosDvdOverlay.Launch(host, 1, 1, 1);
                Assert.Null(ChaosDvdOverlay.Window);
                Assert.False(ChaosDvdOverlay.AnyActive);
                Assert.Empty(src.Subs);

                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.Launch(host, 1, 1, 1);   // 1 s flight + 240 ms fade
                Assert.Single(src.Subs);
                for (var t = 0.0; t <= 2.0 && src.Subs.Count > 0; t += 0.05) src.Frame(t);
                Assert.False(ChaosDvdOverlay.AnyActive);
                Assert.Empty(src.Subs);
                if (shared) Assert.Empty(Assert.IsType<Canvas>(ChaosDvdOverlay.Window!.Content).Children);
                else Assert.False(ChaosDvdOverlay.Window!.IsVisible);
            }
            finally
            {
                ChaosDvdOverlay.CloseActive();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx) = (can, prep, frames, sfx);
                host.Close();
                CoreSettings.Current.ChaosDvdSharedHost = previousShared;
            }
            Assert.Equal(previousShared, CoreSettings.Current.ChaosDvdSharedHost);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task NoCompositorMeansNoWindowAndNoLogo()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var previousShared = CoreSettings.Current.ChaosDvdSharedHost;
            Setup();
            var (can, prep) = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow);
            var host = new Window();
            host.Show();
            try
            {
                CoreSettings.Current.ChaosDvdSharedHost = true;
                ChaosDvdOverlay.CanOverlay = () => false;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.Launch(host, 10, 1, 1);
                Assert.Null(ChaosDvdOverlay.Window);
                Assert.False(ChaosDvdOverlay.AnyActive);
            }
            finally
            {
                ChaosDvdOverlay.CloseActive();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow) = (can, prep);
                host.Close();
                CoreSettings.Current.ChaosDvdSharedHost = previousShared;
            }
            Assert.Equal(previousShared, CoreSettings.Current.ChaosDvdSharedHost);
            return Task.CompletedTask;
        });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StageSpansConfiguredMonitorsButLogosStayInPrimaryWorkArea(bool dual)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.DualMonitorEnabled, CoreSettings.Current.ChaosDvdSharedHost);
            try
            {
                CoreSettings.Current.ChaosDvdSharedHost = true;
                Setup();
                var primary = MakeScreen(new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 30, 1920, 1010), true);
                var secondary = MakeScreen(new PixelRect(-1280, -100, 1280, 1024), new PixelRect(-1280, -100, 1280, 1000), false);
                ConditioningControlPanel.CoreSettings.Current.DualMonitorEnabled = dual;
                ConditioningControlPanel.Avalonia.Views.Features.ScreenList.QueryOverride = _ => new[] { secondary, primary };
                ChaosDvdOverlay.CanOverlay = () => true;
                PixelRect placed = default;
                ChaosDvdOverlay.PrepareWindow = (_, b) => { placed = b; return true; };
                ChaosDvdOverlay.FrameSourceFor = _ => new SteppedFrames();
                var host = new Window();
                try
                {
                    ChaosDvdOverlay.Launch(host, 1, 1, 1);
                    Assert.Equal(dual ? new PixelRect(-1280, -100, 3200, 1180) : primary.Bounds, placed);
                    Assert.Equal(dual ? 640 : 0, ChaosDvdOverlay.Flock.Left);
                    Assert.Equal(dual ? 65 : 15, ChaosDvdOverlay.Flock.Top);
                    Assert.Equal(960, ChaosDvdOverlay.Flock.Right - ChaosDvdOverlay.Flock.Left);
                    Assert.Equal(505, ChaosDvdOverlay.Flock.Bottom - ChaosDvdOverlay.Flock.Top);
                    var window = ChaosDvdOverlay.Window!;
                    Assert.Equal(placed.Width / 2.0, window.Width);
                    Assert.Equal(placed.Height / 2.0, window.Height);
                    ChaosDvdOverlay.Advance(2);
                    ChaosDvdOverlay.Advance(0.3);
                    Assert.Same(window, ChaosDvdOverlay.Window); // WPF keep-alive until teardown
                }
                finally { ChaosDvdOverlay.CloseActive(); host.Close(); }
            }
            finally { (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.DualMonitorEnabled, CoreSettings.Current.ChaosDvdSharedHost) = saved; }
            Assert.Equal(saved, (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.DualMonitorEnabled, CoreSettings.Current.ChaosDvdSharedHost));
            return Task.CompletedTask;
        });
    }

    private static global::Avalonia.Platform.Screen MakeScreen(PixelRect bounds, PixelRect work, bool primary)
    {
        var screen = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(bounds.X), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(screen, 2.0);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(screen, bounds);
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(screen, work);
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(screen, primary);
        return screen;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HostChoiceIsLatchedAndWindowsAreReusedUntilTeardown(bool shared)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.DualMonitorEnabled, CoreSettings.Current.ChaosDvdSharedHost);
            try
            {
                CoreSettings.Current.ChaosDvdSharedHost = true;
                Setup();
                ConditioningControlPanel.CoreSettings.Current.ChaosDvdSharedHost = shared;
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.FrameSourceFor = _ => new SteppedFrames();
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                var host = new Window(); host.Show();
                try
                {
                    ChaosDvdOverlay.Launch(host, 1, 1, 1, count: 2);
                    var windows = ChaosDvdOverlay.Windows.ToArray();
                    Assert.Equal(shared ? 1 : 2, windows.Length);
                    Assert.All(windows, w => Assert.True(w.IsVisible));
                    Assert.All(windows, w => Assert.Equal(shared, w.Content is Canvas));
                    ChaosDvdOverlay.Advance(2);
                    ChaosDvdOverlay.Advance(0.3);
                    Assert.All(windows, w => Assert.Equal(shared, w.IsVisible));
                    ConditioningControlPanel.CoreSettings.Current.ChaosDvdSharedHost = !shared;
                    ChaosDvdOverlay.Launch(host, 1, 1, 1, count: 2);
                    Assert.Equal(windows.Length, ChaosDvdOverlay.Windows.Count);
                    Assert.All(ChaosDvdOverlay.Windows, w => Assert.Contains(w, windows));
                    ChaosDvdOverlay.CloseActive();
                    Assert.All(windows, w => Assert.False(w.IsVisible));
                    Assert.Empty(ChaosDvdOverlay.Windows);
                }
                finally { ChaosDvdOverlay.CloseActive(); host.Close(); }
            }
            finally { (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.DualMonitorEnabled, CoreSettings.Current.ChaosDvdSharedHost) = saved; }
            Assert.Equal(saved, (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.DualMonitorEnabled, CoreSettings.Current.ChaosDvdSharedHost));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PerLogoMotionHandsFramesToAVisibleWindowWhenTheDriverRetires()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.DualMonitorEnabled, CoreSettings.Current.ChaosDvdSharedHost);
            try
            {
                CoreSettings.Current.ChaosDvdSharedHost = true;
                Setup();
                ConditioningControlPanel.CoreSettings.Current.ChaosDvdSharedHost = false;
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                var sources = new Dictionary<TopLevel, SteppedFrames>();
                ChaosDvdOverlay.FrameSourceFor = w => sources.TryGetValue(w, out var source) ? source : sources[w] = new SteppedFrames();
                var host = new Window(); host.Show();
                try
                {
                    ChaosDvdOverlay.Launch(host, 10, 1, 1, count: 2);
                    var first = ChaosDvdOverlay.Window!;
                    var before = first.Position;
                    sources[first].Frame(0);
                    sources[first].Frame(0.05);
                    Assert.NotEqual(before, first.Position);
                    ChaosDvdOverlay.Flock.Active[0].RemainingSec = 0;
                    ChaosDvdOverlay.Advance(0.1);
                    ChaosDvdOverlay.Advance(0.3);
                    Assert.False(first.IsVisible);
                    Assert.Empty(sources[first].Subs);
                    var next = ChaosDvdOverlay.Window!;
                    Assert.NotSame(first, next);
                    Assert.True(next.IsVisible);
                    Assert.Single(sources[next].Subs);
                    before = next.Position;
                    sources[next].Frame(1);
                    sources[next].Frame(1.05);
                    Assert.NotEqual(before, next.Position);
                }
                finally { ChaosDvdOverlay.CloseActive(); host.Close(); }
            }
            finally { (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.DualMonitorEnabled, CoreSettings.Current.ChaosDvdSharedHost) = saved; }
            Assert.Equal(saved, (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor, ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.DualMonitorEnabled, CoreSettings.Current.ChaosDvdSharedHost));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FlyingThoughtContactsGlobalBubbleBoxesAndSplitsWithoutEatingTheRabbit(bool shared)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor,
                ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.ChaosDvdSharedHost, CoreSettings.Current.DualMonitorEnabled);
            var host = new Window();
            try
            {
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.FrameSourceFor = _ => new SteppedFrames();
                var cues = new List<(string, float)>();
                ChaosDvdOverlay.PlaySfx = (k, v) => cues.Add((k, v));
                CoreSettings.Current.ChaosDvdSharedHost = shared;
                CoreSettings.Current.DualMonitorEnabled = false;
                ScreenList.QueryOverride = _ => new[] { MakeScreen(new PixelRect(-1000, -200, 1920, 1080), new PixelRect(-1000, -200, 1920, 1080), true) };
                int treats = 0, snaps = 0;
                BubbleOverlay.BeginChaos(host, _ => treats++, _ => snaps++);
                ChaosDvdOverlay.Launch(host, 3, 1.3, 0.8, text: "thought", splitOnRabbit: true);
                var logo = Assert.Single(ChaosDvdOverlay.Flock.Active);
                (logo.X, logo.Y, logo.Vx, logo.Vy) = (300, 200, 0, 0);
                // Stage origin / primary scale = (-500, -100), global position = (-200, 100).
                AmbientBubble Add(EffectBubbleSpec? spec, double x = -200)
                {
                    var b = new AmbientBubble { X = x, Y = 100, Size = 40, Spec = spec };
                    BubbleOverlay.Field.Bubbles.Add(b);
                    return b;
                }
                var treat = Add(new EffectBubbleSpec());
                var live = Add(new EffectBubbleSpec { IsLive = true });
                var rabbit = Add(new EffectBubbleSpec { IsDarter = true });
                var freeze = Add(new EffectBubbleSpec { IsFreeze = true });
                var ambient = Add(null);
                var outside = Add(new EffectBubbleSpec(), 1000);
                BubbleOverlay.HoldChaos(true);
                ChaosDvdOverlay.Advance(0.1);
                Assert.False(treat.Popping);
                Assert.False(live.Popping);
                BubbleOverlay.HoldChaos(false);
                // Rabbit splitting is independent of the pop pause lock in WPF.
                Assert.Equal(2, ChaosDvdOverlay.Flock.Active.Count);
                Assert.Contains(("dvd_bounce", 0.4f), cues);
                ChaosDvdOverlay.Advance(0.1);
                Assert.True(treat.Popping);
                Assert.True(live.Deflating);
                Assert.Equal((1, 1), (treats, snaps));
                Assert.All(new[] { rabbit, freeze, ambient, outside }, b => Assert.False(b.Popping));
                Assert.Equal(shared ? 1 : 3, ChaosDvdOverlay.Windows.Count); // clone can split next frame
                ChaosDvdOverlay.Advance(0.1);
                Assert.Equal((1, 1), (treats, snaps));
            }
            finally
            {
                ChaosDvdOverlay.CloseActive(); BubbleOverlay.EndChaos(); BubbleOverlay.Field.Bubbles.Clear(); host.Close();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.FrameSourceFor,
                    ChaosDvdOverlay.PlaySfx, ScreenList.QueryOverride, CoreSettings.Current.ChaosDvdSharedHost, CoreSettings.Current.DualMonitorEnabled) = saved;
            }
            return Task.CompletedTask;
        });
    }
}
