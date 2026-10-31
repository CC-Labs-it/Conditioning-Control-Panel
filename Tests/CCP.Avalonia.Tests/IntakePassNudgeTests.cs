using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
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
/// whose server announcement already took the popup slot.</summary>
public sealed class IntakePassNudgeTests
{
    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
        {
            // Behavior tests use bundled faces when this Linux worker lacks the WPF fonts.
            var fonts = AppFonts.Options(systemEmoji: false);
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI Black"] = mappings["Segoe UI"] = new FontFamily(AppFonts.Inter);
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(new FontManagerOptions
                {
                    DefaultFamilyName = fonts.DefaultFamilyName, FontFamilyMappings = mappings,
                    FontFallbacks = fonts.FontFallbacks,
                })
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        var s = CoreSettings.Current;
        var old = (s.IntakeNudgeEnabled, s.IntakeNudgeDismissedWeek, s.IntakePassSpentWeek, s.IntakePassSpentUtc,
                   s.UserDisplayName, s.OfflineMode, s.UnifiedId, s.DismissedAnnouncementId);
        var (oldLogged, oldPremium) = (CoreAccount.IsLoggedInProvider, CoreAccount.HasPremiumAccessProvider);
        (s.IntakeNudgeEnabled, s.IntakeNudgeDismissedWeek, s.IntakePassSpentWeek, s.IntakePassSpentUtc) = (true, "", "", null);
        CoreAccount.IsLoggedInProvider = () => true;
        CoreAccount.HasPremiumAccessProvider = () => false;
        StartupLadder.ResetForTests();
        try { await body(); }
        finally
        {
            MainShellWindow.AnnouncementClient = null;
            StartupLadder.ResetForTests();
            (CoreAccount.IsLoggedInProvider, CoreAccount.HasPremiumAccessProvider) = (oldLogged, oldPremium);
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
        Silent(() => CoreAccount.HasPremiumAccessProvider = () => true);     // patron: no pass to advertise
        CoreAccount.HasPremiumAccessProvider = () => false;
        Silent(() => CoreAccount.IsLoggedInProvider = () => false);      // signed out
        CoreAccount.IsLoggedInProvider = () => true;

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
            Assert.Equal(MainShellWindow.IntakePassCardImage() is not null,
                popup.FindControl<Border>("CardArtPanel")!.IsVisible); // missing art keeps the text layout

            popup.FindControl<Button>("BtnDismiss")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Week, CoreSettings.Current.IntakeNudgeDismissedWeek);
        }
        finally { shell.Close(); foreach (var p in opened) p.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task NudgeActionUsesTheIntakeAiGateAndKeepsTheServerDismissal() => Run(() =>
    {
        CoreAi.IsAvailableProvider = () => false;
        CoreSettings.Current.DismissedAnnouncementId = "server-record";
        var opened = new List<Window>();
        using var sub = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.CheckIntakePassNudge();
            Dispatcher.UIThread.RunJobs();
            var popup = Assert.Single(opened.OfType<AnnouncementPopup>());
            popup.FindControl<Button>("BtnDownload")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Week, CoreSettings.Current.IntakeNudgeDismissedWeek);
            Assert.Equal("server-record", CoreSettings.Current.DismissedAnnouncementId);
            Assert.Single(opened.OfType<ConditioningControlPanel.Avalonia.Views.Dialogs.MessageDialog>());
            Assert.Empty(IntakeHostWindow.Snapshot());
        }
        finally { foreach (var w in opened) w.Close(); shell.Close(); }
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
