using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
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

public sealed class PossessionFallTests
{
    private sealed class Host(Window window, Canvas layer) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => layer;
        public IFrameSource PossessionFrames { get; } = new FakeFrames();
        public PossessionRuntime PossessionEffectRuntime { get; } = new(new FakeFrames());
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public Rect? BoundsOnGhostLayer(Control c) => c.TransformToVisual(layer) is { } t ? new Rect(c.Bounds.Size).TransformToAABB(t) : null;
        public IReadOnlyList<PossessionTarget> Targets { get; set; } = Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
    }
    private sealed class Attribution : IPossessionAttribution
    {
        private sealed class Handle : IDisposable { public void Dispose() { } }
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Task.CompletedTask;
        public IDisposable Possess(object target) => new Handle();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }
    private static Task Run(Func<FallEffect, Border, Canvas, PossessionTarget, PossessionContext, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var card = new Border { Width = 100, Height = 70, Opacity = .7, Child = new Button { Content = "Option" } };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 400, Height = 300, Content = new Grid { Children = { card, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var target = new PossessionTarget { Visual = card, Key = "card", Role = PossessionRole.Card };
        var ctx = new PossessionContext { Host = new Host(window, layer), Attribution = new Attribution(),
            Rung = PossessionRung.Collapse, Intensity = PossessionIntensity.Gentle, Photosafe = false,
            Rng = new Random(1), ElapsedFraction = .9, Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
        var effect = new FallEffect();
        try { await body(effect, card, layer, target, ctx); }
        finally { await effect.UndoAsync(TimeSpan.Zero); PossessionRuntime.UndoAllLive(); window.Close(); }
    });

    [Fact]
    public void TiltAndFallUseWpfCurves() => AvaloniaTestDispatcher.Run(() =>
    {
        var rotate = new RotateTransform();
        var chalk = new Rectangle();
        FallEffect.TiltFrame(rotate, chalk, 1, 0);
        Assert.InRange(chalk.Opacity, 0, .5);
        FallEffect.TiltFrame(rotate, chalk, 1, .25);
        Assert.Equal(.5, rotate.Angle, 5);
        Assert.Equal(.55 * .578125, chalk.Opacity, 5);
        FallEffect.TiltFrame(rotate, chalk, .5, 1);
        Assert.Equal(4, rotate.Angle, 5);
        Assert.Equal(.55, chalk.Opacity, 5);
        Assert.Equal(8.75, FallEffect.FallAngle(1, .5), 5);
        Assert.Equal(7, FallEffect.FallAngle(.5, 1), 5);
    });

    [Fact]
    public void TiltRotationUsesCubicInOut() => AvaloniaTestDispatcher.Run(() =>
    {
        var rotate = new RotateTransform();
        FallEffect.TiltFrame(rotate, new Rectangle(), 1, .25);
        Assert.Equal(.5, rotate.Angle, 5);
    });

    [Fact]
    public void FallRotationUsesCubicIn() =>
        Assert.Equal(8.75, FallEffect.FallAngle(1, .5), 5);

    [Fact]
    public void ChalkReturnFadesOverFirstSixtyPercent() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.Equal(.3, FallEffect.ReturnChalkOpacity(.3, 0), 5);
        Assert.Equal(.15, FallEffect.ReturnChalkOpacity(.3, .3), 5);
        Assert.Equal(0, FallEffect.ReturnChalkOpacity(.3, .6), 5);
        Assert.Equal(0, FallEffect.ReturnChalkOpacity(.3, 1), 5);
    });

    [Fact]
    public Task ChalkUsesWpfEmberColor() => Run(async (effect, card, layer, target, ctx) =>
    {
        var apply = effect.ApplyAsync(ctx, target, CancellationToken.None);
        var chalk = Assert.IsType<Rectangle>(Assert.Single(layer.Children));
        Assert.Equal(Color.FromRgb(0xFF, 0x8A, 0x5C), Assert.IsType<SolidColorBrush>(chalk.Stroke).Color);
        await effect.UndoAsync(TimeSpan.Zero);
        await apply;
    });

    [Fact]
    public Task ChalkStartsTransparent() => Run(async (effect, card, layer, target, ctx) =>
    {
        var apply = effect.ApplyAsync(ctx, target, CancellationToken.None);
        var chalk = Assert.IsType<Rectangle>(Assert.Single(layer.Children));
        Assert.InRange(chalk.Opacity, 0, .5);
        await effect.UndoAsync(TimeSpan.Zero);
        await apply;
    });

    [Fact]
    public Task FallReturnsAndRestoresHitTesting() => Run(async (effect, card, layer, target, ctx) =>
    {
        Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "fall");
        Assert.True(effect.CanApply(ctx, target));
        await effect.ApplyAsync(ctx, target, CancellationToken.None);
        Assert.False(card.IsHitTestVisible);
        Assert.Single(layer.Children);
        var lease = Assert.IsType<TransformGroup>(card.RenderTransform);
        Assert.True(((TranslateTransform)lease.Children[^1]).Y > 0);
        var undo = effect.UndoAsync(TimeSpan.FromMilliseconds(400));
        Assert.False(undo.IsCompleted);
        await undo;
        Assert.True(card.IsHitTestVisible);
        Assert.Empty(layer.Children);
    });

    [Fact]
    public Task ExitCardIsRefused() => Run((effect, card, layer, target, ctx) =>
    {
        card.Child = new Button { Name = "BtnEmergencyExit" };
        Dispatcher.UIThread.RunJobs();
        Assert.False(effect.CanApply(ctx, target));
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicRestoresDuringFall() => Run(async (effect, card, layer, target, ctx) =>
    {
        card.IsHitTestVisible = false;
        var apply = effect.ApplyAsync(ctx, target, CancellationToken.None);
        await Task.Delay(500);
        Assert.Single(layer.Children);
        await effect.UndoAsync(TimeSpan.Zero);
        await apply;
        Assert.False(card.IsHitTestVisible);
        Assert.Empty(layer.Children);
        Assert.False(effect.IsLive);
    });
}
