using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Lights Down "Follow eyes or mouse" (owner, 2026-11-19, ccp-bugs #1672): off by default, so the
/// reworked effect stays as it shipped; on, the dim only comes in while a video is watched.
/// </summary>
public class LightsDownFollowTests
{
    private const double Dt = 1.0 / 30;

    [Fact]
    public void Off_by_default()
        => Assert.False(new AppSettings().LightsDownFollow);

    [Theory]
    // faceLost, gazeLive, gazeOnVideo, cursorOnVideo, watched
    [InlineData(false, false, false, true, true)]    // no camera: the mouse on the video decides
    [InlineData(false, false, false, false, false)]
    [InlineData(false, true, true, false, true)]     // fresh gaze decides over the mouse
    [InlineData(false, true, false, true, false)]
    [InlineData(true, false, false, true, false)]    // a lost face is never watching
    public void Watched_rule(bool faceLost, bool gazeLive, bool gazeOn, bool cursorOn, bool watched)
        => Assert.Equal(watched, LightsDownRules.Watched(faceLost, gazeLive, gazeOn, cursorOn));

    [Fact]
    public void Floating_grows_only_while_watched_and_pulls_back_when_not()
    {
        var f = new LightsDownFloating();
        var t = LightsDownTuning.Stock;
        for (int i = 0; i < 300; i++) f.Step(Dt, t, 0, watched: true);
        double grown = f.Grow;
        Assert.True(grown > 0.3);

        f.Step(Dt, t, 0, watched: false);
        Assert.True(f.Grow < grown);
        for (int i = 0; i < 60; i++) f.Step(Dt, t, 0, watched: false);
        Assert.Equal(0, f.Grow, 6);
    }

    [Fact]
    public void Floating_default_step_still_grows()
    {
        var f = new LightsDownFloating();
        f.Step(1, LightsDownTuning.Stock, 0);
        Assert.True(f.Grow > 0);
    }

    [Fact]
    public void Full_screen_follow_pulls_back_when_not_watched()
    {
        var s = new LightsDownFullScreen();
        var t = LightsDownTuning.Stock;
        // Follow on = never "moved", away = not watched.
        for (int i = 0; i < 120; i++) s.Step(Dt, false, false, t, 0);
        double lit = s.Level;
        Assert.True(lit > 0.5);
        for (int i = 0; i < 30; i++) s.Step(Dt, false, true, t, 0);
        Assert.True(s.Level < lit);
    }
}
