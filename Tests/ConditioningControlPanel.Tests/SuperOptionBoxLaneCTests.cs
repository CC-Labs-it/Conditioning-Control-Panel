using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The option boxes of Scrawl, Afterglow and Froth (owner, 2026-11-15): defaults reproduce the
/// effects as built, clamps hold, every option reaches the effect, and no Froth option touches pay.
/// </summary>
public class SuperOptionBoxLaneCTests
{
    // ---- Scrawl ----

    [Fact]
    public void Scrawl_defaults_are_the_shipped_numbers()
    {
        Assert.Equal(ScrawlRules.StampLife, ScrawlOptions.StampSecondsDefault);
        Assert.Equal(ScrawlRules.StampCap, ScrawlOptions.StampCapDefault);
        Assert.Equal(1.0, ScrawlOptions.KnockFactor(ScrawlOptions.SlamDefault));
        Assert.Equal(1.0, ScrawlOptions.ShakeFactor(ScrawlOptions.SlamDefault, gentle: false));
        var s = new AppSettings();
        Assert.Equal(14, s.ScrawlStampSeconds);
        Assert.Equal(44, s.ScrawlStampCap);
        Assert.Equal(100, s.ScrawlSlam);
    }

    [Fact]
    public void Scrawl_clamps_snap_and_off()
    {
        Assert.Equal(3, ScrawlOptions.ClampStampSeconds(0));
        Assert.Equal(60, ScrawlOptions.ClampStampSeconds(999));
        Assert.Equal(6, ScrawlOptions.ClampStampCap(1));
        Assert.Equal(80, ScrawlOptions.ClampStampCap(500));
        Assert.Equal(125, ScrawlOptions.ClampSlam(130));
        Assert.Equal(200, ScrawlOptions.ClampSlam(900));
        Assert.Equal(0, ScrawlOptions.ClampSlam(-5));
        Assert.False(ScrawlOptions.SlamOn(0));
        Assert.True(ScrawlOptions.SlamOn(25));
        Assert.Equal(0, ScrawlOptions.KnockFactor(0));
        // Gentle visuals never shake harder than the stock slam.
        Assert.Equal(2.0, ScrawlOptions.ShakeFactor(200, gentle: false));
        Assert.Equal(1.0, ScrawlOptions.ShakeFactor(200, gentle: true));
        Assert.Equal(0.5, ScrawlOptions.ShakeFactor(50, gentle: true));
        var st = new AppSettings { ScrawlSlam = 130, ScrawlStampCap = 2, ScrawlStampSeconds = 100 };
        Assert.Equal(125, st.ScrawlSlam);
        Assert.Equal(6, st.ScrawlStampCap);
        Assert.Equal(60, st.ScrawlStampSeconds);
    }

    private static readonly ScrawlBounds Screen = new(0, 0, 1920, 1080);

    private static void HitTop(ScrawlScene s, double x) =>
        s.WallHit("SINK", ScrawlWall.Top, x, 0, x + 200, 80, Screen, new Point(960, 540));

    private static int Stamps(ScrawlScene s)
    {
        var rects = new List<Rect>();
        s.AppendRects(rects);
        return rects.Count;
    }

    [Fact]
    public void Scrawl_stamp_life_and_cap_reach_the_scene()
    {
        var s = new ScrawlScene(new FontFamily("Segoe UI"), 72, 1.0);
        s.Configure(4, 10, 1);
        Assert.Equal(4, s.StampLife);
        for (int i = 0; i < 30; i++) HitTop(s, 300 + i * 20);
        Assert.Equal(10, Stamps(s));
        s.Configure(4, 6, 1);                       // a lower cap drops the oldest at once
        Assert.Equal(6, Stamps(s));
        for (int i = 0; i < 4 * 10 + 2; i++) s.Tick(0.1, MotionLevel.Full);
        Assert.Equal(0, Stamps(s));                 // gone after four seconds, not fourteen
    }

