using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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

    private sealed class CountCommand(Action execute) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
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
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); HeadlessInput.RenderLiveTree(window);
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
            var applying = effect.ApplyAsync(ctx, targets[0], CancellationToken.None);
            Assert.False(applying.IsCompleted);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(260));
            Assert.False(applying.IsCompleted);
            frames.Frame(TimeSpan.FromMilliseconds(520)); await applying;
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
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CrossPanelDoorsRemainClickableAndPanicCancelsTheSwap(bool panic) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var frames = new FakeFrames();
        var a = new Button { Content = "Home", Width = 80, Height = 40 };
        var b = new Button { Content = "Settings", Width = 80, Height = 40 };
        var wrapperA = new Border { Width = 80, Height = 40, Child = a, ClipToBounds = true };
        var wrapperB = new Border { Width = 80, Height = 40, Child = b, ClipToBounds = true };
        var panel = new Canvas { Background = Brushes.Transparent, Children = { wrapperA, wrapperB } };
        Canvas.SetLeft(wrapperA, 20); Canvas.SetTop(wrapperA, 20);
        Canvas.SetLeft(wrapperB, 20); Canvas.SetTop(wrapperB, 100);
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 250, Height = 200, Content = new Grid { Children = { panel, layer } } };
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); HeadlessInput.RenderLiveTree(window);
        var host = new Host(window, layer, frames);
        var targets = new[] { new PossessionTarget { Visual = a, Key = "home", Role = PossessionRole.TabHeader, DisplayName = "home" },
            new PossessionTarget { Visual = b, Key = "settings", Role = PossessionRole.TabHeader, DisplayName = "settings" } };
        host.Targets = targets;
        string? named = null;
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Collapse,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(1), Name = (_, name) => named = name };
        var effect = new ReorderDoorsEffect();
        var originalA = a.RenderTransform; var originalB = b.RenderTransform;
        try
        {
            Assert.True(effect.CanApply(ctx, targets[0]));
            var homeA = host.BoundsOnGhostLayer(a)!.Value;
            var homeB = host.BoundsOnGhostLayer(b)!.Value;
            var applying = effect.ApplyAsync(ctx, targets[0], CancellationToken.None);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("possession_reorder_doors"), named);
            Assert.False(applying.IsCompleted);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(260));
            Assert.False(applying.IsCompleted);
            if (panic)
                PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null!);
            else
            {
                frames.Frame(TimeSpan.FromMilliseconds(520));
                await applying.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(homeB.Y, host.BoundsOnGhostLayer(a)!.Value.Y, 1);
                Assert.Equal(homeA.Y, host.BoundsOnGhostLayer(b)!.Value.Y, 1);
                HeadlessInput.RenderLiveTree(window);
                Assert.True(HeadlessInput.PressAt(a, window, homeB.Center).Pressed);
                Assert.True(HeadlessInput.PressAt(b, window, homeA.Center).Pressed);
                Assert.False(wrapperA.ClipToBounds); Assert.False(wrapperB.ClipToBounds);
                await effect.UndoAsync(TimeSpan.Zero);
            }
            await applying.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(effect.IsLive);
            Assert.Same(originalA, a.RenderTransform); Assert.Same(originalB, b.RenderTransform);
            Assert.True(wrapperA.ClipToBounds); Assert.True(wrapperB.ClipToBounds);
            Assert.Equal(0, frames.Count);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public Task CoveredContainerDoorsMoveAndRemainClickable(int stop) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var frames = new FakeFrames();
        var a = new Button { Content = "Home", Width = 80, Height = 40, Opacity = 0.7 };
        var b = new Button { Content = "Settings", Width = 80, Height = 40, Opacity = 0.8 };
        var wrapperA = new Border { Width = 80, Height = 40, Child = a, Background = Brushes.Transparent };
        var wrapperB = new Border { Width = 80, Height = 40, Child = b, Background = Brushes.Transparent };
        var panel = new Canvas { Background = Brushes.Transparent, Children = { wrapperA, wrapperB } };
        Canvas.SetLeft(wrapperA, 20); Canvas.SetTop(wrapperA, 20);
        Canvas.SetLeft(wrapperB, 20); Canvas.SetTop(wrapperB, 100);
        var layer = new Canvas { IsHitTestVisible = false };
        var root = new Grid { Children = { panel, layer } };
        var window = new Window { Width = 250, Height = 200, Content = root };
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); HeadlessInput.RenderLiveTree(window);
        var host = new Host(window, layer, frames);
        var targets = new[] { new PossessionTarget { Visual = a, Key = "home", Role = PossessionRole.TabHeader },
            new PossessionTarget { Visual = b, Key = "settings", Role = PossessionRole.TabHeader } };
        host.Targets = targets;
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Collapse,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { } };
        var previousLockdown = LockdownService.Current;
        using var lockdown = new LockdownService();
        LockdownService.Current = lockdown;
        if (stop == 3) lockdown.Activate(TimeSpan.FromMinutes(5));
        var effect = new ReorderDoorsEffect();
        var originalA = a.RenderTransform; var originalB = b.RenderTransform;
        int clicksA = 0, clicksB = 0, commandsA = 0, commandsB = 0;
        a.Click += (_, _) => clicksA++; b.Click += (_, _) => clicksB++;
        a.Command = new CountCommand(() => commandsA++); b.Command = new CountCommand(() => commandsB++);
        try
        {
            var homeA = host.BoundsOnGhostLayer(a)!.Value; var homeB = host.BoundsOnGhostLayer(b)!.Value;
            var applying = effect.ApplyAsync(ctx, targets[0], CancellationToken.None);
            Assert.False(applying.IsCompleted);
            Assert.True(targets.All(t => t.IsLive));
            Assert.Equal(2, layer.Children.Count);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(260));
            if (stop == 1) PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null!);
            else if (stop == 3) lockdown.Deactivate();
            else
            {
                frames.Frame(TimeSpan.FromMilliseconds(520)); await applying.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Equal(homeB.Y, host.BoundsOnGhostLayer(a)!.Value.Y, 1);
                Assert.Equal(homeA.Y, host.BoundsOnGhostLayer(b)!.Value.Y, 1);
                HeadlessInput.RenderLiveTree(window);
                window.MouseDown(homeB.Center, MouseButton.Left); window.MouseUp(homeB.Center, MouseButton.Left);
                window.MouseDown(homeA.Center, MouseButton.Left); window.MouseUp(homeA.Center, MouseButton.Left);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, clicksA); Assert.Equal(1, clicksB);
                Assert.Equal(1, commandsA); Assert.Equal(1, commandsB);
                if (stop == 2)
                {
                    var returning = effect.UndoAsync(TimeSpan.FromMilliseconds(520));
                    frames.Frame(TimeSpan.FromMilliseconds(600)); frames.Frame(TimeSpan.FromMilliseconds(700));
                    PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null!);
                    await returning.WaitAsync(TimeSpan.FromSeconds(5));
                }
                else await effect.UndoAsync(TimeSpan.Zero);
            }
            await applying.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(effect.IsLive); Assert.All(targets, t => Assert.False(t.IsLive));
            Assert.Empty(layer.Children); Assert.Equal(2, root.Children.Count);
            Assert.Equal(0.7, a.Opacity); Assert.Equal(0.8, b.Opacity);
            Assert.True(a.IsHitTestVisible); Assert.True(b.IsHitTestVisible);
            Assert.Same(originalA, a.RenderTransform); Assert.Same(originalB, b.RenderTransform);
            Assert.False(PossessionLease.IsLeased(a)); Assert.False(PossessionLease.IsLeased(b));
            Assert.Equal(0, frames.Count);
        }
        finally
        {
            await effect.UndoAsync(TimeSpan.Zero);
            if (lockdown.IsActive) lockdown.Deactivate();
            LockdownService.Current = previousLockdown; window.Close();
        }
    });

    [Fact]
    public Task BarkNamesTheDoorGroup() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var frames = new FakeFrames();
        var a = new Button { Content = "Home", Width = 80, Height = 40 };
        var b = new Button { Content = "Settings", Width = 80, Height = 40 };
        var rail = new StackPanel { Children = { a, b } };
        var layer = new Canvas { IsHitTestVisible = false };
        var window = new Window { Width = 250, Height = 200, Content = new Grid { Children = { rail, layer } } };
        window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); HeadlessInput.RenderLiveTree(window);
        var host = new Host(window, layer, frames);
        var target = new PossessionTarget { Visual = a, Key = "home", Role = PossessionRole.TabHeader, DisplayName = "home" };
        host.Targets = new[] { target, new PossessionTarget { Visual = b, Key = "settings", Role = PossessionRole.TabHeader } };
        string? named = null;
        var ctx = new PossessionContext { Host = host, Attribution = new Attribution(), Rung = PossessionRung.Collapse,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(1), Name = (_, name) => named = name };
        var effect = new ReorderDoorsEffect();
        try
        {
            var applying = effect.ApplyAsync(ctx, target, CancellationToken.None);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("possession_reorder_doors"), named);
            await effect.UndoAsync(TimeSpan.Zero);
            await applying.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); window.Close(); }
    });

    [Theory]
    [InlineData("en", "the doors")]
    [InlineData("de", "die Türen")]
    [InlineData("es", "las puertas")]
    [InlineData("fr", "les portes")]
    [InlineData("it", "le porte")]
    [InlineData("ja", "扉")]
    [InlineData("ko", "문들")]
    [InlineData("pt-BR", "as portas")]
    [InlineData("ru", "двери")]
    [InlineData("zh-CN", "门")]
    public void DoorNameIsLocalized(string language, string expected) => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var previous = loc.CurrentLanguage;
        try { loc.SetLanguage(language); Assert.Equal(expected, ConditioningControlPanel.Localization.Loc.Get("possession_reorder_doors")); }
        finally { loc.SetLanguage(previous); }
    });

}
