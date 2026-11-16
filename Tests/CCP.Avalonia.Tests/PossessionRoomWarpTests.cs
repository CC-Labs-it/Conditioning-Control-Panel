using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionRoomWarpTests
{
    private sealed class Host : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow { get; init; }
        public Canvas? GhostLayer { get; init; }
        public FakeFrames Frames { get; } = new();
        public IFrameSource PossessionFrames => Frames;
        public PossessionRuntime PossessionEffectRuntime => new(Frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control element) => null;
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new CancellationTokenSource();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }
    private static (double angle, double sag, double wash) Run(bool photosafe, PossessionRung rung, bool early = false)
    {
        var canvas = new Grid { Width = 800, Height = 600 };
        var layer = new Canvas { Width = 800, Height = 600 };
        var window = new Window { Width = 800, Height = 600, Content = new Viewbox { Child = canvas } };
        var host = new Host { PossessionWindow = window, GhostLayer = layer };
        var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = rung,
            Intensity = PossessionIntensity.Eerie, Photosafe = photosafe, Rng = new Random(7),
            ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try
        {
            var effect = new RoomWarpEffect();
            Assert.True(effect.CanApply(context, null));
            effect.ApplyAsync(context, null, CancellationToken.None).GetAwaiter().GetResult();
            Assert.Equal(1, host.Frames.Count);
            host.Frames.Frame(TimeSpan.Zero);
            host.Frames.Frame(TimeSpan.FromMilliseconds(early ? 225 : 900));
            var group = Assert.IsType<TransformGroup>(canvas.RenderTransform);
            var angle = group.Children.OfType<RotateTransform>().Single().Angle;
            var sag = group.Children.OfType<TranslateTransform>().Single().Y;
            var wash = Assert.IsType<Border>(Assert.Single(layer.Children)).Opacity;
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Equal(Matrix.Identity, canvas.RenderTransform?.Value ?? Matrix.Identity);
            Assert.Empty(layer.Children);
            Assert.Equal(0, host.Frames.Count);
            Assert.False(effect.IsLive);
            return (angle, sag, wash);
        }
        finally { window.Close(); }
    }

    [Fact]
    public void RoomLeanAndWashRestoreAndPhotosafeHalvesAmplitude() => AvaloniaTestDispatcher.Run(() =>
    {
        var normal = Run(false, PossessionRung.Collapse);
        var safe = Run(true, PossessionRung.Collapse);
        Assert.InRange(Math.Abs(normal.angle), 0.5, 1.0);
        Assert.Equal(4, normal.sag);
        Assert.Equal(0.04, normal.wash, 6);
        Assert.Equal(normal.angle / 2, safe.angle, 6);
        Assert.Equal(normal.sag / 2, safe.sag, 6);
        Assert.Equal(normal.wash / 2, safe.wash, 6);
        Assert.Equal(0.08, Run(false, PossessionRung.ItKnows).wash, 6);
    });

    [Fact]
    public void UndoDuringLeanUnsubscribesAndRestores() => AvaloniaTestDispatcher.Run(() =>
    {
        var midway = Run(false, PossessionRung.Collapse, early: true);
        Assert.Equal(0.25, midway.sag, 6);
        Assert.Equal(0.0025, midway.wash, 6);
    });

    [Theory]
    [InlineData("finish")]
    [InlineData("panic")]
    [InlineData("director")]
    [InlineData("detach")]
    public Task ReassemblyEasesHomeAndStopsStillEndWithinTheReturnBound(string stop) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var canvas = new Grid { Width = 800, Height = 600 };
        var layer = new Canvas { Width = 800, Height = 600 };
        var window = new Window { Width = 800, Height = 600, Content = new Viewbox { Child = canvas } };
        var host = new Host { PossessionWindow = window, GhostLayer = layer };
        var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Collapse,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(7),
            ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new RoomWarpEffect();
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, Array.Empty<IPossessionEffect>(), PossessionAvaloniaHead.CreateDirectorHead());
        director.AttachHost(host);
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        try
        {
            Assert.True(effect.CanApply(context, null));
            await effect.ApplyAsync(context, null, CancellationToken.None);
            host.Frames.Frame(TimeSpan.Zero);
            host.Frames.Frame(TimeSpan.FromMilliseconds(900));
            var group = Assert.IsType<TransformGroup>(canvas.RenderTransform);
            var sag = group.Children.OfType<TranslateTransform>().Single();
            var rotate = group.Children.OfType<RotateTransform>().Single();
            var angle = rotate.Angle;
            var wash = Assert.IsType<Border>(Assert.Single(layer.Children));
            Assert.Equal(4, sag.Y, 6);
            var returning = effect.UndoAsync(TimeSpan.FromMilliseconds(600));
            Assert.False(returning.IsCompleted);
            host.Frames.Frame(TimeSpan.FromMilliseconds(1000));
            host.Frames.Frame(TimeSpan.FromMilliseconds(1300));
            Assert.Equal(2, sag.Y, 6);
            Assert.Equal(angle / 2, rotate.Angle, 6);
            Assert.Equal(0.02, wash.Opacity, 6);
            if (stop != "finish")
            {
                // WPF PossessionEffectBase.cs:195 and RoomWarpEffect.cs:107-140: a released return is not cut short.
                if (stop == "panic") PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
                else if (stop == "director") director.UndoAll();
                else director.DetachHost(host);
                Assert.False(returning.IsCompleted);
                Assert.Same(wash, Assert.Single(layer.Children));
                Assert.Equal(2, sag.Y, 6);
                Assert.Equal(1, host.Frames.Count);
                // With no more frames, the wall-clock bound (600 + 100 ms) still ends the return.
                await returning.WaitAsync(TimeSpan.FromSeconds(3));
            }
            else
                host.Frames.Frame(TimeSpan.FromMilliseconds(1600));
            await returning;
            Assert.Empty(layer.Children);
            Assert.Equal(Matrix.Identity, canvas.RenderTransform?.Value ?? Matrix.Identity);
            Assert.Equal(0, host.Frames.Count);
            Assert.False(effect.IsLive);
            host.Frames.Frame(TimeSpan.FromMilliseconds(2000));
            Assert.Empty(layer.Children);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

    [Fact]
    public void CatalogOffersRoomWarp() => Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "roomwarp");
}
