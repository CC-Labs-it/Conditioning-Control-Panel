using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Afterglow take two: ghosts build a line, the full line has a finale, then it clears.</summary>
public class AfterglowLineTests
{
    private static void Run(AfterglowLine l, double seconds, System.Action<string>? done = null)
    {
        for (double t = 0; t < seconds; t += 1 / 30.0)
            if (l.Step(1 / 30.0) is { } s) done?.Invoke(s);
    }

    [Fact]
    public void A_ghost_rises_then_travels_and_rests_faint()
    {
        var l = new AfterglowLine();
        Assert.True(l.Add("  Drop  "));
        Assert.Equal("Drop", l.TextAt(0));
        Assert.Equal(0, l.Alpha(0), 3);
        Run(l, AfterglowLine.RiseSeconds);
        Assert.InRange(l.Alpha(0), 0.8, 0.95);
        Assert.InRange(l.Travel(0), 0, 0.05);
        Run(l, AfterglowLine.TravelSeconds + 0.1);
        Assert.Equal(1, l.Travel(0), 3);
        Assert.Equal(AfterglowLine.GhostAlpha, l.Alpha(0), 2);
        Assert.False(l.Animating);
    }

    [Fact]
    public void A_full_line_is_announced_once_then_glows_sinks_and_clears()
    {
        var l = new AfterglowLine();
        foreach (var w in new[] { "let", "go", "and", "sink" }) Assert.True(l.Add(w));
        Assert.False(l.Add("extra"));
        string? got = null; int times = 0;
        Run(l, 2, s => { got = s; times++; });
        Assert.Equal("let go and sink", got);
        Assert.Equal(1, times);
        Assert.True(l.InFinale);
        Assert.False(l.Add("late"));
        Run(l, AfterglowLine.FinaleRise);
        Assert.Equal(1, l.Alpha(0), 2);
        Run(l, AfterglowLine.FinaleHold + AfterglowLine.FinaleSink + 0.2);
        Assert.Equal(0, l.Count);
        Assert.False(l.InFinale);
        Assert.True(l.Add("again"));
    }

    [Fact]
    public void The_finale_rise_is_never_faster_than_the_gentle_floor()
        => Assert.True(AfterglowLine.FinaleRise >= 0.3);

    [Fact]
    public void An_abandoned_line_fades_away()
    {
        var l = new AfterglowLine();
        l.Add("drop");
        Run(l, AfterglowLine.StaleSeconds - 1);
        Assert.Equal(1, l.Count);
        Assert.True(l.Alpha(0) < AfterglowLine.GhostAlpha);
        Run(l, 1.5);
        Assert.Equal(0, l.Count);
    }

    [Fact]
    public void Empty_and_overlong_words_are_handled()
    {
        var l = new AfterglowLine();
        Assert.False(l.Add("   "));
        Assert.False(l.Add(null));
        Assert.True(l.Add(new string('x', 80)));
        Assert.Equal(AfterglowLine.MaxChars, l.TextAt(0).Length);
    }
}
