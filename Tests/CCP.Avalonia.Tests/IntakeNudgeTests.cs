using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ConditioningControlPanel.Services;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF MainWindow.Marquee.cs:725,998-1115. Assembly process-state isolation applies.
public sealed class IntakeNudgeTests
{
    private static Task Run(Action<MainShellWindow> body, bool seeded = true) => AvaloniaTestDispatcher.RunAsync(() =>
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
            foreach (var popup in shell.OwnedWindows.ToArray()) popup.Close(); shell.Close();
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
        Assert.True(ConditioningControlPanel.Avalonia.Controls.IconMap.TryGet(row.Glyph, out var icon));
        Assert.Equal(FluentIcons.Common.Symbol.TicketDiagonal, icon.Kind);
        StartupLadder.Inbox.Items.Clear();
        shell.CheckIntakePassNudge();
        Assert.Empty(StartupLadder.Inbox.Items); // per-launch slot, independently of inbox dedupe
        row.Dismiss!();
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
        Dispatcher.UIThread.RunJobs(); // the popup posts its action behind the fade (Background)
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
    [InlineData("spent")]
    public Task NudgeRespectsExclusions(string reason) => Run(shell =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        if (reason == "disabled") CoreSettings.Current.IntakeNudgeEnabled = false;
        if (reason == "premium") CoreAccount.HasPremiumAccessProvider = () => true;
        if (reason == "signed-out") CoreAccount.IsLoggedInProvider = () => false;
        if (reason == "session") CoreSession.IsSessionRunningProvider = () => true;
        if (reason == "dismissed") CoreSettings.Current.IntakeNudgeDismissedWeek = ConditioningControlPanel.Services.IntakePassService.CurrentWeekKey();
        if (reason == "spent") CoreSettings.Current.IntakePassSpentWeek = IntakePassService.CurrentWeekKey();
        shell.CheckIntakePassNudge(); Assert.Empty(StartupLadder.Inbox.Items);
    });

    [Fact]
    public Task ActiveHostSuppressesTheNudge() => Run(shell =>
    {
        var original = PanicSurfaces.All;
        try
        {
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("intake", _ => { }, () => true) };
            shell.CheckIntakePassNudge();
            Assert.Empty(StartupLadder.Inbox.Items);
        }
        finally { PanicSurfaces.All = original; }
    });
    [Fact]
    public Task FeedAlsoSuppressesWeeklyIntake() => Run(shell =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var feed = new GameWindow(GameWindow.Games[GameWindow.FypId]);
        try
        {
            feed.Show();
            shell.CheckIntakePassNudge();
            Assert.Empty(StartupLadder.Inbox.Items);
        }
        finally { feed.Close(); }
    });

    [Fact]
    public Task NonquietOfferOpensPopupAndDismissesOnlyTheWeek() => Run(shell =>
    {
        shell.Show();
        shell.CheckIntakePassNudge();
        Assert.Empty(StartupLadder.Inbox.Items);
        var popup = Assert.Single(shell.OwnedWindows.OfType<AnnouncementPopup>());
        popup.FindControl<Button>("BtnDismiss")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(IntakePassService.CurrentWeekKey(), CoreSettings.Current.IntakeNudgeDismissedWeek);
        Assert.Equal("server-slot", CoreSettings.Current.DismissedAnnouncementId);
    });

    [Fact]
    public Task NudgeRowRendersTheFluentTicket() => Run(shell =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        shell.CheckIntakePassNudge();
        var host = new Window { Content = new ConditioningControlPanel.Avalonia.Views.Controls.InboxFlyout() };
        try
        {
            host.Show(); Dispatcher.UIThread.RunJobs();
            Assert.Contains(host.GetVisualDescendants().OfType<ConditioningControlPanel.Avalonia.Controls.IconGlyph>(),
                g => g.Kind == FluentIcons.Common.Symbol.TicketDiagonal);
        }
        finally { host.Close(); }
    });

}
