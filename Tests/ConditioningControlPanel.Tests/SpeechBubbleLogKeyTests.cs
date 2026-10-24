using System;
using System.IO;
using ConditioningControlPanel.AvatarTubeLayout;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The "Speech bubble: rect=..." debug line fired on every typewriter character, because its
/// de-dup key held the bubble's left edge and width. It filled about a quarter of the activity log
/// in ccp-bugs #1451, #1454, #1471 and #1479 and pushed the lines that explain the bug off the top.
/// </summary>
public sealed class SpeechBubbleLogKeyTests
{
    private static readonly Box Work = new(0, 0, 1920, 1032);
    private static readonly Box Avatar = new(1620, 494, 131, 211);

    private static BubblePlan Plan(double x, double w, double y = 427, double h = 51,
        TailEdge tail = TailEdge.Bottom, bool growsLeft = true)
        => new(new Box(x, y, w, h), 300, tail, x + 40, growsLeft);

    [Fact]
    public void TypewriterGrowth_DoesNotChangeTheKey()
    {
        var first = SpeechBubblePlacement.LogKey(Plan(1601, 126), Avatar, Work, null, 1.0);
        var later = SpeechBubblePlacement.LogKey(Plan(1487, 240), Avatar, Work, null, 1.0);
        Assert.Equal(first, later);
    }

    [Fact]
    public void ANewRow_ASideFlip_OrAMovedAvatar_ChangesTheKey()
    {
        var baseKey = SpeechBubblePlacement.LogKey(Plan(1601, 126), Avatar, Work, null, 1.0);
        Assert.NotEqual(baseKey, SpeechBubblePlacement.LogKey(Plan(1601, 126, y: 406, h: 72), Avatar, Work, null, 1.0));
        Assert.NotEqual(baseKey, SpeechBubblePlacement.LogKey(Plan(1601, 126, growsLeft: false), Avatar, Work, null, 1.0));
        Assert.NotEqual(baseKey, SpeechBubblePlacement.LogKey(Plan(1601, 126, tail: TailEdge.Top), Avatar, Work, null, 1.0));
        Assert.NotEqual(baseKey, SpeechBubblePlacement.LogKey(Plan(1601, 126), new Box(13, 476, 87, 140), Work, null, 1.0));
        Assert.NotEqual(baseKey, SpeechBubblePlacement.LogKey(Plan(1601, 126), Avatar, Work, new Box(115, 0, 1426, 860), 1.0));
        Assert.NotEqual(baseKey, SpeechBubblePlacement.LogKey(Plan(1601, 126), Avatar, Work, null, 1.25));
    }

    [Fact]
    public void ThePlacementLog_UsesTheKey()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "AvatarTube",
            "AvatarTubeWindow.Speech.cs"));
        Assert.Contains("string decision = AvatarTubeLayout.SpeechBubblePlacement.LogKey(", src);
    }
}
