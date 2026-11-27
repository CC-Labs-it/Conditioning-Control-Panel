using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Diagnostics;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Freeze hunt (ccp-bugs #1747 #1748 #1760 #1721): every stop path writes one line with each step's
/// milliseconds when it took a second or more, and nothing when it was fast.
/// </summary>
public class StopTraceTests
{
    [Fact]
    public void A_fast_stop_writes_nothing()
    {
        Assert.Null(StopTrace.Line("engine stop", 999, new List<(string, long)> { ("video", 900) }));
        Assert.Null(StopTrace.Begin("engine stop").Finish());
    }

    [Fact]
    public void A_slow_stop_names_the_slowest_step_and_every_step_in_order()
    {
        var line = StopTrace.Line("engine stop", 12_500, new List<(string, long)>
        {
            ("flash", 3), ("bubbles", 0), ("video", 11_800), ("overlays", 650),
        });
        Assert.Equal("[StopTrace] engine stop took 12500 ms; slowest video 11800; steps flash=3 video=11800 overlays=650 after=47", line);
    }

    [Fact]
    public void A_slow_stop_with_no_marks_still_says_how_long()
        => Assert.Equal("[StopTrace] lockdown end took 1500 ms", StopTrace.Line("lockdown end", 1500, new List<(string, long)>()));

    [Fact]
    public void Each_raises_every_handler_in_order_and_a_throw_still_propagates()
    {
        var order = new List<int>();
        Action handlers = () => order.Add(1);
        handlers += () => order.Add(2);
        var trace = StopTrace.Begin("t");
        trace.Each(handlers, d => ((Action)d)());
        Assert.Equal(new[] { 1, 2 }, order);

        order.Clear();
        Action throwing = () => order.Add(1);
        throwing += () => throw new InvalidOperationException("boom");
        throwing += () => order.Add(3);
        Assert.Throws<InvalidOperationException>(() => trace.Each(throwing, d => ((Action)d)()));
        Assert.Equal(new[] { 1 }, order);   // exactly a plain Invoke
        trace.Each(null, d => ((Action)d)());
    }

    [Fact]
    public void A_handler_is_named_by_its_type_and_method()
    {
        Action named = NamedHandler;
        Assert.Equal("StopTraceTests.NamedHandler", StopTrace.HandlerName(named));
        Action lambda = () => { };
        Assert.StartsWith("StopTraceTests.", StopTrace.HandlerName(lambda));
    }

    private static void NamedHandler() { }

    [Theory]
    [InlineData("MainWindow/MainWindow.StartStop.cs", "StopTrace.Begin(\"engine stop\")")]
    [InlineData("MainWindow/MainWindow.xaml.cs", "StopTrace.Begin(\"panic stop-all\")")]
    [InlineData("Services/Session/SessionEngine.cs", "StopTrace.Begin(\"session stop\")")]
    [InlineData("Services/Haptics/LockdownService.cs", "StopTrace.Begin(\"lockdown end\")")]
    [InlineData("App.xaml.cs", "StopTrace.Begin(\"kill all audio\")")]
    public void Every_stop_path_is_timed_and_finished(string file, string begin)
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", file.Replace('/', Path.DirectorySeparatorChar)));
        var at = src.IndexOf(begin, StringComparison.Ordinal);
        Assert.True(at > 0, begin);
        Assert.True(src.IndexOf("trace.Finish();", at, StringComparison.Ordinal) > at, file);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
