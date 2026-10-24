using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>shell-marquee (wire-shell-marquee-b): the weekly intake pass nudge (WPF MainWindow.Marquee.cs:992-1121)
/// goes through the startup ladder, stands down for a dismissed week, a disabled switch, a patron and a launch
/// whose server announcement already took the popup slot; and the welcome-back line follows sign-in
/// (WPF Login.cs:57/123/324).</summary>
public sealed class IntakePassNudgeTests
{
    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.IntakeNudgeEnabled, s.IntakeNudgeDismissedWeek, s.IntakePassSpentWeek, s.IntakePassSpentUtc,
                   s.UserDisplayName, s.OfflineMode, s.UnifiedId, s.DismissedAnnouncementId);
        var (oldLogged, oldLab) = (CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider);
        (s.IntakeNudgeEnabled, s.IntakeNudgeDismissedWeek, s.IntakePassSpentWeek, s.IntakePassSpentUtc) = (true, "", "", null);
        CoreAccount.IsLoggedInProvider = () => true;
        CoreAccount.HasLabAccessProvider = () => false;
        StartupLadder.ResetForTests();
        try { await body(); }
        finally
        {
            MainShellWindow.AnnouncementClient = null;
            StartupLadder.ResetForTests();
            (CoreAccount.IsLoggedInProvider, CoreAccount.HasLabAccessProvider) = (oldLogged, oldLab);
            (s.IntakeNudgeEnabled, s.IntakeNudgeDismissedWeek, s.IntakePassSpentWeek, s.IntakePassSpentUtc,
             s.UserDisplayName, s.OfflineMode, s.UnifiedId, s.DismissedAnnouncementId) = old;
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static string Week => IntakePassService.CurrentWeekKey();

    [Fact]
    public Task QuietLaunchParksTheNudgeAndDismissStampsTheWeek() => Run(() =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.CheckIntakePassNudge();
            Dispatcher.UIThread.RunJobs();

            var row = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Equal("intake-pass:" + Week, row.Key);
            Assert.Equal("🎟️", row.Glyph);
            Assert.Equal(Loc.Get("intake_nudge_title"), row.Title);
            Assert.Equal(Loc.Get("intake_nudge_action"), row.Summary);
            Assert.NotEqual("intake_nudge_action", row.Summary);

            row.Dismiss!();
            Assert.Equal(Week, CoreSettings.Current.IntakeNudgeDismissedWeek);

            // Dismissed this week: a later launch stays silent.
            StartupLadder.ResetForTests();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            var next = new MainShellWindow();
            try { next.Show(); next.CheckIntakePassNudge(); Assert.Empty(StartupLadder.Inbox.Items); }
            finally { next.Close(); }
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task EveryStandDownReasonKeepsTheNudgeAway() => Run(async () =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        void Silent(Action arrange)
        {
            var shell = new MainShellWindow();
            try { shell.Show(); arrange(); shell.CheckIntakePassNudge(); Assert.Empty(StartupLadder.Inbox.Items); }
            finally { shell.Close(); }
        }

        Silent(() => CoreSettings.Current.IntakeNudgeEnabled = false);
        CoreSettings.Current.IntakeNudgeEnabled = true;
        Silent(() => CoreAccount.HasLabAccessProvider = () => true);     // patron: no pass to advertise
        CoreAccount.HasLabAccessProvider = () => false;
        Silent(() => CoreAccount.IsLoggedInProvider = () => false);      // signed out
        CoreAccount.IsLoggedInProvider = () => true;

        // WPF DtRH host stands down even during its countdown (not just while descending).
        var active = typeof(global::ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost)
            .GetField("_active", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var wasActive = active.GetValue(null);
        try { Silent(() => active.SetValue(null, true)); }
        finally { active.SetValue(null, wasActive); }

        // A server announcement routed this launch takes the one popup slot (WPF Marquee.cs:913).
        var s = CoreSettings.Current;
        s.UnifiedId = "u1";
        s.DismissedAnnouncementId = null;
        MainShellWindow.AnnouncementClient = () => new V2AuthService(() => s, new AnnouncementFeed());
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            await shell.CheckServerAnnouncementAsync();
            shell.CheckIntakePassNudge();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("announcement:a1", Assert.Single(StartupLadder.Inbox.Items).Key);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public Task NothingQuietOpensTheCardWithWpfCopyAndItsDismissStampsTheWeek() => Run(() =>
    {
        var opened = new List<AnnouncementPopup>();
        using var sub = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is AnnouncementPopup p) opened.Add(p); });
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.CheckIntakePassNudge();
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(StartupLadder.Inbox.Items);
            var popup = Assert.Single(opened);
            Assert.Equal(Loc.Get("intake_nudge_title"), popup.FindControl<TextBlock>("TxtTitle")!.Text);
            Assert.Equal(Loc.Get("intake_nudge_pitch_nopunch"), popup.FindControl<TextBlock>("TxtMessage")!.Text);
            Assert.Equal(Loc.Get("intake_nudge_action"), popup.FindControl<Button>("BtnDownload")!.Content);
            Assert.Equal(Loc.Get("intake_nudge_later"), popup.FindControl<Button>("BtnDismiss")!.Content);
            Assert.True(popup.FindControl<Border>("CardArtPanel")!.IsVisible);   // the niche pass card loaded

            popup.FindControl<Button>("BtnDismiss")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Week, CoreSettings.Current.IntakeNudgeDismissedWeek);
        }
        finally { shell.Close(); foreach (var p in opened) p.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task SignInRepaintsTheWelcomeBackLine() => Run(() =>
    {
        var s = CoreSettings.Current;
        (s.OfflineMode, s.UserDisplayName) = (false, null);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var line = shell.Named<TextBlock>("TxtBannerSecondary")!;
            Assert.Equal(Loc.Get("label_welcome_consider_logging_in_with_patreon_for"), line.Text);

            s.UserDisplayName = "Bambi";
            shell.UpdateQuickLoginUI(accountChanged: true);
            Assert.Equal(Loc.GetF("label_welcome_back_0", "Bambi"), line.Text);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    private sealed class AnnouncementFeed : System.Net.Http.HttpMessageHandler
    {
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage r, System.Threading.CancellationToken ct) =>
            Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(r.RequestUri!.AbsolutePath == "/config/announcement"
                    ? "{\"enabled\":true,\"id\":\"a1\",\"title\":\"News\",\"message\":\"m\"}" : "{}"),
            });
    }
}
