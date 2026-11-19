using ConditioningControlPanel.Services.Super.Draw;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1684: Afterglow line words crossed each other as the finale grew them.</summary>
public class AfterglowLineSpacingTests
{
    [Theory]
    [InlineData(60f, 60f)]
    [InlineData(120f, 40f)]
    [InlineData(40f, 140f)]
    public void Neighbours_never_touch_at_the_finale_peak(float leftWidth, float rightWidth)
    {
        const float size = 30f, mid = 500f;
        float gap = size * AfterglowLineDraw.WordGap;
        float leftSlot = mid - gap / 2 - leftWidth / 2;
        float rightSlot = mid + gap / 2 + rightWidth / 2;

        float grow = AfterglowLineDraw.FinaleGrow(glowIn: 1f, sink: 0f);
        float l = AfterglowLineDraw.SpreadSlot(leftSlot, mid, grow);
        float r = AfterglowLineDraw.SpreadSlot(rightSlot, mid, grow);

        float leftEdge = l + leftWidth * grow / 2;
        float rightEdge = r - rightWidth * grow / 2;
        Assert.True(rightEdge - leftEdge >= gap, $"gap at peak {rightEdge - leftEdge} < {gap}");
    }

    [Fact]
    public void Outside_the_finale_slots_stay_where_they_were_laid()
    {
        float grow = AfterglowLineDraw.FinaleGrow(glowIn: 0f, sink: 0f);
        Assert.Equal(1f, grow);
        Assert.Equal(320f, AfterglowLineDraw.SpreadSlot(320f, 500f, grow));
    }
}
