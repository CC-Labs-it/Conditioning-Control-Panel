using System;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
