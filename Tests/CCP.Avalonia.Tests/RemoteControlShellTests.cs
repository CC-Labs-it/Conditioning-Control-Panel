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
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shell half of Remote Control against a fake relay (no network): WPF
/// MainWindow.RemoteControl.cs overlay, toast, Start lock, big emotes and End Session.</summary>
public sealed class RemoteControlShellTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Remote_overlay_lifetime_reaches_existing_hosts(bool pink) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        CoreSession.IsEngineRunningProvider = () => false;
        RemoteCommands.RefreshOverlays = () => { };
        var type = pink ? typeof(ConditioningControlPanel.Avalonia.Views.Overlays.PinkFilterOverlay)
            : typeof(ConditioningControlPanel.Avalonia.Views.Overlays.SpiralOverlay);
        var gate = type.GetMethod("ShouldShow", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        Assert.False((bool)gate.Invoke(null, null)!);
        var runner = new SessionRunner(new SessionLogService());
        try
        {
            global::ConditioningControlPanel.Avalonia.App.Sessions = runner;
            Assert.Null(RemoteCommands.Execute(pink ? "show_pink_filter" : "show_spiral", null));
            Assert.True((bool)gate.Invoke(null, null)!);
            runner.Start(new Session { DurationMinutes = 1 });
            Assert.Null(RemoteCommands.Execute(pink ? "show_pink_filter" : "show_spiral", null));
            runner.Pause();
            Assert.False((bool)gate.Invoke(null, null)!); // WPF SessionEngine.cs:530
            runner.Resume();
            Assert.True((bool)gate.Invoke(null, null)!); // WPF SessionEngine.cs:582
            runner.Pause();
            Assert.Null(RemoteCommands.Execute(pink ? "show_pink_filter" : "show_spiral", null));
            Assert.True(runner.IsPaused);
            Assert.True((bool)gate.Invoke(null, null)!); // WPF RemoteControlService.cs:1316-1390
            CoreEngine.Stop();
            Assert.False((bool)gate.Invoke(null, null)!);
        }
        finally { runner.Stop(); CoreEngine.Stop(); }
        return Task.CompletedTask;
    });

    // WPF RemoteControlService.cs:1476 start/stop; :1159 a leave only cancels pulses; :980 panic and session
    // end stop and re-arm on the saved switch; :1488 trigger_lock_card shows a card, :1179 teardown closes it.
    [Fact]
    public void RemoteAutonomyAndLockCardReachTheShellAndTeardownFollowsWpf() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.AutonomyModeEnabled, s.AutonomyConsentGiven, CoreEntitlement.HasPremiumProvider, s.LockCardPhrases,
            s.LockCardStrict, CoreLockCard.ShowHandler, CoreLockCard.CloseAllHandler);
        MainShellWindow? shell = null;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            shell = new MainShellWindow();
            shell.Show();
            s.AutonomyModeEnabled = s.AutonomyConsentGiven = true;
            Assert.Null(RemoteCommands.Execute("start_autonomy", null));
            Assert.True(shell.Autonomy.IsEnabled);
            Assert.True(RemoteCommands.AutonomyRunning?.Invoke());   // the status lists "autonomy"
            Assert.Null(RemoteCommands.Execute("stop_autonomy", null));
            Assert.False(shell.Autonomy.IsEnabled);
            Assert.False(RemoteCommands.AutonomyRunning?.Invoke());

            Assert.True(shell.Autonomy.Start());   // the wearer's own run
            RemoteCommands.StopEffects(false);      // the controller left
            Assert.True(shell.Autonomy.IsEnabled);
            Assert.Null(RemoteCommands.Execute("stop_autonomy", null));
            RemoteCommands.ResetAutonomy();         // panic or session end: back on the saved switch
            Assert.True(shell.Autonomy.IsEnabled);
            s.AutonomyModeEnabled = false;
            RemoteCommands.ResetAutonomy();
            Assert.False(shell.Autonomy.IsEnabled);

            (s.LockCardPhrases, s.LockCardStrict) = (new Dictionary<string, bool> { ["GOOD GIRL"] = true }, false);
            (CoreLockCard.ShowHandler, CoreLockCard.CloseAllHandler) = (LockCardWindow.PostNext, LockCardWindow.ForceCloseAll);
            Assert.Null(RemoteCommands.Execute("trigger_lock_card", null));
            RemoteCommands.StopEffects(false);      // teardown before the posted card drew
            Dispatcher.UIThread.RunJobs();
            Assert.False(LockCardWindow.IsAnyOpen());
            Assert.Null(RemoteCommands.Execute("trigger_lock_card", null));
            Dispatcher.UIThread.RunJobs();
            Assert.True(LockCardWindow.IsAnyOpen());
            RemoteCommands.StopEffects(false);
            Assert.False(LockCardWindow.IsAnyOpen());
        }
        finally
        {
            LockCardWindow.ForceCloseAll();
            shell?.Close();
            (s.AutonomyModeEnabled, s.AutonomyConsentGiven, CoreEntitlement.HasPremiumProvider, s.LockCardPhrases,
                s.LockCardStrict, CoreLockCard.ShowHandler, CoreLockCard.CloseAllHandler) = old;
        }
    });

    private sealed class FakeRelay : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public readonly List<JObject> Emotes = new();
        public HttpStatusCode EmoteStatus = HttpStatusCode.OK;
        public string Poll = "{}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var path = r.RequestUri!.AbsolutePath;
            if (path == "/v2/remote/emote")
            {
                var payload = JObject.Parse(await r.Content!.ReadAsStringAsync(ct));
                lock (Emotes) Emotes.Add(payload);
            }
            lock (Paths) Paths.Add(path);
            var body = path switch { "/v2/remote/start" => "{\"code\":\"ABC123\"}", "/v2/remote/poll" => Poll, _ => "{}" };
            return new HttpResponseMessage(path == "/v2/remote/emote" ? EmoteStatus : HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    [Theory]
    [InlineData("panic")]
    [InlineData("end")]
    [InlineData("wearer")]
    [InlineData("disconnect")]
    public Task Remote_stop_reaches_head_audio(string cause) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var playlistSlot = typeof(global::ConditioningControlPanel.Avalonia.App).GetProperty("AudioPlaylist")!;
        var oldPlaylist = playlistSlot.GetValue(null);
        var playlist = new AudioPlaylistCardModelTests.FakePlaylist { IsPlaying = true };
        var chant = global::ConditioningControlPanel.Avalonia.App.MantraChant;
        var settings = CoreSettings.Current;
        var oldChant = settings.MantraChantEnabled;
        var oldDisconnect = settings.StopEffectsOnRemoteDisconnect;
        var fake = new FakeRelay();
        var oldMod = (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider);
        var id = "remote-chant-" + Guid.NewGuid().ToString("N");
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), id);
        var sounds = System.IO.Path.Combine(root, "resources", "sounds", "companion_audio");
        System.IO.Directory.CreateDirectory(sounds);
        System.IO.File.WriteAllText(System.IO.Path.Combine(sounds, "p.mp3"), "audio");
        System.IO.File.WriteAllText(System.IO.Path.Combine(sounds, "mantras.json"),
            "{\"mantras\":[{\"id\":\"a\",\"phrase\":\"Obey\",\"promptAudio\":\"p.mp3\"}]}");
        CoreMods.ActiveModIdProvider = () => id;
        CoreMods.ActiveModPackageProvider = () => new ModPackage(new ModManifest { Id = id }, root, false);
        using var relay = global::ConditioningControlPanel.Avalonia.App.WireRemoteQuestProgress(
            new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, RemoteCommands.StopEffects, fake) { AutoPoll = false });
        try
        {
            playlistSlot.SetValue(null, playlist);
            settings.MantraChantEnabled = true;
            settings.StopEffectsOnRemoteDisconnect = cause == "disconnect";
            chant.Start();
            Assert.True(chant.IsRunning);
            if (cause == "wearer") CoreEngine.Start();
            Assert.Equal("ABC123", await relay.StartAsync("light"));
            if (cause == "panic")
            {
                fake.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"trigger_panic\"}]}";
                await relay.PollOnceAsync();
            }
            else if (cause == "disconnect")
            {
                fake.Poll = "{\"controller_connected\":true}";
                await relay.PollOnceAsync();
                fake.Poll = "{\"controller_connected\":false}";
                await relay.PollOnceAsync();
            }
            else await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(cause == "wearer", chant.IsRunning);
            Assert.Equal(cause == "wearer", playlist.IsPlaying);
        }
        finally
        {
            if (relay.IsActive) await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            chant.Stop();
            CoreEngine.Stop();
            settings.MantraChantEnabled = oldChant;
            settings.StopEffectsOnRemoteDisconnect = oldDisconnect;
            playlistSlot.SetValue(null, oldPlaylist);
            (CoreMods.ActiveModIdProvider, CoreMods.ActiveModPackageProvider) = oldMod;
            System.IO.Directory.Delete(root, true);
        }
    });

    [Theory]
    [InlineData("panic")]
    [InlineData("leave")]
    [InlineData("end")]
    [InlineData("none")]
    public Task Queued_remote_resume_waits_for_portal_and_is_cancelled(string interruption) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var oldRelay = RemoteControlTabView.Relay;
        var oldSessions = global::ConditioningControlPanel.Avalonia.App.Sessions;
        var oldPanic = CoreSettings.Current.PanicKeyEnabled;
        var fake = new FakeRelay();
        using var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, fake) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        var shell = new MainShellWindow();
        var runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
        var bind = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            runner.Start(new Session { Id = "delayed_remote", DurationMinutes = 30 });
            runner.Pause();
            CoreSettings.Current.PanicKeyEnabled = true;
            global::ConditioningControlPanel.Avalonia.Platform.PortalPanicShortcut.BindForTests = (_, _) => bind.Task;
            Assert.Equal("ABC123", await relay.StartAsync("full"));
            fake.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"resume_session\"}]}";
            await relay.PollOnceAsync();
            Assert.True(runner.IsPaused);
            Assert.False(bind.Task.IsCompleted);
            if (interruption == "panic") RemoteCommands.Execute("trigger_panic", null);
            if (interruption == "leave")
            {
                fake.Poll = "{\"controller_connected\":false}";
                await relay.PollOnceAsync();
            }
            if (interruption == "end") relay.EndSessionNow();
            bind.SetResult("F8");
            // Both BindPortalAsync and StartEffect post to the UI dispatcher.
            await Task.Delay(150);
            Dispatcher.UIThread.RunJobs();
            // A remote panic ends the session (WPF StopAllRemoteEffects -> StopSessionFromRemote); a leave or an
            // end keeps it paused. Either way the queued resume never runs.
            if (interruption == "panic") Assert.False(runner.IsRunning);
            else Assert.Equal(interruption != "none", runner.IsPaused);
        }
        finally
        {
            bind.TrySetResult("F8");
            global::ConditioningControlPanel.Avalonia.Platform.PortalPanicShortcut.BindForTests = null;
            runner.Stop(); CoreEngine.Stop();
            global::ConditioningControlPanel.Avalonia.Platform.PortalPanicShortcut.UnbindAsync().GetAwaiter().GetResult();
            MainShellWindow.ReleasePortalWhenUnused();
            CoreSettings.Current.PanicKeyEnabled = oldPanic;
            global::ConditioningControlPanel.Avalonia.App.Sessions = oldSessions;
            shell.Close();
            RemoteControlTabView.Relay = oldRelay;
        }
    });

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
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldRelay, oldTime, oldProvider) = (RemoteControlTabView.Relay, MainShellWindow.RemoteOverlayTime, CoreSettings.ServiceProvider);
        var oldSink = global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink;
        var notified = new List<string>();
        var oldAttention = MainShellWindow.RemoteJoinAttention;
        var attention = 0;
        MainShellWindow.RemoteJoinAttention = _ => attention++;
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
            Assert.NotNull(relay.SessionsProvider);
            Assert.Contains(relay.SessionsProvider!(), s => s.IsAvailable);
            Assert.NotNull(relay.SessionRunnerProvider);
            Assert.NotNull(RemoteCommands.RefreshOverlays);
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

            Assert.Equal(0, attention); // visible joins notify without flashing or restoring
            shell.WindowState = WindowState.Minimized; // Hide()+Show() threw "Cannot re-show a closed window" headless
            shell.OnRemoteControllerChanged();
            Assert.Equal(1, attention);
            Assert.Equal(WindowState.Minimized, shell.WindowState); // a join never restores the subject's window
            shell.WindowState = WindowState.Normal;

            // Overlay up with code + PIN; Start disabled, green, "REMOTE CONNECTED" (WPF StartStop.cs:992).
            Assert.True(overlay.IsVisible);
            Assert.False(shell.LeashBrowser!.IsVisible); // native browser must not paint over the remote card
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

            // A reconnect during the exit fade cancels the pending browser restoration.
            f.Poll = "{\"controller_connected\":false}";
            await relay.PollOnceAsync(); Dispatcher.UIThread.RunJobs();
            Assert.False(shell.LeashBrowser!.IsVisible);
            clock.Step(TimeSpan.FromMilliseconds(100));
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync(); Dispatcher.UIThread.RunJobs();
            clock.Step(MainShellWindow.RemoteOverlayFadeOut);
            shell.RemoteOverlayTick();
            Assert.True(overlay.IsVisible);
            Assert.False(shell.LeashBrowser.IsVisible);

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
            Assert.True(shell.LeashBrowser!.IsVisible); // restore only after the 200 ms fade
            Assert.True(start.IsEnabled);
            Assert.Equal(Loc.Get("label_start"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
            // Remote exit hands its blindfold to an open leash gate; dismissing that gate restores it.
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(MainShellWindow).GetMethod("ShowRemoteControlOverlay", flags)!.Invoke(shell, null);
            shell.LeashGate!.IsVisible = true;
            typeof(MainShellWindow).GetMethod("HideRemoteControlOverlay", flags)!.Invoke(shell, null);
            clock.Step(MainShellWindow.RemoteOverlayFadeOut);
            shell.RemoteOverlayTick();
            Assert.False(shell.LeashBrowser.IsVisible);
            typeof(MainShellWindow).GetMethod("HideLeashGate", flags)!.Invoke(shell, null);
            Assert.True(shell.LeashBrowser.IsVisible);

        }
        finally
        {
            shell?.Close();
            MainShellWindow.RemoteJoinAttention = oldAttention;
            relay.Dispose();
            (RemoteControlTabView.Relay, MainShellWindow.RemoteOverlayTime, CoreSettings.ServiceProvider) = (oldRelay, oldTime, oldProvider);
            global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = oldSink;
        }
    });

    // WPF Windows/RemoteHud/RemoteHudWindow.cs + RemoteControlService.V2.cs: the pill's More / Easy / Stop reach the
    // relay, Easy halves the remote strength; Stop ends the running session (WPF StopSessionFromRemote) only when
    // the panic key would run, else it stops the remote's effects and the session survives.
    [Fact]
    public Task Hud_pill_sends_more_easy_and_stop_and_stop_ends_the_session_only_when_the_panic_key_would() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var (oldRelay, oldProvider, oldSessions) = (RemoteControlTabView.Relay, CoreSettings.ServiceProvider, global::ConditioningControlPanel.Avalonia.App.Sessions);
        var (oldSink, oldAttention) = (global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink, MainShellWindow.RemoteJoinAttention);
        global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => { };
        MainShellWindow.RemoteJoinAttention = _ => { };
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, RemoteCommands.StopEffects, f) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        var runner = new SessionRunner(new SessionLogService());
        global::ConditioningControlPanel.Avalonia.App.Sessions = runner;
        RemoteCommands.ResetEasy();
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            await relay.StartAsync("light");
            f.Poll = "{\"controller_connected\":true,\"controller_name\":\"Ana\"}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            var hud = shell.RemoteHudPill!;
            Assert.True(hud.IsVisible); Assert.True(hud.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>().Single(e => e.Name == "RemoteLiveDot").IsVisible);
            Assert.Equal(Loc.GetF("remote_hud_has_remote", "Ana"), hud.Headline);
            Assert.Null(hud.StrengthTag);
            var raised = 0;   // WPF KeepOnTop (RemoteHudWindow.cs:271): every 1 s Refresh re-asserts topmost
            hud.PropertyChanged += (_, e) => { if (e.Property == Window.TopmostProperty && e.NewValue is true) raised++; };
            hud.Refresh(); Assert.Equal(1, raised); Assert.True(hud.Topmost);
            int Emotes() { lock (f.Paths) return f.Paths.Count(x => x == "/v2/remote/emote"); }
            async Task Press(string kind, string confirm)
            {
                var n = Emotes();
                hud.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BtnHud_" + kind).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitFor(() => Emotes() == n + 1 && hud.Subline == confirm);
            }

            await Press("more", Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_more")));
            Assert.Equal(1.0, RemoteCommands.EasyFactor);
            await Press("easy", Loc.GetF("remote_hud_sent", Loc.Get("remote_hud_easy")));
            Assert.Equal(0.5, RemoteCommands.EasyFactor);
            Assert.Equal(Loc.Get("remote_hud_half"), hud.StrengthTag);

            runner.Start(new Session { DurationMinutes = 1 });
            Assert.True(runner.IsRunning);
            var engineRan = CoreEngine.IsRunning;
            service.Current.PanicKeyEnabled = false;   // WPF StopFromRemoteHud: panic key off, no local panic
            await Press("stop", Loc.Get("remote_hud_stopped"));
            // Panic key off: Stop is never more permissive than the key, so only the remote's effects stop.
            Assert.True(runner.IsRunning);
            Assert.Equal(engineRan, CoreEngine.IsRunning);

            service.Current.PanicKeyEnabled = true;   // the key would run: the forced stop ends the session
            Assert.True(await relay.SendSignalAsync("stop"));
            Dispatcher.UIThread.RunJobs();
            Assert.False(runner.IsRunning);   // WPF StopAllRemoteEffects -> StopSessionFromRemote
            Assert.False(CoreEngine.IsRunning);

            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(shell.RemoteHudPill);   // WPF OnRemoteSessionEnded disposes the pill
            Assert.Equal(1.0, RemoteCommands.EasyFactor);
        }
        finally
        {
            shell?.Close();
            relay.Dispose();
            runner.Stop();
            CoreEngine.Stop();
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            RemoteCommands.ResetEasy();
            ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            (RemoteControlTabView.Relay, CoreSettings.ServiceProvider, global::ConditioningControlPanel.Avalonia.App.Sessions) = (oldRelay, oldProvider, oldSessions);
            (global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink, MainShellWindow.RemoteJoinAttention) = (oldSink, oldAttention);
        }
    });

    [Fact]
    public Task Teardown_restores_tray_shell_and_avatar_but_continuity_does_not() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var oldRelay = RemoteControlTabView.Relay;
        var oldProvider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        service.Current.AvatarEnabled = true;
        // A fresh settings file is a first run: the wizard opens on Show, and hiding the shell to the
        // tray ends that dialog unaccepted, which really closes the shell (FirstRunWizard.Run abort).
        service.Current.Welcomed = service.Current.HasAcceptedAgeVerification = true;
        var f = new FakeRelay();
        var relay = new RemoteRelay(() => "tok", () => "uid", "9.9.9", (_, _) => null, _ => { }, f) { AutoPoll = false };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.CreateTray();
            shell.TrayHostPresent = () => true;
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            void HideToTray() { shell.Close(); Dispatcher.UIThread.RunJobs(); Assert.False(shell.IsVisible); }
            void Restored() { Dispatcher.UIThread.RunJobs(); Assert.True(shell.IsVisible); Assert.True(shell.Tube!.IsVisible); }
            await relay.StartAsync("light");
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            HideToTray();
            f.Poll = "{\"controller_connected\":false}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.IsVisible);
            Assert.False(shell.Tube!.IsVisible);
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            service.Current.StopEffectsOnRemoteDisconnect = true;
            f.Poll = "{\"controller_connected\":false}";
            await relay.PollOnceAsync();
            Restored();
            f.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            HideToTray();
            f.Poll = "{\"controller_connected\":true,\"commands\":[{\"id\":\"1\",\"action\":\"trigger_panic\"}]}";
            await relay.PollOnceAsync();
            Restored();
            HideToTray();
            await relay.StopAsync();
            Restored();
        }
        finally
        {
            if (shell != null) { shell.TrayHostPresent = () => false; shell.Close(); }
            relay.Dispose();
            RemoteControlTabView.Relay = oldRelay;
            CoreSettings.ServiceProvider = oldProvider;
        }
    });

    [Fact]
    public Task Connected_overlay_more_receives_pointer_click_and_posts_signal() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var (oldRelay, oldAttention, oldSink) = (RemoteControlTabView.Relay, MainShellWindow.RemoteJoinAttention,
            global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink);
        var oldDelay = RemoteControlTabView.MoreConfirmationDelay;
        RemoteControlTabView.MoreConfirmationDelay = (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token);
        var fake = new FakeRelay();
        using var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", (_, _) => null, _ => { }, fake) { AutoPoll = false };
        MainShellWindow.RemoteJoinAttention = _ => { };
        global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => { };
        RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
        MainShellWindow? shell = null;
        try
        {
            shell = new MainShellWindow();
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var overlay = shell.Named<Border>("RemoteControlOverlay")!;
            overlay.Transitions = null;
            Assert.False(overlay.IsVisible);
            await relay.StartAsync("light");
            fake.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(overlay.IsVisible);
            var more = shell.Named<Button>("BtnRemoteMoreOverlay");
            Assert.NotNull(more);
            Assert.True(more!.IsEffectivelyVisible);
            Assert.True((await relay.SendEmoteAsync("hello", "", "custom")).ok);
            var point = more.TranslatePoint(new Point(more.Bounds.Width / 2, more.Bounds.Height / 2), shell);
            Assert.NotNull(point);
            Assert.InRange(point.Value.X, 0, shell.ClientSize.Width);
            Assert.InRange(point.Value.Y, 0, shell.ClientSize.Height);
            shell.MouseMove(point.Value);
            shell.MouseDown(point.Value, MouseButton.Left);
            shell.MouseUp(point.Value, MouseButton.Left);
            await WaitFor(() => { lock (fake.Emotes) return fake.Emotes.Count == 2; });
            lock (fake.Emotes)
            {
                Assert.Equal("more", (string?)fake.Emotes[1]["text"]);
                Assert.Equal("signal", (string?)fake.Emotes[1]["kind"]);
                Assert.Equal("uid-1", (string?)fake.Emotes[1]["unified_id"]);
            }
            var status = shell.Named<TextBlock>("TxtRemoteMoreStatus")!;
            await WaitFor(() => status.Text == string.Format(Loc.Get("remote_hud_sent"), Loc.Get("remote_hud_more")));
            fake.EmoteStatus = HttpStatusCode.ServiceUnavailable;
            shell.MouseDown(point.Value, MouseButton.Left);
            shell.MouseUp(point.Value, MouseButton.Left);
            await WaitFor(() => status.Text == Loc.Get("remote_hud_not_sent"));
            fake.Poll = "{\"controller_connected\":false}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // fade may still be in progress
            lock (fake.Emotes) Assert.Equal(3, fake.Emotes.Count);
            fake.Poll = "{\"controller_connected\":true}";
            await relay.PollOnceAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("", status.Text);
            await relay.StopAsync();
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            if (relay.IsActive) await relay.StopAsync();
            shell?.Close();
            RemoteControlTabView.MoreConfirmationDelay = oldDelay;
            (RemoteControlTabView.Relay, MainShellWindow.RemoteJoinAttention,
                global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink) = (oldRelay, oldAttention, oldSink);
        }
    });

    /// <summary>The opacity the fade is heading to (the transition animates the visible value).</summary>
    private static double Target(Visual v) => v.GetBaseValue(Visual.OpacityProperty).Value;

    /// <summary>The click handlers are async void: wait on their side effect, not on time (P56).</summary>
    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
        Assert.True(done());
    }

    /// <summary>WPF SettleTemporarySettingsForExit (main bafe52cfc): exit with a controller connected hands its
    /// strict lock back before the final save, through the app's own relay.</summary>
    [Fact]
    public Task Exit_save_hands_back_the_controllers_strict_lock() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var (oldRelay, strict, panic) = (RemoteControlTabView.Relay, s.StrictLockEnabled, s.PanicKeyEnabled);
        using var relay = new RemoteRelay(() => "tok", () => "uid-1", "9.9.9", RemoteCommands.Execute, _ => { }, new FakeRelay()) { AutoPoll = false };
        try
        {
            RemoteControlTabView.Relay = new Lazy<RemoteRelay>(() => relay);
            s.StrictLockEnabled = false;
            Assert.Equal("ABC123", await RemoteControlTabView.Relay.Value.StartAsync("full"));
            Assert.Null(RemoteCommands.Execute("enable_strict_lock", null));
            bool? saved = null;
            global::ConditioningControlPanel.Avalonia.App.SaveSettingsOnExit(null, () => saved = s.StrictLockEnabled);
            Assert.False(saved);
        }
        finally
        {
            global::ConditioningControlPanel.Services.Remote.RemoteWearer.Drop();
            RemoteControlTabView.Relay = oldRelay;
            (s.StrictLockEnabled, s.PanicKeyEnabled) = (strict, panic);
        }
    });
}
