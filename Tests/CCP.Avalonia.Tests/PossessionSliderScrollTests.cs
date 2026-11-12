using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionSliderScrollTests
{
    private sealed class Host(FakeFrames frames) : IAvaloniaPossessionHost
    {
        public IReadOnlyList<PossessionTarget> Targets { get; set; } = Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
        public Window? PossessionWindow => null;
        public Canvas? GhostLayer { get; } = new();
        public IFrameSource PossessionFrames => frames;
        public PossessionRuntime PossessionEffectRuntime { get; } = new(frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control element) => element is Slider ? new Rect(10, 20, 200, 40) : null;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new Release();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
        private sealed class Release : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public void CatalogOffersBothEffects()
    {
        var effects = PossessionAvaloniaHead.CreateEffects();
        Assert.Contains(effects, effect => effect is SliderCreepEffect);
        Assert.Contains(effects, effect => effect is ScrollHijackEffect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrollUndoRestoresHomeIncludingUserMovement(bool userScroll) => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames();
        var viewer = new ScrollViewer { Width = 200, Height = 100, Content = new Border { Height = 1000 } };
        var window = new Window { Content = viewer, Width = 200, Height = 100 };
        window.Show();
        try
        {
            window.UpdateLayout();
            viewer.Offset = new Vector(0, 100);
            var target = new PossessionTarget { Visual = viewer, Role = PossessionRole.Scroll, Key = "scroll" };
            string? named = null;
            var context = new PossessionContext { Host = new Host(frames), Attribution = new Attribution(), Rung = PossessionRung.Melt,
                Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, name) => named = name };
            var effect = new ScrollHijackEffect();
            Assert.True(effect.CanApply(context, target));
            effect.ApplyAsync(context, target, CancellationToken.None).GetAwaiter().GetResult();
            frames.Frame(TimeSpan.Zero);
            frames.Frame(TimeSpan.FromMilliseconds(600));
            Assert.Equal(140, viewer.Offset.Y, 2);
            if (userScroll) viewer.Offset = new Vector(0, 160);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Equal(userScroll ? 120 : 100, viewer.Offset.Y, 2);
            Assert.Equal(0, frames.Count);
            Assert.Equal("the page", named);
        }
        finally { window.Close(); }
    });
    [Theory]
    [InlineData(Orientation.Horizontal)]
    [InlineData(Orientation.Vertical)]
    public void SliderGhostCreepsWithoutWritingValueAndUndoRemovesIt(Orientation orientation) => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames();
        var host = new Host(frames);
        var first = new Slider { Minimum = 0, Maximum = 100, Value = 60, Width = 200, Height = 40, Orientation = orientation };
        var other = new Slider { Minimum = 0, Maximum = 100, Value = 30, Width = 200, Height = 40 };
        var window = new Window { Content = new StackPanel { Children = { first, other } } };
        window.Show();
        try
        {
            window.UpdateLayout();
            var a = new PossessionTarget { Visual = first, Role = PossessionRole.Slider, Key = "first" };
            var b = new PossessionTarget { Visual = other, Role = PossessionRole.Slider, Key = "other" };
            host.Targets = new[] { a, b };
            host.PossessionPointer.Press(default, first, DateTime.Now);
            var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Settle,
                Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, _) => { } };
            var effect = new SliderCreepEffect();
            Assert.False(effect.CanApply(context, b));
            Assert.True(effect.CanApply(context, a));
            int writes = 0;
            first.PropertyChanged += (_, e) => { if (e.Property == Slider.ValueProperty) writes++; };
            effect.ApplyAsync(context, a, CancellationToken.None).GetAwaiter().GetResult();
            var ghost = Assert.IsType<Border>(Assert.Single(host.GhostLayer!.Children));
            Assert.False(ghost.IsHitTestVisible);
            if (orientation == Orientation.Horizontal) Assert.Equal(122.8, Canvas.GetLeft(ghost), 1);
            else Assert.Equal(32.8, Canvas.GetTop(ghost), 1);
            frames.Frame(TimeSpan.Zero);
            frames.Frame(TimeSpan.FromMilliseconds(750));
            var move = Assert.IsType<TranslateTransform>(ghost.RenderTransform);
            Assert.True(orientation == Orientation.Vertical ? move.Y > 0 : move.X < 0);
            Assert.True(ghost.Opacity > 0);
            Assert.Equal(60, first.Value);
            Assert.Equal(0, writes);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Empty(host.GhostLayer.Children);
            Assert.Equal(0, frames.Count);
            Assert.Equal(60, first.Value);
            Assert.Equal(0, writes);
            host.PossessionPointer.Press(default, first, DateTime.Now - TimeSpan.FromSeconds(11));
            Assert.True(effect.CanApply(context, b));
        }
        finally { window.Close(); }
    });

    [Fact]
    public void UserScrollDuringDriftBecomesNewHome() => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames();
        var viewer = new ScrollViewer { Width = 200, Height = 100, Content = new Border { Height = 1000 } };
        var window = new Window { Content = viewer, Width = 200, Height = 100 };
        window.Show();
        try
        {
            window.UpdateLayout();
            viewer.Offset = new Vector(0, 100);
            var target = new PossessionTarget { Visual = viewer, Role = PossessionRole.Scroll, Key = "scroll" };
            var context = new PossessionContext { Host = new Host(frames), Attribution = new Attribution(), Rung = PossessionRung.Melt,
                Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, _) => { } };
            var effect = new ScrollHijackEffect();
            effect.ApplyAsync(context, target, CancellationToken.None).GetAwaiter().GetResult();
            frames.Frame(TimeSpan.Zero);
            frames.Frame(TimeSpan.FromMilliseconds(600));
            viewer.Offset = new Vector(0, 160);
            frames.Frame(TimeSpan.FromMilliseconds(900));
            Assert.Equal(140, viewer.Offset.Y, 2);
            frames.Frame(TimeSpan.FromMilliseconds(1200));
            Assert.Equal(120, viewer.Offset.Y, 2);
            Assert.Equal(0, frames.Count);
            viewer.Offset = new Vector(0, 145);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Equal(145, viewer.Offset.Y, 2);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SliderGhostFadesAndKeepsTheUsersNewValue(bool photosafe) => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames();
        var host = new Host(frames);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 60, Width = 200, Height = 40 };
        var window = new Window { Content = slider };
        window.Show();
        try
        {
            window.UpdateLayout();
            var target = new PossessionTarget { Visual = slider, Role = PossessionRole.Slider, Key = "slider" };
            host.Targets = new[] { target };
            var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Settle,
                Intensity = PossessionIntensity.Gentle, Photosafe = photosafe, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, _) => { } };
            var effect = new SliderCreepEffect();
            effect.ApplyAsync(context, target, CancellationToken.None).GetAwaiter().GetResult();
            var ghost = Assert.IsType<Border>(Assert.Single(host.GhostLayer!.Children));
            frames.Frame(TimeSpan.Zero);
            slider.Value = 75;
            frames.Frame(TimeSpan.FromMilliseconds(1750 * (photosafe ? 1.3 : 1)));
            Assert.True(Assert.IsType<TranslateTransform>(ghost.RenderTransform).X < 0);
            Assert.InRange(ghost.Opacity, 0.01, 0.59);
            frames.Frame(TimeSpan.FromMilliseconds(2000 * (photosafe ? 1.3 : 1)));
            Assert.Empty(host.GhostLayer.Children);
            Assert.Equal(0, frames.Count);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Equal(75, slider.Value);
        }
        finally { window.Close(); }
    });

    [Fact]
    public void SliderGhostDisappearsAfterItsAnimation() => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames();
        var host = new Host(frames);
        var slider = new Slider { Minimum = 0, Maximum = 100, Value = 60, Width = 200, Height = 40 };
        var window = new Window { Content = slider };
        window.Show();
        try
        {
            window.UpdateLayout();
            var target = new PossessionTarget { Visual = slider, Role = PossessionRole.Slider, Key = "slider" };
            var context = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Settle,
                Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, _) => { } };
            var effect = new SliderCreepEffect();
            effect.ApplyAsync(context, target, CancellationToken.None).GetAwaiter().GetResult();
            frames.Frame(TimeSpan.Zero);
            frames.Frame(TimeSpan.FromMilliseconds(2000));
            Assert.Empty(host.GhostLayer!.Children);
            Assert.Equal(0, frames.Count);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        }
        finally { window.Close(); }
    });

    [Fact]
    public void ScrollRequiresFortyPixelsOfRoom() => AvaloniaTestDispatcher.Run(() =>
    {
        var frames = new FakeFrames();
        var viewer = new ScrollViewer { Width = 200, Height = 100, Content = new Border { Height = 1000 } };
        var window = new Window { Content = viewer, Width = 200, Height = 100 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var limit = viewer.Extent.Height - viewer.Viewport.Height;
            viewer.Offset = new Vector(0, limit - 39);
            var target = new PossessionTarget { Visual = viewer, Role = PossessionRole.Scroll, Key = "scroll" };
            var context = new PossessionContext { Host = new Host(frames), Attribution = new Attribution(), Rung = PossessionRung.Melt,
                Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
                Remaining = TimeSpan.Zero, Name = (_, _) => { } };
            var effect = new ScrollHijackEffect();
            Assert.False(effect.CanApply(context, target));
            viewer.Offset = new Vector(0, limit - 40);
            Assert.True(effect.CanApply(context, target));
        }
        finally { window.Close(); }
    });

}
