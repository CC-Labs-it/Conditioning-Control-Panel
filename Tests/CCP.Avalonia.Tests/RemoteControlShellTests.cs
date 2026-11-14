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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shell half of Remote Control against a fake relay (no network): WPF
/// MainWindow.RemoteControl.cs overlay, toast, Start lock, big emotes and End Session.</summary>
public sealed class RemoteControlShellTests
{
    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public string Poll = "{}";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            lock (Paths) Paths.Add(path);
            var body = path switch { "/v2/remote/start" => "{\"code\":\"ABC123\"}", "/v2/remote/poll" => Poll, _ => "{}" };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now = 1_000_000;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Step(TimeSpan t) => Now += t.Ticks;
    }

    [Fact]
    public Task Controller_gets_the_overlay_toast_start_lock_emotes_and_end_session() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldRelay, oldTime, oldProvider) = (RemoteControlTabView.Relay, MainShellWindow.RemoteOverlayTime, CoreSettings.ServiceProvider);
        var oldSink = global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink;
        var notified = new List<string>();
        global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (t, b) => { lock (notified) notified.Add($"{t}|{b}"); };
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        var emoteNow = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        relay.Now = () => emoteNow;
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        var clock = new SteppedClock();
        MainShellWindow.RemoteOverlayTime = clock;
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var overlay = shell.Named<Border>("RemoteControlOverlay")!;
            var start = shell.Named<Button>("BtnStart")!;
            Assert.False(overlay.IsVisible);
            var browser = shell.Named<SettingsTabView>("SettingsTab")!.BrowserContainer;
            Assert.True(browser.IsVisible);

            Assert.Equal("ABC123", await relay.StartAsync("light"));
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();

            // WPF NotifyRemoteControllerJoined (:1481): the join raises the OS notification (via the seam, never D-Bus).
            lock (notified)
                Assert.Equal(new[] { $"{Loc.Get("title_remote_controller_joined")}|{Loc.Get("msg_remote_controller_joined")}" }, notified);

            // Overlay up with code + PIN; Start disabled, green, "REMOTE CONNECTED" (WPF StartStop.cs:992).
            Assert.True(overlay.IsVisible);
            Assert.False(browser.IsVisible);
            Assert.Equal(1, Target(overlay));
            Assert.Equal($"Session: A B C 1 2 3  PIN: {relay.ConnectPin}", shell.Named<TextBlock>("TxtOverlaySessionCode")!.Text);
            Assert.False(start.IsEnabled);
            Assert.Equal(Loc.Get("label_remote_connected"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
            shell.UpdateStartButton();   // an engine refresh keeps the remote label (WPF :941)
            Assert.Equal("\U0001F3AE", shell.Named<TextBlock>("TxtStartIcon")!.Text);
            Assert.True(shell.Named<Control>("RemoteSessionIdle")!.IsVisible);   // no session running
            Assert.Equal(MainShellWindow.RemoteOverlaySlowTick, shell.RemoteOverlayInterval);

            // A controller leaving restores the browser after the overlay fades; reconnect hides it again.
            f.Poll = "{\"controller_connected\":false}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            clock.Step(MainShellWindow.RemoteOverlayFadeOut);
            shell.RemoteOverlayTick();
            Assert.True(browser.IsVisible);
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(browser.IsVisible);

            // A loud verb toasts for 2 s; a quiet one does not.
            var toast = shell.Named<Border>("RemoteCommandNotification")!;
            f.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"trigger_flash\"}]}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, Target(toast));
            f.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"2\",\"action\":\"show_spiral\"}]}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, Target(toast));
            Assert.Equal(MainShellWindow.RemoteOverlayFastTick, shell.RemoteOverlayInterval);   // only while the toast is pending
            Assert.Equal(Loc.Get("cmd_spiral_enabled"), shell.Named<TextBlock>("TxtRemoteCommand")!.Text);
            clock.Step(TimeSpan.FromSeconds(1.9));
            shell.RemoteOverlayTick();
            Assert.Equal(1, Target(toast));
            clock.Step(TimeSpan.FromSeconds(0.1));
            shell.RemoteOverlayTick();
            Assert.Equal(0, Target(toast));
            Assert.Equal(MainShellWindow.RemoteOverlaySlowTick, shell.RemoteOverlayInterval);   // back to WPF's 1 s

            // Idle controller: the orange subtitle.
            f.Poll = "{\"controller_connected\":true,\"controller_idle\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("label_controller_may_be_idle"), shell.Named<TextBlock>("TxtRemoteOverlaySubtitle")!.Text);

            // Big picker: three slots above End Session, two below; a click posts the emote and says so.
            var top = shell.Named<ItemsControl>("LstEmotePresetsBigTop")!;
            Assert.Equal(3, top.ItemCount);
            Assert.Equal(2, shell.Named<ItemsControl>("LstEmotePresetsBigBottom")!.ItemCount);
            Dispatcher.UIThread.RunJobs();
            var big = top.GetVisualDescendants().OfType<Button>().First(b => b.Tag is EmotePreset);
            big.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => f.Paths.Contains("/v2/remote/emote"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Loc.Get("status_emote_sent"), shell.Named<TextBlock>("TxtEmoteStatusBig")!.Text);

            // Enter in the big custom box sends it too, clears the box and keeps it as the ghost (WPF :362).
            emoteNow = emoteNow.AddSeconds(1);
            var box = shell.Named<TextBox>("TxtEmoteCustomBig")!;
            box.Text = "  on my way ";
            box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitFor(() => f.Paths.Count(x => x == "/v2/remote/emote") == 2);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", box.Text);
            Assert.Equal("on my way", box.Watermark);

            // End Session: the relay stops, the overlay fades 200 ms then goes, Start comes back.
            var end = overlay.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == Loc.Get("btn_end_session"));
            end.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitFor(() => !relay.IsActive);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("/v2/remote/stop", f.Paths);
            Assert.Equal(0, Target(overlay));
            Assert.True(overlay.IsVisible);   // still fading
            clock.Step(MainShellWindow.RemoteOverlayFadeOut);
            shell.RemoteOverlayTick();
            Assert.False(overlay.IsVisible);
            Assert.True(browser.IsVisible);
            Assert.True(start.IsEnabled);
            Assert.Equal(Loc.Get("label_start"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
        }
        finally
        {
            shell?.Close();
            relay.Dispose();
            (RemoteControlTabView.Relay, MainShellWindow.RemoteOverlayTime, CoreSettings.ServiceProvider) = (oldRelay, oldTime, oldProvider);
            global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = oldSink;
        }
    });

    /// <summary>WPF play_hypnotube (RemoteControlService.cs:1413 -> MainWindow.Browser.cs:3188): the page opens over the
    /// overlay, a reconnect mid-fade keeps it, stop_video and a leave put the blindfold back, offline refuses.</summary>
    [Fact]
    public Task Controller_video_opens_over_the_overlay_and_stop_leave_and_offline_restore_the_blindfold() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldRelay, oldTime, oldProvider) = (RemoteControlTabView.Relay, MainShellWindow.RemoteOverlayTime, CoreSettings.ServiceProvider);
        var (oldSink, oldPlay, oldStop) = (global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink, RemoteCommands.PlayHypnotube, RemoteCommands.StopBrowserVideo);
        var flashWasRunning = CoreFlash.IsRunning;
        global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => { };
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var f = new FakeRelay();
        // Leave runs only the browser stop: the rest of StopEffects is covered in Core (RemoteHypnotubeTests).
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => RemoteCommands.StopBrowserVideo?.Invoke(), f) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        var clock = new SteppedClock();
        MainShellWindow.RemoteOverlayTime = clock;
        MainShellWindow? shell = null;
        const string video = "https://hypnotube.com/video/example-123.html";
        async Task Poll(string body) { f.Poll = body; await relay.PollOnceAsync(); Dispatcher.UIThread.RunJobs(); }
        Func<string, string?>? hookAfterClose = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var settings = shell.Named<SettingsTabView>("SettingsTab")!;
            var (browser, web) = (settings.BrowserContainer, settings.FindControl<global::ConditioningControlPanel.Avalonia.Views.Controls.WebHost>("BrowserWebHost")!);
            const string play = "{\"controller_connected\":true,\"commands\":[{\"id\":\"ID\",\"action\":\"play_hypnotube\",\"params\":{\"url\":\"" + video + "\"}}]}";
            Assert.Equal("ABC123", await relay.StartAsync("light"));
            await Poll("{\"controller_connected\":true}");
            Assert.False(browser.IsVisible);

            await Poll(play.Replace("ID", "1"));
            Assert.True(browser.IsVisible);
            Assert.Equal(new Uri(video), web.Source);

            // Leave without the stop (the subject's choice), back before the fade ends: still visible.
            service.Current.StopEffectsOnRemoteDisconnect = false;
            await Poll("{\"controller_connected\":false}");
            await Poll("{\"controller_connected\":true}");
            Assert.True(browser.IsVisible);

            // WPF start_flash starts the schedule without closing the controller's page (:1287).
            await Poll("{\"controller_connected\":true,\"commands\":[{\"id\":\"flash1\",\"action\":\"start_flash\"}]}");
            Assert.True(browser.IsVisible);
            Assert.Equal(new Uri(video), web.Source);

            // stop_video: back to the site home and under the overlay again (WPF :1410, :995).
            await Poll("{\"controller_connected\":true,\"commands\":[{\"id\":\"2\",\"action\":\"stop_video\"}]}");
            Assert.False(browser.IsVisible);
            Assert.Equal(new Uri("https://hypnotube.com/"), web.Source);

            // With no remote video, start_flash also leaves the user's page alone.
            web.Source = new Uri("https://example.com/");
            await Poll("{\"controller_connected\":true,\"commands\":[{\"id\":\"flash2\",\"action\":\"start_flash\"}]}");
            Assert.Equal(new Uri("https://example.com/"), web.Source);
            Assert.False(browser.IsVisible);

            // A leave with the stop on: the video goes, then the fade hands the browser back (WPF :1164, :915).
            service.Current.StopEffectsOnRemoteDisconnect = true;
            await Poll(play.Replace("ID", "3"));
            Assert.True(browser.IsVisible);
            await Poll("{\"controller_connected\":false}");
            Assert.Equal(new Uri("https://hypnotube.com/"), web.Source);
            Assert.False(browser.IsVisible);
            clock.Step(MainShellWindow.RemoteOverlayFadeOut);
            shell.RemoteOverlayTick();
            Assert.True(browser.IsVisible);

            // Graded Intake owns the screen: refused, the blindfold stays, the page is untouched (WPF Browser.cs:3195).
            await Poll("{\"controller_connected\":true}");
            Assert.False(browser.IsVisible);
            var intake = new IntakeHostWindow();
            try
            {
                intake.Show();
                Assert.Equal("Graded Intake is on screen", RemoteCommands.Execute("play_hypnotube", new Newtonsoft.Json.Linq.JObject { ["url"] = video }));
                Assert.False(browser.IsVisible);
                Assert.Equal(new Uri("https://hypnotube.com/"), web.Source);
            }
            finally { intake.Close(); }

            // The local panic key ends the controller's video too (WPF MainWindow.xaml.cs:2066 PanicEndBrowserMedia).
            Assert.Null(RemoteCommands.Execute("play_hypnotube", new Newtonsoft.Json.Linq.JObject { ["url"] = video }));
            Assert.True(browser.IsVisible);
            PanicSurfaces.All.Single(s => s.Id == "remote-browser-video").Stop(shell);
            Assert.False(browser.IsVisible);
            Assert.Equal(new Uri("https://hypnotube.com/"), web.Source);

            // Offline: the browser cannot open it, the controller is told, the blindfold stays.
            service.Current.OfflineMode = true;
            Assert.Equal("the browser could not open it", RemoteCommands.Execute("play_hypnotube", new Newtonsoft.Json.Linq.JObject { ["url"] = video }));
            Assert.False(browser.IsVisible);
            Assert.Equal(new Uri("https://hypnotube.com/"), web.Source);
        }
        finally
        {
            shell?.Close();
            relay.Dispose();
            CoreFlash.Stop();
            hookAfterClose = RemoteCommands.PlayHypnotube;
            (RemoteControlTabView.Relay, MainShellWindow.RemoteOverlayTime, CoreSettings.ServiceProvider) = (oldRelay, oldTime, oldProvider);
            if (flashWasRunning) CoreFlash.Start();
            (RemoteCommands.PlayHypnotube, RemoteCommands.StopBrowserVideo) = (oldPlay, oldStop);
            global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = oldSink;
        }
        Assert.Null(hookAfterClose);   // the shell lets go of the hook on close
    });

    /// <summary>The opacity the fade is heading to (the transition animates the visible value).</summary>
    private static double Target(Visual v) => v.GetBaseValue(Visual.OpacityProperty).Value;

    /// <summary>The click handlers are async void: wait on their side effect, not on time (P56).</summary>
    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.True(done());
    }
}
