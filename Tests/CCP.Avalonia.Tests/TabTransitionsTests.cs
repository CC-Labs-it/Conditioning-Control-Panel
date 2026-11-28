using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using ConditioningControlPanel.Services;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.ChromeFx.cs AnimateTabIn and MainWindow.TabNavigation.cs
/// StartNavDoorHeaderPulse, driven through ShowTab and the startup seam.</summary>
public sealed class TabTransitionsTests
{
    private static Task Run(Action<MainShellWindow> check, Action<MainShellWindow>? beforeShow = null) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var (offline, motion, perf) = (s.OfflineMode, s.MotionLevel, s.PerformanceMode);
        var (seenPrograms, seenDeeper, deeper) = (s.HasSeenProgramsTab, s.HasSeenDeeperTab, s.EnableDeeper);
        var seen = s.SeenFeatureIntros.ToArray();
        var lastTabs = s.NavLastTabBySection;
        s.OfflineMode = true;
        s.PerformanceMode = false;
        s.MotionLevel = MotionLevel.Full;
        foreach (var key in FeatureIntros.All.Keys)
            if (!s.SeenFeatureIntros.Contains(key)) s.SeenFeatureIntros.Add(key);
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            beforeShow?.Invoke(shell);
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            check(shell);
        }
        finally
        {
            shell?.Close();
            (s.OfflineMode, s.MotionLevel, s.PerformanceMode) = (offline, motion, perf);
            (s.HasSeenProgramsTab, s.HasSeenDeeperTab, s.EnableDeeper) = (seenPrograms, seenDeeper, deeper);
            s.SeenFeatureIntros.Clear();
            s.SeenFeatureIntros.AddRange(seen);
            s.NavLastTabBySection = lastTabs;
        }
        return Task.CompletedTask;
    });

    // Restack b118 (#3496): the PR's panic route (a "program-cue" PanicSurfaces row) was left out; WPF panic
    // does not stop the decorative Programs pulse (MainWindow.ProgramsTab.cs:66, TabNavigation.cs:453).
    [Theory]
    [InlineData("hide")]
    [InlineData("motion")]
    public Task ProgramsCueParksOnHideAndMotion(string route) => Run(shell =>
    {
        CoreSettings.Current.HasSeenProgramsTab = false;
        shell.ShowTab("settings");
        shell.StartFirstVisitPulses();
        Assert.True(shell.NavDoorHeaderPulsing("programs"));
        if (route == "hide") shell.Hide();
        else if (route == "motion")
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        }
        Assert.False(shell.NavDoorHeaderPulsing("programs"));
        Assert.Equal(1, shell.Named<Button>("DoorYou")!.Opacity);
    });

    [Fact]
    public Task ShowTabFadesTheIncomingTabInUnlessMotionIsOff() => Run(shell =>
    {
        shell.ShowTab("settings");
        shell.ShowTab("quests");
        Assert.Same(shell.Named<Control>("QuestsTab"), shell.EnteringTab);

        // WPF :236: Reduced keeps the crossfade.
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        shell.ShowTab("settings");
        Assert.Same(shell.Named<Control>("SettingsTab"), shell.EnteringTab);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;

        // WPF :306/:677: the Lockdown plate breathes only while its tab is the one shown.
        shell.ShowTab("lockdown");
        Assert.True(shell.LockdownPulsing);
        shell.ShowTab("settings");
        Assert.False(shell.LockdownPulsing);

        // WPF :226: Motion Off is an instant swap, no clocks at all.
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        shell.ShowTab("achievements");
        Assert.Null(shell.EnteringTab);
        Assert.Equal(1.0, shell.Named<Control>("AchievementsTab")!.Opacity);
    });

    [Fact]
    public Task FirstVisitPulsesAnnounceUnseenPagesAndTheProgramsVisitSpendsIt() => Run(shell =>
    {
        var s = CoreSettings.Current;
        shell.ShowTab("settings");
        s.HasSeenProgramsTab = false;
        s.EnableDeeper = true;
        s.HasSeenDeeperTab = false;

        shell.StartFirstVisitPulses();
        Assert.True(shell.NavDoorHeaderPulsing("programs"));
        Assert.True(shell.NavDoorHeaderPulsing("deeper"));

        shell.ShowTab("programs");
        Dispatcher.UIThread.RunJobs();
        Assert.True(s.HasSeenProgramsTab);
        Assert.False(shell.NavDoorHeaderPulsing("programs"));
        Assert.Equal(1.0, shell.Named<Button>("DoorYou")!.Opacity);

        // WPF :839: the section already lit gets no rail pulse.
        shell.StopNavDoorHeaderPulse("deeper");
        shell.ShowTab("playeyes");
        Assert.False(shell.StartNavDoorHeaderPulse("deeper"));

        // WPF ProgramsTab.cs:54: the Programs announcement is an ambient loop, Reduced drops it.
        shell.ShowTab("settings");
        s.HasSeenProgramsTab = false;
        s.MotionLevel = MotionLevel.Reduced;
        shell.StartFirstVisitPulses();
        Assert.False(shell.NavDoorHeaderPulsing("programs"));
        Assert.True(shell.NavDoorHeaderPulsing("deeper"));
    });

    [Fact]
    public Task ReducedBeforeStartupCancelsOnlyProgramsDelay()
    {
        var pending = new System.Collections.Generic.List<Pending>();
        return Run(shell =>
        {
            var s = CoreSettings.Current;
            s.HasSeenProgramsTab = s.HasSeenDeeperTab = false;
            s.EnableDeeper = true;
            shell.ShowTab("settings");
            Assert.False(shell.NavDoorHeaderPulsing("deeper"));
            s.MotionLevel = MotionLevel.Reduced;
            ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            foreach (var delay in pending) delay.Fire();
            Assert.True(shell.NavDoorHeaderPulsing("deeper"));
            Assert.False(shell.NavDoorHeaderPulsing("programs"));
            Assert.Single(pending.Where(p => p.Disposed));
        }, shell => shell.ScheduleOnce = (fire, delay) =>
        {
            var scheduled = new Pending(fire, delay);
            pending.Add(scheduled);
            return scheduled;
        });
    }

    [Fact]
    public Task OpeningTheWindowArmsTheFirstVisitPulsesOnceAfterOnePointTwoSeconds()
    {
        var armed = new System.Collections.Generic.List<Pending>();
        return Run(shell =>
        {
            var s = CoreSettings.Current;
            s.HasSeenProgramsTab = false;
            s.EnableDeeper = true;
            s.HasSeenDeeperTab = false;
            shell.ShowTab("settings");

            // WPF xaml.cs:3891,3912: independent 1.2 s delays, armed only once.
            Assert.Equal(2, armed.Count);
            Assert.All(armed, p => Assert.Equal(TimeSpan.FromMilliseconds(1200), p.Delay));
            Assert.False(shell.NavDoorHeaderPulsing("deeper"));
            foreach (var delay in armed) delay.Fire();
            Assert.True(shell.NavDoorHeaderPulsing("deeper"));
            Assert.True(shell.NavDoorHeaderPulsing("programs"));

            shell.Hide();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, armed.Count);
        }, shell => shell.ScheduleOnce = (fire, delay) =>
        {
            var scheduled = new Pending(fire, delay);
            armed.Add(scheduled);
            return scheduled;
        });
    }

    [Fact]
    public Task AHeadlessRenderArmsNoFirstVisitPulse()
    {
        // --render-all on a slow host fired the 1.2 s timer inside CaptureRenderedFrame and drew
        // the Play and You rows mid-dip (Linux beta shots). A render draws the rail at rest.
        var armed = new System.Collections.Generic.List<TimeSpan>();
        return Run(shell =>
        {
            Assert.Empty(armed);
            Assert.False(shell.NavDoorHeaderPulsing("deeper"));
            Assert.Equal(1.0, shell.Named<Button>("DoorPlay")!.Opacity);
        }, shell =>
        {
            global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
            shell.Closed += (_, _) => global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = false;
            shell.ScheduleOnce = (fire, delay) => { armed.Add(delay); return new Pending(fire, delay); };
        });
    }

    // A stepped entrance lets pointer tests sample resting geometry without sleeping.
    private static void Step(MainShellWindow shell, double milliseconds) => shell.StepTabTransition(milliseconds);

    // The compositor draws the entrance; these are the animations it was handed for one control.
    private static MainShellWindow.EntranceTrack[] Tracks(MainShellWindow shell, Control target, string property) =>
        shell.EntranceTracks.Where(t => t.Target == target && t.Property == property).ToArray();

    [Fact]
    public Task FullEntranceUsesRailDirectionAndSettlesBeforePointerInput() => Run(shell =>
    {
        using var time = CompositorTime.Freeze(shell);
        shell.ShowTab("quests");
        var quests = shell.Named<Control>("QuestsTab")!;
        var slide = Assert.Single(Tracks(shell, quests, "Translation"));
        Assert.Equal((0, 200), (slide.DelayMs, slide.DurationMs));
        Assert.Equal(1, quests.Opacity); // the client value stays at rest; the compositor animates its copy
        // What the compositor draws (WPF ChromeFx.cs:235-291): 12 px off and transparent at the switch,
        // QuadraticEaseOut at half time, at rest after 200 ms.
        time.RenderAt(0);
        Assert.Equal(new Vector3D(12, 0, 0), CompositorTime.Translation(quests));
        Assert.Equal(0, CompositorTime.Opacity(quests));
        time.RenderAt(100);
        Assert.Equal(3, CompositorTime.Translation(quests).X, 3);
        Assert.Equal(0.75, CompositorTime.Opacity(quests), 3);
        time.RenderAt(620);
        Assert.Equal(default, CompositorTime.Translation(quests));
        Assert.Equal(1, CompositorTime.Opacity(quests));
        Step(shell, 620);
        Assert.Empty(shell.EntranceTracks);
        Assert.Equal(1, quests.Opacity);
        shell.ShowTab("achievements");
        time.Mark();
        shell.ShowTab("quests");
        Assert.Equal(-12, Assert.Single(Tracks(shell, quests, "Translation")).FromX);
        time.RenderAt(0);
        Assert.Equal(new Vector3D(-12, 0, 0), CompositorTime.Translation(quests));
        time.RenderAt(100);
        Assert.Equal(-3, CompositorTime.Translation(quests).X, 3);
        time.Mark();
        shell.ShowTab("quests"); // same key has no direction: WPF rises
        slide = Assert.Single(Tracks(shell, quests, "Translation"));
        Assert.Equal((0, 12), (slide.FromX, slide.FromY));
        time.RenderAt(0);
        Assert.Equal(new Vector3D(0, 12, 0), CompositorTime.Translation(quests));
        time.RenderAt(620);
        Assert.Equal(default, CompositorTime.Translation(quests));
        shell.ShowTab("settings"); // browser airspace never slides
        Assert.Empty(Tracks(shell, shell.SettingsPage!, "Translation"));
        Assert.Empty(Tracks(shell, shell.Named<Control>("SettingsTab")!, "Translation"));
    });

    [Fact]
    public Task EntranceRunsOnTheCompositorAndLeavesTheUiThreadIdle() => Run(shell =>
    {
        shell.ShowTab("settings");
        shell.CompleteTabTransition();
        shell.ShowTab("quests");
        var quests = shell.Named<Control>("QuestsTab")!;
        // A UI-thread clock committed a batch every frame; on a CPU renderer the next click waited behind it.
        Assert.DoesNotContain("MainShellWindow@", ConditioningControlPanel.Avalonia.Controls.Fx.TopLevelFrameSource.For(shell).DescribeSubscribers());
        Assert.Equal(1, quests.Opacity); // the client value stays at rest; the compositor animates its copy
        var fade = Assert.Single(Tracks(shell, quests, "Opacity"));
        Assert.Equal((0, 0, 200), (fade.FromX, fade.DelayMs, fade.DurationMs));

        // Early settlement must stop the compositor animation, not just the client value.
        using var time = CompositorTime.Freeze(shell);
        time.RenderAt(0);
        Assert.Equal(0, CompositorTime.Opacity(quests));
        shell.CompleteTabTransition();
        time.RenderAt(1);
        Assert.Equal(1, CompositorTime.Opacity(quests));
    });

    [Fact]
    public Task LeavingTabFadesWithoutASnapshotOrRestartingServicesAndRapidReturnIsClickable() => Run(shell =>
    {
        shell.ShowTab("quests");
        Step(shell, 620);
        var quests = shell.Named<Control>("QuestsTab")!;
        var host = (Panel)quests.Parent!;
        var children = host.Children.Count;
        shell.ShowTab("achievements");
        // WPF ChromeFx.cs:303-339, with logical visibility kept false throughout.
        Assert.Same(quests, shell.LeavingTab);
        Assert.False(quests.IsHitTestVisible);
        Assert.False(quests.IsVisible);
        Assert.Equal(children, host.Children.Count);
        Assert.Empty(host.Children.OfType<Image>());
        Step(shell, 50);
        Assert.Same(quests, shell.LeavingTab);
        Assert.False(quests.IsVisible);
        shell.ShowTab("quests"); // an unfinished entrance never wins a late completion
        Step(shell, 620);
        Assert.True(quests.IsVisible);
        Assert.True(quests.IsHitTestVisible);
        Assert.Equal(1, quests.Opacity);
        Assert.Equal(1, shell.Named<Control>("AchievementsTab")!.Opacity);
    });

    [Fact]
    public Task OutgoingFadeStartsVisibleOnTheFirstCompositorFrame() => Run(shell =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        shell.ShowTab("quests");
        shell.CompleteTabTransition();
        var outgoing = shell.Named<Control>("QuestsTab")!;
        using var time = CompositorTime.Freeze(shell);
        shell.ShowTab("achievements");
        time.RenderAt(0);
        Assert.False(outgoing.IsVisible);
        Assert.True(CompositorTime.Visible(outgoing));
        shell.CompleteTabTransition();
    });

    [Fact]
    public Task HiddenOutgoingTabStillDrawsUntilFadeCleanup() => Run(shell =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        var outgoing = shell.Named<ContentControl>("QuestsTab")!;
        var incoming = shell.Named<ContentControl>("AchievementsTab")!;
        var saved = (outgoing.Content, incoming.Content);
        shell.TabClock = new SteppedTime();
        try
        {
            outgoing.Content = new Border { Background = Brushes.Magenta };
            incoming.Content = new Border { Background = Brushes.Black };
            shell.ShowTab("quests");
            shell.CompleteTabTransition();
            using var before = shell.CaptureRenderedFrame(); // materialize retained drawing
            var point = outgoing.TranslatePoint(new Point(outgoing.Bounds.Width / 2, outgoing.Bounds.Height / 2), shell)!.Value;
            Assert.NotEqual(0u, Rgb(before!, point));
            shell.ShowTab("achievements");
            // CaptureRenderedFrame drains many frames. Sample exactly one compositor frame.
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var fading = shell.GetLastRenderedFrame();
            Assert.False(outgoing.IsVisible);
            var pixel = Rgb(fading!, point);
            // A real compositor frame may already be partway through the fade. Require
            // magenta dominance rather than assuming its first sample is fully opaque.
            var green = (pixel >> 8) & 0xFF;
            Assert.True((pixel & 0xFF) > green + 32 && ((pixel >> 16) & 0xFF) > green + 32,
                $"Expected the outgoing magenta page, got {pixel:X6}");
            shell.CompleteTabTransition();
            using var after = shell.CaptureRenderedFrame();
            Assert.Equal(0u, Rgb(after!, point)); // the incoming black page owns the pixel now
        }
        finally { shell.CompleteTabTransition(); (outgoing.Content, incoming.Content) = saved; }
    });

    private static uint Rgb(global::Avalonia.Media.Imaging.Bitmap bitmap, Point point)
    {
        var buffer = System.Runtime.InteropServices.Marshal.AllocHGlobal(4);
        try
        {
            var scale = bitmap.PixelSize.Width / bitmap.Size.Width;
            bitmap.CopyPixels(new PixelRect((int)(point.X * scale), (int)(point.Y * scale), 1, 1), buffer, 4, 4);
            return (uint)System.Runtime.InteropServices.Marshal.ReadInt32(buffer) & 0xFFFFFFu;
        }
        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(buffer); }
    }

    [Fact]
    public Task OutgoingFadeExpiresAt100MsCutsAirspaceAndCancelsOnOffAndClose() => Run(shell =>
    {
        shell.ShowTab("quests");
        shell.CompleteTabTransition();
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        shell.ShowTab("achievements");
        Assert.NotNull(shell.LeavingTab);
        Step(shell, 99);
        Assert.NotNull(shell.LeavingTab);
        Step(shell, 100);
        Assert.Null(shell.LeavingTab);
        shell.ShowTab("settings");
        shell.ShowTab("quests");
        Assert.Null(shell.LeavingTab); // WPF ChromeFx.cs:241: native airspace cuts
        shell.ShowTab("quests");
        Assert.Null(shell.LeavingTab); // aliases and same panel never fade themselves
        shell.ShowTab("achievements");
        Assert.NotNull(shell.LeavingTab);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        shell.ShowTab("quests");
        Assert.Null(shell.LeavingTab);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        shell.ShowTab("achievements");
        Assert.NotNull(shell.LeavingTab);
        shell.Close();
        Assert.Null(shell.LeavingTab);
    });

    [Fact]
    public Task EntranceFramesReadElapsedTimeFromTheTabClock() => Run(shell =>
    {
        var time = new SteppedTime();
        shell.TabClock = time;
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        shell.ShowTab("quests");
        shell.ShowTab("achievements");
        var tab = shell.Named<Control>("AchievementsTab")!;
        Assert.Equal(0.75, Assert.Single(Tracks(shell, tab, "Opacity")).At(100), 5); // QuadraticEaseOut at half of 200 ms
        time.Now = TimeSpan.FromMilliseconds(99).Ticks;
        shell.TickTabTransition();
        Assert.NotNull(shell.LeavingTab);
        time.Now = TimeSpan.FromMilliseconds(100).Ticks;
        shell.TickTabTransition();
        Assert.Null(shell.LeavingTab);
        Assert.Same(tab, shell.EnteringTab);
        time.Now = TimeSpan.FromMilliseconds(620).Ticks;
        shell.TickTabTransition();
        Assert.Equal(1, tab.Opacity);
        Assert.Null(shell.EnteringTab);
    });

    [Fact]
    public Task ReducedCrossfadesAndOffCancelsAllEntranceGeometry() => Run(shell =>
    {
        shell.ShowTab("quests");
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        using (var time = CompositorTime.Freeze(shell))
        {
            shell.ShowTab("achievements");
            var fade = Assert.Single(shell.EntranceTracks); // the crossfade only: no slide, no card stagger
            Assert.Equal((shell.Named<Control>("AchievementsTab"), "Opacity"), (fade.Target, fade.Property));
            var entering = shell.Named<Control>("AchievementsTab")!;
            time.RenderAt(0);
            Assert.Equal(0, CompositorTime.Opacity(entering));
            Assert.Equal(default, CompositorTime.Translation(entering));
            time.RenderAt(100);
            Assert.Equal(0.75, CompositorTime.Opacity(entering), 3);
            Assert.Equal(default, CompositorTime.Translation(entering));
        }
        var tab = shell.Named<Control>("AchievementsTab")!;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        shell.ShowTab("quests");
        Assert.Equal(1, tab.Opacity);
        Assert.Empty(((Panel)tab.Parent!).Children.OfType<Image>());
        Assert.Null(shell.EnteringTab);
        Assert.Empty(shell.EntranceTracks);
    });

    [Fact]
    public Task IntakePassRefreshesOnHomeEntryAndStateChangesAndParksOnLeave() => Run(shell =>
    {
        var loggedIn = CoreAccount.IsLoggedInProvider;
        var premium = CoreAccount.HasPremiumAccessProvider; // tip: IntakePassService.IsPremium reads HasPremiumAccess
        try
        {
            CoreAccount.IsLoggedInProvider = () => true;
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            shell.ShowTab("quests");
            shell.ShowTab("settings");
            var face = shell.SettingsPage!.FindControl<Border>("IntakePassFace")!;
            var logo = shell.SettingsPage.FindControl<Border>("LogoFaceLogo")!;
            Assert.True(face.IsVisible);
            Assert.False(logo.IsVisible);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("intake_pass_card_cta"),
                shell.SettingsPage.FindControl<TextBlock>("IntakePassCta")!.Text);
            CoreAccount.HasPremiumAccessProvider = () => true;
            global::ConditioningControlPanel.Avalonia.App.IntakePass.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.False(face.IsVisible);
            Assert.True(logo.IsVisible);
            CoreAccount.HasPremiumAccessProvider = () => false;
            CoreAccount.IsLoggedInProvider = () => false;
            global::ConditioningControlPanel.Avalonia.App.IntakePass.RaiseChanged();
            Dispatcher.UIThread.RunJobs();
            Assert.False(face.IsVisible); // a signed-out account never gets a ticket
            CoreAccount.IsLoggedInProvider = () => true;
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            shell.ShowTab("settings");
            Assert.False(face.IsVisible); // WPF holds the wordmark for 8 s before turning
            shell.ShowTab("quests");
            var group = Assert.IsType<TransformGroup>(shell.SettingsPage.FindControl<Grid>("LogoFlipHost")!.RenderTransform);
            Assert.Equal(1, Assert.Single(group.Children.OfType<ScaleTransform>()).ScaleX);
        }
        finally
        {
            CoreAccount.IsLoggedInProvider = loggedIn;
            CoreAccount.HasPremiumAccessProvider = premium;
        }
    });

    [Fact]
    public Task CardStaggerCapsSlotsPreservesTransformsAndSkipsHiddenOrDimmedCards() => Run(shell =>
    {
        var tab = shell.Named<ContentControl>("QuestsTab")!;
        var content = tab.Content;
        var cards = new StackPanel();
        var authored = new ScaleTransform(2, 2);
        var dim = new Border { Opacity = 0.6 };
        var hidden = new Border { IsVisible = false };
        for (var i = 0; i < 10; i++) cards.Children.Add(new Border());
        cards.Children[0].RenderTransform = authored;
        cards.Children.Insert(1, dim);
        cards.Children.Insert(2, hidden);
        tab.Content = new Border { Child = cards };
        var time = CompositorTime.Freeze(shell);
        try
        {
            shell.ShowTab("quests");
            Assert.Same(authored, cards.Children[0].RenderTransform);
            Assert.Empty(Tracks(shell, cards.Children[0], "Translation")); // an authored transform is left alone
            Assert.Empty(Tracks(shell, cards.Children[10], "Opacity")); // ninth card is outside Take(8)
            Assert.Empty(Tracks(shell, dim, "Opacity"));
            Assert.Empty(Tracks(shell, hidden, "Opacity"));
            // What the compositor draws (WPF MotionFx.cs:106-138): the first card fades from 0 at the switch while
            // the second eligible card holds its base value until its 40 ms BeginTime, then starts at 0 and 10 px.
            var (first, second) = (cards.Children[0], cards.Children[3]);
            time.RenderAt(0);
            Assert.Equal(0, CompositorTime.Opacity(first));
            Assert.Equal(default, CompositorTime.Translation(first));
            Assert.Equal((1f, default(Vector3D)), (CompositorTime.Opacity(second), CompositorTime.Translation(second)));
            time.RenderAt(40);
            Assert.InRange(CompositorTime.Opacity(first), 0.01, 0.99);
            Assert.Equal((1f, default(Vector3D)), (CompositorTime.Opacity(second), CompositorTime.Translation(second)));
            time.RenderAt(41);
            Assert.InRange(CompositorTime.Opacity(second), 0, 0.02);
            Assert.InRange(CompositorTime.Translation(second).Y, 9.8, 10);
            time.RenderAt(250); // slots 6 and 7 share the 240 ms cap
            Assert.InRange(CompositorTime.Opacity(cards.Children[9]), 0.01, 0.99);
            Assert.Equal(CompositorTime.Opacity(cards.Children[8]), CompositorTime.Opacity(cards.Children[9]));
            Assert.Equal(CompositorTime.Translation(cards.Children[8]), CompositorTime.Translation(cards.Children[9]));
            Assert.Equal(1, CompositorTime.Opacity(cards.Children[10]));
            Assert.Equal(0.6, CompositorTime.Opacity(dim), 5);
            Assert.Equal(0.6, dim.Opacity);
            Assert.False(hidden.IsVisible);
            time.RenderAt(620);
            Assert.All(cards.Children.Where(c => c != dim && c != hidden), c => Assert.Equal(1, CompositorTime.Opacity(c)));
            Assert.All(cards.Children.Skip(1).Where(c => c != hidden), c => Assert.Equal(default, CompositorTime.Translation(c)));
            shell.CompleteTabTransition();
            Assert.Equal(8, MainShellWindow.FindStaggerTargets(tab).Count);
            Assert.All(cards.Children.Where(c => c != dim), c => Assert.Equal(1, c.Opacity));
        }
        finally { time.Dispose(); shell.CompleteTabTransition(); tab.Content = content; }
    });

    [Fact]
    public Task IntakeTurnSwapsAtTheThinPointAndCtaUsesTheSharedBeat() => Run(shell =>
    {
        var saved = (CoreAccount.IsLoggedInProvider, CoreAccount.HasPremiumAccessProvider);
        var savedTime = ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time;
        var time = new SteppedTime();
        ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = time;
        try
        {
            CoreAccount.IsLoggedInProvider = () => true;
            CoreAccount.HasPremiumAccessProvider = () => false;
            shell.ShowTab("settings");
            var group = Assert.IsType<TransformGroup>(shell.SettingsPage!.FindControl<Grid>("LogoFlipHost")!.RenderTransform);
            var scale = Assert.Single(group.Children.OfType<ScaleTransform>());
            void At(double seconds)
            {
                time.Now = TimeSpan.FromSeconds(seconds).Ticks;
                ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            }
            At(8.052); // first 105/2 ms close finishes: edge-on, face swaps
            Assert.Equal(0, scale.ScaleX, 5);
            Assert.True(shell.IntakePassShowingCard);
            At(9.058); // five half-turns complete; the card is square
            Assert.Equal(1, scale.ScaleX, 5);
            Assert.True(shell.IntakePassShowingCard);
            var cta = shell.SettingsPage!.FindControl<TextBlock>("IntakePassCta")!;
            Assert.InRange(cta.Opacity, 0.65, 1);
            Assert.InRange(Assert.IsType<ScaleTransform>(cta.RenderTransform).ScaleX, 1.0001, 1.05);
            shell.WindowState = WindowState.Minimized;
            Assert.False(shell.IntakePassLoopRunning);
            Assert.Equal(1, scale.ScaleX);
            shell.WindowState = WindowState.Normal;
            Assert.True(shell.IntakePassLoopRunning);
            shell.ShowTab("quests");
            Assert.False(shell.IntakePassLoopRunning);
            At(30);
            Assert.Equal(1, scale.ScaleX);
            Assert.Equal(1, cta.Opacity);
            shell.Close();
            Assert.Null(shell.EnteringTab);
        }
        finally
        {
            (CoreAccount.IsLoggedInProvider, CoreAccount.HasPremiumAccessProvider) = saved;
            ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = savedTime;
        }
    });

    [Fact]
    public Task NavigationFromAnyDoorCountsForEmisRingAndUnknownKeysDoNot() => Run(shell =>
    {
        var state = ConditioningControlPanel.Services.EmiDesk.EmiState.Current;
        var usage = new System.Collections.Generic.Dictionary<string, int>(state.Usage);
        var scores = new System.Collections.Generic.Dictionary<string, double>(state.OpenScore);
        var at = new System.Collections.Generic.Dictionary<string, long>(state.UsageAt);
        try
        {
            state.Usage.TryGetValue("sessions", out var before);
            shell.ShowTab("presets");
            Assert.Equal(before + 1, state.Usage["sessions"]);
            shell.ShowTab("not-a-tab");
            Assert.Equal(before + 1, state.Usage["sessions"]);
            ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk.EmiTargets.NoteTabOpened("Presets"); // WPF OrdinalIgnoreCase
            Assert.Equal(before + 2, state.Usage["sessions"]);
        }
        finally
        {
            state.Usage = usage; state.OpenScore = scores; state.UsageAt = at;
        }
    });

    private sealed class SteppedTime : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
    }

    private sealed class Pending(Action fire, TimeSpan delay) : IDisposable
    {
        public TimeSpan Delay => delay;
        public bool Disposed { get; private set; }
        public void Fire() { if (!Disposed) fire(); }
        public void Dispose() => Disposed = true;
    }
}
