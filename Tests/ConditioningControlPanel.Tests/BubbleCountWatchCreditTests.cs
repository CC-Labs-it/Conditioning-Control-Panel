using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs#1571: Bubble Count's clip feeds "Watch N minutes of video" with REAL watch time,
/// once per game, whether the clip ends or the game is left early.
/// </summary>
public class BubbleCountWatchCreditTests
{
    [Fact]
    public void A_clip_watched_to_the_end_credits_the_time_watched()
    {
        var w = new BubbleCountWatchCredit();
        w.MarkPlaying(100);
        Assert.Equal(42, w.Take(142, clipSeconds: 45), 3);
    }

    [Fact]
    public void A_game_left_early_still_credits_what_was_watched()
    {
        var w = new BubbleCountWatchCredit();
        w.MarkPlaying(10);
        Assert.Equal(12, w.Take(22, clipSeconds: 60), 3);
    }

    [Fact]
    public void Credit_never_exceeds_the_clip_length()
    {
        var w = new BubbleCountWatchCredit();
        w.MarkPlaying(0);
        Assert.Equal(30, w.Take(95, clipSeconds: 30), 3); // stalled clip, safety timer end
    }

    [Fact]
    public void A_game_is_credited_once()
    {
        var w = new BubbleCountWatchCredit();
        w.MarkPlaying(0);
        Assert.True(w.Take(20, 60) > 0);
        Assert.Equal(0, w.Take(40, 60)); // the close after the result window
        w.MarkPlaying(50);
        Assert.Equal(0, w.Take(70, 60));
    }

    [Fact]
    public void A_video_that_never_started_credits_nothing()
        => Assert.Equal(0, new BubbleCountWatchCredit().Take(30, 30));

    [Fact]
    public void A_second_playing_event_does_not_restart_the_clock()
    {
        var w = new BubbleCountWatchCredit();
        w.MarkPlaying(0);
        w.MarkPlaying(15);
        Assert.Equal(20, w.Take(20, 60), 3);
    }

    [Fact]
    public void The_window_credits_on_end_and_on_every_close()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(dir!.FullName, "ConditioningControlPanel", "Windows", "BubbleCountWindow.xaml.cs"));
        Assert.Contains("CreditWatchTime(\"ended\")", src);
        Assert.Contains("CreditWatchTime(\"close\")", src);
        Assert.Contains("CreditWatchTime(\"panic\")", src);
        Assert.DoesNotContain("TrackVideoWatched(_videoDurationSeconds)", src);
    }
}
