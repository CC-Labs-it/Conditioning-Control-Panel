using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// "Moving effects pop bubbles and flash images" (owner, 2026-11-18, tier-2 report BUG-H6LB5YCH6F):
/// the V2 bouncing word swept every bubble and flash card it crossed and the Whirlpool ate what
/// reached its eye, so with Hydra on the pops cascaded. Off by default: they still pull and push,
/// but pop and break nothing.
/// </summary>
public class MovingPopsSwitchTests
{
    private static SuperStage FullPool(bool pops)
    {
        var s = new SuperStage { PoolOn = true, MovingPops = pops };
        for (double t = 0; t < 20; t += 1 / 30.0) s.Pool.Step(1 / 30.0, 500, 400, 0, 1, 1080);
        Assert.True(s.PoolLive);
        return s;
    }

    [Fact]
    public void The_switch_is_off_by_default()
    {
        Assert.False(new AppSettings().MovingEffectsPop);
        Assert.False(new SuperStage().MovingPops);
    }

    [Fact]
    public void Off_the_word_sweeps_nothing()
    {
        var s = new SuperStage();
        int swept = 0;
        s.Swept += (_, _, _, _) => swept++;
        s.Sweep(0, 0, 100, 100);
        Assert.Equal(0, swept);
        s.MovingPops = true;
        s.Sweep(0, 0, 100, 100);
        Assert.Equal(1, swept);
    }

    [Theory]
    [InlineData(PullKind.Bubble)]
    [InlineData(PullKind.Picture)]
    public void Off_the_pool_still_pulls_but_never_eats(PullKind kind)
    {
        var off = FullPool(pops: false);
        var at = off.Pull(off.Pool.X + 3, off.Pool.Y, 1, kind);
        Assert.True(at.Inside);
        Assert.False(at.Eaten);
        var mid = off.Pull(off.Pool.X + off.Pool.Radius * 0.4, off.Pool.Y, 1, kind);
        Assert.True(mid.Ax < 0);                 // still drawn toward the eye
        Assert.InRange(mid.Scale, 0.14, 0.99);   // and still shrinks on the way in

        var on = FullPool(pops: true);
        Assert.True(on.Pull(on.Pool.X + 3, on.Pool.Y, 1, kind).Eaten);
    }

    [Fact]
    public void Both_drivers_hand_the_stage_the_players_switch()
    {
        var root = Root();
        var host = SourceRoots.ReadProductFile("Super", "SuperStageHost.cs");
        Assert.Contains("Stage.MovingPops = set?.MovingEffectsPop == true;", host);
        var word = File.ReadAllText(Path.Combine(root, "Services", "Subliminal", "BouncingTextService.cs"));
        int set = word.IndexOf("stage.MovingPops = App.Settings?.Current?.MovingEffectsPop == true;", StringComparison.Ordinal);
        int sweep = word.IndexOf("stage.Sweep(left, top, left + w, top + h);", StringComparison.Ordinal);
        Assert.True(set >= 0 && sweep > set, "the word reads the switch before it sweeps");
        // One setting, a row in both gold boxes.
        Assert.Contains("feat:OptionSection.Setting=\"MovingEffectsPop\"", File.ReadAllText(Path.Combine(root, "Features", "BouncingTextFeatureControl.xaml")));
        Assert.Contains("feat:OptionSection.Setting=\"MovingEffectsPop\"", File.ReadAllText(Path.Combine(root, "Features", "SpiralFeatureControl.xaml")));
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }
}
