using System;
using System.Collections.Generic;
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
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionReorderDoorsTests
{
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
        public TaskCompletionSource? Gate;
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => Gate?.Task.WaitAsync(ct) ?? Task.CompletedTask;
        public IDisposable Possess(object target) => new Handle();
        public bool AnyPossessed => false;
        public void EdgePulse(double strength) { }
        public void ReleaseAll() { }
        private sealed class Handle : IDisposable { public void Dispose() { } }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task RailMovesAndStopRestoresImmediately(int stop) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var frames = new FakeFrames();
        var a = new Button { Content = "Home", Width = 80, Height = 40 };
        var b = new Button { Content = "Studio", Width = 80, Height = 40 };
        var rail = new StackPanel { Name = "NavSectionRows", HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
            VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top, Children = { a, b } };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 250, Height = 200, Content = new Grid { Children = { rail, layer } } };
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
        var host = new Host(window, layer, frames);
        var targets = new[] { new PossessionTarget { Visual = a, Key = "a", Role = PossessionRole.TabHeader },
            new PossessionTarget { Visual = b, Key = "b", Role = PossessionRole.TabHeader } };
        host.Targets = targets;
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Collapse,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
        var previousLockdown = LockdownService.Current;
        using var lockdown = new LockdownService();
        LockdownService.Current = lockdown;
        if (stop == 2) lockdown.Activate(TimeSpan.FromMinutes(5));
        var effect = new ReorderDoorsEffect();
        var originalA = a.RenderTransform; var originalB = b.RenderTransform;
        try
        {
            Assert.Contains(PossessionAvaloniaHead.CreateEffects(), e => e is ReorderDoorsEffect);
            Assert.True(effect.CanApply(ctx, targets[0]));
            var homeA = host.BoundsOnGhostLayer(a)!.Value;
            var homeB = host.BoundsOnGhostLayer(b)!.Value;
            await effect.ApplyAsync(ctx, targets[0], CancellationToken.None);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(520));
            Assert.Equal(homeB.Y, host.BoundsOnGhostLayer(a)!.Value.Y, 1);
            Assert.Equal(homeA.Y, host.BoundsOnGhostLayer(b)!.Value.Y, 1);
            HeadlessInput.RenderLiveTree(window);
            Assert.True(HeadlessInput.PressAt(a, window, homeB.Center).Pressed);
            Assert.True(HeadlessInput.PressAt(b, window, homeA.Center).Pressed);
            Assert.True(targets.All(t => t.IsLive));
            Task? returning = null;
            if (stop == 1)
            {
                returning = effect.UndoAsync(TimeSpan.FromMilliseconds(520));
                frames.Frame(TimeSpan.FromMilliseconds(600));
                frames.Frame(TimeSpan.FromMilliseconds(700));
            }
            if (stop == 2) lockdown.Deactivate();
            else PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null!);
            if (returning != null) await returning;
            Assert.False(effect.IsLive);
            Assert.All(targets, t => Assert.False(t.IsLive));
            Assert.Same(originalA, a.RenderTransform); Assert.Same(originalB, b.RenderTransform);
            Assert.False(PossessionLease.IsLeased(a)); Assert.False(PossessionLease.IsLeased(b));
            Assert.Equal(0, frames.Count);
            if (stop == 0)
            {
                var gated = new Attribution { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
                var pendingContext = new PossessionContext { Host = host, Attribution = gated, Rung = ctx.Rung, Intensity = ctx.Intensity,
                    Photosafe = false, Rng = new Random(1), ElapsedFraction = 0, Remaining = ctx.Remaining, Name = (_, _) => { } };
                var pending = effect.ApplyAsync(pendingContext, targets[0], CancellationToken.None);
                Assert.True(effect.IsLive);
                PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null!);
                Assert.False(effect.IsLive);
                await pending.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.All(targets, t => Assert.False(t.IsLive));
                Assert.False(PossessionLease.IsLeased(a));
            }
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); if (lockdown.IsActive) lockdown.Deactivate(); LockdownService.Current = previousLockdown; window.Close(); }
    });
}
