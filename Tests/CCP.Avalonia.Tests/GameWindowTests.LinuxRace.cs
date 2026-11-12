using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;
/// <summary>B2 (#3702) step 3: the Linux race on the owned shell (LinuxRaceHost), /bin/cat standing in for Electron.
/// The unshown race window keeps the Glassroad session; the shell owns focus, Escape and its process group.</summary>
public sealed partial class GameWindowTests
{
    private static void RunShellRace(Action<MainShellWindow, GameWindow, LinuxRaceHost, Process, Process> body,
        Action<DateTime>? panic = null, bool listening = false, string? room = null) => RunGlassroad((shell, _, _) =>
    {
        if (!OperatingSystem.IsLinux()) return;
        var (oldLaunch, oldShell) = (GameWindow.LaunchRaceShell, LinuxRaceHost.Shell);
        var s = CoreSettings.Current;
        var (key, enabled, overrides) = (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll);
        using var p = LinuxRaceHostTests.StartCat();
        using var observer = Process.GetProcessById(p.Id);   // the host disposes p itself
        LinuxRaceHost? host = null;
        try
        {
            GameWindow.UseLinuxRaceCard = () => true;   // RunGlassroad restores it
            LinuxRaceHost.Shell = () => shell;
            GameWindow.LaunchRaceShell = (w, _) => host = new LinuxRaceHost(w, p, panic, () => listening);
            var w = room == null ? GameWindow.Launch(GameWindow.GlassroadId) : GameWindow.LaunchFriendRace(room);
            body(shell, w!, host!, p, observer);
        }
        finally
        {
            host?.Dispose();
            (GameWindow.LaunchRaceShell, LinuxRaceHost.Shell) = (oldLaunch, oldShell);
            (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = (key, enabled, overrides);
            Dispatcher.UIThread.RunJobs();
        }
    });
    [Fact]
    public void LinuxShell_SessionRunsInTheUnshownWindow_RoomInitPipeAndExit() => RunShellRace((_, w, host, p, o) =>
    {
        Assert.False(w.IsVisible);
        Assert.Same(host, w.RaceShell);
        Assert.Contains("join=abcdef123456", w.PageUrl!.Query);   // the friend-race room rides the shell's launch URL
        Assert.True(GameWindow.IsAnyOpen());
        host.Receive(new JObject { ["type"] = "shell-loaded", ["displayBackend"] = "x11" });
        var echoed = p.StandardOutput.ReadLineAsync();   // cat echoes what the host wrote to the shell's stdin
        Assert.True(echoed.Wait(5000));
        var init = JObject.Parse(echoed.Result!);
        Assert.Equal("init", (string?)init["type"]);
        Assert.Equal("webgl2", (string?)init["caps"]!["forceRenderer"]);
        host.Receive(new JObject { ["type"] = "ready" });
        Assert.True(w.IsReady);
        Assert.Same(w, GameWindow.Launch(GameWindow.GlassroadId));   // a second door focuses the live race
        host.Receive(new JObject { ["type"] = "exit" });   // the page's own Leave: GameWindow.Glassroad.cs closes it
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());
        Assert.Null(LinuxRaceHost.Current);
        Assert.True(o.WaitForExit(5000));
    }, room: "abcdef123456");
    [Theory, InlineData(0), InlineData(1), InlineData(2)]
    public void LinuxShell_StartFailureShowsTheCardSayingSo(int mode) => RunGlassroad((_, _, _) =>
    {
        if (!OperatingSystem.IsLinux()) return;
        var oldLaunch = GameWindow.LaunchRaceShell; LinuxRaceHost ClosedInput(GameWindow w) { var p = Process.Start(new ProcessStartInfo("/usr/bin/setsid", "/bin/sleep 60") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true })!; p.StandardInput.Close(); var host = new LinuxRaceHost(w, p); host.Send(new { type = "launch" }); return host.StartReading(); }
        try
        {
            GameWindow.UseLinuxRaceCard = () => true;
            GameWindow.LaunchRaceShell = (w, _) => mode == 2 ? ClosedInput(w) : mode == 1 ? new LinuxRaceHost(w, LinuxRaceHostTests.StartCat("/bin/true")).StartReading()
                : throw new System.ComponentModel.Win32Exception("setsid unavailable");
            GameWindow.Launch(GameWindow.GlassroadId);
            GameWindow? card = null;
            Assert.True(System.Threading.SpinWait.SpinUntil(() =>
            {
                Dispatcher.UIThread.RunJobs();
                card = GameWindow.OpenWindowsForTests().SingleOrDefault(x => x.RaceShell == null);
                return card != null;
            }, 5000));
            Assert.Single(GameWindow.OpenWindowsForTests());   // the dead shell's session window is gone
            Assert.True(card!.IsVisible);
            Assert.Equal(Loc.Get("rt_engine_start_failed"), card.RaceStatus.Text);
            Assert.Null(LinuxRaceHost.Current);
        }
        finally { GameWindow.LaunchRaceShell = oldLaunch; }
    });
    [Fact]   // P2 (r72): the reader kills for a failed start, then the writer's posted Dispose runs first
    public void LinuxShell_StartFailureSurvivesTheWriterDisposingFirst() => RunShellRace((_, w, host, _, o) =>
    {
        host.Kill(pipeEnded: true); host.Kill(pipeEnded: true); host.Dispose(); Dispatcher.UIThread.RunJobs();
        var card = Assert.Single(GameWindow.OpenWindowsForTests());
        Assert.NotSame(w, card); Assert.True(o.WaitForExit(5000));
        Assert.Equal(Loc.Get("rt_engine_start_failed"), card.RaceStatus.Text);
    });
    /// <summary>P1: after the panic watchdog killed the race (before the reader's Dispose ran), the press the recovered UI
    /// runs, from the X11 listener or queued from the shell pipe, is a full panic: never a pause for a dead race, no exit rung.</summary>
    [Theory, InlineData(true), InlineData(false)]
    public void LinuxShell_PanicAfterWatchdogKillIsAFullPanicNotAPause(bool x11) => RunShellRace((shell, w, host, _, o) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKey, s.PanicKeyEnabled, s.PanicOverridesAll) = ("Escape", true, false);
        host.Receive(new JObject { ["type"] = "shell-loaded", ["displayBackend"] = x11 ? "x11" : "wayland" });
        host.Receive(new JObject { ["type"] = "ready" });
        host.Receive(new JObject { ["type"] = "shell-focus", ["active"] = true });
        Assert.Same(w, LinuxRaceHost.InFrontWindow);
        var sent = new List<string>();
        w.Posted += json => sent.Add((string?)JObject.Parse(json)["type"] ?? "");
        var (surfaces, watchdog) = (PanicSurfaces.All, ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.Teardown); var stops = 0;
        using var fired = new System.Threading.ManualResetEventSlim();
        try
        {
            PanicSurfaces.All = new[] { new PanicSurfaces.Surface("games", _ => { stops++; GameWindow.CloseAllForPanic(); }, GameWindow.IsAnyOpen) };
            ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.Teardown = () => { host.Kill(); fired.Set(); };
            if (x11) { host.Kill(); shell.HandlePanicKeyPress(DateTime.Now); }   // the X11 press queued behind the teardown
            else { host.ReadFrame(new JObject { ["type"] = "shell-escape", ["sequence"] = 1 }); Assert.True(fired.Wait(TimeSpan.FromSeconds(6))); }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, stops);
            Assert.DoesNotContain("kept-escape", sent);
            Assert.Equal(0, shell.PanicPressCountForTest);
            Assert.False(GameWindow.IsAnyOpen());
            Assert.Null(LinuxRaceHost.Current);
            Assert.True(o.WaitForExit(5000));
        }
        finally { (PanicSurfaces.All, ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.Teardown) = (surfaces, watchdog); Dispatcher.UIThread.RunJobs(); }
    });
    [Fact]
    public void LinuxShell_WaylandEscapePausesThenPanicsWithXWaylandListening()
    {
        MainShellWindow? main = null; var press = 0; var now = DateTime.Now;
        RunShellRace((shell, w, host, _, o) =>
        {
            main = shell;
            (CoreSettings.Current.PanicKey, CoreSettings.Current.PanicKeyEnabled) = ("Escape", true);
            host.Receive(new JObject { ["type"] = "shell-loaded", ["displayBackend"] = "wayland" });
            host.Receive(new JObject { ["type"] = "ready" });
            host.Receive(new JObject { ["type"] = "shell-focus", ["active"] = true });
            var sent = new List<string>();
            w.Posted += json => sent.Add((string?)JObject.Parse(json)["type"] ?? "");
            var escape = new JObject { ["type"] = "shell-escape", ["sequence"] = 1 };
            host.Receive(escape); host.Receive(escape);   // a repeat delivery keeps its sequence
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("kept-escape", Assert.Single(sent));
            Assert.False(o.HasExited);
            escape["sequence"] = 2; host.Receive(escape);
            Dispatcher.UIThread.RunJobs();
            Assert.True(o.WaitForExit(5000));
            Assert.Null(LinuxRaceHost.Current);
            Assert.False(GameWindow.IsAnyOpen());
        }, panic: _ => main!.HandlePanicKeyPress(now.AddMilliseconds(100 * press++)), listening: true);
    }
    [Theory, InlineData("wayland", "F12", true, true, true), InlineData("wayland", "Pause", true, true, true)]
    [InlineData(null, "F12", true, true, true), InlineData("x11", "F12", true, true, false)]
    [InlineData("x11", "F12", false, true, true), InlineData("x11", "Escape", false, true, false)]
    [InlineData("wayland", "F12", false, false, false)]   // a disabled panic key rejects nothing
    public void LinuxShell_UnsupportedPanicKeyFailsClosed(string? backend, string key, bool listening, bool enabled, bool closed) => RunShellRace((_, _, host, _, o) =>
    {
        (CoreSettings.Current.PanicKey, CoreSettings.Current.PanicKeyEnabled) = (key, enabled);
        Dispatcher.UIThread.RunJobs();
        host.ReadFrame(new JObject { ["type"] = "shell-loaded", ["displayBackend"] = backend });
        Dispatcher.UIThread.RunJobs();
        if (closed) { Assert.True(o.WaitForExit(5000)); Assert.Null(LinuxRaceHost.Current); }
        else { Assert.False(o.HasExited); Assert.Same(host, LinuxRaceHost.Current); }
    }, listening: listening);
    [Theory, InlineData(true), InlineData(false)]
    public void LinuxShell_LeashRequiresPanicRouteEvenWhenPanicIsOff(bool initiallyLeashed) => RunShellRace((_, _, host, _, o) =>
    {
        var old = ConditioningControlPanel.Avalonia.Platform.LeashHead.Service; var svc = new ConditioningControlPanel.Services.Leash.LeashService(new LeashHeadTests.NoLeashWire(), () => "u_me");
        try {
            ConditioningControlPanel.Avalonia.Platform.LeashHead.Service = svc; (CoreSettings.Current.PanicKey, CoreSettings.Current.PanicKeyEnabled) = ("F12", false);
            if (initiallyLeashed) { svc.ApplyBlock(LeashHeadTests.BlockWithPunishment()); } host.ReadFrame(new JObject { ["type"] = "shell-loaded", ["displayBackend"] = "x11" }); Dispatcher.UIThread.RunJobs();
            if (!initiallyLeashed) { Assert.False(o.HasExited); svc.ApplyBlock(LeashHeadTests.BlockWithPunishment()); host.CheckPanicKey(); }
            Assert.True(System.Threading.SpinWait.SpinUntil(() => { Dispatcher.UIThread.RunJobs(); return o.HasExited; }, 8000)); Assert.Null(LinuxRaceHost.Current);
        } finally { ConditioningControlPanel.Avalonia.Platform.LeashHead.Service = old; }
    }, listening: false);
    [Fact]
    public void LinuxShell_ReaderEscapeWatchdogKillsWhileUiIsBlocked()
    {
        var handled = 0; LinuxRaceHost? atPanic = null;
        RunShellRace((_, _, host, p, o) =>
        {
            (CoreSettings.Current.PanicKey, CoreSettings.Current.PanicKeyEnabled) = ("Escape", true);
            using var fired = new System.Threading.ManualResetEventSlim();
            var teardown = ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.Teardown;
            try
            {
                ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.Teardown = () => { teardown(); fired.Set(); };
                host.StartReading();
                p.StandardInput.WriteLine("{\"type\":\"shell-loaded\",\"displayBackend\":\"wayland\"}");
                p.StandardInput.WriteLine("{\"type\":\"shell-escape\",\"sequence\":1}"); p.StandardInput.Flush();
                // Deliberately block the dispatcher: only the pipe reader and emergency teardown can run.
                Assert.True(fired.Wait(TimeSpan.FromSeconds(6)));
                Assert.True(o.WaitForExit(5000));
                Assert.Equal(0, handled);
                Dispatcher.UIThread.RunJobs();   // the UI recovers: the queued press still runs the full panic
                Assert.Equal(1, handled);
                Assert.Same(host, atPanic);
            }
            finally { ConditioningControlPanel.Avalonia.Platform.PanicWatchdog.Teardown = teardown; Dispatcher.UIThread.RunJobs(); }
        }, panic: _ => { handled++; atPanic = LinuxRaceHost.Current; });
    }
    [Fact]
    public void LinuxShell_LaunchUrlTravelsOnStdinAndNotInProcessArguments() => RunGlassroad((_, _, _) =>
    {
        if (!OperatingSystem.IsLinux()) return;
        var dir = System.IO.Directory.CreateTempSubdirectory("ccp-race-").FullName;
        var engine = System.IO.Path.Combine(dir, "engine"); var shellJs = System.IO.Path.Combine(dir, "shell");
        var args = System.IO.Path.Combine(dir, "args"); var frame = System.IO.Path.Combine(dir, "frame");
        System.IO.File.WriteAllText(shellJs, "");
        System.IO.File.WriteAllText(engine, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{args}'\nIFS= read -r line\nprintf '%s' \"$line\" > '{frame}'\nsleep 60\n");
        System.IO.File.SetUnixFileMode(engine, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute);
        var url = new Uri("http://127.0.0.1:1234/race?token=fake-test-token");
        LinuxRaceHost? host = null;
        try
        {
            Assert.Null(LinuxRaceHost.TryLaunch(new GameWindow(), url, System.IO.Path.Combine(dir, "missing"), shellJs));
            host = LinuxRaceHost.TryLaunch(new GameWindow(), url, engine, shellJs);
            Assert.NotNull(host);
            Assert.True(System.Threading.SpinWait.SpinUntil(() => System.IO.File.Exists(frame) && new System.IO.FileInfo(frame).Length > 0, 10000));
            Assert.DoesNotContain("fake-test-token", System.IO.File.ReadAllText(args));
            var launch = JObject.Parse(System.IO.File.ReadAllText(frame));
            Assert.Equal("launch", (string?)launch["type"]);
            Assert.Equal(url.AbsoluteUri, (string?)launch["url"]);
        }
        finally { host?.Dispose(); System.IO.Directory.Delete(dir, true); }
    });
    [Fact]
    public void LinuxShell_PanicKillsExactShellAndItsProcessGroup() => RunGlassroad((_, _, _) =>
    {
        if (!OperatingSystem.IsLinux()) return;
        var start = new ProcessStartInfo("/usr/bin/setsid") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/bin/sh"); start.ArgumentList.Add("-c"); start.ArgumentList.Add("sleep 60 & echo $!; wait");
        using var p = Process.Start(start)!;
        var child = int.Parse(p.StandardOutput.ReadLine()!);
        var host = new LinuxRaceHost(new GameWindow(), p);
        try
        {
            host.Kill(); Assert.True(p.WaitForExit(5000));
            Assert.True(System.Threading.SpinWait.SpinUntil(() => !System.IO.File.Exists($"/proc/{child}/stat")
                || System.IO.File.ReadAllText($"/proc/{child}/stat").Split(' ')[2] == "Z", 5000));
        }
        finally { host.Dispose(); try { Process.GetProcessById(child).Kill(); } catch { } }
    });
}
