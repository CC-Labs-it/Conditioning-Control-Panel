using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Localization;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Possession;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>shell-possession slice 1: the Core PossessionDirector haunting the Avalonia shell. WPF refs:
/// MainWindow/MainWindow.Possession.cs (host + walk), MainWindow.Lab.cs:871-986 (readout) and 1100
/// (first-run rules), Services/Possession/PossessionWpfHead.cs, EmberAttribution.cs, PossessionEffectBase.cs.
/// The director is built BEFORE the shell, as App does (its lockdown handlers run first).</summary>
public sealed class PossessionShellTests
{
    private sealed record Rig(MainShellWindow Shell, LockdownService Lockdown, PossessionDirector Director, FakeFrames Frames)
    {
        public DateTime Clock = DateTime.UtcNow;
    }

    private static void Run(Action<Rig> body, Action<AppSettings>? seed = null)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.Welcomed = s.HasAcceptedAgeVerification = true;
            s.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);   // P52: no intro card taking the keyboard
            s.WhatMovedCardShown = 1;
            s.MotionLevel = MotionLevel.Full;
            s.LockdownPossessionEnabled = true;
            s.LockdownPossessionIntroSeen = true;
            s.LockdownPossessionIntensity = 1;
            s.LockdownPhotosafe = false;
            s.LockdownForceStrictLock = false;
            s.LockdownDisablePanicKey = false;
            seed?.Invoke(s);
            // The app attaches CoreDispatch, so the lockdown's one-second tick reaches the director on the UI
            // thread; headless has no head, which would run it on the timer's pool thread.
            var priorPost = CoreDispatch.PostProvider;
            CoreDispatch.PostProvider = a => Dispatcher.UIThread.Post(a);
            var ld = LockdownService.Current = new LockdownService();
            var director = new PossessionDirector(ld, PossessionAvaloniaHead.CreateEffects(), PossessionAvaloniaHead.CreateDirectorHead());
            var frames = new FakeFrames();
            var shell = new MainShellWindow { PossessionFramesOverride = frames, PossessionDirectorOverride = director };
            var rig = new Rig(shell, ld, director, frames);
            ld.UtcNow = () => rig.Clock;
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                body(rig);
            }
            finally
            {
                AvApp.Possession = null;
                director.Dispose();
                ld.Dispose();
                LockdownService.Current = null;
                CoreDispatch.PostProvider = priorPost;
                LauncherWindow.Instance?.Close();
                LauncherWindow.Boot = BootDecision.PanelFirst;
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                shell.RequestExit();
                shell.Close();
                StartupLadder.ResetForTests();
                CoreSettings.ServiceProvider = null;
            }
        });
    }

    private static void Frame()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The lockdown's real tick (LockdownService.OnCountdownTick), as its timer raises it.</summary>
    private static void Tick(LockdownService ld) =>
        typeof(LockdownService).GetMethod("OnCountdownTick", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(ld, new object?[] { null, EventArgs.Empty });

    /// <summary>The director's cadence is wall-clock (WPF too): make the next beat due now.</summary>
    private static void DueNow(PossessionDirector d) =>
        typeof(PossessionDirector).GetField("_nextDue", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(d, DateTime.MinValue);

    private static string? Hex(IBrush? brush) => (brush as ISolidColorBrush)?.Color.ToUInt32().ToString("x8");

    private static string[] Pips(LockdownTabView page) =>
        page.PossessionPips.Children.OfType<Border>().Select(b => Hex(b.Background)!).ToArray();

    private const string Ember = "ffff8a5c", Dim = "33ff8a5c";

    [Fact]
    public void Readout_PaintsSettleOnActivation_FollowsTheLadder_AndExitClearsIt() => Run(rig =>
    {
        var page = rig.Shell.Named<LockdownTabView>("LockdownTab")!;
        Assert.False(page.TxtPossessionRung.IsVisible);

        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        Frame();
        Assert.True(page.TxtPossessionRung.IsVisible);
        Assert.True(page.PossessionPips.IsVisible);
        Assert.Equal("Possession  ·  Settle", page.TxtPossessionRung.Text);
        Assert.Equal(new[] { Ember, Dim, Dim, Dim, Dim }, Pips(page));
        // A panel swap landing after the paint leaves the row up (the stub's hide used to blank it).
        page.ShowLockdownState();
        Assert.True(page.TxtPossessionRung.IsVisible);
        Assert.True(page.PossessionPips.IsVisible);

        // 40% of the timer served: Eerie is at Melt (PossessionDeck.RungFor), raised from the real tick.
        rig.Clock += TimeSpan.FromMinutes(12);
        Tick(rig.Lockdown);
        Frame();
        Assert.Equal(PossessionRung.Melt, rig.Director.CurrentRung);
        Assert.Equal("Possession  ·  Melt", page.TxtPossessionRung.Text);
        Assert.Equal(new[] { Ember, Ember, Ember, Dim, Dim }, Pips(page));

        rig.Lockdown.Deactivate();
        Frame();
        Assert.False(page.TxtPossessionRung.IsVisible);
        Assert.False(page.PossessionPips.IsVisible);
        Assert.Equal("", page.TxtPossessionRung.Text);
        Assert.Equal(new[] { Dim, Dim, Dim, Dim, Dim }, Pips(page));

        // A second run starts at Settle again (the handler was the SAME delegate, so it is gone).
        rig.Clock += TimeSpan.FromMinutes(1);
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        Frame();
        Assert.Equal("Possession  ·  Settle", page.TxtPossessionRung.Text);
    });

    [Fact]
    public void Readout_StaysCollapsed_WhenPossessionIsOff() => Run(rig =>
    {
        var page = rig.Shell.Named<LockdownTabView>("LockdownTab")!;
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        Frame();
        Assert.False(page.TxtPossessionRung.IsVisible);
        Assert.False(page.PossessionPips.IsVisible);
        Assert.False(rig.Director.IsHaunting);
    }, s => s.LockdownPossessionEnabled = false);

    [Fact]
    public void TheShellIsAHauntableRoom_NeverNamesStayOut_TargetsSurviveRebuilds() => Run(rig =>
    {
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        rig.Shell.ShowTab("lockdown");
        Frame();
        var host = (IPossessionHost)rig.Shell;
        Assert.True(host.IsUsable);
        var targets = host.Targets;
        Assert.NotEmpty(targets);
        Assert.Contains(targets, t => t.Role == PossessionRole.Button);
        // Hand-tagged timer is offered; the other never-names remain excluded.
        var page = rig.Shell.Named<LockdownTabView>("LockdownTab")!;
        Assert.True(page.TxtLockdownTimer.IsEffectivelyVisible);
        Assert.True(page.PossessionPips.IsEffectivelyVisible);
        foreach (var t in targets)
        {
            var name = ((Control)t.Visual).Name;
            Assert.False(name != "TxtLockdownTimer" && name != null && MainShellWindow.PossessionNeverNames.Contains(name), $"never-name {name} was offered");
            Assert.False(t.Visual is Border b && ReferenceEquals(b, rig.Shell.PossessionGhostLayer));
            Assert.False(string.IsNullOrEmpty(t.Key));
            Assert.False(string.IsNullOrEmpty(t.DisplayName));
        }
        Assert.True(targets.Count(t => t.Role == PossessionRole.Label) <= MainShellWindow.MaxAutoLabels);
        // The ghost layer covers the window and never takes a click.
        var layer = Assert.IsType<Canvas>(rig.Shell.PossessionGhostLayer);
        Assert.False(layer.IsHitTestVisible);

        // A rebuild hands back the SAME target objects, so a cooldown on the Stop button survives it.
        var first = targets[0];
        rig.Shell.InvalidatePossessionTargets();
        var again = host.Targets;
        Assert.Contains(first, again);
    });

    // Restack b77 (#3303): WPF MainWindow.xaml:332 excludes the Lockdown badge; WPF's effective
    // IsHitTestVisible / IsEnabled keep a passive or disabled subtree out of the auto-tag walk.
    [Fact]
    public void BadgeAndPassiveOrDisabledSubtreesStayOutOfTheWalk() => Run(rig =>
    {
        var root = rig.Shell.Named<Grid>("RootGrid")!;
        Button Make(string name) => new() { Name = name, Content = "Start", Width = 100, Height = 30 };
        var passive = Make("PassiveChild");
        var disabled = Make("DisabledChild");
        var live = Make("LiveChild");
        root.Children.Add(new StackPanel
        {
            Children =
            {
                new StackPanel { IsHitTestVisible = false, Children = { passive } },
                new StackPanel { IsEnabled = false, Children = { disabled } },
                live,
            },
        });
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        Frame();
        var badge = rig.Shell.Named<Border>("LockdownBadge")!;
        Assert.True(badge.IsEffectivelyVisible);
        Assert.True(PossessionTree.GetExclude(badge));
        rig.Shell.InvalidatePossessionTargets();
        var targets = ((IPossessionHost)rig.Shell).Targets;
        Assert.Contains(targets, t => t.Visual == live);
        Assert.True(passive.IsHitTestVisible);
        Assert.DoesNotContain(targets, t => t.Visual == passive);
        Assert.DoesNotContain(targets, t => t.Visual == disabled);
        Assert.DoesNotContain(targets, t => t.Visual is Visual v && (v == badge || badge.IsVisualAncestorOf(v)));
    });

    [Fact]
    public void HandTagsOfferDoorsAndTheLockdownTimer() => Run(rig =>
    {
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        rig.Shell.ShowTab("lockdown");
        Frame();
        var targets = ((IPossessionHost)rig.Shell).Targets;
        foreach (var name in new[] { "Home", "Studio", "Companion", "Play", "Social", "You", "Library", "Settings" })
        {
            var door = Assert.Single(targets.Where(t => t.Key == "Door" + name));
            Assert.Equal(PossessionRole.TabHeader, door.Role);
            Assert.Equal("the " + name + " door", door.DisplayName);
        }
        var timer = Assert.Single(targets.Where(t => t.Key == "TxtLockdownTimer"));
        Assert.Equal(PossessionRole.Timer, timer.Role);
        Assert.Equal("the timer", timer.DisplayName);
    });

    [Fact]
    public void ABridgedEffect_ChargesFirst_ThenMoves_ThenEasesHomeOnUndo() => Run(rig =>
    {
        rig.Shell.ShowTab("settings");
        Frame();
        var host = (IAvaloniaPossessionHost)rig.Shell;
        var target = ((IPossessionHost)rig.Shell).Targets.First(t =>
            t.Role == PossessionRole.Button && t.Visual is Control c && !PossessionTree.IsOffLimits(c));
        var el = (Control)target.Visual;
        var attribution = new EmberAttribution(host);
        var nudge = new DirectedHeadEffect(new NudgeHeadEffect());
        var ctx = new PossessionContext
        {
            Host = rig.Shell, Attribution = attribution, Rung = PossessionRung.Settle, Intensity = PossessionIntensity.Eerie,
            Photosafe = false, Rng = new Random(3), ElapsedFraction = 0, Remaining = TimeSpan.FromMinutes(30), Name = (_, _) => { },
        };
        Assert.True(nudge.CanApply(ctx, target));

        var apply = nudge.ApplyAsync(ctx, target, CancellationToken.None);
        Dispatcher.UIThread.RunJobs();
        // The charge comes FIRST: an ember overlay over the victim, and nothing has moved yet.
        Assert.Equal(1, attribution.LiveOverlayCount);
        Assert.False(PossessionLease.IsLeased(el));
        Assert.False(apply.IsCompleted);

        rig.Frames.Frame(TimeSpan.FromMilliseconds(1000));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1200));
        Dispatcher.UIThread.RunJobs();
        Assert.False(PossessionLease.IsLeased(el));    // 200 of 400 ms: still charging
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1450));
        Dispatcher.UIThread.RunJobs();
        Assert.True(apply.IsCompleted);
        Assert.True(PossessionLease.IsLeased(el));     // moved only after the charge
        Assert.True(attribution.AnyPossessed);         // and outlined while it stays moved
        Assert.True(nudge.IsLive);

        rig.Frames.Frame(TimeSpan.FromMilliseconds(1800));   // the director owns the hold: no home yet
        Assert.True(PossessionLease.IsLeased(el));
        var undo = nudge.UndoAsync(TimeSpan.FromMilliseconds(600));
        Assert.False(attribution.AnyPossessed);
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1810));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(2110));
        Assert.False(undo.IsCompleted);                // halfway home
        rig.Frames.Frame(TimeSpan.FromMilliseconds(2420));
        Dispatcher.UIThread.RunJobs();
        Assert.True(undo.IsCompleted);
        Assert.False(PossessionLease.IsLeased(el));
        Assert.False(nudge.IsLive);
        // Undo is safe twice and when nothing is live.
        Assert.True(nudge.UndoAsync(TimeSpan.Zero).IsCompleted);
    });

    private sealed class DodgeAttribution : IPossessionAttribution
    {
        public int Charges, Releases;
        public Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) { Charges++; return Task.CompletedTask; }
        public IDisposable Possess(object target) => new DisposeAction(() => Releases++);
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
        private sealed class DisposeAction(Action action) : IDisposable { public void Dispose() => action(); }
    }

    [Theory]
    [InlineData("BtnClose")]
    [InlineData("BtnMinimize")]
    public void CatalogDodgeMovesRealCloseButtonAndUndoRestoresItsLease(string buttonName) => Run(rig =>
    {
        Frame();
        var host = (IAvaloniaPossessionHost)rig.Shell;
        var close = rig.Shell.Named<Button>(buttonName)!;
        var target = ((IPossessionHost)rig.Shell).Targets.Single(t => ReferenceEquals(t.Visual, close));
        var effect = PossessionAvaloniaHead.CreateEffects().Single(e => e.Id == "dodge");
        var attribution = new DodgeAttribution();
        var names = 0;
        var ctx = new PossessionContext { Host = rig.Shell, Attribution = attribution, Rung = PossessionRung.Drift,
            Intensity = PossessionIntensity.Eerie, Photosafe = false, Rng = new Random(3), ElapsedFraction = 0,
            Remaining = TimeSpan.FromMinutes(20), Name = (_, _) => names++ };
        var original = close.RenderTransform;
        var origin = close.RenderTransformOrigin;
        var transitions = close.Transitions;
        Assert.True(effect.CanApply(ctx, target));
        effect.ApplyAsync(ctx, target, CancellationToken.None).GetAwaiter().GetResult();
        Assert.True(effect.IsLive);
        Assert.False(PossessionLease.IsLeased(close));
        var home = host.BoundsOnGhostLayer(close)!.Value;
        var pointer = host.PossessionPointer;
        var now = DateTime.UtcNow;
        pointer.Move(new Point(home.X - 110, home.Center.Y), null, now);
        pointer.Move(new Point(home.X - 70, home.Center.Y), null, now.AddMilliseconds(50));
        Assert.Equal(1, attribution.Charges);
        Assert.Equal(1, names);
        Assert.True(PossessionLease.IsLeased(close));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1000));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1400));
        var moved = host.BoundsOnGhostLayer(close)!.Value;
        Assert.NotEqual(home.X, moved.X);
        Assert.Equal(home.Y, moved.Y, 2);
        Assert.True(moved.X >= 0 && moved.Right <= rig.Shell.Bounds.Width);
        PointerTestFrame.Render(rig.Shell);
        var hit = rig.Shell.InputHitTest(moved.Center) as Visual;
        Assert.True(ReferenceEquals(hit, close) || (hit != null && close.IsVisualAncestorOf(hit)));
        Assert.True(HeadlessInput.PressAt(close, rig.Shell, moved.Center).Pressed);
        // Two more steps are allowed, then further approaches do nothing.
        for (int i = 0; i < 2; i++)
        {
            var current = host.BoundsOnGhostLayer(close)!.Value;
            pointer.Move(current.Center, null, now.AddSeconds(i + 2));
            rig.Frames.Frame(TimeSpan.FromMilliseconds(1500 + 500 * i));
            rig.Frames.Frame(TimeSpan.FromMilliseconds(1900 + 500 * i));
            Assert.NotEqual(current.X, host.BoundsOnGhostLayer(close)!.Value.X);
        }
        var afterThree = host.BoundsOnGhostLayer(close)!.Value;
        pointer.Move(afterThree.Center, null, now.AddSeconds(5));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(3000));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(3500));
        Assert.Equal(afterThree, host.BoundsOnGhostLayer(close)!.Value);
        Assert.Equal(1, attribution.Charges);
        var returning = effect.UndoAsync(TimeSpan.FromMilliseconds(600));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(4000));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(4300));
        Assert.False(returning.IsCompleted);
        // Panic can interrupt an in-progress eased return synchronously.
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.True(returning.IsCompleted);
        Assert.False(effect.IsLive);
        Assert.False(PossessionLease.IsLeased(close));
        Assert.Same(original, close.RenderTransform);
        Assert.Equal(origin, close.RenderTransformOrigin);
        Assert.Same(transitions, close.Transitions);
        Assert.Equal(1, attribution.Releases);
        Assert.False(target.IsLive);
        pointer.Move(new Point(home.X, home.Center.Y), null, now.AddSeconds(2));
        Assert.Equal(1, attribution.Charges);
    });

    [Fact]
    public void DirectorBooksAndStartsDodgeThenPanicRestoresIt() => Run(rig =>
    {
        Frame();
        var host = (IAvaloniaPossessionHost)rig.Shell;
        var close = rig.Shell.Named<Button>("BtnClose")!;
        var target = ((IPossessionHost)rig.Shell).Targets.Single(t => ReferenceEquals(t.Visual, close));
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        rig.Clock = rig.Clock.AddMinutes(4);
        Tick(rig.Lockdown);
        Assert.True(rig.Director.CurrentRung >= PossessionRung.Drift);
        rig.Director.RequestReactive("dodge", target);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, rig.Director.LiveEffectCount);
        Assert.True(target.IsLive);
        var home = host.BoundsOnGhostLayer(close)!.Value;
        var now = DateTime.UtcNow;
        host.PossessionPointer.Move(new Point(home.X - 110, home.Center.Y), null, now);
        host.PossessionPointer.Move(new Point(home.X - 70, home.Center.Y), null, now.AddMilliseconds(50));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1000));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1450));
        Dispatcher.UIThread.RunJobs();
        Assert.True(PossessionLease.IsLeased(close));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1500));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1900));
        Assert.NotEqual(home.X, host.BoundsOnGhostLayer(close)!.Value.X);
        rig.Director.UndoAll();
        Assert.False(PossessionLease.IsLeased(close));
        Assert.False(target.IsLive);
        Assert.Equal(0, rig.Director.LiveEffectCount);
    });

    [Fact]
    public void TheDirectorHauntsTheShell_AndPanicGivesEverythingBack() => Run(rig =>
    {
        AvApp.Possession = rig.Director;
        rig.Shell.ShowTab("settings");
        Frame();
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        Frame();
        Assert.True(rig.Director.IsHaunting);

        // Beats until the deck lands on a control a head effect may take (Lockdown's own room is off limits).
        for (int i = 0; i < 40 && rig.Director.LiveEffectCount == 0; i++)
        {
            DueNow(rig.Director);
            Tick(rig.Lockdown);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(1, rig.Director.LiveEffectCount);
        var layer = rig.Shell.PossessionGhostLayer!;
        Assert.Contains(layer.Children, c => c is Border);   // the ember charge, before anything moves

        for (int ms = 0; ms <= 600; ms += 50) { rig.Frames.Frame(TimeSpan.FromMilliseconds(5000 + ms)); Dispatcher.UIThread.RunJobs(); }
        var leased = ((IPossessionHost)rig.Shell).Targets.Where(t => PossessionLease.IsLeased(t.Visual as Control)).ToList();
        Assert.Single(leased);

        var panic = PanicSurfaces.All.Single(s => s.Id == "possession");
        panic.Stop(rig.Shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, rig.Director.LiveEffectCount);
        Assert.False(PossessionLease.IsLeased(leased[0].Visual as Control));
        Assert.DoesNotContain(layer.Children, c => c is Border);   // outline and charge stripped
    });

    [Fact]
    public void RulesCard_FirstLockdownOnly_FlagSpentWhenTheCardOpens() => Run(rig =>
    {
        StartupLadder.Hold();   // the launcher holds the room: the card goes to the Inbox, as mid-session
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        Frame();
        Assert.True(StartupLadder.Inbox.Contains("intro:possession"));
        Assert.False(CoreSettings.Current.LockdownPossessionIntroSeen);   // a row waved away unread keeps the offer
        var row = StartupLadder.Inbox.Items.Single(i => i.Key == "intro:possession");
        row.Open();
        Assert.True(CoreSettings.Current.LockdownPossessionIntroSeen);

        StartupLadder.ResetForTests();
        StartupLadder.Inbox.Items.Clear();
        rig.Lockdown.Deactivate();
        Frame();
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        Frame();
        Assert.False(StartupLadder.Inbox.Contains("intro:possession"));
    }, s => s.LockdownPossessionIntroSeen = false);

    [Fact]
    public void Consent_NamesPossession_AndFullDokiScaresOnlyWhenThisHeadHasThem()
    {
        var cfg = new AppSettings { LockdownPossessionEnabled = true, LockdownPossessionIntensity = 1 };
        var text = LockdownTabView.LockdownWarning(20, cfg, windows: false);
        Assert.Contains("- Possession is ON (Eerie): the app's own UI will misbehave, on purpose.", text);
        Assert.Contains("Ember glow = it was Lockdown, not a bug.", text);

        cfg.LockdownPossessionIntensity = 2;
        var sb = new System.Text.StringBuilder();
        LockdownTabView.AppendPossessionConsent(sb, cfg, fullDokiScares: false);
        Assert.Contains("(Full Doki)", sb.ToString());
        Assert.DoesNotContain("fake-crash", sb.ToString());
        sb.Clear();
        LockdownTabView.AppendPossessionConsent(sb, cfg, fullDokiScares: true);
        Assert.Contains("- Full Doki adds themed fake-crash scares. They are theatre.", sb.ToString());
        // The catalog now carries the themed dialog.
        Assert.True(PossessionAvaloniaHead.HasFullDokiEffects());
        Assert.Contains("fake-crash", LockdownTabView.LockdownWarning(20, cfg, windows: false));

        cfg.LockdownPossessionEnabled = false;
        Assert.DoesNotContain("Possession", LockdownTabView.LockdownWarning(20, cfg, windows: false));
    }

    private sealed class ImmediateAttribution : IPossessionAttribution
    {
        public System.Threading.Tasks.Task ChargeAsync(object target, CancellationToken ct, int durationMs = 400) => System.Threading.Tasks.Task.CompletedTask;
        public IDisposable Possess(object target) => new CancellationTokenSource();
        public void EdgePulse(double strength) { }
        public bool AnyPossessed => false;
        public void ReleaseAll() { }
    }

    private static PossessionContext DokiContext(Rig rig, PossessionRung rung, PossessionIntensity intensity) => new()
    {
        Host = rig.Shell, Attribution = new ImmediateAttribution(), Rung = rung, Intensity = intensity,
        Photosafe = false, Rng = new Random(1), ElapsedFraction = 0.9,
        Remaining = TimeSpan.FromMinutes(1), Name = (_, _) => { }
    };

    [Theory]
    [InlineData(PossessionRung.Collapse, PossessionIntensity.FullDoki)]
    [InlineData(PossessionRung.ItKnows, PossessionIntensity.Eerie)]
    public void DokiDialog_RefusesBelowBothGates(PossessionRung rung, PossessionIntensity intensity) => Run(rig =>
    {
        var effect = new DokiDialogEffect();
        Assert.False(effect.CanApply(DokiContext(rig, rung, intensity), null));
        Assert.True(effect.CanApply(DokiContext(rig, PossessionRung.ItKnows, PossessionIntensity.FullDoki), null));
    });

    [Fact]
    public void DokiDialog_RendersLocalizedTextAndClosesOnUndo() => Run(rig =>
    {
        var effect = new DokiDialogEffect();
        effect.ApplyAsync(DokiContext(rig, PossessionRung.ItKnows, PossessionIntensity.FullDoki), null, CancellationToken.None).GetAwaiter().GetResult();
        var dialog = Assert.IsType<Window>(effect.Dialog);
        Frame();
        Assert.True(dialog.IsVisible);
        Assert.Equal(WindowDecorations.None, dialog.SystemDecorations);
        Assert.NotEqual("possession_doki_message", Loc.Get("possession_doki_message"));
        Assert.NotEqual("possession_doki_progress", Loc.Get("possession_doki_progress"));
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("possession_doki_message"));
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text != null && (t.Text.StartsWith(Loc.Get("possession_doki_progress"), StringComparison.Ordinal) || t.Text == Loc.Get("possession_doki_done")));
        Assert.DoesNotContain("possession_doki_", dialog.Title);
        Assert.NotNull(dialog.CaptureRenderedFrame());
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
        Assert.False(dialog.IsVisible);
        Assert.Null(effect.Dialog);
    });

    [Fact]
    public void DokiDialog_ProgressAdmitsNothingHappened_AndFailsafeCloses() => Run(rig =>
    {
        var effect = new DokiDialogEffect();
        effect.ApplyAsync(DokiContext(rig, PossessionRung.ItKnows, PossessionIntensity.FullDoki), null, CancellationToken.None).GetAwaiter().GetResult();
        var dialog = Assert.IsType<Window>(effect.Dialog);
        effect.StepProgress(2200);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("possession_doki_done"));
        effect.StepProgress(6000);
        Assert.False(dialog.IsVisible);
        Assert.Null(effect.Dialog);
        effect.UndoAsync(TimeSpan.Zero).GetAwaiter().GetResult();
    });

    [Fact]
    public void DokiDialog_DirectorUndoAllClosesTheWindow() => Run(rig =>
    {
        rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
        Frame();
        typeof(PossessionDirector).GetField("_nextDue", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(rig.Director, DateTime.MaxValue);
        rig.Clock += TimeSpan.FromMinutes(27);
        Tick(rig.Lockdown);
        Frame();
        Assert.Equal(PossessionRung.ItKnows, rig.Director.CurrentRung);
        var effect = new DokiDialogEffect();
        var effects = (System.Collections.Generic.List<IPossessionEffect>)typeof(PossessionDirector).GetField("_effects", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(rig.Director)!;
        effects.Clear(); effects.Add(effect);
        rig.Director.RequestReactive("dokidialog", null);
        Dispatcher.UIThread.RunJobs();
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1000));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1500));
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.IsType<Window>(effect.Dialog);
        Assert.True(dialog.IsVisible);
        rig.Director.UndoAll();
        Assert.False(dialog.IsVisible);
        Assert.Null(effect.Dialog);
    }, s => s.LockdownPossessionIntensity = 2);

    [Fact]
    public void ARepeatedTripwire_PulsesTheEdges_AndShakesUnlessPhotosafe() => Run(rig =>
    {
        var prior = ConditioningControlPanel.Avalonia.Views.Deeper.ScreenShake.WindowsProvider;
        ConditioningControlPanel.Avalonia.Views.Deeper.ScreenShake.WindowsProvider = () => new Window[] { rig.Shell };
        try
        {
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
            Frame();
            Assert.True(rig.Director.IsHaunting);
            rig.Director.OnEscapeAttempted(new EscapeAttempt(EscapeKinds.Close, 2, 2, DateTime.Now));
            Dispatcher.UIThread.RunJobs();
            Assert.True(ConditioningControlPanel.Avalonia.Views.Deeper.ScreenShake.IsRunning);   // WPF App.ScreenShake (0.4, 250 ms)
            Assert.Contains(rig.Shell.PossessionGhostLayer!.Children, c => c is Border);          // the ember edge pulse
            ConditioningControlPanel.Avalonia.Views.Deeper.ScreenShake.Stop();

            // Photosafe: the pulse still names it, the shake never comes (the director gates it, WPF too).
            CoreSettings.Current.LockdownPhotosafe = true;
            rig.Lockdown.Deactivate();
            Frame();
            rig.Lockdown.Activate(TimeSpan.FromMinutes(30));
            Frame();
            rig.Director.OnEscapeAttempted(new EscapeAttempt(EscapeKinds.Close, 3, 3, DateTime.Now.AddSeconds(5)));
            Dispatcher.UIThread.RunJobs();
            Assert.False(ConditioningControlPanel.Avalonia.Views.Deeper.ScreenShake.IsRunning);
        }
        finally
        {
            ConditioningControlPanel.Avalonia.Views.Deeper.ScreenShake.Stop();
            ConditioningControlPanel.Avalonia.Views.Deeper.ScreenShake.WindowsProvider = prior;
        }
    });

    [Fact]
    public void EdgePulses_StackToThree_StillModeIsCapped_AndReleaseAllLeavesNothing() => Run(rig =>
    {
        Frame();
        var host = (IAvaloniaPossessionHost)rig.Shell;
        var photosafe = false;
        var attribution = new EmberAttribution(host, () => photosafe);
        for (int i = 0; i < 5; i++) attribution.EdgePulse(0.9);
        Assert.Equal(EmberAttribution.MaxLivePulses, attribution.LivePulseCount);
        rig.Frames.Frame(TimeSpan.FromMilliseconds(100));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(300));   // 40% of 500 ms: at the peak
        var pulse = rig.Shell.PossessionGhostLayer!.Children.OfType<Border>().First();
        Assert.Equal(0.9, pulse.Opacity, 2);
        rig.Frames.Frame(TimeSpan.FromMilliseconds(700));
        Assert.Equal(0, attribution.LivePulseCount);

        photosafe = true;   // still mode: strength capped at 0.5, 900 ms
        attribution.EdgePulse(1.0);
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1000));
        rig.Frames.Frame(TimeSpan.FromMilliseconds(1360));
        Assert.Equal(0.5, rig.Shell.PossessionGhostLayer!.Children.OfType<Border>().Single().Opacity, 2);

        var el = ((IPossessionHost)rig.Shell).Targets.First(t => t.Role == PossessionRole.Button).Visual;
        var handle = attribution.Possess(el);
        Assert.True(attribution.AnyPossessed);
        attribution.ReleaseAll();
        Assert.False(attribution.AnyPossessed);
        Assert.Equal(0, attribution.LiveOverlayCount);
        Assert.DoesNotContain(rig.Shell.PossessionGhostLayer!.Children, c => c is Border);
        handle.Dispose();   // a late dispose after ReleaseAll is harmless
    });
}
