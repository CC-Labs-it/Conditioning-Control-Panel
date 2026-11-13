using ConditioningControlPanel.AvatarTubeLayout;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Player reports fixed together on 2026-11-05: ccp-bugs #1533 (a paused session pauses keyword
/// detection), #1535 (the speech bubble clears the open chat box) and #1555 (a late spiral decode
/// never shows after the engine stopped).
/// </summary>
public class SmallBugs1105Tests
{
    // ---- #1533 ----

    [Fact]
    public void KeywordDetection_ListensWhileActive()
        => Assert.True(KeywordTriggerService.ShouldListen(active: true, disposed: false, sessionPaused: false));

    [Fact]
    public void KeywordDetection_StopsWhileTheSessionIsPaused()
        => Assert.False(KeywordTriggerService.ShouldListen(active: true, disposed: false, sessionPaused: true));

    [Fact]
    public void KeywordDetection_StaysOffWhenStoppedOrDisposed()
    {
        Assert.False(KeywordTriggerService.ShouldListen(active: false, disposed: false, sessionPaused: false));
        Assert.False(KeywordTriggerService.ShouldListen(active: true, disposed: true, sessionPaused: false));
    }

    // ---- #1535 ----

    [Fact]
    public void ChatBoxAboveHerHead_RaisesTheBubbleAnchorToTheBoxTop()
    {
        var avatar = new Box(100, 500, 200, 400);
        var input = new Box(120, 420, 160, 50);
        var cleared = SpeechBubblePlacement.ClearOfInput(avatar, input);
        Assert.Equal(420, cleared.Y);
        Assert.Equal(avatar.Bottom, cleared.Bottom);
        Assert.Equal(avatar.X, cleared.X);
        Assert.Equal(avatar.W, cleared.W);
    }

    [Fact]
    public void ChatBoxOffToTheSide_OrBelowHerHead_ChangesNothing()
    {
        var avatar = new Box(100, 500, 200, 400);
        Assert.Equal(avatar, SpeechBubblePlacement.ClearOfInput(avatar, new Box(400, 420, 100, 50)));
        Assert.Equal(avatar, SpeechBubblePlacement.ClearOfInput(avatar, new Box(120, 600, 100, 50)));
        Assert.Equal(avatar, SpeechBubblePlacement.ClearOfInput(avatar, default));
    }

    [Fact]
    public void ChatBoxOpen_BubbleLandsAboveTheBox()
    {
        var work = new Box(0, 0, 1920, 1080);
        var avatar = new Box(800, 500, 200, 400);
        var input = new Box(820, 420, 160, 50);
        var plan = SpeechBubblePlacement.Place(work, work, null,
            SpeechBubblePlacement.ClearOfInput(avatar, input), 340, 20, _ => (300, 80));
        Assert.True(plan.Bubble.Bottom <= input.Y, $"bubble bottom {plan.Bubble.Bottom} overlaps the chat box top {input.Y}");
    }

    // ---- #1555 ----

    [Fact]
    public void LateSpiralDecode_AfterTheEngineStopped_DoesNotShow()
        => Assert.False(OverlayService.SpiralDecodeStillWanted(samePath: true, running: false,
            spiralEnabled: true, timedHolds: 0, sustainedHeld: false));

    [Fact]
    public void LateSpiralDecode_WhileRunning_Shows()
        => Assert.True(OverlayService.SpiralDecodeStillWanted(samePath: true, running: true,
            spiralEnabled: true, timedHolds: 0, sustainedHeld: false));

    [Fact]
    public void LateSpiralDecode_HeldByAnEffect_ShowsWithoutTheEngine()
    {
        Assert.True(OverlayService.SpiralDecodeStillWanted(true, false, false, timedHolds: 1, sustainedHeld: false));
        Assert.True(OverlayService.SpiralDecodeStillWanted(true, false, false, timedHolds: 0, sustainedHeld: true));
    }

    [Fact]
    public void LateSpiralDecode_ForAnotherPath_DoesNotShow()
        => Assert.False(OverlayService.SpiralDecodeStillWanted(samePath: false, running: true,
            spiralEnabled: true, timedHolds: 1, sustainedHeld: true));
}
