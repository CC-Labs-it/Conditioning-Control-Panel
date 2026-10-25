using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
    public Task FullEntranceMovesInRailOrderAndSettlesOnInterruption() => Run(shell =>
    {
        shell.ShowTab("quests");
        shell.SettleTabEntrance();
        shell.ShowTab("achievements");
        var page = shell.Named<Control>("AchievementsTab")!;
        var slide = Assert.IsType<global::Avalonia.Media.TranslateTransform>(page.RenderTransform);
        var expected = global::ConditioningControlPanel.Services.UI.ChromeFxNav.EntranceOffset("quests", "achievements", 12);
        Assert.Equal(expected.X, slide.X);
        Assert.Equal(expected.Y, slide.Y);
        Assert.Equal(0, page.Opacity);
        shell.AdvanceTabEntrance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(0.75, page.Opacity, 3);
        Assert.Equal(expected.X / 4, slide.X, 3);
        shell.ShowTab("quests");
        Assert.Equal(0, slide.X);
        Assert.Equal(0, slide.Y);
        Assert.Equal(1, page.Opacity);
        shell.AdvanceTabEntrance(TimeSpan.FromMilliseconds(620));
        Assert.Null(shell.EnteringTab);
    });

    [Fact]
    public Task ReducedAndAirspacePagesOnlyCrossfadeAndOutgoingFrameExpires() => Run(shell =>
    {
        shell.ShowTab("quests");
        shell.SettleTabEntrance();
        shell.UpdateLayout();
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        shell.ShowTab("achievements");
        Assert.Equal(0, shell.EntranceCardCount);
        Assert.True(shell.HasLeavingTabFrame);
        shell.AdvanceTabEntrance(TimeSpan.FromMilliseconds(100));
        Assert.False(shell.HasLeavingTabFrame);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        shell.ShowTab("settings");
        Assert.Equal(0, shell.EntranceCardCount);
        shell.ShowTab("quests");
        Assert.False(shell.HasLeavingTabFrame); // leaving native airspace is always a cut
        shell.SettleTabEntrance();
        shell.ShowTab("achievements");
        Assert.True(shell.HasLeavingTabFrame);
        shell.SettleTabEntrance();
        Assert.False(shell.HasLeavingTabFrame);
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        shell.ShowTab("achievements");
        Assert.Null(shell.EnteringTab);
        Assert.False(shell.HasLeavingTabFrame);
    });

    [Fact]
    public Task CardSelectionDescendsWrappersCapsEightAndSkipsDimmedCards() => Run(shell =>
    {
        var panel = new StackPanel();
        for (int i = 0; i < 10; i++) panel.Children.Add(new Border());
        panel.Children.Insert(0, new Border { Opacity = 0.6 });
        panel.Children.Insert(0, new Border { IsVisible = false });
        var cards = MainShellWindow.FindEntranceCards(new UserControl { Content = new Border { Child = panel } });
        Assert.Equal(8, cards.Count);
        Assert.Same(panel.Children[2], cards[0]);
        shell.ShowTab("quests");
        Assert.True(shell.EntranceCardCount > 0);
        shell.SettleTabEntrance();
        Assert.Equal(0, shell.EntranceCardCount);
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

    private sealed class Pending : IDisposable { public void Dispose() { } }
}
