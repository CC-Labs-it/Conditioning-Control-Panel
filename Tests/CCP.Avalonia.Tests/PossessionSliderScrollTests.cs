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
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
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
        public IDisposable Possess(object target) { Live++; return new Release(this); }
        public void EdgePulse(double strength) { }
        public int Live { get; private set; }
        public bool AnyPossessed => Live != 0;
        public void ReleaseAll() { }
        private sealed class Release(Attribution owner) : IDisposable { public void Dispose() => owner.Live--; }
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
            _ = effect.ApplyAsync(context, target, CancellationToken.None);
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
            _ = effect.ApplyAsync(context, a, CancellationToken.None);
            var ghost = Assert.IsType<Border>(Assert.Single(host.GhostLayer!.Children));
            Assert.False(ghost.IsHitTestVisible);
            Assert.Equal(14, ghost.BoxShadow[0].Blur);
            Assert.Equal(Color.Parse("#D9FF8A5C"), ghost.BoxShadow[0].Color);
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
    public Task UserScrollDuringDriftBecomesNewHome() => AvaloniaTestDispatcher.RunAsync(async () =>
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
            _ = effect.ApplyAsync(context, target, CancellationToken.None);
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
            var pending = effect.ApplyAsync(context, target, CancellationToken.None);
            await Task.Delay(1700);
            Assert.True(pending.IsCompleted);
            Assert.Equal(0, frames.Count);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
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
            _ = effect.ApplyAsync(context, target, CancellationToken.None);
            var ghost = Assert.IsType<Border>(Assert.Single(host.GhostLayer!.Children));
            frames.Frame(TimeSpan.Zero);
            slider.Value = 75;
            frames.Frame(TimeSpan.FromMilliseconds(1750 * (photosafe ? 1.3 : 1)));
            Assert.True(Assert.IsType<TranslateTransform>(ghost.RenderTransform).X < 0);
            Assert.InRange(ghost.Opacity, 0.01, 0.59);
            frames.Frame(TimeSpan.FromMilliseconds(2040 * (photosafe ? 1.3 : 1)));
            Assert.Empty(host.GhostLayer.Children);
            Assert.Equal(0, frames.Count);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Equal(75, slider.Value);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task SliderGhostDisappearsAfterItsAnimation() => AvaloniaTestDispatcher.RunAsync(async () =>
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
            _ = effect.ApplyAsync(context, target, CancellationToken.None);
            frames.Frame(TimeSpan.Zero);
            frames.Frame(TimeSpan.FromMilliseconds(2040));
            Assert.Empty(host.GhostLayer!.Children);
            Assert.Equal(0, frames.Count);
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            var pending = effect.ApplyAsync(context, target, CancellationToken.None);
            await Task.Delay(2800);
            Assert.True(pending.IsCompleted);
            Assert.Empty(host.GhostLayer.Children);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DirectorHoldsAfterAnimationAndUndoCancels(bool scroll) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var frames = new FakeFrames();
        var host = new Host(frames);
        Control control = scroll ? new ScrollViewer { Width = 200, Height = 100, Content = new Border { Height = 1000 } }
            : new Slider { Width = 200, Height = 40, Minimum = 0, Maximum = 100, Value = 60 };
        var window = new Window { Content = control, Width = 250, Height = 150 };
        window.Show();
        using var lockdown = new LockdownService();
        var attribution = new Attribution();
        IPossessionEffect effect = scroll ? new ScrollHijackEffect() : new SliderCreepEffect();
        var head = new PossessionDirectorHead { Settings = () => new AppSettings { LockdownPossessionEnabled = true,
            LockdownPossessionIntensity = (int)PossessionIntensity.Eerie }, Attribution = _ => attribution };
        using var director = new PossessionDirector(lockdown, new[] { effect }, head);
        try
        {
            window.UpdateLayout();
            var target = new PossessionTarget { Visual = control, Role = scroll ? PossessionRole.Scroll : PossessionRole.Slider, Key = "target" };
            host.Targets = new[] { target };
            director.AttachHost(host);
            director.OnLockdownActivated();
            if (scroll) director.ApplyRestartReset(PossessionRung.Melt);
            director.RequestReactive(effect.Id, target);
            Assert.True(target.IsLive);
            Assert.True(attribution.AnyPossessed);
            frames.Frame(TimeSpan.Zero);
            await Task.Delay(900);
            frames.Frame(TimeSpan.FromMilliseconds(scroll ? 1200 : 2040));
            await Task.Delay(2400);
            Assert.Equal(1, director.LiveEffectCount);
            Assert.True(target.IsLive);
            Assert.True(attribution.AnyPossessed);
            await Task.Delay(1100);
            Assert.Equal(0, director.LiveEffectCount);
            Assert.False(target.IsLive);
            Assert.False(attribution.AnyPossessed);
            director.OnLockdownActivated();
            if (scroll) director.ApplyRestartReset(PossessionRung.Melt);
            target.CooldownUntil = DateTime.MinValue;
            director.RequestReactive(effect.Id, target);
            Assert.True(target.IsLive);
            director.UndoAll();
            await Task.Delay(50);
            Assert.False(target.IsLive);
            Assert.False(attribution.AnyPossessed);
            Assert.Equal(0, frames.Count);
            director.OnLockdownActivated();
            if (scroll) director.ApplyRestartReset(PossessionRung.Melt);
            target.CooldownUntil = DateTime.MinValue;
            director.RequestReactive(effect.Id, target);
            Assert.Equal(1, frames.Count);
            await Task.Delay(scroll ? 1500 : 2300);
            Assert.Equal(0, frames.Count);
            Assert.True(target.IsLive);
        }
        finally { director.UndoAll(); window.Close(); }
    });

}
