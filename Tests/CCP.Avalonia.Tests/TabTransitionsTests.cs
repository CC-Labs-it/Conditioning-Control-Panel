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
        shell.ShowTab("play");
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
    public Task OpeningTheWindowArmsTheFirstVisitPulsesOnceAfterOnePointTwoSeconds()
    {
        var armed = new System.Collections.Generic.List<(Action Fire, TimeSpan Delay)>();
        return Run(shell =>
        {
            var s = CoreSettings.Current;
            s.HasSeenProgramsTab = false;
            s.EnableDeeper = true;
            s.HasSeenDeeperTab = false;
            shell.ShowTab("settings");

            // WPF xaml.cs:3795: Opened arms one 1.2 s delay; nothing pulses before it elapses.
            var only = Assert.Single(armed);
            Assert.Equal(TimeSpan.FromMilliseconds(1200), only.Delay);
            Assert.False(shell.NavDoorHeaderPulsing("deeper"));

            // Opened fires on every Show; the arming does not repeat.
            shell.Hide();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Single(armed);

            only.Fire();
            Assert.True(shell.NavDoorHeaderPulsing("deeper"));
            Assert.True(shell.NavDoorHeaderPulsing("programs"));
        }, shell => shell.ScheduleOnce = (fire, delay) =>
        {
            armed.Add((fire, delay));
            return new Pending();
        });
    }

    // A stepped entrance lets pointer tests sample resting geometry without sleeping.
    private static void Step(MainShellWindow shell, double milliseconds) => shell.StepTabTransition(milliseconds);

    [Fact]
    public Task FullEntranceUsesRailDirectionAndSettlesBeforePointerInput() => Run(shell =>
    {
        shell.ShowTab("quests");
        var quests = shell.Named<Control>("QuestsTab")!;
        var slide = Assert.IsType<TranslateTransform>(quests.RenderTransform);
        Assert.Equal(12, slide.X);
        Step(shell, 100);
        Assert.Equal(3, slide.X, 5); // QuadraticEaseOut at half time
        Step(shell, 620);
        Assert.Equal(0, slide.X);
        Assert.Equal(1, quests.Opacity);
        shell.ShowTab("achievements");
        shell.ShowTab("quests");
        Assert.Equal(-12, slide.X);
        shell.ShowTab("quests"); // same key has no direction: WPF rises
        Assert.Equal(0, slide.X);
        Assert.Equal(12, slide.Y);
        shell.ShowTab("settings"); // browser airspace never slides
        Assert.False(shell.SettingsPage!.RenderTransform is TranslateTransform { X: not 0 } or TranslateTransform { Y: not 0 });
    });

    [Fact]
    public Task LeavingTabIsCutWithoutASnapshotAndRapidReturnIsClickable() => Run(shell =>
    {
        shell.ShowTab("quests");
        Step(shell, 620);
        var quests = shell.Named<Control>("QuestsTab")!;
        var host = (Panel)quests.Parent!;
        var children = host.Children.Count;
        shell.ShowTab("achievements");
        // Review P1: no outgoing snapshot render; the leaving tab is cut, as on base.
        Assert.False(quests.IsVisible);
        Assert.Equal(children, host.Children.Count);
        Assert.Empty(host.Children.OfType<Image>());
        Step(shell, 50);
        shell.ShowTab("quests"); // an unfinished entrance never wins a late completion
        Step(shell, 620);
        Assert.True(quests.IsVisible);
        Assert.True(quests.IsHitTestVisible);
        Assert.Equal(1, quests.Opacity);
        Assert.Equal(1, shell.Named<Control>("AchievementsTab")!.Opacity);
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
        Assert.Equal(0, tab.Opacity);
        time.Now = TimeSpan.FromMilliseconds(100).Ticks;
        shell.TickTabTransition();
        Assert.Equal(0.75, tab.Opacity, 5); // QuadraticEaseOut at half of 200 ms
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
        shell.ShowTab("achievements");
        var tab = shell.Named<Control>("AchievementsTab")!;
        Assert.Equal(0, tab.Opacity);
        Assert.False(tab.RenderTransform is TranslateTransform { X: not 0 } or TranslateTransform { Y: not 0 });
        Step(shell, 100);
        Assert.Equal(0.75, tab.Opacity, 5);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        shell.ShowTab("quests");
        Assert.Equal(1, tab.Opacity);
        Assert.Empty(((Panel)tab.Parent!).Children.OfType<Image>());
        Assert.Null(shell.EnteringTab);
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
        try
        {
            shell.ShowTab("quests");
            // First card is animated to 0 already; test the original logical selection separately.
            Assert.Equal(0, cards.Children[0].Opacity);
            Assert.Same(authored, cards.Children[0].RenderTransform);
            Step(shell, 40);
            Assert.InRange(cards.Children[0].Opacity, 0.01, 1);
            Assert.Equal(0, cards.Children[3].Opacity); // second eligible card starts at 40 ms
            Step(shell, 250);
            Assert.Equal(cards.Children[8].Opacity, cards.Children[9].Opacity); // slots 6 and 7 share the cap
            Assert.Equal(1, cards.Children[10].Opacity); // ninth card is outside Take(8)
            Assert.Equal(0.6, dim.Opacity);
            Assert.False(hidden.IsVisible);
            shell.CompleteTabTransition();
            Assert.Equal(8, MainShellWindow.FindStaggerTargets(tab).Count);
            Assert.All(cards.Children.Where(c => c != dim), c => Assert.Equal(1, c.Opacity));
        }
        finally { shell.CompleteTabTransition(); tab.Content = content; }
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

    [Theory]
    [InlineData("takeover", "tab_takeover", 1)]
    [InlineData("awareness", "tab_awareness", 1)]
    [InlineData("haptics", "tab_haptics", 1)]
    [InlineData("voice", "tab_shelistening", 1)]
    [InlineData("fyp", "tab_fyp", 1)]
    [InlineData("remote", "tab_remote_control", 1)]
    [InlineData("dtrh", null, 2)]
    [InlineData(null, "mosaic_daily_gift", 0)]
    public Task HomeEntryRepaintsMysteryOfferAndReveal(string? key, string? nameKey, int tier) => Run(shell =>
    {
        ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.ReadDailyGiftKey = () => key;
        CoreEntitlement.IsFreeTodayProvider = _ => true; // extra member/post grants must not choose the box
        CoreEntitlement.HasPremiumProvider = () => false;
        shell.ShowTab("quests");
        shell.ShowTab("settings"); // WPF FavoritesRail.cs:107, TabNavigation.cs:389
        var dash = shell.SettingsPage!;
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("mosaic_free_today"), dash.CardMystery.Title);
        Assert.Null(dash.CardMystery.TierBadge);
        Assert.Equal(nameKey == null ? "Down the Rabbit Hole" : ConditioningControlPanel.Localization.Loc.Get(nameKey),
            dash.FindControl<TextBlock>("MysteryRevealName")!.Text);
        var badge = dash.FindControl<ConditioningControlPanel.Avalonia.Controls.TierBadge>("MysteryRevealBadge")!;
        Assert.Equal(tier, badge.Tier);
        Assert.Equal(tier > 0, badge.FreeToday);
        var face = dash.FindControl<Border>("MysteryRevealFace")!;
        var art = Assert.IsType<ImageBrush>(((Grid)face.Child!).Children.OfType<Border>().First().Background);
        Assert.NotNull(art.Source);
        CoreEntitlement.HasPremiumProvider = () => true;
        shell.RefreshNavPremiumTags(); // existing daily-free / entitlement callback
        Assert.Equal(0, badge.Tier);
        Assert.False(badge.FreeToday);
    });

    private sealed class SteppedTime : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
    }

    private sealed class Pending : IDisposable { public void Dispose() { } }
}
