using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Avalonia;
using ConditioningControlPanel.Avalonia.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public class PossessionSwapTests
{
    [Fact]
    public void PartnerDistanceUsesCentresAndIncludesTheBoundary()
    {
        var origin = new Rect(0, 0, 100, 40);
        Assert.Equal(220, SwapPartnerRule.Score(origin, new Rect(220, 0, 100, 40), false, false));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(221, 0, 100, 40), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(7, 0, 100, 40), false, false)));
        Assert.Equal(8, SwapPartnerRule.Score(origin, new Rect(8, 0, 100, 40), false, false));
        Assert.Equal(100, SwapPartnerRule.Score(origin, new Rect(80, 0, 140, 40), false, false));
    }

    [Fact]
    public void PartnerSizeChecksEachDimensionInBothDirections()
    {
        var origin = new Rect(0, 0, 100, 50);
        Assert.True(double.IsFinite(SwapPartnerRule.Score(origin, new Rect(100, 0, 160, 80), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(100, 0, 161, 50), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(100, 0, 100, 81), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(new Rect(0, 0, 161, 50), new Rect(100, 0, 100, 50), false, false)));
        Assert.True(double.IsPositiveInfinity(SwapPartnerRule.Score(origin, new Rect(100, 0, 0, 50), false, false)));
    }

    [Fact]
    public void StartStopCandidateBeatsNearerOrdinaryCandidate()
    {
        var origin = new Rect(0, 0, 50, 40);
        var near = new Rect(50, 0, 50, 40);
        var far = new Rect(200, 0, 50, 40);
        Assert.True(SwapPartnerRule.Score(origin, far, true, true) < SwapPartnerRule.Score(origin, near, true, false));
        Assert.True(SwapPartnerRule.Score(origin, near, false, false) < SwapPartnerRule.Score(origin, far, false, true));
    }
    private sealed class Host(Window window, Canvas layer, FakeFrames frames) : IAvaloniaPossessionHost
    {
        public Window? PossessionWindow => window;
        public Canvas? GhostLayer => layer;
        public IFrameSource PossessionFrames => frames;
        public PossessionRuntime PossessionEffectRuntime { get; } = new(frames);
        public Point PointerPosition => default;
        public PossessionPointer PossessionPointer { get; } = new();
        public IReadOnlyList<PossessionTarget> Targets { get; set; } = Array.Empty<PossessionTarget>();
        public bool IsUsable => true;
        public Rect? BoundsOnGhostLayer(Control c) => c.TransformToVisual(layer) is { } m ? new Rect(c.Bounds.Size).TransformToAABB(m) : null;
    }

    private sealed class Attribution : IPossessionAttribution
    {
        public readonly List<object> Charges = new();
        public int Outlines;
        public TaskCompletionSource? Gate;
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400)
        { Charges.Add(target); return Gate?.Task.WaitAsync(ct) ?? Task.CompletedTask; }
        public IDisposable Possess(object target) { Outlines++; return new Handle(this); }
        public bool AnyPossessed => Outlines > 0;
        public void EdgePulse(double strength) { }
        public void ReleaseAll() { }
        private sealed class Handle(Attribution owner) : IDisposable { public void Dispose() => owner.Outlines--; }
    }

    private static Task Run(Func<Host, Button, Button, Button, PossessionTarget[], PossessionContext, FakeFrames, Task> test) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var frames = new FakeFrames();
            var a = new Button { Content = "Start", Width = 60, Height = 40 };
            var b = new Button { Content = "More", Width = 60, Height = 40 };
            var c = new Button { Content = "Stop", Width = 60, Height = 40 };
            var panel = new Canvas { Background = Brushes.Transparent, Children = { a, b, c } };
            Canvas.SetLeft(a, 20); Canvas.SetLeft(b, 100); Canvas.SetLeft(c, 180);
            Canvas.SetTop(a, 40); Canvas.SetTop(b, 40); Canvas.SetTop(c, 40);
            var layer = new Canvas { IsHitTestVisible = false };
            var window = new Window { Width = 400, Height = 200, Content = new Grid { Children = { panel, layer } } };
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var host = new Host(window, layer, frames);
            var targets = new[] { Target(a, "a"), Target(b, "b"), Target(c, "c") };
            host.Targets = targets;
            var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Drift,
                Intensity = PossessionIntensity.Gentle, Photosafe = false, Rng = new Random(1),
                ElapsedFraction = 0, Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
            try { await test(host, a, b, c, targets, ctx, frames); }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        });

    private static PossessionTarget Target(Control c, string key) => new() { Visual = c, Key = key, Role = PossessionRole.Button, DisplayName = key };

    [Fact]
    public Task CatalogAndPartnerSafety() => Run((host, a, b, c, targets, ctx, frames) =>
    {
        Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e is SwapEffect);
        var effect = new SwapEffect();
        Assert.True(effect.CanApply(ctx, targets[0]));
        Assert.Same(targets[2], SwapEffect.FindPartner(ctx, targets[0]));
        targets[2].IsLive = true;
        Assert.Same(targets[1], SwapEffect.FindPartner(ctx, targets[0]));
        targets[2].IsLive = false;
        PossessionTree.SetExclude(c, true);
        PossessionTree.SetExclude(b, true);
        var chrome = new Button { Name = "BtnClose", Content = "Close", Width = 60, Height = 40 };
        var panel = (Canvas)a.GetVisualParent()!;
        panel.Children.Add(chrome); Canvas.SetLeft(chrome, 110); Canvas.SetTop(chrome, 100);
        host.PossessionWindow!.UpdateLayout();
        host.Targets = targets.Concat(new[] { Target(chrome, "chrome") }).ToArray();
        Assert.False(effect.CanApply(ctx, targets[0]));
        PossessionTree.SetExclude(c, false);
        var lease = PossessionLease.Take(c)!;
        try { Assert.False(effect.CanApply(ctx, targets[0])); }
        finally { lease.ReleaseImmediate(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task BothChargeGlideHitAndReturn() => Run(async (host, a, b, c, targets, ctx, frames) =>
    {
        var effect = new SwapEffect();
        var attribution = (Attribution)ctx.Attribution;
        string? named = null;
        ctx = new PossessionContext { Host = ctx.Host, Attribution = attribution, Rung = ctx.Rung, Intensity = ctx.Intensity,
            Photosafe = ctx.Photosafe, Rng = ctx.Rng, ElapsedFraction = ctx.ElapsedFraction, Remaining = ctx.Remaining, Name = (_, n) => named = n };
        attribution.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transformA = a.RenderTransform; var transformC = c.RenderTransform;
        var homeA = host.BoundsOnGhostLayer(a)!.Value; var homeC = host.BoundsOnGhostLayer(c)!.Value;
        var apply = effect.ApplyAsync(ctx, targets[0], CancellationToken.None);
        Assert.Equal(2, attribution.Charges.Count);
        Assert.False(PossessionLease.IsLeased(a)); Assert.False(PossessionLease.IsLeased(c));
        attribution.Gate.SetResult();
        for (int i = 0; i < 200 && frames.Count == 0; i++) await Task.Delay(5);
        Assert.Equal(1, frames.Count);
        Assert.True(PossessionLease.IsLeased(a)); Assert.True(PossessionLease.IsLeased(c));
        Assert.Equal(2, attribution.Outlines);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("possession_swap_pair", "a", "c"), named);
        frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(275));
        Assert.Equal(80, host.BoundsOnGhostLayer(a)!.Value.X - homeA.X, 1);
        frames.Frame(TimeSpan.FromMilliseconds(550)); await apply;
        Assert.Equal(homeC.X, host.BoundsOnGhostLayer(a)!.Value.X, 1);
        Assert.Equal(homeA.X, host.BoundsOnGhostLayer(c)!.Value.X, 1);
        Assert.True(targets[0].IsLive && targets[2].IsLive);
        var window = host.PossessionWindow!;
        var batch = ElementComposition.GetElementVisual(window)!.Compositor.RequestCompositionBatchCommitAsync();
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (!batch.Rendered.IsCompleted) { Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(10)); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        var hit = window.InputHitTest(homeC.Center) as Visual;
        Assert.True(ReferenceEquals(hit, a) || (hit != null && a.IsVisualAncestorOf(hit)));
        Assert.True(HeadlessInput.PressAt(a, window, homeC.Center).Pressed);
        var otherHit = window.InputHitTest(homeA.Center) as Visual;
        Assert.True(ReferenceEquals(otherHit, c) || (otherHit != null && c.IsVisualAncestorOf(otherHit)));
        Assert.True(HeadlessInput.PressAt(c, window, homeA.Center).Pressed);
        var undo = effect.UndoAsync(TimeSpan.FromMilliseconds(600));
        frames.Frame(TimeSpan.FromMilliseconds(1000)); frames.Frame(TimeSpan.FromMilliseconds(1300));
        Assert.Equal(80, host.BoundsOnGhostLayer(a)!.Value.X - homeA.X, 1);
        frames.Frame(TimeSpan.FromMilliseconds(1600)); await undo;
        Assert.Same(transformA, a.RenderTransform); Assert.Same(transformC, c.RenderTransform);
        Assert.False(PossessionLease.IsLeased(a)); Assert.False(PossessionLease.IsLeased(c));
        Assert.False(targets[0].IsLive || targets[2].IsLive); Assert.Equal(0, attribution.Outlines);
        Assert.Equal(0, frames.Count);
    });

    [Fact]
    public Task DirectorPanicRestoresBothDuringGlide() => Run((host, a, b, c, targets, ctx, frames) =>
    {
        var effect = new SwapEffect();
        var originalA = a.RenderTransform; var originalC = c.RenderTransform;
        using var lockdown = new LockdownService();
        using var director = new PossessionDirector(lockdown, new[] { effect }, new PossessionDirectorHead
        { Settings = () => new AppSettings { LockdownPossessionEnabled = true }, Attribution = _ => ctx.Attribution });
        director.AttachHost(host); director.OnLockdownActivated(); director.ApplyRestartReset(PossessionRung.Drift);
        director.RequestReactive("swap", targets[0]);
        Assert.Equal(1, director.LiveEffectCount);
        Assert.True(PossessionLease.IsLeased(a) && PossessionLease.IsLeased(c));
        frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(275));
        director.UndoAll();
        Assert.Equal(0, director.LiveEffectCount);
        Assert.False(PossessionLease.IsLeased(a) || PossessionLease.IsLeased(c));
        Assert.Same(originalA, a.RenderTransform); Assert.Same(originalC, c.RenderTransform);
        Assert.False(targets[0].IsLive || targets[2].IsLive);
        Assert.Equal(0, ((Attribution)ctx.Attribution).Outlines); Assert.Equal(0, frames.Count);
        return Task.CompletedTask;
    });

    [Fact]
    public Task CancelDuringChargeRestoresBookings() => Run(async (host, a, b, c, targets, ctx, frames) =>
    {
        var effect = new SwapEffect();
        var attribution = (Attribution)ctx.Attribution;
        attribution.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply = effect.ApplyAsync(ctx, targets[0], CancellationToken.None);
        Assert.True(targets[0].IsLive && targets[2].IsLive);
        await effect.UndoAsync(TimeSpan.Zero); await apply;
        Assert.False(effect.IsLive || targets[0].IsLive || targets[2].IsLive);
        Assert.False(PossessionLease.IsLeased(a) || PossessionLease.IsLeased(c));
        Assert.Equal(0, attribution.Outlines); Assert.Equal(0, frames.Count);
    });

    [Fact]
    public Task SafetyIsRecheckedAfterCharge() => Run(async (host, a, b, c, targets, ctx, frames) =>
    {
        var effect = new SwapEffect();
        var attribution = (Attribution)ctx.Attribution;
        attribution.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var apply = effect.ApplyAsync(ctx, targets[0], CancellationToken.None);
        PossessionTree.SetExclude(c, true);
        attribution.Gate.SetResult(); await apply;
        Assert.False(effect.IsLive || targets[0].IsLive || targets[2].IsLive);
        Assert.False(PossessionLease.IsLeased(a) || PossessionLease.IsLeased(c));
        Assert.Equal(0, attribution.Outlines); Assert.Equal(0, frames.Count);
    });

    [Fact]
    public Task LandingOutsideParentIsNotOffered() => Run((host, a, b, c, targets, ctx, frames) =>
    {
        var panel = (Canvas)a.GetVisualParent()!;
        panel.Children.Remove(c);
        var wrapper = new Border { Width = 60, Height = 40, Child = c };
        panel.Children.Add(wrapper); Canvas.SetLeft(wrapper, 180); Canvas.SetTop(wrapper, 40);
        host.PossessionWindow!.UpdateLayout();
        host.Targets = new[] { targets[0], targets[2] };
        Assert.False(new SwapEffect().CanApply(ctx, targets[0]));
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("en", "A and B", "two buttons")]
    [InlineData("de", "A und B", "zwei Schaltflächen")]
    [InlineData("es", "A y B", "dos botones")]
    [InlineData("fr", "A et B", "deux boutons")]
    [InlineData("it", "A e B", "due pulsanti")]
    [InlineData("ja", "AとB", "二つのボタン")]
    [InlineData("ko", "A 및 B", "버튼 두 개")]
    [InlineData("pt-BR", "A e B", "dois botões")]
    [InlineData("ru", "A и B", "две кнопки")]
    [InlineData("zh-CN", "A和B", "两个按钮")]
    public void PairNameUsesEachLanguageAndPreservesSingleNames(string language, string pair, string fallback) => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage(language);
            var a = new PossessionTarget { Visual = new Button(), Key = "a", Role = PossessionRole.Button, DisplayName = "A" };
            var b = new PossessionTarget { Visual = new Button(), Key = "b", Role = PossessionRole.Button, DisplayName = "B" };
            var empty = new PossessionTarget { Visual = new Button(), Key = "empty", Role = PossessionRole.Button };
            Assert.Equal(pair, SwapEffect.PairName(a, b));
            Assert.Equal("A", SwapEffect.PairName(a, empty));
            Assert.Equal("B", SwapEffect.PairName(empty, b));
            Assert.Equal(fallback, SwapEffect.PairName(empty, empty));
        }
        finally { loc.SetLanguage(previous); }
    });

}
