using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public class BubbleLuckyProcTests
{
    private sealed class LuckyRandom : Random { public override double NextDouble() => 0; }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(299, 1)]
    [InlineData(300, 0)]
    public void Lucky_roll_announces_once_even_when_the_bucket_is_empty(int alreadyPaid, int expected)
    {
        var s = new AppSettings { AmbientBubbleXpDayKey = DateTime.Now.ToString("yyyy-MM-dd"), AmbientBubbleXpPaidToday = alreadyPaid };
        s.UnlockedSkills.Add("lucky_bubbles");
        var field = new AmbientBubbleField(new LuckyRandom());
        int calls = 0;
        field.LuckyProc = multiplier => { Assert.Equal(20, multiplier); calls++; };
        var bubble = new AmbientBubble();
        Assert.Equal(expected, field.Pop(bubble, s));
        Assert.Equal(1, calls);
        Assert.Equal(expected > 0, bubble.Lucky);
        Assert.Equal(0, field.Pop(bubble, s));
        Assert.Equal(1, calls);
        Assert.Equal(5, field.Pop(new AmbientBubble(), new AppSettings()));
        Assert.Equal(1, calls); // no skill: no announcement
    }
}
