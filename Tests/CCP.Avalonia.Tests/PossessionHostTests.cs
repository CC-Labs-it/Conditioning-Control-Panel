using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionHostTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static (Window Window, Grid Root) Show(Control content)
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = new Grid { Children = { content } };
        var window = new Window { Width = 500, Height = 500, Content = root };
        window.Show(); Dispatcher.UIThread.RunJobs(); return (window, root);
    }
    private static Button Button(string name) => new() { Name = name, Content = "Run", Width = 80, Height = 30 };

    [Fact]
    public Task WalkKeepsInnermostCardsAndLeavesAndHonoursExplicitTags() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var button = Button("Action");
        var inner = new Border { Background = Brushes.Gray, CornerRadius = new CornerRadius(8), Width = 160, Height = 80, Child = button };
        var outer = new Border { Background = Brushes.Gray, CornerRadius = new CornerRadius(8), Width = 200, Height = 120, Child = inner };
        var timer = new TextBlock { Name = "TxtLockdownTimer", Text = "01:23", Width = 80, Height = 30 };
        PossessionTree.SetRole(timer, PossessionRole.Timer); PossessionTree.SetName(timer, "the timer glass");
        var exit = Button("BtnEmergencyExit");
        var hidden = new StackPanel { IsVisible = false, Children = { Button("Hidden") } };
        var excluded = new StackPanel { Children = { Button("Excluded") } }; PossessionTree.SetExclude(excluded, true);
        var (w, root) = Show(new StackPanel { Children = { outer, timer, exit, hidden, excluded } });
        using var host = new PossessionHost(w, root);
        try
        {
            var targets = host.Targets;
            Assert.Contains(targets, t => ReferenceEquals(t.Visual, button) && t.Role == PossessionRole.Button && t.DisplayName == "the Run button");
            Assert.Contains(targets, t => ReferenceEquals(t.Visual, inner) && t.Role == PossessionRole.Card);
            Assert.DoesNotContain(targets, t => ReferenceEquals(t.Visual, outer));
            Assert.Contains(targets, t => ReferenceEquals(t.Visual, timer) && t.Role == PossessionRole.Timer && t.DisplayName == "the timer glass");
            Assert.DoesNotContain(targets, t => t.Key is "BtnEmergencyExit" or "Hidden" or "Excluded");
            Assert.DoesNotContain(targets, t => t.Role == PossessionRole.Label); // button's template caption is not another victim
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CacheSurvivesRebuildAndNeverWalksOffThread() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var button = Button("Action"); var (w, root) = Show(button); var clock = new Clock();
        using var host = new PossessionHost(w, root, time: clock);
        try
        {
            var first = host.Targets; var target = Assert.Single(first); target.CooldownUntil = DateTime.MaxValue;
            button.IsVisible = false; Dispatcher.UIThread.RunJobs();
            Assert.Same(first, host.Targets);
            Assert.Same(first, await Task.Run(() => host.Targets));
            clock.Now += TimeSpan.FromMilliseconds(751); Assert.Empty(host.Targets);
            button.IsVisible = true; Dispatcher.UIThread.RunJobs(); clock.Now += TimeSpan.FromSeconds(11);
            Assert.Same(target, Assert.Single(host.Targets)); Assert.Equal(DateTime.MaxValue, target.CooldownUntil);
        }
        finally { w.Close(); }
    });

    [Fact]
    public Task LabelBudgetChoosesLargestAndPlacementRejectsTinyDisabledControls() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var panel = new StackPanel();
        for (int i = 0; i < 30; i++) panel.Children.Add(new TextBlock { Name = "Label" + i, Text = "label", FontSize = 12, Width = 50 + i, Height = 10 });
        panel.Children.Add(new Button { Name = "Tiny", Width = 7, Height = 20 });
        panel.Children.Add(new Button { Name = "Disabled", Width = 80, Height = 20, IsEnabled = false });
        var (w, root) = Show(panel); using var host = new PossessionHost(w, root);
        try
        {
            Assert.Equal(24, host.Targets.Count(t => t.Role == PossessionRole.Label));
            Assert.DoesNotContain(host.Targets, t => t.Key is "Label0" or "Tiny" or "Disabled");
            Assert.Contains(host.Targets, t => t.Key == "Label29");
            Assert.True(host.IsUsable); w.Hide(); Assert.False(host.IsUsable);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, 400, false, 600)]
    [InlineData(true, 600, false, 600)]
    [InlineData(false, 400, true, 700)]
    [InlineData(true, 600, true, 700)]
    public Task ChargePrecedesMotionAndUndoRestoresTransformAndAttribution(bool photosafe, int chargeMs, bool breathe, int returnMs) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        bool saved = CoreSettings.Current.LockdownPhotosafe, gentle = CoreSettings.Current.GentleVisuals;
        CoreSettings.Current.LockdownPhotosafe = photosafe; CoreSettings.Current.GentleVisuals = false;
        var button = Button("Action"); var (w, root) = Show(button); var frames = new FakeFrames();
        using var host = new PossessionHost(w, root, frames); var effect = new PossessionDirectorEffect(breathe ? new BreatheHeadEffect() : new NudgeHeadEffect());
        var prior = button.RenderTransform;
        var target = Assert.Single(host.Targets);
        var ctx = new PossessionContext { Host = host, Attribution = host.Attribution, Rung = PossessionRung.Settle,
            Intensity = PossessionIntensity.Eerie, Photosafe = photosafe, Rng = new Random(1), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(10), Name = (_, _) => { } };
        try
        {
            var apply = effect.ApplyAsync(ctx, target, CancellationToken.None);
            Assert.Same(prior, button.RenderTransform); Assert.False(apply.IsCompleted);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(chargeMs - 1)); Assert.False(apply.IsCompleted);
            frames.Frame(TimeSpan.FromMilliseconds(chargeMs)); await apply;
            Assert.True(host.Attribution.AnyPossessed); Assert.IsType<TransformGroup>(button.RenderTransform);
            frames.Frame(TimeSpan.FromMilliseconds(chargeMs + 1)); frames.Frame(TimeSpan.FromMilliseconds(chargeMs + 400));
            Assert.NotEqual(prior?.Value ?? Matrix.Identity, button.RenderTransform!.Value);
            var undo = effect.UndoAsync(TimeSpan.FromSeconds(3)); frames.Frame(TimeSpan.FromSeconds(2));
            frames.Frame(TimeSpan.FromMilliseconds(2000 + returnMs - 1)); Assert.True(effect.IsLive);
            frames.Frame(TimeSpan.FromMilliseconds(2000 + returnMs)); await undo;
            Assert.Same(prior, button.RenderTransform); Assert.False(host.Attribution.AnyPossessed); Assert.Empty(host.GhostLayer.Children); Assert.Equal(0, frames.Count);
        }
        finally { await effect.UndoAsync(TimeSpan.Zero); w.Close(); CoreSettings.Current.LockdownPhotosafe = saved; CoreSettings.Current.GentleVisuals = gentle; }
    });

    [Fact]
    public Task InstalledDirectorFollowsLockdownAndPanicCancelsPendingCharge() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        bool saved = CoreSettings.Current.LockdownPossessionEnabled; CoreSettings.Current.LockdownPossessionEnabled = true;
        var (w, root) = Show(Button("Action")); var frames = new FakeFrames(); using var host = new PossessionHost(w, root, frames);
        using var lockdown = new LockdownService();
        try
        {
            MainShellWindow.InstallPossession(lockdown); var director = MainShellWindow.PossessionDirector!; director.AttachHost(host);
            Assert.False(director.IsHaunting); lockdown.Activate(TimeSpan.FromMinutes(10)); Assert.True(director.IsHaunting);
            director.RequestReactive("nudge", Assert.Single(host.Targets)); Assert.NotEmpty(host.GhostLayer.Children);
            PanicSurfaces.All.Single(s => s.Id == "possession").Stop(null);
            frames.Frame(TimeSpan.FromSeconds(1)); frames.Frame(TimeSpan.FromSeconds(3));
            Assert.Empty(host.GhostLayer.Children); Assert.Equal(0, director.LiveEffectCount); Assert.Equal(0, frames.Count);
            Assert.False(PossessionLease.IsLeased((Control)Assert.Single(host.Targets).Visual));
            lockdown.Deactivate(); Assert.False(director.IsHaunting);
        }
        finally { MainShellWindow.DisposePossession(); w.Close(); CoreSettings.Current.LockdownPossessionEnabled = saved; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TaggedTimerMayOnlyWobbleWhileExcludedExitRemainsProtected() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var timer = new TextBlock { Name = "TxtLockdownTimer", Text = "09:59", Width = 80, Height = 30 };
        PossessionTree.SetRole(timer, PossessionRole.Timer);
        var panel = new StackPanel { Name = "LockdownActivePanel", Children = { timer } };
        var (w, root) = Show(panel); var frames = new FakeFrames(); var runtime = new PossessionRuntime(frames);
        try
        {
            Assert.True(runtime.TryApply(new WobbleHeadEffect(), new(timer, PossessionRole.Timer, "timer")));
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(300)); Assert.Equal("09:59", timer.Text);
            runtime.UndoAll(); PossessionTree.SetExclude(panel, true);
            Assert.False(runtime.TryApply(new WobbleHeadEffect(), new(timer, PossessionRole.Timer, "timer")));
            Assert.True(PossessionTree.IsOffLimits(new Button { Name = "BtnEmergencyExit" }));
        }
        finally { runtime.UndoAll(); w.Close(); }
        return Task.CompletedTask;
    });
    [Fact]
    public Task ReadoutShowsEveryRungAndHidesOnExitAndConsentNamesPossession() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previous = LockdownService.Current; using var ld = new LockdownService(); LockdownService.Current = ld;
        bool enabled = CoreSettings.Current.LockdownPossessionEnabled; CoreSettings.Current.LockdownPossessionEnabled = true;
        var (w, _) = Show(new Border());
        try
        {
            var tab = new global::ConditioningControlPanel.Avalonia.Views.Tabs.LockdownTabView();
            Assert.True(PossessionTree.GetExclude(tab.BtnEmergencyExit));
            Assert.True(PossessionTree.GetExclude(tab.TxtLockdownExit));
            Assert.Equal(PossessionRole.Timer, PossessionTree.GetRole(tab.TxtLockdownTimer));
            ld.Activate(TimeSpan.FromMinutes(10));
            for (int rung = 0; rung <= 4; rung++)
            {
                tab.PaintPossessionReadout((PossessionRung)rung);
                Assert.True(tab.TxtPossessionRung.IsVisible); Assert.True(tab.PossessionPips.IsVisible);
                Assert.Equal(rung + 1, tab.PossessionPips.Children.OfType<Border>().Count(p => ((ISolidColorBrush)p.Background!).Color.A == 255));
            }
            ld.Deactivate(); tab.PaintPossessionReadout(PossessionRung.Settle); Assert.False(tab.TxtPossessionRung.IsVisible);
            var settings = new global::ConditioningControlPanel.Models.AppSettings { LockdownPossessionEnabled = true };
            Assert.Contains("Possession is ON (Eerie)", global::ConditioningControlPanel.Avalonia.Views.Tabs.LockdownTabView.LockdownWarning(10, settings, false));
        }
        finally { w.Close(); LockdownService.Current = previous; CoreSettings.Current.LockdownPossessionEnabled = enabled; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task TakeoverAndThrowingUsabilityProbesRefuseNewHaunts() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var (w, root) = Show(Button("Action")); using var host = new PossessionHost(w, root);
        var saved = PanicSurfaces.All;
        try
        {
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("test", _ => { }, () => true) };
            Assert.False(host.IsUsable);
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("test", _ => { }, () => throw new InvalidOperationException()) };
            Assert.False(host.IsUsable);
            PanicSurfaces.All = Array.Empty<PanicSurfaces.Surface>(); Assert.True(host.IsUsable);
        }
        finally { PanicSurfaces.All = saved; w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CursorRingFadesFor250MillisecondsAndPanicDropsItImmediately() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var button = Button("Action"); var (w, root) = Show(button); var frames = new FakeFrames();
        using var host = new PossessionHost(w, root, frames);
        try
        {
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); w.MouseMove(new Point(10, 10));
            using var outline = host.Attribution.Possess(button);
            var ring = Assert.Single(host.GhostLayer.Children.OfType<global::Avalonia.Controls.Shapes.Ellipse>());
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(125)); Assert.InRange(ring.Opacity, 0.49, 0.51);
            frames.Frame(TimeSpan.FromMilliseconds(250)); Assert.Equal(1, ring.Opacity);
            outline.Dispose(); frames.Frame(TimeSpan.FromMilliseconds(300)); frames.Frame(TimeSpan.FromMilliseconds(425));
            Assert.InRange(ring.Opacity, 0.49, 0.51); host.Attribution.ReleaseAll();
            Assert.Empty(host.GhostLayer.Children); Assert.Equal(0, frames.Count);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, 500, 1.0)]
    [InlineData(true, 900, 0.5)]
    public Task EdgePulseCapsOverlapAndUsesWpfPhotosafeTiming(bool photosafe, int ms, double peak) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        bool saved = CoreSettings.Current.LockdownPhotosafe, gentle = CoreSettings.Current.GentleVisuals;
        CoreSettings.Current.LockdownPhotosafe = photosafe; CoreSettings.Current.GentleVisuals = false;
        var (w, root) = Show(new Border()); var frames = new FakeFrames(); using var host = new PossessionHost(w, root, frames);
        try
        {
            Dispatcher.UIThread.RunJobs();
            for (int i = 0; i < 4; i++) host.Attribution.EdgePulse(2);
            Assert.Equal(3, host.GhostLayer.Children.Count);
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(ms * 0.4));
            Assert.All(host.GhostLayer.Children, c => Assert.Equal(peak, c.Opacity, 6));
            frames.Frame(TimeSpan.FromMilliseconds(ms)); Assert.Empty(host.GhostLayer.Children); Assert.Equal(0, frames.Count);
        }
        finally { w.Close(); CoreSettings.Current.LockdownPhotosafe = saved; CoreSettings.Current.GentleVisuals = gentle; }
        return Task.CompletedTask;
    });

}
