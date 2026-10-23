using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Services;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

// Compiled on the cloud runner; run on the font-equipped headless gate.
public sealed class InboxStartupSurfaceTests
{
    private static Task Run(Action<MainShellWindow> body, bool seeded = false) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        StartupLadder.ResetForTests();
        var provider = CoreSettings.ServiceProvider;
        SettingsService? settings = null;
        if (seeded)
        {
            settings = new SettingsService();
            settings.Current.Welcomed = settings.Current.HasAcceptedAgeVerification = true;
            settings.Current.SeenFeatureIntros.AddRange(FeatureIntros.All.Keys);
            settings.Current.IntakePassSpentWeek = "";
            settings.Current.IntakePassSpentUtc = null;
            CoreSettings.ServiceProvider = () => settings;
        }
        var login = CoreAccount.IsLoggedInProvider; var premium = CoreAccount.HasPremiumAccessProvider;
        var session = CoreSession.IsSessionRunningProvider;
        CoreAccount.IsLoggedInProvider = () => true; CoreAccount.HasPremiumAccessProvider = () => false;
        CoreSession.IsSessionRunningProvider = () => false;
        var s = CoreSettings.Current;
        var old = (s.IntakeNudgeEnabled, s.IntakeNudgeDismissedWeek, s.DismissedAnnouncementId);
        s.IntakeNudgeEnabled = true; s.IntakeNudgeDismissedWeek = ""; s.DismissedAnnouncementId = "server-slot";
        var shell = new MainShellWindow();
        try { body(shell); }
        finally
        {
            StartupLadder.FoldPassiveForPanic(); shell.Close();
            (s.IntakeNudgeEnabled, s.IntakeNudgeDismissedWeek, s.DismissedAnnouncementId) = old;
            CoreAccount.IsLoggedInProvider = login; CoreAccount.HasPremiumAccessProvider = premium;
            CoreSession.IsSessionRunningProvider = session;
            settings?.SaveImmediate(); CoreSettings.ServiceProvider = provider;
            StartupLadder.ResetForTests(); Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task NudgeRowIsOncePerLaunchAndDismissesOnlyItsWeek() => Run(shell =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        shell.CheckIntakePassNudge(); shell.CheckIntakePassNudge();
        var row = Assert.Single(StartupLadder.Inbox.Items);
        var week = ConditioningControlPanel.Services.IntakePassService.CurrentWeekKey();
        Assert.Equal("intake-pass:" + week, row.Key);
        StartupLadder.Inbox.Dismiss(row);
        Assert.Equal(week, CoreSettings.Current.IntakeNudgeDismissedWeek);
        Assert.Equal("server-slot", CoreSettings.Current.DismissedAnnouncementId);
        shell.CheckIntakePassNudge(); Assert.Empty(StartupLadder.Inbox.Items);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task NudgeTimerWaitsFourteenSecondsAndCancelsOnClose(bool close) => Run(shell =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        shell.Show(); Dispatcher.UIThread.RunJobs();
        var clock = (StartupLadder.TestClock)StartupLadder.Time;
        clock.Advance(TimeSpan.FromMilliseconds(13999)); Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key.StartsWith("intake-pass:"));
        if (close) shell.Close();
        clock.Advance(TimeSpan.FromMilliseconds(1)); Dispatcher.UIThread.RunJobs();
        Assert.Equal(!close, StartupLadder.Inbox.Items.Any(i => i.Key.StartsWith("intake-pass:")));
    }, seeded: true);

    [Fact]
    public Task NudgePrimaryUsesTheExistingIntakeDoorAndKeepsServerSlot() => Run(shell =>
    {
        shell.Show();
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        shell.CheckIntakePassNudge();
        StartupLadder.Inbox.Open(Assert.Single(StartupLadder.Inbox.Items));
        var popup = shell.OwnedWindows.OfType<AnnouncementPopup>().Single();
        popup.FindControl<Button>("BtnDownload")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(shell.Named<ConditioningControlPanel.Avalonia.Views.Tabs.GradedIntakeTabView>("GradedIntakeTab")!.IsVisible);
        Assert.Equal("server-slot", CoreSettings.Current.DismissedAnnouncementId);
        Assert.Equal(IntakePassService.CurrentWeekKey(), CoreSettings.Current.IntakeNudgeDismissedWeek);
    });

    [Theory]
    [InlineData("disabled")]
    [InlineData("premium")]
    [InlineData("signed-out")]
    [InlineData("session")]
    [InlineData("dismissed")]
    public Task NudgeRespectsExclusions(string reason) => Run(shell =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        if (reason == "disabled") CoreSettings.Current.IntakeNudgeEnabled = false;
        if (reason == "premium") CoreAccount.HasPremiumAccessProvider = () => true;
        if (reason == "signed-out") CoreAccount.IsLoggedInProvider = () => false;
        if (reason == "session") CoreSession.IsSessionRunningProvider = () => true;
        if (reason == "dismissed") CoreSettings.Current.IntakeNudgeDismissedWeek = ConditioningControlPanel.Services.IntakePassService.CurrentWeekKey();
        shell.CheckIntakePassNudge(); Assert.Empty(StartupLadder.Inbox.Items);
    });

    [Theory]
    [InlineData("breakoutdemo", true)]
    [InlineData("fyp", false)]
    public Task RealGameParksCardsButFeedDoesNot(string id, bool parked) => Run(shell =>
    {
        var game = new GameWindow(GameWindow.Games[id]);
        try
        {
            game.Show();
            Assert.Equal(parked, StartupLadder.World(DateTime.UtcNow).GameHostUp);
            var opened = false;
            StartupLadder.PresentOrInbox(new() { Key = "game-card", Open = () => opened = true });
            Assert.Equal(!parked, opened);
            Assert.Equal(parked ? 1 : 0, StartupLadder.Inbox.UnreadCount);
            shell.CheckIntakePassNudge(); // Even the feed suppresses the weekly nudge (WPF AnyHostActive).
            Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key.StartsWith("intake-pass:"));
            var modal = false;
            StartupLadder.WindowReady = () => true;
            StartupLadder.EnqueueModal("game-wait", 10, _ => { modal = true; return Task.CompletedTask; });
            Dispatcher.UIThread.RunJobs();
            var clock = (StartupLadder.TestClock)StartupLadder.Time;
            clock.Advance(TimeSpan.FromMilliseconds(750));
            Assert.Equal(!parked, modal);
            game.Close();
            clock.Advance(TimeSpan.FromMilliseconds(500));
            Assert.True(modal);
        }
        finally { game.Close(); }
    });
}
