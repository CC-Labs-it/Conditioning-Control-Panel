using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Goon Game host (WPF GoonHostService.cs:229-337 Launch, :426 OnPageReady, :983
/// ApplyHostFullscreen, :1667 watchdog; MainWindow.PlayTab.cs:60-64 door; GameSurfaces.cs:64 panic).</summary>
public sealed partial class GameWindowTests
{
    [Fact]
    public void GoonWatch_PingsOnlyAfterReadyAndPanicCancelsRecovery() => Run((shell, launcher) =>
    {
        var old = GameWindow.GoonUtcNow;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.GoonUtcNow = () => now;
        try
        {
            var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            w.Load(GameWindow.Server()); w.Show();
            var pings = 0;
            w.Posted += json => { if (json.Contains("\"ping\"")) pings++; };
            now = now.AddSeconds(30); w.CheckGoonWatch(now);
            Assert.Contains(w, GameWindow.OpenWindowsForTests());
            Assert.Equal(0, pings);
            w.HandleMessage("{\"type\":\"ready\"}");
            now = now.AddSeconds(9); w.CheckGoonWatch(now);
            Assert.Equal(1, pings);
            w.Closed += (_, _) => GameWindow.CloseAllForPanic();
            now = now.AddSeconds(12); w.CheckGoonWatch(now);
            Assert.DoesNotContain(GameWindow.OpenWindowsForTests(), x => x.Spec.Id == "goon");
        }
        finally { GameWindow.GoonUtcNow = old; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GoonWatch_RecoversOnceFromSilentOrFrozenPage(bool frozen) => Run((shell, launcher) =>
    {
        var old = GameWindow.GoonUtcNow;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.GoonUtcNow = () => now;
        try
        {
            var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            w.Load(GameWindow.Server());
            w.Show();
            w.HandleMessage("{\"type\":\"ready\"}");
            if (frozen) w.HandleMessage("{\"type\":\"heartbeat\",\"paint\":1,\"vis\":\"visible\"}");
            for (var i = 0; i < 5; i++)
            {
                now = now.AddSeconds(5);
                if (frozen) w.HandleMessage("{\"type\":\"pong\",\"paint\":1,\"vis\":\"visible\"}");
                w.CheckGoonWatch(now);
            }
            var next = Assert.Single(GameWindow.OpenWindowsForTests().Where(x => x.Spec.Id == "goon"));
            Assert.NotSame(w, next);
            next.HandleMessage("{\"type\":\"ready\"}");
            for (var i = 0; i < 5; i++) { now = now.AddSeconds(5); next.CheckGoonWatch(now); }
            Assert.DoesNotContain(next, GameWindow.OpenWindowsForTests());
            Assert.DoesNotContain(GameWindow.OpenWindowsForTests(), x => x.Spec.Id == "goon");
        }
        finally { GameWindow.GoonUtcNow = old; }
    });

    /// <summary>WPF GoonHostService.cs:300,1789-1792: a recovery relaunch comes back windowed whatever the
    /// remembered mode, and the remembered mode itself is left alone.</summary>
    [Fact]
    public void GoonWatch_RecoveryRelaunchIsWindowed() => Run((shell, launcher) =>
    {
        var old = GameWindow.GoonUtcNow;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.GoonUtcNow = () => now;
        try
        {
            CoreSettings.Current.GoonFullscreen = true;
            var w = GameWindow.Launch(GameWindow.GoonId)!;
            Assert.Equal(WindowState.FullScreen, w.WindowState);
            w.HandleMessage("{\"type\":\"ready\"}");
            now = now.AddSeconds(21); w.CheckGoonWatch(now);
            var next = Assert.Single(GameWindow.OpenWindowsForTests(), x => x.Spec.Id == GameWindow.GoonId);
            Assert.NotSame(w, next);
            Assert.Equal(WindowState.Normal, next.WindowState);
            Assert.True(CoreSettings.Current.GoonFullscreen);
            next.Close();
            // Only the relaunch is windowed: the next ordinary open uses the remembered mode again.
            var again = GameWindow.Launch(GameWindow.GoonId)!;
            Assert.Equal(WindowState.FullScreen, again.WindowState);
            again.Close();
        }
        finally { GameWindow.GoonUtcNow = old; }
    });

    /// <summary>WPF MainWindow.xaml.cs:1526-1588 through the shell: once Goon is up the first Escape is its
    /// pause (kept-escape), a second within 2 s is the panic that closes it.</summary>
    [Fact]
    public void Goon_EscapePausesOnceUp_ASecondEscapeClosesIt() => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        var (oldKey, oldEnabled) = (s.PanicKey, s.PanicKeyEnabled);
        try
        {
            CoreAccount.IsLoggedInProvider = () => true;
            (s.PanicKey, s.PanicKeyEnabled) = ("Escape", true);
            launcher.Hide();
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            var w = GameWindow.Launch(GameWindow.GoonId)!;
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            var sent = new System.Collections.Generic.List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\"}");
            WaitGoonReplies(sent, 3);   // init, fullscreen and the off-thread manifest
            sent.Clear();
            shell.HandlePanicKeyPress(t0);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(w, GameWindow.OpenWindowsForTests());
            Assert.Equal("kept-escape", (string?)Assert.Single(sent)["type"]);

            shell.HandlePanicKeyPress(t0.AddSeconds(1));
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally { (s.PanicKey, s.PanicKeyEnabled) = (oldKey, oldEnabled); }
    });

    [Fact]
    public void GoonWatch_HiddenPageAndLateUiTickDoNotRecover() => Run((shell, launcher) =>
    {
        var old = GameWindow.GoonUtcNow;
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        GameWindow.GoonUtcNow = () => now;
        try
        {
            var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            w.Load(GameWindow.Server()); w.Show(); w.HandleMessage("{\"type\":\"ready\"}");
            w.HandleMessage("{\"type\":\"heartbeat\",\"paint\":1}");
            for (var i = 0; i < 6; i++)
            {
                now = now.AddSeconds(5);
                w.HandleMessage("{\"type\":\"heartbeat\",\"paint\":1,\"vis\":\"hidden\"}");
                w.CheckGoonWatch(now);
            }
            now = now.AddSeconds(30);
            w.HandleMessage("{\"type\":\"pong\",\"paint\":1,\"vis\":\"visible\"}");
            w.CheckGoonWatch(now);
            Assert.Contains(w, GameWindow.OpenWindowsForTests());
            GameWindow.CloseAllForPanic();
            Assert.DoesNotContain(w, GameWindow.OpenWindowsForTests());
        }
        finally { GameWindow.GoonUtcNow = old; }
    });

    [Fact]
    public void Goon_SignedInPlayOpensTheGameAndPanicClosesIt() => Run((shell, _) =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        shell.ShowTab("play");
        Dispatcher.UIThread.RunJobs();
        var button = shell.Named<Control>("PlayTab")!.FindControl<Button>("BtnPlayGoon")!;
        Assert.True(button.IsEnabled);
        Assert.Null(ToolTip.GetTip(button));

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        var goon = GameWindow.OpenForTests(GameWindow.GoonId);
        Assert.NotNull(goon);
        Assert.EndsWith("/goon/index.html", goon!.PageUrl!.AbsolutePath);
        // A second press focuses the live game, never a second window.
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Single(GameWindow.OpenWindowsForTests(), w => w.Spec.Id == GameWindow.GoonId);

        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(GameWindow.OpenForTests(GameWindow.GoonId));
        Assert.False(GameWindow.IsAnyOpen());
    });

    [Fact]
    public void Goon_ReadyGetsInitManifestAndWindowMode() => Run((_, _) =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        var goon = GameWindow.Launch(GameWindow.GoonId)!;
        var frames = new System.Collections.Generic.List<JObject>();
        goon.Posted += json => frames.Add(JObject.Parse(json));
        goon.HandleMessage("{\"type\":\"ready\"}");
        WaitGoonReplies(frames, 3);   // the inbox boot posts the manifest off the UI thread

        Assert.Equal(new[] { "init", "fullscreen", "manifest" }, frames.Select(f => (string?)f["type"]));
        var init = frames[0];
        Assert.Equal(1, (int)init["protocol"]!);
        Assert.False((bool)init["solo"]!);
        Assert.Equal("", (string?)init["net"]!["authToken"]);   // the token never reaches the page
        Assert.True((bool)init["net"]!["viaHost"]!);
        Assert.False((bool)init["caps"]!["haptics"]!);   // no toy connected
        Assert.NotNull(init["consent"]!["liveDurationSec"]);
        Assert.NotNull(frames[2]["images"]);
        Assert.IsType<JArray>(frames[2]["received"]);
        Assert.True(goon.KeptEscapeReady);
        Assert.Equal(PanicPolicy.KeptEscapePage.Goon, goon.KeptEscapePage);
    });

    [Fact]
    public void Goon_NonMediaFramesLeaveTheStaticMediaDelegatesAlone() => Run((_, _) =>
    {
        // r2 review: every Goon frame used to set these process-wide delegates and leak them into later tests.
        var (materialize, release) = (ConditioningControlPanel.Services.GoonGame.GoonOnlineMedia.Materialize,
            ConditioningControlPanel.Services.GoonGame.GoonOnlineMedia.ReleaseTempFile);
        try
        {
            ConditioningControlPanel.Services.GoonGame.GoonOnlineMedia.Materialize = null;
            ConditioningControlPanel.Services.GoonGame.GoonOnlineMedia.ReleaseTempFile = null;
            CoreAccount.IsLoggedInProvider = () => true;
            var goon = GameWindow.Launch(GameWindow.GoonId)!;
            goon.HandleMessage("{\"type\":\"ready\"}");
            goon.HandleMessage("{\"type\":\"heartbeat\"}");
            Assert.Null(ConditioningControlPanel.Services.GoonGame.GoonOnlineMedia.Materialize);
            Assert.Null(ConditioningControlPanel.Services.GoonGame.GoonOnlineMedia.ReleaseTempFile);
        }
        finally
        {
            ConditioningControlPanel.Services.GoonGame.GoonOnlineMedia.Materialize = materialize;
            ConditioningControlPanel.Services.GoonGame.GoonOnlineMedia.ReleaseTempFile = release;
        }
    });

    [Fact]
    public void Goon_NetPostIsRefusedAtOnceAndFullscreenIsEchoedAndRemembered() => Run((_, _) =>
    {
        CoreAccount.IsLoggedInProvider = () => true;
        var goon = GameWindow.Launch(GameWindow.GoonId)!;
        var frames = new System.Collections.Generic.List<JObject>();
        goon.Posted += json => frames.Add(JObject.Parse(json));

        goon.HandleMessage("{\"type\":\"net-post\",\"id\":\"n7\",\"path\":\"/v2/goon/invite\",\"body\":{}}");
        var reply = Assert.Single(frames);
        Assert.Equal("net-post-result", (string?)reply["type"]);
        Assert.Equal("n7", (string?)reply["id"]);
        Assert.Equal(0, (int)reply["status"]!);

        frames.Clear();
        goon.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":true}");
        Assert.Equal(WindowState.FullScreen, goon.WindowState);
        Assert.True((bool)Assert.Single(frames)["on"]!);
        Assert.True(CoreSettings.Current.GoonFullscreen);
        goon.Close();
        // WPF :300: the next window is built in the remembered mode.
        var again = GameWindow.Launch(GameWindow.GoonId)!;
        Assert.Equal(WindowState.FullScreen, again.WindowState);
        again.HandleMessage("{\"type\":\"fullscreen-set\",\"on\":false}");
        Assert.False(CoreSettings.Current.GoonFullscreen);
    });

    /// <summary>WPF GoonHostService.cs:757-768: a finished practice claims practice and the slip reaches the page.</summary>
    [Fact]
    public void Goon_PracticeResultClaimsSparklesAndPostsTheSlip() => Run((_, _) =>
    {
        var previous = SparklesHead.Service;
        var wire = new RewardWire();
        using var http = new System.Net.Http.HttpClient(wire);
        try
        {
            CoreAccount.IsLoggedInProvider = () => true;
            SparklesHead.Service = new ConditioningControlPanel.Services.Sparkles.Claims.SparklesClaimService(http, () => ("account", "test-token"), "http://localhost");
            var goon = GameWindow.Launch(GameWindow.GoonId)!;
            var frames = new System.Collections.Generic.List<JObject>();
            goon.Posted += json => frames.Add(JObject.Parse(json));
            goon.HandleMessage("{\"type\":\"match-result\",\"practice\":true,\"code\":\"\",\"result\":{\"endReason\":1,\"winnerIsHost\":true,\"localIsHost\":true,\"survivedMs\":90000}}");
            wire.Reply.SetResult(new(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("{ok:true,receipt:{paid:8,credited:8,result:{kind:'duel',outcome:'win'}}}") });
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!goon.SparklesPending.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(1); }
            Assert.True(goon.SparklesPending.IsCompletedSuccessfully);
            Assert.Equal(1, wire.Calls);
            Assert.Equal(("goon", "practice", "win"), ((string?)wire.Body!["game"], (string?)wire.Body["mode"], (string?)wire.Body["result"]!["outcome"]));
            Assert.Single(frames, f => (string?)f["type"] == "sparkles-receipt");
            goon.Close();
        }
        finally { SparklesHead.Service = previous; }
    });

    /// <summary>WPF GoonHostService.cs:469,749-756,2074-2099 and :361: a connected toy is advertised, its state is
    /// pushed, cues play on it, and once the window closes the cue stops and later cues are refused.</summary>
    [Fact]
    public void Goon_ToyCuesPlayOnAConnectedToyAndStopWithTheWindow() => Run((_, _) =>
    {
        var s = CoreSettings.Current.Haptics;
        s.EnsureV2Migrated();
        var old = (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, MockProviderV2.Toast);
        var toasts = new System.Collections.Concurrent.ConcurrentQueue<string>();
        MockProviderV2.Toast = toasts.Enqueue;
        CoreEntitlement.HasPremiumProvider = () => true;
        (s.Enabled, s.ButtplugUrl) = (true, "ws://127.0.0.1:1");
        s.V2.Provider("mock").Enabled = true;
        s.V2.Provider("buttplug").Enabled = false;
        s.V2.Provider("lovense").Enabled = false;
        var haptics = new HapticService(s);
        CoreHaptics.Service = haptics;
        bool Wait(Func<bool> done)
        {
            for (var t = 0; t < 3000 && !done(); t += 20) { Dispatcher.UIThread.RunJobs(); System.Threading.Thread.Sleep(20); }
            return done();
        }
        try
        {
            Assert.True(haptics.ConnectAsync().Result);
            CoreAccount.IsLoggedInProvider = () => true;
            var goon = GameWindow.Launch(GameWindow.GoonId)!;
            var frames = new System.Collections.Generic.List<JObject>();
            goon.Posted += json => frames.Add(JObject.Parse(json));
            goon.HandleMessage("{\"type\":\"ready\"}");
            Assert.True((bool)frames.First(f => (string?)f["type"] == "init")["caps"]!["haptics"]!);

            haptics.DisconnectAsync().Wait();
            Assert.True(Wait(() => frames.Any(f => (string?)f["type"] == "haptics-state" && !(bool)f["connected"]!)));
            Assert.True(haptics.ConnectAsync().Result);
            Assert.True(Wait(() => frames.Any(f => (string?)f["type"] == "haptics-state" && (bool)f["connected"]!)));

            goon.HandleMessage("{\"type\":\"toy-pattern\",\"intensity\":0.3,\"durationMs\":0,\"pattern\":\"steady\"}");
            Assert.True(Wait(() => toasts.Any(t => t.Contains('%'))), string.Join(" | ", toasts));
            goon.Close();
            System.Threading.Thread.Sleep(400);
            toasts.Clear();
            GoonToyCues.OnPattern(JObject.Parse("{\"intensity\":0.9,\"durationMs\":0}"));
            System.Threading.Thread.Sleep(600);
            Assert.DoesNotContain(toasts, t => t.Contains('%'));
        }
        finally
        {
            haptics.PanicStop();
            haptics.Dispose();
            (CoreHaptics.Service, CoreEntitlement.HasPremiumProvider, MockProviderV2.Toast) = old;
        }
    });
}
