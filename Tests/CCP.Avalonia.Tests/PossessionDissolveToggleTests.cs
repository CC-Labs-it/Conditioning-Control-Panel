using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionDissolveToggleTests
{
    private sealed class Host(Window window, Canvas layer, FakeFrames frames) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => layer;
        public IFrameSource PossessionFrames => frames;
        public PossessionRuntime PossessionEffectRuntime { get; } = new(frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control element) => null;
        public IReadOnlyList<PossessionTarget> Targets => Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NamedKnobMovesAndUndoRestoresWithoutWritingChecked(bool checkedValue) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var original = new TranslateTransform(2, 0);
        var knob = new Border { Name = "knob", Width = 16, Height = 16, RenderTransform = original };
        var toggle = new ToggleButton { Width = 100, Height = 30, IsChecked = checkedValue, Opacity = 0.7,
            Template = new FuncControlTemplate<ToggleButton>((_, _) => new Canvas { Children = { knob } }) };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 200, Height = 100, Content = new Grid { Children = { toggle, layer } } };
        var frames = new FakeFrames();
        window.Show(); Dispatcher.UIThread.RunJobs();
        var target = new PossessionTarget { Visual = toggle, Role = PossessionRole.Toggle, Key = "switch", DisplayName = "switch" };
        var ctx = new PossessionContext { Host = new Host(window, layer, frames), Attribution = new Attribution(),
            Rung = PossessionRung.Melt, Intensity = PossessionIntensity.Eerie, Photosafe = false,
            Rng = new Random(1), ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new ToggleLieEffect();
        int writes = 0;
        toggle.PropertyChanged += (_, e) => { if (e.Property == ToggleButton.IsCheckedProperty) writes++; };
        try
        {
            Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "togglelie");
            Assert.True(effect.CanApply(ctx, target));
            var apply = effect.ApplyAsync(ctx, target, CancellationToken.None);
            Assert.False(apply.IsCompleted);
            Assert.True(PossessionLease.IsLeased(knob));
            frames.Frame(TimeSpan.FromMilliseconds(1000));
            frames.Frame(TimeSpan.FromMilliseconds(1240));
            var group = Assert.IsType<TransformGroup>(knob.RenderTransform);
            var movement = Assert.IsType<TranslateTransform>(group.Children.Last());
            Assert.True(Math.Abs(movement.X) >= 6);
            Assert.Equal(checkedValue, toggle.IsChecked);
            Assert.Equal(0, writes);
            await apply;
            effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
            Assert.Same(original, knob.RenderTransform);
            Assert.False(PossessionLease.IsLeased(knob));
            Assert.Equal(0.7, toggle.Opacity);
            Assert.True(toggle.IsVisible);
            Assert.Equal(checkedValue, toggle.IsChecked);
            Assert.Equal(0, writes);
            Assert.False(target.IsLive);
            frames.Frame(TimeSpan.FromMilliseconds(1500));
            Assert.Same(original, knob.RenderTransform);
        }
        finally { effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult(); window.Close(); }
    });
    [Theory]
    [InlineData("thumb")]
    [InlineData("ellipse")]
    [InlineData("border")]
    [InlineData("switch")]
    [InlineData("fallback")]
    [InlineData("fallback-normal")]
    public Task FindsKnobOrUsesGhostAndRestoresOnUndo(string kind) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Control part = kind switch
        {
            "thumb" => new Thumb { Width = 16, Height = 16 },
            "ellipse" => new Ellipse { Width = 16, Height = 16 },
            "border" => new Border { Width = 16, Height = 16 },
            "switch" => new Border { Name = "switch", Width = 16, Height = 16 },
            _ => new TextBlock { Width = 80, Height = 16, Text = "toggle" }
        };
        var toggle = new ToggleButton { Width = 100, Height = 30, IsChecked = true, Opacity = 0.6,
            Template = new FuncControlTemplate<ToggleButton>((_, _) => new Canvas { Children = { part } }) };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 200, Height = 100, Content = new Grid { Children = { toggle, layer } } };
        var frames = new FakeFrames();
        window.Show(); Dispatcher.UIThread.RunJobs();
        var target = new PossessionTarget { Visual = toggle, Role = PossessionRole.Toggle, Key = "toggle", DisplayName = "toggle" };
        var ctx = new PossessionContext { Host = new Host(window, layer, frames), Attribution = new Attribution(),
            Rung = PossessionRung.Melt, Intensity = PossessionIntensity.Eerie, Photosafe = false,
            Rng = new Random(1), ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new ToggleLieEffect();
        try
        {
            Assert.True(effect.CanApply(ctx, target));
            var apply = effect.ApplyAsync(ctx, target, CancellationToken.None);
            Assert.False(apply.IsCompleted);
            if (kind.StartsWith("fallback"))
            {
                Assert.Equal(0, toggle.Opacity);
                Assert.True(toggle.IsHitTestVisible);
                Assert.Equal(2, layer.Children.Count);
            }
            else
            {
                Assert.True(PossessionLease.IsLeased(part));
                frames.Frame(TimeSpan.FromMilliseconds(1000));
                frames.Frame(TimeSpan.FromMilliseconds(1240));
            }
            await apply;
            Assert.True(toggle.IsChecked);
            if (kind == "fallback")
            {
                PossessionRuntime.UndoAllLive();
                Assert.Equal(0.6, toggle.Opacity);
                Assert.Empty(layer.Children);
            }
            await effect.UndoAsync(TimeSpan.Zero);
            Assert.Equal(0.6, toggle.Opacity);
            Assert.True(toggle.IsVisible);
            Assert.True(toggle.IsChecked);
            Assert.False(PossessionLease.IsLeased(part));
            Assert.Empty(layer.Children);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TimedUndoAnimatesAndPanicInterruptsIt(bool photosafe) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var knob = new Border { Name = "knob", Width = 16, Height = 16 };
        var toggle = new ToggleButton { Width = 100, Height = 30,
            Template = new FuncControlTemplate<ToggleButton>((_, _) => new Canvas { Children = { knob } }) };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 200, Height = 100, Content = new Grid { Children = { toggle, layer } } };
        var frames = new FakeFrames();
        window.Show(); Dispatcher.UIThread.RunJobs();
        var target = new PossessionTarget { Visual = toggle, Role = PossessionRole.Toggle, Key = "toggle", DisplayName = "toggle" };
        var ctx = new PossessionContext { Host = new Host(window, layer, frames), Attribution = new Attribution(),
            Rung = PossessionRung.Melt, Intensity = PossessionIntensity.Eerie, Photosafe = photosafe,
            Rng = new Random(1), ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new ToggleLieEffect();
        try
        {
            var apply = effect.ApplyAsync(ctx, target, CancellationToken.None);
            frames.Frame(TimeSpan.FromMilliseconds(1000)); frames.Frame(TimeSpan.FromMilliseconds(1240));
            await apply;
            var movement = Assert.IsType<TranslateTransform>(Assert.IsType<TransformGroup>(knob.RenderTransform).Children.Last());
            double displaced = movement.X;
            var undo = effect.UndoAsync(TimeSpan.FromMilliseconds(300));
            Assert.False(undo.IsCompleted);
            Assert.Single(layer.Children);
            frames.Frame(TimeSpan.FromMilliseconds(2000)); frames.Frame(TimeSpan.FromMilliseconds(2090));
            Assert.InRange(movement.X, displaced * 0.34, displaced * 0.35);
            Assert.InRange(layer.Children[0].Opacity, photosafe ? 0.1 : 0.44, photosafe ? 0.22 : 0.46);
            await effect.UndoAsync(TimeSpan.Zero);
            Assert.False(PossessionLease.IsLeased(knob));
            Assert.Empty(layer.Children);
            await undo;
            Assert.False(effect.IsLive);
            var secondApply = effect.ApplyAsync(ctx, target, CancellationToken.None);
            frames.Frame(TimeSpan.FromMilliseconds(3000)); frames.Frame(TimeSpan.FromMilliseconds(3240));
            await secondApply;
            var normalUndo = effect.UndoAsync(TimeSpan.FromMilliseconds(150));
            Assert.False(normalUndo.IsCompleted);
            frames.Frame(TimeSpan.FromMilliseconds(4000)); frames.Frame(TimeSpan.FromMilliseconds(4150));
            await normalUndo;
            Assert.False(effect.IsLive);
            Assert.False(PossessionLease.IsLeased(knob));
            Assert.Empty(layer.Children);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

    [Fact]
    public Task CancellationDuringSlideCleansUp() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var knob = new Border { Name = "knob", Width = 16, Height = 16 };
        var toggle = new ToggleButton { Width = 100, Height = 30,
            Template = new FuncControlTemplate<ToggleButton>((_, _) => new Canvas { Children = { knob } }) };
        var layer = new Canvas(); var frames = new FakeFrames();
        var window = new Window { Width = 200, Height = 100, Content = new Grid { Children = { toggle, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var target = new PossessionTarget { Visual = toggle, Role = PossessionRole.Toggle, Key = "toggle", DisplayName = "toggle" };
        var ctx = new PossessionContext { Host = new Host(window, layer, frames), Attribution = new Attribution(),
            Rung = PossessionRung.Melt, Intensity = PossessionIntensity.Eerie, Photosafe = false,
            Rng = new Random(1), ElapsedFraction = 0, Remaining = TimeSpan.Zero, Name = (_, _) => { } };
        var effect = new ToggleLieEffect(); using var cts = new CancellationTokenSource();
        try
        {
            var apply = effect.ApplyAsync(ctx, target, cts.Token);
            Assert.False(apply.IsCompleted);
            Assert.True(PossessionLease.IsLeased(knob));
            cts.Cancel(); await apply;
            Assert.False(effect.IsLive);
            Assert.False(PossessionLease.IsLeased(knob));
            Assert.False(target.IsLive);
            Assert.True(toggle.IsVisible);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

}
