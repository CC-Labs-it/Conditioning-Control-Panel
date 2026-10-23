using System;
using System.Linq;
using System.Reflection;
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

public sealed class PossessionShellTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Button Button(string name) => new() { Name = name, Width = 100, Height = 30, Content = "Start" };

    [Fact]
    public Task WalkPrunesSafetyAndHiddenSubtreesAndKeepsInnermostCardsAndStableVictims() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var previous = LockdownService.Current;
        using var lockdown = new LockdownService();
        LockdownService.Current = lockdown;
        var shell = new MainShellWindow();
        Assert.True(PossessionTree.GetExclude(shell.Named<Border>("LockdownBadge")!)); // WPF MainWindow.xaml Exclude
        var button = Button("BtnVictim");
        var inner = new Border { Name = "Inner", Width = 180, Height = 80, Background = Brushes.Gray,
            CornerRadius = new CornerRadius(5), Child = button };
        var outer = new Border { Name = "Outer", Width = 220, Height = 100, Background = Brushes.Gray,
            CornerRadius = new CornerRadius(5), Child = inner };
        var excluded = new StackPanel { Children = { Button("BtnExcluded") } };
        PossessionTree.SetExclude(excluded, true);
        var hidden = new StackPanel { IsVisible = false, Children = { Button("BtnHidden") } };
        var tiny = Button("BtnTiny"); tiny.Width = 4;
        var disabled = Button("BtnDisabled"); disabled.IsEnabled = false;
        var dock = new Border { Name = "EmiDockChip", Child = Button("BtnCompanion") };
        var tagged = new TextBlock { Name = "TxtXP", Text = "500", Width = 80, Height = 25 };
        PossessionTree.SetRole(tagged, PossessionRole.Progress);
        PossessionTree.SetName(tagged, "the bank");
        var anonymous = Button("");
        var lockTab = new StackPanel { Name = "LockdownTab", Children = { Button("BtnLockStart") } };
        var gated = new StackPanel { IsEnabled = false, Children = { Button("BtnGatedChild") } };
        var panel = new StackPanel { Children = { outer, excluded, hidden, tiny, disabled, dock, tagged, anonymous, lockTab, gated } };
        shell.Content = panel;
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var targets = shell.GetPossessionTargets();
            Assert.Contains(targets, t => ReferenceEquals(t.Visual, button) && t.Role == PossessionRole.Button
                && t.DisplayName == "the Start button");
            Assert.Contains(targets, t => ReferenceEquals(t.Visual, inner) && t.Role == PossessionRole.Card);
            Assert.DoesNotContain(targets, t => ReferenceEquals(t.Visual, outer));
            Assert.DoesNotContain(targets, t => t.Key is "BtnExcluded" or "BtnHidden" or "BtnTiny" or "BtnDisabled" or "BtnCompanion"
                or "BtnLockStart" or "BtnGatedChild"); // CanApply refuses off-limits; IsEnabled does not inherit
            Assert.DoesNotContain(targets, t => ReferenceEquals(t.Visual, lockTab));
            var hand = Assert.Single(targets, t => ReferenceEquals(t.Visual, tagged));
            Assert.Equal(PossessionRole.Progress, hand.Role);
            Assert.Equal("the bank", hand.DisplayName);
            Assert.StartsWith("Button#", targets.Single(t => ReferenceEquals(t.Visual, anonymous)).Key);
            Assert.Same(targets, shell.GetPossessionTargets()); // no per-frame walk
            typeof(MainShellWindow).GetField("_possessionCacheAt", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(shell, DateTime.MinValue);
            Assert.Same(targets, Task.Run(shell.GetPossessionTargets).GetAwaiter().GetResult()); // stale off-thread reads return the last safe snapshot
            var rebuilt = shell.GetPossessionTargets();
            Assert.Same(hand, rebuilt.Single(t => ReferenceEquals(t.Visual, tagged))); // live/cooldown survives
            shell.WindowState = WindowState.Minimized;
            Assert.False(((IPossessionHost)shell).IsUsable);
        }
        finally { shell.Close(); LockdownService.Current = previous; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LockdownArmsCoreDirectorUsesLiveRungsAndRealEffectsAndPanicRestoresTheVictim() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        bool enabled = s.LockdownPossessionEnabled, safe = s.LockdownPhotosafe;
        int intensity = s.LockdownPossessionIntensity;
        s.LockdownPossessionEnabled = true;
        s.LockdownPossessionIntensity = (int)PossessionIntensity.FullDoki;
        s.LockdownPhotosafe = true;
        var previous = LockdownService.Current;
        using var lockdown = new LockdownService();
        var now = DateTime.UtcNow;
        lockdown.UtcNow = () => now;
        LockdownService.Current = lockdown;
        var frames = new Frames();
        MainShellWindow.PossessionFramesForTests = frames;
        var shell = new MainShellWindow();
        var label = new TextBlock { Name = "TxtVictim", Text = "drifting", Width = 100, Height = 30, FontSize = 14 };
        var prior = new ScaleTransform(1.1, 1.1);
        label.RenderTransform = prior;
        shell.Content = new StackPanel { Children = { label } };
        shell.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var warning = global::ConditioningControlPanel.Avalonia.Views.Tabs.LockdownTabView.LockdownWarning(10, s);
            Assert.Contains("- Possession is ON (Full Doki): the app's own UI will misbehave, on purpose.", warning); // WPF Lab.cs:94-104
            Assert.Contains("Ember glow = it was Lockdown, not a bug.", warning);
            var director = Assert.IsType<PossessionDirector>(shell.PossessionDirector);
            Assert.False(director.IsHaunting);
            lockdown.Activate(TimeSpan.FromMinutes(10));
            Dispatcher.UIThread.RunJobs();
            Assert.True(director.IsHaunting);
            Assert.Equal(PossessionRung.Settle, director.CurrentRung);
            var due = typeof(PossessionDirector).GetField("_nextDue", BindingFlags.NonPublic | BindingFlags.Instance)!;
            Assert.True((DateTime)due.GetValue(director)! > DateTime.Now); // FirstDelay, no instant twitch
            now += TimeSpan.FromMinutes(9);
            void Tick() => typeof(LockdownService).GetMethod("OnCountdownTick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(lockdown, new object?[] { null, EventArgs.Empty });
            Tick();
            Assert.Equal(PossessionRung.ItKnows, director.CurrentRung);
            s.LockdownPossessionIntensity = (int)PossessionIntensity.Gentle;
            Tick();
            Assert.Equal(PossessionRung.Melt, director.CurrentRung); // live settings, Core cap
            due.SetValue(director, DateTime.MinValue); // step the cadence deadline without a wall-clock sleep
            Tick();
            Assert.Equal(1, director.LiveEffectCount);
            Assert.False(PossessionLease.IsLeased(label)); // charge precedes any movement
            frames.Frame(TimeSpan.Zero); Dispatcher.UIThread.RunJobs();
            frames.Frame(TimeSpan.FromMilliseconds(600)); Dispatcher.UIThread.RunJobs(); // photosafe charge ends
            frames.Frame(TimeSpan.FromMilliseconds(616)); Dispatcher.UIThread.RunJobs();
            Assert.True(PossessionLease.IsLeased(label));
            Assert.True((DateTime)due.GetValue(director)! > DateTime.Now); // NextDelay, no immediate second pick
            PanicSurfaces.All.Single(x => x.Id == "possession").Stop(shell);
            Assert.Equal(0, director.LiveEffectCount);
            Assert.False(PossessionLease.IsLeased(label));
            Assert.Same(prior, label.RenderTransform);
            lockdown.RestartTimer("test"); Dispatcher.UIThread.RunJobs();
            Assert.Equal(PossessionRung.Settle, director.CurrentRung);
            lockdown.Deactivate(); Dispatcher.UIThread.RunJobs();
            Assert.False(director.IsHaunting);
        }
        finally
        {
            lockdown.Deactivate(); Dispatcher.UIThread.RunJobs(); shell.Close(); LockdownService.Current = previous;
            MainShellWindow.PossessionFramesForTests = null;
            s.LockdownPossessionEnabled = enabled; s.LockdownPhotosafe = safe; s.LockdownPossessionIntensity = intensity;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task LabelBudgetMatchesWpfAndActiveBootArmsTheDirector() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var previous = LockdownService.Current;
        bool enabled = CoreSettings.Current.LockdownPossessionEnabled;
        CoreSettings.Current.LockdownPossessionEnabled = true;
        using var lockdown = new LockdownService();
        LockdownService.Current = lockdown;
        lockdown.Activate(TimeSpan.FromMinutes(10));
        Dispatcher.UIThread.RunJobs();
        var shell = new MainShellWindow();
        var panel = new StackPanel();
        for (int i = 0; i < 30; i++) panel.Children.Add(new TextBlock
            { Name = "Label" + i, Text = "caption", FontSize = 14, Width = 80, Height = 12 });
        // WPF keeps the largest headings before the first pointer reading.
        var large = new TextBlock { Name = "LargeLabel", Text = "important", FontSize = 14, Width = 140, Height = 25 };
        panel.Children.Add(large);
        shell.Content = panel;
        shell.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            Assert.True(shell.PossessionDirector!.IsHaunting); // seed an already-active Lockdown
            var labels = shell.GetPossessionTargets().Where(t => t.Role == PossessionRole.Label).ToArray();
            Assert.Equal(24, labels.Length);
            Assert.Same(large, labels[0].Visual);
            Assert.True(shell.TryPossessionBounds(large, out var bounds));
            Assert.Equal(140, bounds.Width);
        }
        finally
        {
            lockdown.Deactivate(); Dispatcher.UIThread.RunJobs(); shell.Close();
            LockdownService.Current = previous; CoreSettings.Current.LockdownPossessionEnabled = enabled;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ExplicitTimerWobblesWithoutTouchingItsValueAndUnwindsOnReturn() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var timer = new TextBlock { Name = "TxtLockdownTimer", Text = "09:59", Width = 80, Height = 30 };
        PossessionTree.SetRole(timer, PossessionRole.Timer);
        var window = new Window { Width = 400, Height = 300, Content = timer };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var frames = new Frames();
        var runtime = new PossessionRuntime(frames);
        var effect = new WobbleHeadEffect();
        try
        {
            Assert.True(runtime.TryApply(effect, new(timer, PossessionRole.Timer, "timer")));
            frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(385));
            Assert.Equal("09:59", timer.Text);
            Assert.NotEqual(Matrix.Identity, timer.RenderTransform!.Value);
            var returning = runtime.Return(effect, TimeSpan.FromMilliseconds(600));
            frames.Frame(TimeSpan.FromMilliseconds(400)); frames.Frame(TimeSpan.FromMilliseconds(1000));
            Assert.True(returning.IsCompletedSuccessfully);
            Assert.Null(timer.RenderTransform);
            Assert.Equal("09:59", timer.Text);
            PossessionTree.SetExclude(timer, true);
            Assert.False(runtime.TryApply(effect, new(timer, PossessionRole.Timer, "timer")));
        }
        finally { runtime.UndoAll(); window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EmberChargePrecedesMovementPhotosafeIsStaticAndReleaseStopsItsClock() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var layer = new Canvas();
        var frames = new Frames();
        var rectangle = new Rect(10, 20, 80, 30);
        bool safe = false;
        var attribution = new EmberHeadAttribution(layer, _ => rectangle, frames, () => safe);
        var button = Button("Victim");
        var charging = attribution.ChargeAsync(button, default);
        var glass = Assert.IsType<Border>(Assert.Single(layer.Children));
        Assert.False(glass.IsHitTestVisible);
        Assert.Equal(10, Canvas.GetLeft(glass));
        frames.Frame(TimeSpan.Zero); frames.Frame(TimeSpan.FromMilliseconds(140));
        Assert.Equal(0.95, glass.Opacity, 5);
        frames.Frame(TimeSpan.FromMilliseconds(399));
        Assert.False(charging.IsCompleted);
        Assert.Null(glass.RenderTransform); // WPF charge is an opacity ripple, never a transform
        Assert.InRange(glass.Opacity, 0, 1);
        frames.Frame(TimeSpan.FromMilliseconds(400));
        Assert.True(charging.IsCompletedSuccessfully);
        Assert.Empty(layer.Children);
        Assert.Equal(0, frames.Count);
        using (attribution.Possess(button))
        {
            Assert.True(attribution.AnyPossessed);
            rectangle = new Rect(30, 40, 100, 35);
            frames.Frame(TimeSpan.FromMilliseconds(500));
            Assert.Equal(30, Canvas.GetLeft(Assert.Single(layer.Children)));
        }
        Assert.False(attribution.AnyPossessed);
        safe = true;
        charging = attribution.ChargeAsync(button, default);
        glass = Assert.IsType<Border>(Assert.Single(layer.Children));
        frames.Frame(TimeSpan.FromMilliseconds(1000)); frames.Frame(TimeSpan.FromMilliseconds(1599));
        Assert.False(charging.IsCompleted);
        Assert.Null(glass.RenderTransform);
        frames.Frame(TimeSpan.FromMilliseconds(1600));
        Assert.True(charging.IsCompletedSuccessfully);
        using var cancellation = new System.Threading.CancellationTokenSource();
        charging = attribution.ChargeAsync(button, cancellation.Token);
        cancellation.Cancel();
        Assert.True(charging.IsCanceled);
        Assert.Empty(layer.Children);
        attribution.Possess(button);
        attribution.ReleaseAll(); attribution.ReleaseAll();
        Assert.Empty(layer.Children);
        Assert.Equal(0, frames.Count);
        return Task.CompletedTask;
    });

    private sealed class Frames : global::ConditioningControlPanel.Avalonia.Controls.Fx.IFrameSource
    {
        private event Action<TimeSpan>? Tick;
        public int Count => Tick?.GetInvocationList().Length ?? 0;
        public void Subscribe(Action<TimeSpan> callback) => Tick += callback;
        public void Unsubscribe(Action<TimeSpan> callback) => Tick -= callback;
        public void Frame(TimeSpan time) => Tick?.Invoke(time);
    }
}
