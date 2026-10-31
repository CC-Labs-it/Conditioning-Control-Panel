using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>shell-inbox: the server announcement poster (WPF MainWindow.Marquee.cs:857) goes through the startup
/// ladder - a "📣" row while quiet, the popup otherwise - and every dismissal records both halves.</summary>
public sealed class ServerAnnouncementInboxTests
{
    private sealed class FakeProxy : HttpMessageHandler
    {
        public readonly List<string> Calls = new();
        public TaskCompletionSource<bool>? AnnouncementGate;
        public string[] AnnouncementCalls
        {
            get { lock (Calls) return Calls.Where(c => c.StartsWith("GET /config/announcement?")).ToArray(); }
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            lock (Calls) Calls.Add(r.Method + " " + path + r.RequestUri.Query + " " + r.Content?.ReadAsStringAsync().Result);
            if (path == "/config/announcement" && AnnouncementGate is { } gate)
                await gate.Task.WaitAsync(ct);
            var body = path == "/config/announcement"
                ? "{\"enabled\":true,\"id\":\"a1\",\"title\":\"The Spiral is open\",\"message\":\"Come\\n\\n  in,   dear.\"}"
                : "{}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private static Task Run(Func<FakeProxy, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;   // the profile TestUserDataProfile seeded (feature intros spent)
        var (oldId, oldDismissed) = (s.UnifiedId, s.DismissedAnnouncementId);
        s.UnifiedId = "u1";
        s.DismissedAnnouncementId = null;
        var fake = new FakeProxy();
        StartupLadder.ResetForTests();
        MainShellWindow.AnnouncementClient = () => new V2AuthService(() => s, fake);
        try { await body(fake); }
        finally
        {
            MainShellWindow.AnnouncementClient = null;
            StartupLadder.ResetForTests();
            (s.UnifiedId, s.DismissedAnnouncementId) = (oldId, oldDismissed);
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static async Task Until(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
    }

    [Fact]
    public Task LiveShellChecksOnceAfterTheStartupDelay() => Run(async fake =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var clock = new StartupLadder.TestClock();
        var shell = new MainShellWindow { AnnouncementTime = clock };
        try
        {
            shell.Show();
            clock.Advance(MainShellWindow.AnnouncementDelay - TimeSpan.FromTicks(1));
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(fake.AnnouncementCalls);
            clock.Advance(TimeSpan.FromTicks(1));
            await Until(() => StartupLadder.Inbox.UnreadCount == 1);
            Assert.Single(fake.AnnouncementCalls);
            Assert.Equal("announcement:a1", Assert.Single(StartupLadder.Inbox.Items).Key);
            clock.Advance(MainShellWindow.AnnouncementDelay);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(fake.AnnouncementCalls);
        }
        finally { shell.Close(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosingShellCancelsTheStartupCheckIncludingQueuedCallbacks(bool callbackQueued) => Run(fake =>
    {
        var clock = new StartupLadder.TestClock();
        var shell = new MainShellWindow { AnnouncementTime = clock };
        shell.Show();
        if (callbackQueued) clock.Advance(MainShellWindow.AnnouncementDelay);
        shell.Close();
        Assert.Empty(clock.Timers);
        clock.Advance(MainShellWindow.AnnouncementDelay);
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(fake.AnnouncementCalls);
        Assert.Empty(StartupLadder.Inbox.Items);
        return Task.CompletedTask;
    });

    [Fact]
    public Task ClosingShellDiscardsAnAnnouncementResponseAlreadyInFlight() => Run(async fake =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var opened = new List<AnnouncementPopup>();
        using var sub = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is AnnouncementPopup p) opened.Add(p); });
        fake.AnnouncementGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shell = new MainShellWindow { AnnouncementTime = StartupLadder.Time };
        shell.Show();
        var check = shell.CheckServerAnnouncementAsync();
        try
        {
            Assert.Single(fake.AnnouncementCalls);
            Assert.False(check.IsCompleted);
            shell.Close();
            fake.AnnouncementGate.SetResult(true);
            await check;
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(StartupLadder.Inbox.Items);
            Assert.Empty(opened);
        }
        finally
        {
            fake.AnnouncementGate.TrySetResult(true);
            await check;
            shell.Close();
            foreach (var p in opened) p.Close();
        }
    });

    [Fact]
    public Task QuietLaunchParksTheAnnouncementAndDismissRecordsBothHalves() => Run(async fake =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var shell = new MainShellWindow { AnnouncementTime = StartupLadder.Time };
        try
        {
            shell.Show();
            await shell.CheckServerAnnouncementAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains("GET /config/announcement?unified_id=u1 ", fake.Calls);
            var row = Assert.Single(StartupLadder.Inbox.Items);
            Assert.Equal("announcement:a1", row.Key);
            Assert.Equal("📣", row.Glyph);
            Assert.Equal("The Spiral is open", row.Title);
            Assert.Equal("Come in, dear.", row.Summary);

            var badge = shell.Named<Button>("BtnInbox")!;
            Click(badge);
            Dispatcher.UIThread.RunJobs();
            var flyout = TopLevel.GetTopLevel(shell)!.GetVisualDescendants().OfType<InboxFlyout>().Single();
            flyout.UpdateLayout();
            Click(flyout.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("dismiss")));
            await Until(() => fake.Calls.Any(c => c.StartsWith("POST")));

            Assert.Equal("a1", CoreSettings.Current.DismissedAnnouncementId);
            Assert.Contains("POST /v2/announcement/dismiss {\"unified_id\":\"u1\",\"announcement_id\":\"a1\"}", fake.Calls);
            Assert.Empty(StartupLadder.Inbox.Items);

            // dismissed: the next check fetches and stays silent
            await shell.CheckServerAnnouncementAsync();
            Assert.Empty(StartupLadder.Inbox.Items);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public Task OpeningTheRowShowsTheAnnouncementPopup() => Run(async _ =>
    {
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
        var opened = new List<AnnouncementPopup>();
        using var sub = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is AnnouncementPopup p) opened.Add(p); });
        var shell = new MainShellWindow { AnnouncementTime = StartupLadder.Time };
        try
        {
            shell.Show();
            await shell.CheckServerAnnouncementAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(opened);

            Click(shell.Named<Button>("BtnInbox")!);
            Dispatcher.UIThread.RunJobs();
            var flyout = TopLevel.GetTopLevel(shell)!.GetVisualDescendants().OfType<InboxFlyout>().Single();
            flyout.UpdateLayout();
            Click(flyout.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("open")));
            Dispatcher.UIThread.RunJobs();

            var popup = Assert.Single(opened);
            Assert.Equal("The Spiral is open", popup.FindControl<TextBlock>("TxtTitle")!.Text);
            Assert.Empty(StartupLadder.Inbox.Items);
        }
        finally { shell.Close(); foreach (var p in opened) p.Close(); }
    });

    [Fact]
    public Task NothingQuietOpensThePopupWhoseDismissRecordsTheAccount() => Run(async fake =>
    {
        var opened = new List<AnnouncementPopup>();
        using var sub = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w is AnnouncementPopup p) opened.Add(p); });
        var shell = new MainShellWindow { AnnouncementTime = StartupLadder.Time };
        try
        {
            shell.Show();
            await shell.CheckServerAnnouncementAsync();
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(StartupLadder.Inbox.Items);
            var popup = Assert.Single(opened);
            Click(popup.FindControl<Button>("BtnDismiss")!);
            await Until(() => fake.Calls.Any(c => c.StartsWith("POST")));

            Assert.Equal("a1", CoreSettings.Current.DismissedAnnouncementId);
            Assert.Contains("POST /v2/announcement/dismiss {\"unified_id\":\"u1\",\"announcement_id\":\"a1\"}", fake.Calls);
            popup.Close();
        }
        finally { shell.Close(); foreach (var p in opened) p.Close(); }
    });
}
