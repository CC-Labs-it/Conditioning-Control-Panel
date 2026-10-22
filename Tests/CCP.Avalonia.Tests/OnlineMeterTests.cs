using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>"Online media today" on this head (WPF MainWindow.OnlineMeter.cs, main 76cb2f8b0 / 50a0074f1 /
/// 6ea63f274): the title bar pill and the Assets card follow the source, the remote media consent and the
/// day's share; the spent notice waits out a hold.</summary>
public sealed class OnlineMeterTests
{
    private static readonly DateTime SixPm = new(2026, 10, 20, 18, 0, 0);
    private static readonly LandingWorld Quiet = new(false, false, false, true, false, false);
    private static readonly LandingWorld Session = new(false, false, false, true, false, false, SessionRunning: true);

    [Fact]
    public void PillAndCardFollowSourceConsentAndTheDaysShare() => Run((shell, s) =>
    {
        FriendsLanding.World = () => Session;   // the spent notice waits; this test is about the pill and card
        ScrolllerAllowance.Charge(ScrolllerAllowance.LimitBytes * 42 / 100);
        shell.RefreshOnlineMeter();
        var pill = shell.Named<Border>("OnlineMeterPill")!;
        var pillText = shell.Named<TextBlock>("TxtOnlineMeterPill")!;
        var tab = shell.Named<AssetsTabView>("AssetsTab")!;
        Assert.True(pill.IsVisible);
        Assert.Equal(string.Format(Loc.Get("online_pill"), ScrolllerAllowance.Percent), pillText.Text);
        Assert.True(tab.OnlineMeterCard.IsVisible);
        Assert.Equal(string.Format(Loc.Get("online_meter_pct"), ScrolllerAllowance.Percent), tab.TxtOnlineMeterPct.Text);
        var line = tab.TxtOnlineMeterLine.Inlines!;
        Assert.Equal(Loc.Get("online_meter_under_free"), ((Run)line[0]).Text);
        Assert.Contains(line.OfType<InlineUIContainer>(), c => c.Child is global::ConditioningControlPanel.Avalonia.Controls.SafeHyperlinkButton);   // "See plans" (Free sees Basic)

        // 50a0074f1: no remote media consent, no meter, whatever the source says.
        (s.RemoteMediaConsented, s.FypOnlineConsented) = (false, false);
        shell.RefreshOnlineMeter();
        Assert.False(pill.IsVisible);
        Assert.False(tab.OnlineMeterCard.IsVisible);
        s.RemoteMediaConsented = true;
        s.MediaSource = "local";
        shell.RefreshOnlineMeter();
        Assert.False(pill.IsVisible);
        s.MediaSource = "mixed";

        // Spent: grey "done for today", the card says when it is back, the hover card offers plans.
        ScrolllerAllowance.Charge(ScrolllerAllowance.LimitBytes);
        Dispatcher.UIThread.RunJobs();
        shell.RefreshOnlineMeter();
        Assert.True(pill.IsVisible);
        Assert.Equal(Loc.Get("online_pill_done"), pillText.Text);
        Assert.Equal(string.Format(Loc.Get("online_meter_spent"), string.Format(Loc.Get("online_meter_hours"), 6)),
            ((Run)tab.TxtOnlineMeterLine.Inlines![0]).Text);
        shell.OpenOnlineMeterHover();
        Assert.True(shell.OnlineMeterHover!.IsOpen);
        Assert.Contains(shell.OnlineMeterHover.Child!.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("online_see_plans"));

        // The pill is a door to the Assets tab (Enter, as a click).
        shell.ShowTab("dashboard");
        Dispatcher.UIThread.RunJobs();
        pill.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.True(tab.IsVisible);
        Assert.False(shell.OnlineMeterHover.IsOpen);
    });

    [Fact]
    public void SpentNoticeWaitsOutAHoldThenOffersPlans() => Run((shell, s) =>
    {
        var host = shell.Named<Panel>("NotificationHost")!;
        global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(host);
        bool Toast() => host.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == Loc.Get("online_toast_spent"));

        FriendsLanding.World = () => Session;
        ScrolllerAllowance.Charge(ScrolllerAllowance.LimitBytes);   // SpentReached, on the real event path
        Dispatcher.UIThread.RunJobs();
        Assert.False(Toast());

        FriendsLanding.World = () => Quiet;
        shell.ReleaseOnlineSpentNotice();   // what the 3 s wait timer runs
        Dispatcher.UIThread.RunJobs();
        Assert.True(Toast());
        Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("online_see_plans"));
    });

    private static void Run(Action<MainShellWindow, AppSettings> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        var oldSettings = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var svc = new SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => svc;
        var s = svc.Current;
        (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = ("online", true, false);
        var saved = (ScrolllerAllowance.PlanProvider, ScrolllerAllowance.Now, ScrolllerAllowance.AccountProvider,
            ScrolllerAllowance.LedgerPath, ScrolllerAllowance.LegacyLedgerPath, FriendsLanding.World);
        ScrolllerAllowance.ResetForTests();
        ScrolllerAllowance.PlanProvider = () => ScrolllerPlan.Free;
        ScrolllerAllowance.Now = () => SixPm;
        ScrolllerAllowance.AccountProvider = () => null;
        ScrolllerAllowance.LedgerPath = () => null;
        ScrolllerAllowance.LegacyLedgerPath = () => null;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            body(shell, s);
        }
        finally
        {
            foreach (var w in shell.OwnedWindows.ToArray()) w.Close();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            ScrolllerAllowance.ResetForTests();
            (ScrolllerAllowance.PlanProvider, ScrolllerAllowance.Now, ScrolllerAllowance.AccountProvider,
                ScrolllerAllowance.LedgerPath, ScrolllerAllowance.LegacyLedgerPath, FriendsLanding.World) = saved;
            svc.SealForReset();
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldSettings;
        }
    });
}
