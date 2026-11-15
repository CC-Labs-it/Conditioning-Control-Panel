using System;
using System.IO;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Super Froth: the rules added for the wiring, and guards on the wiring itself.</summary>
public class FrothTests
{
    [Fact]
    public void A_merged_bubble_of_n_parts_counts_n()
    {
        // Parts add the same way payouts do: 1 + 1, then that pair + a pair of its own.
        int pair = FrothRules.MergedPayout(1, 1);
        Assert.Equal(2, pair);
        Assert.Equal(4, FrothRules.MergedPayout(pair, pair));
    }

    [Fact]
    public void A_small_pop_reaches_nothing_and_a_big_one_reaches_its_neighbours()
    {
        Assert.False(FrothRules.Chains(20, 20));
        Assert.True(FrothRules.Chains(20 * FrothRules.ChainGrow, 20));
        Assert.False(FrothRules.InReach(50, 0, 30));             // reach 0: nobody
        Assert.True(FrothRules.InReach(120, 100, 30));           // the ring touches the rim
        Assert.False(FrothRules.InReach(131, 100, 30));
        // Further away goes off later, never sooner than a frame.
        Assert.True(FrothRules.ChainDelay(400, 20) > FrothRules.ChainDelay(100, 20));
        Assert.Equal(0.03, FrothRules.ChainDelay(0, 20), 6);
    }

    [Fact]
    public void The_word_pops_what_is_under_its_box()
    {
        Assert.True(FrothRules.UnderBox(50, 50, 10, 0, 0, 100, 100));     // inside
        Assert.True(FrothRules.UnderBox(108, 50, 10, 0, 0, 100, 100));    // rim over the edge
        Assert.False(FrothRules.UnderBox(111, 50, 10, 0, 0, 100, 100));
        Assert.False(FrothRules.UnderBox(108, 108, 10, 0, 0, 100, 100));  // off the corner
        Assert.True(FrothRules.UnderBox(50, 50, 10, 100, 100, 0, 0));     // a box given backwards
    }

    [Fact]
    public void The_cursor_pushes_light_bubbles_more_and_only_near_the_rim()
    {
        Assert.Equal(0, FrothRules.NudgeAccel(-5, 20, 20));                       // cursor inside: no push
        Assert.Equal(0, FrothRules.NudgeAccel(FrothRules.NudgeReach, 20, 20));    // out of reach
        double near = FrothRules.NudgeAccel(5, 20, 20), far = FrothRules.NudgeAccel(50, 20, 20);
        Assert.True(near > far && far > 0);
        Assert.True(FrothRules.NudgeAccel(5, 20, 20) > FrothRules.NudgeAccel(5, 60, 20));
        Assert.True(FrothRules.Mass(60, 20) > FrothRules.Mass(20, 20));
        Assert.Equal(1, FrothRules.Mass(20, 20), 6);
    }

    [Fact]
    public void A_merge_wobbles_and_grows_without_a_snap()
    {
        Assert.Equal(1, FrothRules.Squash(0, 3, false));
        // Gentle keeps the wobble but shallower, and never past a twentieth.
        for (double t = 0; t < 2; t += 0.03)
        {
            Assert.InRange(FrothRules.Squash(1, t, true), 0.95, 1.05);
            Assert.InRange(FrothRules.Squash(1, t, false), 0.88, 1.12);
        }
        // Size eases: after 300 ms at 30 fps it is still short of the target, Gentle further.
        double shown = 1, gentle = 1;
        for (int i = 0; i < 9; i++)
        {
            shown = FrothRules.EaseSize(shown, 2, 1 / 30.0, false);
            gentle = FrothRules.EaseSize(gentle, 2, 1 / 30.0, true);
        }
        Assert.InRange(shown, 1.5, 1.999);
        Assert.True(gentle < shown);
        Assert.Equal(2, FrothRules.EaseSize(1, 2, 5, false), 6);   // a long frame lands, never overshoots
    }

    [Fact]
    public void Pictures_stay_inside_the_glass()
    {
        for (int count = 2; count <= FrothRules.MaxPictures; count++)
            for (int i = 0; i < count; i++)
                for (double t = 0; t < 12; t += 0.25)
                {
                    var s = FrothRules.PictureSlot(i, count, t);
                    double far = Math.Sqrt(s.Dx * s.Dx + s.Dy * s.Dy) + s.Size / 2;
                    Assert.True(far <= 1.0, $"{count} pictures: slot {i} reaches {far:0.00} radii");
                }
    }

    // ---- wiring guards (source scans, like InnerBloomWiringTests) ----

