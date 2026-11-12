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

public sealed class PossessionStealCardTests
{
    private sealed class Host(Window window, Canvas layer) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => layer;
        public IFrameSource PossessionFrames { get; } = new FakeFrames();
        public PossessionRuntime PossessionEffectRuntime { get; } = new(new FakeFrames());
        public Point PointerPosition => default;
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
    private static Task Run(Func<StealCardEffect, Border, Canvas, PossessionTarget, PossessionContext, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var card = new Border { Width = 100, Height = 70, Opacity = .7, Child = new Button { Content = "Option" } };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 400, Height = 300, Content = new Grid { Children = { card, layer } } };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var target = new PossessionTarget { Visual = card, Key = "card", Role = PossessionRole.Card };
        var ctx = new PossessionContext { Host = new Host(window, layer), Attribution = new Attribution(),
            Rung = PossessionRung.Collapse, Intensity = PossessionIntensity.Gentle, Photosafe = false,
            Rng = new Random(1), ElapsedFraction = .9, Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
        var effect = new StealCardEffect();
        try { await body(effect, card, layer, target, ctx); }
        finally { await effect.UndoAsync(TimeSpan.Zero); PossessionRuntime.UndoAllLive(); window.Close(); }
    });

    [Fact]
    public Task ActiveCardImplodesAndReassembles() => Run(async (effect, card, layer, target, ctx) =>
    {
        Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e.Id == "stealcard");
        Assert.True(effect.CanApply(ctx, target));
        await effect.ApplyAsync(ctx, target, CancellationToken.None);
        Assert.Equal(0, card.Opacity); Assert.False(card.IsHitTestVisible);
        var image = Assert.IsType<Image>(Assert.Single(layer.Children));
        Assert.Equal(0, image.Opacity, 2);
        Assert.Equal(.06, Assert.IsType<ScaleTransform>(image.RenderTransform).ScaleX, 2);
        await effect.UndoAsync(TimeSpan.FromMilliseconds(260));
        Assert.Equal(.7, card.Opacity); Assert.True(card.IsHitTestVisible); Assert.Empty(layer.Children);
        Assert.False(effect.CanApply(ctx, target));
    });

    [Fact]
    public Task InertOrExcludedCardsAreNotTaken() => Run((effect, card, layer, target, ctx) =>
    {
        card.Child = new TextBlock { Text = "Decoration" }; Dispatcher.UIThread.RunJobs();
        Assert.False(effect.CanApply(ctx, target));
        var button = new Button { Content = "Option", IsEnabled = false }; card.Child = button; Dispatcher.UIThread.RunJobs();
        Assert.False(effect.CanApply(ctx, target));
        button.IsEnabled = true;
        PossessionTree.SetExclude(button, true);
        Assert.False(effect.CanApply(ctx, target));
        return Task.CompletedTask;
    });

    [Fact]
    public Task PanicRestoresDuringImplosion() => Run(async (effect, card, layer, target, ctx) =>
    {
        var apply = effect.ApplyAsync(ctx, target, CancellationToken.None);
        Assert.Equal(0, card.Opacity); Assert.False(card.IsHitTestVisible);
        await effect.UndoAsync(TimeSpan.Zero);
        await apply;
        Assert.Equal(.7, card.Opacity); Assert.True(card.IsHitTestVisible); Assert.Empty(layer.Children);
    });

    [Fact]
    public Task CancellationRestoresDuringImplosion() => Run(async (effect, card, layer, target, ctx) =>
    {
        using var cts = new CancellationTokenSource();
        var apply = effect.ApplyAsync(ctx, target, cts.Token);
        Assert.Equal(0, card.Opacity); Assert.False(card.IsHitTestVisible);
        cts.Cancel(); await apply;
        Assert.Equal(.7, card.Opacity); Assert.True(card.IsHitTestVisible); Assert.Empty(layer.Children);
        Assert.False(effect.IsLive);
    });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task DirectorUndoOrExitRestoresWhileApplyIsBusy(bool exit) => Run(async (effect, card, layer, target, ctx) =>
    {
        var host = (Host)ctx.Host;
        host.Targets = new[] { target };
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, new[] { effect }, new PossessionDirectorHead
        {
            Settings = () => new AppSettings { LockdownPossessionEnabled = true },
            Attribution = _ => ctx.Attribution
        });
        director.AttachHost(host);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(PossessionDirector).GetMethod("OnLockdownActivated", flags)!.Invoke(director, null);
        var entries = (IList)typeof(PossessionDirector).GetField("_hosts", flags)!.GetValue(director)!;
        var start = (Task)typeof(PossessionDirector).GetMethod("StartOneAsync", flags)!
            .Invoke(director, new[] { entries[0], (object)PossessionRung.Collapse, TimeSpan.FromMinutes(1), .9 })!;
        Assert.Equal(0, card.Opacity); Assert.False(card.IsHitTestVisible);
        if (exit) typeof(PossessionDirector).GetMethod("OnLockdownDeactivated", flags)!.Invoke(director, null);
        else director.UndoAll();
        await start;
        if (exit) await Task.Delay(800);
        Assert.Equal(.7, card.Opacity); Assert.True(card.IsHitTestVisible); Assert.Empty(layer.Children);
        Assert.False(effect.IsLive);
    });

    [Fact]
    public Task NewLockdownAllowsPreviouslyStolenCard() => Run(async (effect, card, layer, target, ctx) =>
    {
        var previous = LockdownService.Current;
        using var lockdown = new LockdownService();
        LockdownService.Current = lockdown;
        try
        {
            Assert.True(effect.CanApply(ctx, target));
            await effect.ApplyAsync(ctx, target, CancellationToken.None);
            await effect.UndoAsync(TimeSpan.Zero);
            Assert.False(effect.CanApply(ctx, target));
            var activated = (Action?)typeof(LockdownService).GetField("LockdownActivated", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lockdown);
            activated?.Invoke();
            Assert.True(effect.CanApply(ctx, target));
        }
        finally { LockdownService.Current = previous; }
    });

    [Fact]
    public Task MidFlightReturnStartsAtCurrentOpacity() => Run(async (effect, card, layer, target, ctx) =>
    {
        var apply = effect.ApplyAsync(ctx, target, CancellationToken.None);
        await Task.Delay(100);
        var image = Assert.IsType<Image>(Assert.Single(layer.Children));
        var opacity = image.Opacity;
        var undo = effect.UndoAsync(TimeSpan.FromMilliseconds(260));
        Assert.Equal(opacity, image.Opacity, 2);
        await Task.WhenAll(apply, undo);
        Assert.Equal(.7, card.Opacity); Assert.True(card.IsHitTestVisible);
    });

    [Fact]
    public void ReturnFadeFinishesAtFortyPercentWhileScaleContinues() => AvaloniaTestDispatcher.Run(() =>
    {
        var image = new Image();
        var scale = new ScaleTransform();
        StealCardEffect.ApplyFrame(image, scale, .06, 1, .25, .2);
        Assert.Equal(.90625, image.Opacity, 5);
        Assert.True(scale.ScaleX < 1);
        StealCardEffect.ApplyFrame(image, scale, .06, 1, .25, .4);
        Assert.Equal(1, image.Opacity, 5);
        Assert.True(scale.ScaleX < 1);
        StealCardEffect.ApplyFrame(image, scale, .06, 1, .25, 1);
        Assert.Equal(1, image.Opacity, 5);
        Assert.Equal(1, scale.ScaleX, 5);
        StealCardEffect.ApplyFrame(image, scale, 1, .06, 1, .5);
        Assert.Equal(.875, image.Opacity, 5);
    });

}
