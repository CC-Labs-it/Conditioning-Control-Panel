using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
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
            w.HandleMessage("{\"type\":\"ready\"}");
            var sent = new System.Collections.Generic.List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
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

        Assert.Equal(new[] { "init", "manifest", "fullscreen" }, frames.Select(f => (string?)f["type"]));
        var init = frames[0];
        Assert.Equal(1, (int)init["protocol"]!);
        Assert.False((bool)init["solo"]!);
        Assert.Equal("", (string?)init["net"]!["authToken"]);   // the token never reaches the page
        Assert.True((bool)init["net"]!["viaHost"]!);
        Assert.False((bool)init["caps"]!["haptics"]!);
        Assert.NotNull(init["consent"]!["liveDurationSec"]);
        Assert.NotNull(frames[1]["images"]);
        Assert.Empty((JArray)frames[1]["received"]!);
        Assert.True(goon.KeptEscapeReady);
        Assert.Equal(PanicPolicy.KeptEscapePage.Goon, goon.KeptEscapePage);
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
}