    private static string Services()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "ConditioningControlPanel", "Services");
    }

    private static string Body(string src, string signature)
    {
        int at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        int open = src.IndexOf('{', at), depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(open, i - open + 1);
        }
        throw new InvalidOperationException(signature);
    }

    /// <summary>Froth took the star: nothing may start an Inner Bloom any more.</summary>
    [Fact]
    public void Nothing_starts_an_inner_bloom()
    {
        var main = File.ReadAllText(Path.Combine(Services(), "BubbleService.cs"));
        Assert.DoesNotContain("TryCreateBloom(", main);
        Assert.DoesNotContain("TryCreateBloom(", File.ReadAllText(Path.Combine(Services(), "BubbleService.Froth.cs")));
    }

    /// <summary>Owner rule: a merged bubble pays the sum of its parts. Both ambient pop paths
    /// (plain and trigger) pay the parts, and a part is credited with every line the ordinary
    /// award pays and counts with, and with nothing the ordinary award does not have.</summary>
    [Fact]
    public void A_merged_bubble_pays_each_part_once_on_both_pop_paths()
    {
        var main = File.ReadAllText(Path.Combine(Services(), "BubbleService.cs"));
        var froth = File.ReadAllText(Path.Combine(Services(), "BubbleService.Froth.cs"));

        Assert.Contains("OnFrothPopped(bubble);", Body(main, "private void OnPop(Bubble bubble)"));
        var create = Body(main, "private Bubble CreateAmbientBubble(");
        int award = create.IndexOf("AwardAmbientPop(b);", StringComparison.Ordinal);
        int parts = create.IndexOf("OnFrothPopped(b);", StringComparison.Ordinal);
        Assert.True(award > 0 && parts > award);

        Assert.Contains("PayFrothParts(b);", Body(froth, "private void OnFrothPopped(Bubble b)"));
        var pay = Body(froth, "private void PayFrothParts(Bubble b)");
        Assert.Contains("foreach (var part in parts)", pay);
        Assert.Contains("CreditFrothPart(part.XpMultiplier);", pay);

        var credit = Body(froth, "private void CreditFrothPart(int xpMultiplier)");
        var ordinary = Body(main, "private void AwardAmbientPop(Bubble bubble)");
        foreach (var line in new[]
                 {
                     "App.SkillTree?.RollLuckyBubble() ?? 1",
                     "TakeFromAmbientBubbleBucket(",
                     "App.Progression?.AddXP(paidXp, XPSource.Bubble)",
                     "AmbientXpBudgetChanged?.Invoke();",
                     "OnBubblePopped?.Invoke();",
                     "App.Achievements?.TrackBubblePopped();",
                 })
        {
            Assert.Contains(line, ordinary);
            Assert.Contains(line, credit);
        }
        Assert.Contains("5 * multiplier * xpMultiplier", credit);
        Assert.Contains("5 * multiplier * bubble.AmbientXpMultiplier", ordinary);
        // No second sound, haptic or Chaster booking for a part.
        Assert.DoesNotContain("PlayPopSound", credit);
        Assert.DoesNotContain("Haptics", credit);
        Assert.DoesNotContain("Chaster", credit);

        // The merge itself takes the other bubble away without a pop.
        var step = Body(froth, "private void StepFroth()");
        int absorb = step.IndexOf("keep.FrothAbsorb(gone, freshDip, maxGrow);", StringComparison.Ordinal);
        Assert.True(absorb > 0);
        Assert.Contains("gone.ForceDestroy();", step.Substring(absorb, 200));
    }

    /// <summary>The gate, the stage and the off switch: Froth asks the one question, listens to
    /// blasts and sweeps, and puts the field back when the star goes off.</summary>
    [Fact]
    public void Froth_asks_the_gate_listens_to_the_stage_and_switches_off()
    {
        var main = File.ReadAllText(Path.Combine(Services(), "BubbleService.cs"));
        var froth = File.ReadAllText(Path.Combine(Services(), "BubbleService.Froth.cs"));
        Assert.Contains("SuperAccess.IsOn(SuperEffect.InnerBloom)", froth);
        Assert.Contains("SuperAccess.Changed += OnFrothStarChanged;", froth);
        Assert.Contains("SuperStageHost.Stage.Blasted += OnStageBlasted;", froth);
        Assert.Contains("SuperStageHost.Stage.Swept += OnStageSwept;", froth);
        Assert.Contains("_frothStar && _ambientHost && !_chaosActive && Bubble.UseCompositor", froth);
        Assert.Contains("StepFroth();", Body(main, "private void AnimateAllBubbles("));
        Assert.Contains("UnhookFroth();", Body(main, "public void Dispose()"));

        var blasted = Body(froth, "private void OnStageBlasted(Blast blast)");
        Assert.Contains("blast.ReachPx <= 0 || !FrothOn", blasted);
        Assert.Contains("FrothRules.ChainDelay(", blasted);
        Assert.Contains("b.FrothFuse >= 0", blasted);            // each bubble at most once
        Assert.Contains("if (!FrothOn) return;", Body(froth, "private void OnStageSwept("));

        var popped = Body(froth, "private void OnFrothPopped(Bubble b)");
        Assert.Contains("FrothRules.Chains(rPx, freshPx) ? FrothRules.Reach(rPx) : 0", popped);
        Assert.Contains("BlastKind.RipePop : BlastKind.BubblePop", popped);
        Assert.Contains("FrothRules.HoleRadii", popped);

        var end = Body(froth, "private void EndFroth()");
        Assert.Contains("b.FrothReset();", end);
        // The pool owns the cursor while it is live: no nudge then.
        Assert.Contains("if (!poolLive && haveCursor && pushGain > 0)", Body(froth, "private void StepFroth()"));
        // Chaos bubbles, magnets, the red one and other trigger kinds are never Froth.
        Assert.Contains("_spec == null || (_isAmbientTrigger && _spec.VariantId == \"flash\")", froth);
        Assert.Contains("!_isMagnetBubble && !_isNatasha", froth);
    }

    /// <summary>With the star off the draw item is neutral, so the base bubble draws as it always did.</summary>
    [Fact]
    public void A_bubble_froth_never_touched_draws_plain()
    {
        var item = new ConditioningControlPanel.Services.Compositor.BubbleLayer.BubbleItem();
        Assert.Equal(1f, item.FrothScale);
        Assert.Equal(1f, item.FrothSquash);
        Assert.Equal(0f, item.FrothRim);
        Assert.Null(item.FrothPictures);
        var main = File.ReadAllText(Path.Combine(Services(), "BubbleService.cs"));
        Assert.Contains("if (_frothTouched) SyncFrothItem(it);", main);
    }
}
