using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Diagnostics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4.2 freeze hunt follow-up (hunt.md B): the stall lines name the stuck op, the GC work since the last
/// good beat, private MB and the machine's commit; an oversleeping heartbeat says the whole process
/// stalled; the two threads outrank starvation; and the once-a-second Debug lines log only on change.
/// </summary>
public class StallFactsTests
{
    [Fact]
    public void A_stall_line_carries_op_gc_work_private_and_machine_memory()
    {
        var line = StallFacts.Line("MediaContext.RenderMessageHandler (14s, Render)",
            new GcMark(10, 5, 2, 1_000), new GcMark(12, 6, 3, 11_250), 2464,
            new MemFacts(97, 33L * 1024 * 1024 * 1024, 512L * 1024 * 1024, 300L * 1024 * 1024));
        Assert.Equal(" | op=MediaContext.RenderMessageHandler (14s, Render) gc0+2 gc1+1 gc2+1 gcPause+10250ms privMB=2464"
            + " sys load 97% commit avail 512/33792 MB phys avail 300 MB", line);
    }

    [Fact]
    public void Unknown_parts_read_as_a_question_mark_and_memory_is_left_out_when_unread()
        => Assert.Equal(" | op=? gc0+0 gc1+0 gc2+0 gcPause+0ms privMB=?",
            StallFacts.Line(null, default, default, -1, null));

    [Fact]
    public void A_heartbeat_late_by_more_than_two_and_a_half_seconds_is_a_process_stall()
    {
        Assert.False(StallFacts.IsLate(2500));
        Assert.True(StallFacts.IsLate(2501));
        Assert.Equal("heartbeat thread late by 10000ms (slept 11000ms for 1000ms) - the whole process stalled",
            StallFacts.LateLine(11_000, 1000));
    }

    [Fact]
    public void The_live_readers_never_throw()
    {
        Assert.StartsWith(" | op=", StallFacts.Now(GcMark.Now()));
        Assert.True(StallFacts.PrivateMb() > 0);
        Assert.NotNull(StallFacts.ReadMemory());
    }

    [Fact]
    public void A_repeated_line_logs_only_when_it_changes()
    {
        var gate = new LogOnChange();
        Assert.True(gate.Changed("126/13962/1"));
        Assert.False(gate.Changed("126/13962/1"));
        Assert.True(gate.Changed("125/13962/1"));
        Assert.True(gate.Changed("126/13962/1"));
    }

    [Fact]
    public void The_stop_breadcrumbs_read_plainly()
    {
        Assert.Equal("stop:begin source=engine stop engine=on windows=7 privMB=2100", StopTrace.BeginLine("engine stop", true, 7, 2100));
        Assert.Equal("stop:begin source=kill all audio engine=off windows=? privMB=?", StopTrace.BeginLine("kill all audio", false, -1, -1));
        Assert.Equal("stop:end source=panic stop-all took 340ms privMB=2050", StopTrace.EndLine("panic stop-all", 340, 2050));
        Assert.Equal("engine stop: gc collect gc2+1 gcPause+42ms loh(last gc) 410->120 MB",
            StopTrace.GcLine("engine stop", new GcMark(0, 0, 4, 100), new GcMark(1, 1, 5, 142), 410, 120));
    }

    [Fact]
    public void The_watch_threads_run_above_normal_and_the_watchdog_mirrors_before_the_logger()
    {
        var diag = Src("Services", "Video", "VideoDiag.cs");
        var beat = diag.Substring(diag.IndexOf("new Thread(() => HeartbeatLoop(dispatcher))", StringComparison.Ordinal), 200);
        Assert.Contains("ThreadPriority.AboveNormal", beat);
        Assert.Contains("StallFacts.IsLate(slept)", diag);
        Assert.Contains("{UiMarkDescription}{Diagnostics.StallFacts.Now(lastGoodGc)}", diag);

        var dog = Src("Services", "UI", "UiHangWatchdog.cs");
        var start = dog.Substring(dog.IndexOf("Name = \"UiHangWatchdog\"", StringComparison.Ordinal), 400);
        Assert.Contains("ThreadPriority.AboveNormal", start);
        int mirror = dog.IndexOf("VideoDiag.Log(\"WATCHDOG\", string.Format(", StringComparison.Ordinal);
        int logger = dog.IndexOf("App.Logger?.Error(\"[WATCHDOG] UI thread unresponsive", StringComparison.Ordinal);
        Assert.True(mirror > 0 && logger > mirror);
    }

    [Fact]
    public void The_once_a_second_debug_lines_go_through_the_change_gate()
    {
        var board = Src("Services", "Progression", "LeaderboardService.cs");
        Assert.Equal(4, board.Split("_percentileLog.Changed(").Length - 1);
        var app = Src("App.xaml.cs");
        Assert.Contains("if (_screenListLog.Changed(screenNames))", app);
    }

    private static string Src(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
