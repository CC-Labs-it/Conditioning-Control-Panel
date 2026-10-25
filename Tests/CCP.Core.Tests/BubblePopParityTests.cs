using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BubblePopParityTests
{
    [Theory]
    [InlineData(false, false, 0.9, BubbleMotionStyle.FloatUp)]
    [InlineData(true, false, 0.9, BubbleMotionStyle.Rain)]
    [InlineData(false, true, 0.9, BubbleMotionStyle.SpiralIn)]
    [InlineData(true, true, 0.4, BubbleMotionStyle.Rain)]
    [InlineData(true, true, 0.9, BubbleMotionStyle.SpiralIn)]
    public void Mix_rolls_only_owned_styles(bool rain, bool spiral, double roll, BubbleMotionStyle expected)
    {
        Assert.Equal(expected, AmbientBubbleMotionRules.Resolve(BubbleMotionStyle.Mix, rain, spiral, MotionLevel.Full, roll));
        Assert.Equal(BubbleMotionStyle.FloatUp, AmbientBubbleMotionRules.Resolve(BubbleMotionStyle.Mix, rain, spiral, MotionLevel.Off, roll));
    }

    [Fact]
    public void Rain_starts_above_scaled_screen_falls_and_exits_quietly_at_bottom()
    {
        var full = new AppSettings { MotionLevel = MotionLevel.Full };
        var reduced = new AppSettings { MotionLevel = MotionLevel.Reduced };
        var b = AmbientBubble.Spawn(new Random(1), 0, -1920, 200, 1920, 1080, 2, full, null, true, BubbleMotionStyle.Rain);
        var slow = AmbientBubble.Spawn(new Random(1), 0, -1920, 200, 1920, 1080, 2, reduced, null, true, BubbleMotionStyle.Rain);
        Assert.Equal(100 - b.Size, b.Y);
        var y = b.Y;
        b.Step(); slow.Step();
        Assert.InRange(b.Y - y, 1, 2);
        Assert.Equal((b.Y - y) / 2, slow.Y - y, 8);
        for (var i = 0; i < 2000; i++) if (b.Step() == AmbientBubble.Result.Missed) break;
        Assert.True(b.Y > 640);
        Assert.False(b.Popping);
    }

    [Fact]
    public void Spiral_reaches_core_fades_and_exits_without_a_pop()
    {
        var b = AmbientBubble.Spawn(new Random(2), 0, -1920, 0, 1920, 1080, 2, new AppSettings(), null, true, BubbleMotionStyle.SpiralIn);
        double Radius() => Math.Sqrt(Math.Pow(b.CenterX + 480, 2) + Math.Pow(b.CenterY - 270, 2));
        Assert.Equal(226.8, Radius(), 6);
        var result = AmbientBubble.Result.Alive;
        for (var i = 0; i < 3000 && result == AmbientBubble.Result.Alive; i++) result = b.Step();
        Assert.Equal(AmbientBubble.Result.Missed, result);
        Assert.Equal(24, Radius(), 6);
        Assert.Equal(0, b.Fade);
        Assert.False(b.Popping);
    }

    [Theory]
    [InlineData(true, false, 16, 4, 8, 0.96)]
    [InlineData(false, true, 8, 3, 6, 0.94)]
    [InlineData(false, false, 0, 0, 0, 0)]
    public void Pop_particles_use_Wpf_count_size_gravity_and_decay(bool lucky, bool boost, int count, int min, int max, double alpha)
    {
        var b = new AmbientBubble { Lucky = lucky, Popping = true };
        b.SetPopVisuals(new Random(3), boost);
        Assert.Equal(count, b.Sparkles?.Count ?? 0);
        if (count == 0) return;
        var p = b.Sparkles![0];
        Assert.InRange(p.Size, min, max);
        var vx = p.VelX; var vy = p.VelY;
        b.Step();
        Assert.Equal(vx, p.X); Assert.Equal(vy, p.Y);
        Assert.Equal(vy + 0.15, p.VelY); Assert.Equal(alpha, p.Alpha, 8);
    }

    [Fact]
    public void Pops_flush_at_50_award_points_at_100_and_unlock_at_1000()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-pop-credit-");
        try
        {
            var path = Path.Combine(dir.FullName, "achievements.json");
            var engine = new AchievementEngine(new AchievementStore(path));
            var settings = new AppSettings { SkillPoints = 7 };
            for (var i = 0; i < 49; i++) Assert.False(engine.TrackBubblePopped(settings));
            Assert.False(File.Exists(path));
            engine.TrackBubblePopped(settings);
            Assert.Equal(50, new AchievementStore(path).Load().TotalBubblesPopped);
            for (var i = 50; i < 99; i++) Assert.False(engine.TrackBubblePopped(settings));
            Assert.True(engine.TrackBubblePopped(settings)); Assert.Equal(8, settings.SkillPoints);
            engine.Progress.TotalBubblesPopped = 999;
            Assert.True(engine.TrackBubblePopped(settings));
            Assert.True(new AchievementStore(path).Load().IsUnlocked("pop_the_thought"));
        }
        finally { dir.Delete(true); }
    }
}
