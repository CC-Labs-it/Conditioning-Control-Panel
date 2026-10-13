using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The announcer queue and envelope, as WPF ChaosAnnouncerOverlay.cs runs them.</summary>
public class ChaosAnnouncerTests
{
    private static ChaosAnnouncement A(string t, int priority = 0) =>
        new(t, ChaosAnnounceKind.Item, null, null, ChaosAnnounceTiming.HoldMs, priority);

    [Fact]
    public void HigherPriorityFirstTiesKeepInsertionOrder()
    {
        var q = new ChaosAnnounceQueue();
        q.Add(A("a"));
        q.Add(A("b"));
        q.Add(A("reactive", ChaosAnnounceQueue.NarratorPriority(1)));
        q.Add(A("story", ChaosAnnounceQueue.NarratorPriority(2)));
        q.Add(A("c"));
        var order = new System.Collections.Generic.List<string>();
        while (q.TryDequeue(out var x)) order.Add(x.Text);
        Assert.Equal(new[] { "story", "reactive", "a", "b", "c" }, order);
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void ClearDropsEverything()
    {
        var q = new ChaosAnnounceQueue();
        q.Add(A("a"));
        q.Add(A("b"));
        q.Clear();
        Assert.False(q.TryDequeue(out _));
    }

    [Fact]
    public void WpfTimings()
    {
        Assert.Equal(110, ChaosAnnounceTiming.InMs);
        Assert.Equal(650, ChaosAnnounceTiming.HoldMs);
        Assert.Equal(3000, ChaosAnnounceTiming.TeachHoldMs);
        Assert.Equal(240, ChaosAnnounceTiming.OutMs);
        Assert.Equal(760, ChaosAnnounceTiming.LifeMs(650));
        Assert.Equal(110, ChaosAnnounceTiming.LifeMs(-5));   // Math.Max(0, holdMs)
        Assert.Equal(1000, ChaosAnnounceTiming.TotalMs(650));
    }

    [Fact]
    public void EnvelopeFadesInHoldsThenFadesOut()
    {
        const int hold = 650;
        Assert.Equal(0, ChaosAnnounceTiming.Opacity(0, hold));
        Assert.Equal(0.5, ChaosAnnounceTiming.Opacity(55, hold), 3);
        Assert.Equal(1, ChaosAnnounceTiming.Opacity(110, hold));
        Assert.Equal(1, ChaosAnnounceTiming.Opacity(759, hold));
        Assert.Equal(0.5, ChaosAnnounceTiming.Opacity(880, hold), 3);
        Assert.Equal(0, ChaosAnnounceTiming.Opacity(1000, hold));
        Assert.False(ChaosAnnounceTiming.IsDone(999, hold));
        Assert.True(ChaosAnnounceTiming.IsDone(1000, hold));
    }

    [Fact]
    public void PopStartsAt085OvershootsAndSettlesAt1()
    {
        Assert.Equal(0.85, ChaosAnnounceTiming.Scale(0), 6);
        Assert.Equal(1.0, ChaosAnnounceTiming.Scale(ChaosAnnounceTiming.PopMs), 6);
        Assert.Equal(1.0, ChaosAnnounceTiming.Scale(5000), 6);
        // BackEase EaseOut amplitude 0.6 overshoots past 1 before settling (WPF pop).
        double peak = 0;
        for (int ms = 0; ms <= ChaosAnnounceTiming.PopMs; ms++) peak = System.Math.Max(peak, ChaosAnnounceTiming.Scale(ms));
        Assert.True(peak > 1.0, $"no overshoot: {peak}");
        // WPF BackEase.EaseInCore at t=0.5: 0.125 - 0.5*0.6*sin(pi/2) = -0.175 -> EaseOut 1.175
        Assert.Equal(1.175, ChaosAnnounceTiming.BackEaseOut(0.5, 0.6), 6);
    }
}