    [Fact]
    public void Scrawl_service_reads_the_box_every_frame_and_slam_off_takes_no_click()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "ConditioningControlPanel", "Services", "Subliminal", "BouncingTextService.cs"));
        var tick = Body(src, "private void TickScrawl(double dt)");
        Assert.Contains("scene.Configure(", tick);
        Assert.Contains("ScrawlOptions.SlamOn(_scrawlSlam)) { _scrawlHitCount = 0; return; }", tick);
        Assert.Contains("ScrawlOptions.ShakeFactor(_scrawlSlam, Safety.GentleVisuals.On)", tick);
        Assert.Contains("ScrawlOptions.KnockFactor(_scrawlSlam)", Body(src, "private void StepSlam(Logo l, double dt)"));
    }

    // ---- Afterglow ----

    [Fact]
    public void Afterglow_defaults_are_the_built_numbers()
    {
        var l = new AfterglowLine();
        Assert.Equal(AfterglowLine.Capacity, l.Words);
        Assert.Equal(AfterglowLine.FinaleHold, l.HoldSeconds);
        Assert.Equal(AfterglowLine.GhostAlpha, l.Ghost);
        Assert.Equal(AfterglowLine.GhostAlpha, AfterglowLineOptions.GhostAlpha(AfterglowLineOptions.GhostDefault), 6);
        var s = new AppSettings();
        Assert.Equal(4, s.AfterglowLineWords);
        Assert.Equal(1.2, s.AfterglowLineHold, 6);
        Assert.Equal(42, s.AfterglowGhostOpacity);
        Assert.Equal(6, AfterglowLineOptions.ClampWords(99));
        Assert.Equal(2, AfterglowLineOptions.ClampWords(0));
        Assert.Equal(0.5, AfterglowLineOptions.ClampHold(0), 6);
        Assert.Equal(1.3, AfterglowLineOptions.ClampHold(1.2999), 6);
        Assert.Equal(90, AfterglowLineOptions.ClampGhost(100));
    }

    private static string? Run(AfterglowLine l, double seconds)
    {
        string? done = null;
        for (double t = 0; t < seconds; t += 1 / 30.0)
            if (l.Step(1 / 30.0) is { } s) done = s;
        return done;
    }

    [Fact]
    public void Afterglow_words_per_line_hold_and_ghost_reach_the_line()
    {
        var l = new AfterglowLine { Words = 2, HoldSeconds = 4, Ghost = 0.7 };
        Assert.True(l.Add("let"));
        Assert.True(l.Add("go"));
        Assert.False(l.Add("more"));                // two make a full line
        Run(l, AfterglowLine.RiseSeconds + AfterglowLine.TravelSeconds - 0.2);
        Assert.Equal("let go", Run(l, 0.4));
        Assert.True(l.InFinale);
        Run(l, AfterglowLine.FinaleRise + 3);       // still lit inside a four second hold
        Assert.True(l.InFinale);
        Assert.Equal(0, l.FinaleSinkOut, 6);
        Run(l, 1 + AfterglowLine.FinaleSink + 0.2);
        Assert.Equal(0, l.Count);

        var r = new AfterglowLine { Ghost = 0.7 };
        r.Add("drop");
        Run(r, AfterglowLine.RiseSeconds + AfterglowLine.TravelSeconds + 0.1);
        Assert.Equal(0.7, r.Alpha(0), 2);
    }

    [Fact]
    public void Afterglow_layer_reads_the_box()
    {
        var src = File.ReadAllText(Path.Combine(Root(), "ConditioningControlPanel", "Services", "Compositor", "AfterglowLineLayer.cs"));
        Assert.Contains("ApplyOptions();", Body(src, "public override void Update(TimeSpan delta)"));
        Assert.Contains("AfterglowLineRules.Apply(_line,", src);
        // The read itself is Core's, shared with the Avalonia head.
        var rule = File.ReadAllText(Path.Combine(Root(), "CCP.Core", "Super", "AfterglowLineRules.cs"));
        Assert.Contains("AfterglowLineOptions.ClampWords(s.AfterglowLineWords)", rule);
    }

    // ---- Froth ----

    [Fact]
    public void Froth_defaults_are_the_built_numbers()
    {
        Assert.Equal(FrothRules.MaxGrow, FrothOptions.MaxSizeDefault);
        Assert.Equal(1.0, FrothOptions.ReachFactor(FrothOptions.ReachDefault));
        Assert.Equal(1.0, FrothOptions.PushFactor(FrothOptions.PushDefault));
        Assert.Equal(FrothRules.MergedRadius(80, 80, 20), FrothRules.MergedRadius(80, 80, 20, FrothOptions.MaxSizeDefault));
        var s = new AppSettings();
        Assert.Equal(4.5, s.FrothMaxSize, 6);
        Assert.Equal(100, s.FrothPopReach);
        Assert.Equal(100, s.FrothCursorPush);
    }

    [Fact]
    public void Froth_clamps_keep_ripe_reachable_and_zero_means_off()
    {
        Assert.Equal(2.5, FrothOptions.ClampMaxSize(1), 6);
        Assert.True(FrothOptions.MaxSizeMin >= FrothRules.RipeGrow);
        Assert.Equal(6.0, FrothOptions.ClampMaxSize(9), 6);
        Assert.Equal(3.5, FrothOptions.ClampMaxSize(3.4), 6);
        Assert.Equal(20 * 3.0, FrothRules.MergedRadius(80, 80, 20, 3.0), 6);
        Assert.Equal(0, FrothOptions.ReachFactor(0));
        Assert.Equal(2.0, FrothOptions.ReachFactor(250));
        Assert.Equal(0, FrothOptions.PushFactor(-10));
        Assert.Equal(60, FrothOptions.ClampPush(57));
    }

    [Fact]
    public void Froth_options_never_touch_pay()
    {
        // Owner rule: a merged bubble pays the sum of its parts at any size.
        Assert.Equal(7, FrothRules.MergedPayout(3, 4));
        var froth = File.ReadAllText(Path.Combine(Root(), "ConditioningControlPanel", "Services", "BubbleService.Froth.cs"));
        Assert.DoesNotContain("FrothOptions", Body(froth, "private void PayFrothParts(Bubble b)"));
        Assert.DoesNotContain("FrothOptions", Body(froth, "private void CreditFrothPart(int xpMultiplier)"));
        Assert.Contains("FrothOptions.ReachFactor(", Body(froth, "private void OnFrothPopped(Bubble b)"));
        Assert.Contains("* pushGain", Body(froth, "private void StepFroth()"));
        Assert.Contains("keep.FrothAbsorb(gone, freshDip, maxGrow);", Body(froth, "private void StepFroth()"));
    }

    // ---- Pages: rows live in the gold box and show only while the V2 is lit ----

    [Theory]
    [InlineData("BouncingTextFeatureControl", "ScrawlRows", "SuperEffect.Scrawl")]
    [InlineData("SubliminalFeatureControl", "AfterglowRows", "SuperEffect.Afterglow")]
    [InlineData("BubblePopFeatureControl", "FrothRows", "SuperEffect.InnerBloom")]
    public void Rows_sit_in_the_gold_box_and_follow_the_switch(string page, string rows, string effect)
    {
        var dir = Path.Combine(Root(), "ConditioningControlPanel", "Features");
        var xaml = File.ReadAllText(Path.Combine(dir, page + ".xaml"));
        int box = xaml.IndexOf("<ctl:SuperBox x:Name=\"SuperBoxMain\"", System.StringComparison.Ordinal);
        int end = xaml.IndexOf("</ctl:SuperBox>", box, System.StringComparison.Ordinal);
        int at = xaml.IndexOf("x:Name=\"" + rows + "\"", System.StringComparison.Ordinal);
        Assert.True(box > 0 && at > box && at < end);
        Assert.Contains("#33FFE08A", xaml.Substring(at, end - at));
        var code = Directory.GetFiles(dir, page + ".*.cs");
        bool wired = false;
        foreach (var f in code)
        {
            var t = File.ReadAllText(f);
            if (t.Contains("SuperRows.Apply(" + rows + ", " + effect + ")") && t.Contains("SuperAccess.Changed +=")) wired = true;
        }
        Assert.True(wired);
    }

    private static string Root()
    {
        var d = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "ConditioningControlPanel", "Services"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Body(string src, string signature)
    {
        int i = src.IndexOf(signature, System.StringComparison.Ordinal);
        Assert.True(i >= 0, signature);
        int open = src.IndexOf('{', i), depth = 0;
        for (int k = open; k < src.Length; k++)
        {
            if (src[k] == '{') depth++;
            else if (src[k] == '}' && --depth == 0) return src.Substring(open, k - open + 1);
        }
        return src.Substring(open);
    }
}
