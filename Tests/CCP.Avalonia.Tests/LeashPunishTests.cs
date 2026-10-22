using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Leash;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>leash-punish (WPF MainWindow.Leash.cs StartLeashTask / OnLeashCompleted / LeashOnPanicPress,
/// AppLeashTaskHost, LeashPunishWindow): from a launcher tile, the gate's main button starts the task on the Core
/// LeashTaskRunner; finishing it completes the punishment and the gate stays down; a panic parks the task and closes
/// the caged video window. The real Core LeashService on a no-network wire; the features behind the runner are faked.</summary>
public sealed class LeashPunishTests
{
    private sealed class FakeHost : ILeashTaskHost
    {
        public int Cards, Shows;
        public int LockCardsCompleted => Cards;
        public bool ShowLockCard() { Shows++; return true; }
        public bool LockCardOpen => false; public bool SessionRunning => false; public bool BubblesRunning => false;
        public bool WatchOpen => false; public bool WatchCaged => false;
        public bool StartSession(PunishKind kind, int minutes) => false;
        public bool StartBubbles() => false;
        public bool OpenWatch(LeashWatch watch) => false;
        public LeashWatchSample? SampleWatch(LeashWatch watch) => null;
        public void StopSession() { } public void StopBubbles() { }
        public event Action? BubblePopped { add { } remove { } }
        public event Action<LeashWatch>? WatchFinished { add { } remove { } }
        public event Action<LeashWatch>? WatchFailed { add { } remove { } }
        public void EndWatch() { }
    }
    private static DateTimeOffset Now = DateTimeOffset.UtcNow;   // stepped by hand (P08)
    private static void Run(Action<MainShellWindow, LauncherWindow, LeashService, FakeHost, LeashTaskRunner> body) =>
        LeashGateCardTests.Run(punished: true, (shell, launcher, svc) =>
        {
            var (oldRunner, oldTimer, oldWeb) = (LeashTaskHost.Runner, LeashTaskRunner.TimerFactory, LeashPunishWindow.CreateWeb);
            var host = new FakeHost();
            LeashTaskRunner.TimerFactory = () => null;   // P08: the test ticks the runner by hand
            var runner = new LeashTaskRunner(host, () => Now);
            LeashTaskHost.Runner = runner;
            LeashPunishWindow.CreateWeb = false;
            try { body(shell, launcher, svc, host, runner); }
            finally
            {
                LeashPunishWindow.CloseNow();
                (LeashTaskHost.Runner, LeashTaskRunner.TimerFactory, LeashPunishWindow.CreateWeb) = (oldRunner, oldTimer, oldWeb);
            }
        });

    private static void StartFromTheGate(MainShellWindow shell, LauncherWindow launcher, FakeHost host, LeashTaskRunner runner)
    {
        LeashGateCardTests.PressTile(launcher);
        Assert.True(shell.LeashGate!.IsUp);
        LeashGateCardTests.Click(LeashGateCardTests.Find(shell, "leash-gate-go"));
        Assert.Equal(1, host.Shows);                    // Lines: the first card opened
        Assert.True(runner.IsRunning);
        Assert.Equal("p1", runner.RunningPid);
        Assert.False(shell.LeashGate.IsUp);             // WPF: the gate stands down while its task runs
    }
    [Fact]
    public void GateButton_StartsTheTask_AndFinishingItClearsTheGate() => Run((shell, launcher, svc, host, runner) =>
    {
        StartFromTheGate(shell, launcher, host, runner);
        host.Cards = 3;                                 // three cards typed (Lines x3)
        runner.Tick();
        var sw = Stopwatch.StartNew();                  // P56/P73: await the async completion, bounded by time
        while (svc.GateDue != null && sw.Elapsed < TimeSpan.FromSeconds(10)) Dispatcher.UIThread.RunJobs();
        Assert.Null(svc.GateDue);
        Assert.False(runner.IsRunning);
        Assert.False(MainShellWindow.LeashBlocksGames);
        shell.CheckLeashGate();
        Assert.False(shell.LeashGate!.IsUp);
    });
    /// <summary>WPF MainWindow.xaml.cs:1567: LeashOnPanicPress runs before the lock-card rung, so the panic key parks a
    /// Lines task and its card does not come back after LockCardRetry.</summary>
    [Fact]
    public void PanicKey_ParksTheTask_NoCardReturns_AndTheGateStandsBack() => Run((shell, launcher, svc, host, runner) =>
    {
        var oldSurfaces = PanicSurfaces.All;
        var s = CoreSettings.Current;
        var oldKey = s.PanicKeyEnabled;
        try
        {
            StartFromTheGate(shell, launcher, host, runner);
            LockCardWindow.ShowOnAllMonitors("obey", 1, strictMode: false, isTest: true);   // the press dismisses a real card
            (PanicSurfaces.All, s.PanicKeyEnabled) = (Array.Empty<PanicSurfaces.Surface>(), true);   // the key route's own call only
            shell.HandlePanicKeyPress(DateTime.Now);
            Now += LeashTaskRunner.LockCardRetry + TimeSpan.FromSeconds(1);
            runner.Tick();
            Assert.False(runner.IsRunning);
            Assert.Equal(1, host.Shows);                // no card reopened
            Assert.NotNull(svc.GateDue);                // still owed
            shell.CheckLeashGate();
            Assert.False(shell.LeashGate!.IsUp);        // standing back ten minutes
        }
        finally { LockCardWindow.ForceCloseAll(); (PanicSurfaces.All, s.PanicKeyEnabled) = (oldSurfaces, oldKey); }
    });

    /// <summary>The real LeashTaskHost: a pending video punishment opens LOCKED (refuses a close), a task watch does not;
    /// the page's 'leash-ended' / 'leash-error' reach the runner; the clock sample and local media lookups.</summary>
    [Fact]
    public void RealHost_CagesTheWatch_AndReportsEndAndFailure() => LeashGateCardTests.Run(punished: false, (shell, _, svc) =>
    {
        var block = LeashHeadTests.BlockWithPunishment();
        var pun = (Newtonsoft.Json.Linq.JObject)block["me"]!["pending"]![0]!;
        (pun["kind"], pun["watch"]) = ("video", new Newtonsoft.Json.Linq.JObject { ["kind"] = "ht", ["id"] = "123" });
        svc.ApplyBlock(block);
        var (oldWeb, motion, oldCut, oldKey) = (LeashPunishWindow.CreateWeb, CoreSettings.Current.MotionLevel, LeashPunishWindow.CutLeash, CoreSettings.Current.PanicKey);
        var host = new LeashTaskHost();
        var dir = Directory.CreateTempSubdirectory("leash-punish-").FullName;
        try
        {
            (LeashPunishWindow.CreateWeb, CoreSettings.Current.MotionLevel) = (false, ConditioningControlPanel.Models.MotionLevel.Off);
            int finished = 0, failed = 0;
            host.WatchFinished += _ => finished++;
            host.WatchFailed += _ => failed++;
            Assert.True(host.OpenWatch(new LeashWatch("ht", "123", null)));
            var w = LeashPunishWindow.Current!;
            Assert.True(w.Locked && host.WatchOpen && host.WatchCaged);
            w.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(w, LeashPunishWindow.Current);  // a punishment refuses a close
            w.OnMessage("\"leash-ended\"");
            w.OnMessage("leash-error");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal((1, 1), (finished, failed));
            // Panic and Cut close a LOCKED window; a palette-closing Escape does not (WPF MainWindow.Leash.cs:382).
            int cuts = 0;
            (LeashPunishWindow.CutLeash, CoreSettings.Current.PanicKey) = (() => cuts++, "Escape");
            SettingsPaletteWindow.Toggle(w);
            LeashTaskHost.OnPanicPress(shell);
            Assert.True(SettingsPaletteWindow.IsOpen && ReferenceEquals(w, LeashPunishWindow.Current));
            SettingsPaletteWindow.CloseIfOpen(); Dispatcher.UIThread.RunJobs();
            LeashTaskHost.OnPanicPress(shell);
            Assert.Null(LeashPunishWindow.Current);
            Assert.True(host.OpenWatch(new LeashWatch("ht", "123", null)) && LeashPunishWindow.Current!.Locked);
            LeashPunishWindow.Current.CutFromWindow();
            Assert.True(LeashPunishWindow.Current == null && cuts == 1);
            Assert.True(host.OpenWatch(new LeashWatch("ht", "456", null)));
            Assert.False(LeashPunishWindow.Current!.Locked);   // a task watch closes normally
            host.EndWatch();
            Assert.Null(LeashPunishWindow.Current);
            Assert.Equal(new LeashWatchSample(12.5, 300, true), LeashTaskHost.ParseSample("\"12.5,300\"", true));
            Assert.Null(LeashTaskHost.ParseSample("", true));
            File.WriteAllText(Path.Combine(dir, "a.json"), "{\"mediaSource\":\"v.mp4\"}");
            File.WriteAllText(Path.Combine(dir, "v.mp4"), "");
            File.WriteAllText(Path.Combine(dir, "b.json"), "{}");
            File.WriteAllText(Path.Combine(dir, "b.webm"), "");
            Assert.Equal(Path.Combine(dir, "v.mp4"), LeashTaskHost.LocalMediaFor(Path.Combine(dir, "a.json")));
            Assert.Equal(Path.Combine(dir, "b.webm"), LeashTaskHost.LocalMediaFor(Path.Combine(dir, "b.json")));
        }
        finally
        {
            host.Dispose();
            SettingsPaletteWindow.CloseIfOpen();
            (LeashPunishWindow.CreateWeb, CoreSettings.Current.MotionLevel, LeashPunishWindow.CutLeash, CoreSettings.Current.PanicKey) = (oldWeb, motion, oldCut, oldKey);
            Directory.Delete(dir, true);
        }
    });
}
