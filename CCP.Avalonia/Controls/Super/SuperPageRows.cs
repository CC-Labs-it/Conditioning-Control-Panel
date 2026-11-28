using System;
using System.Collections.Generic;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Controls.Super;

/// <summary>
/// Each gold box's option rows on this head, the twins of the WPF 7.4.1 XAML rows (same keys, same
/// ranges, same steps, defaults = shipped behaviour). Every clamp and snap is a Core <c>*Options</c>
/// call; nothing here is a rule. A page sets its box's Body to one of these.
/// </summary>
public static class SuperPageRows
{
    private static int R(double v) => (int)Math.Round(v);
    private static int Snap5(double v) => (int)(Math.Round(v / 5) * 5);
    private static string Pct(int v) => $"{v}%";
    private static string PctOrOff(int v) => v <= 0 ? Loc.Get("super_opt_off") : $"{v}%";

    /// <summary>Steam on the pink filter page (WPF PinkFilterFeatureControl.Creep.cs). The old fog cap stays folded, as WPF.</summary>
    public static SuperOptionRows Steam()
    {
        var r = new SuperOptionRows(SuperEffect.Creep, "SteamRows");
        r.Toggle("super_steam_words", s => s.SteamWordsInFog, (s, v) => s.SteamWordsInFog = v);
        r.Combo("super_steam_creep", () => new[]
            {
                (Loc.Get("super_creep_speed_slow"), nameof(CreepSpeed.Slow)),
                (Loc.Get("super_creep_speed_normal"), nameof(CreepSpeed.Normal)),
                (Loc.Get("super_creep_speed_fast"), nameof(CreepSpeed.Fast)),
            },
            s => s.SuperCreepSpeed.ToString(), (s, v) => { if (Enum.TryParse<CreepSpeed>(v, out var c)) s.SuperCreepSpeed = c; });
        r.Slider("super_steam_reach", SteamOptions.ReachMin, SteamOptions.ReachMax, 5,
            s => s.SteamReach, (s, v) => s.SteamReach = SteamOptions.ClampReach(Snap5(v)), s => Pct(s.SteamReach));
        r.Slider("super_steam_size", 40, 300, 5,
            s => s.SteamWipeSize, (s, v) => s.SteamWipeSize = Models.AppSettings.ClampSteamWipeSize(Snap5(v)), s => Pct(s.SteamWipeSize), tip: false);
        r.Slider("super_steam_return", SteamOptions.ReturnMin, SteamOptions.ReturnMax, 5,
            s => s.SteamFogReturn, (s, v) => s.SteamFogReturn = SteamOptions.ClampReturn(Snap5(v)), s => Pct(s.SteamFogReturn));
        r.Slider("super_steam_drift", SteamOptions.DriftMin, SteamOptions.DriftMax, 5,
            s => s.SteamDrift, (s, v) => s.SteamDrift = SteamOptions.ClampDrift(Snap5(v)), s => Pct(s.SteamDrift));
        return r;
    }

    /// <summary>The Deck on the flash page (WPF FlashFeatureControl.Deck.cs): both ends of the flip roll and the break reach.</summary>
    public static SuperOptionRows Deck()
    {
        var r = new SuperOptionRows(SuperEffect.FlickerDeck, "DeckRows");
        r.Slider("super_deck_fewest", DeckOptions.RollMin, DeckOptions.RollMax, 1,
            s => Roll(s).Min, (s, v) => SetRoll(s, R(v), null), s => Roll(s).Min.ToString());
        r.Slider("super_deck_most", DeckOptions.RollMin, DeckOptions.RollMax, 1,
            s => Roll(s).Max, (s, v) => SetRoll(s, null, R(v)), s => Roll(s).Max.ToString());
        r.Slider("super_deck_reach", DeckOptions.ReachMin, DeckOptions.ReachMax, 5,
            s => s.DeckBreakReach, (s, v) => s.DeckBreakReach = DeckOptions.ClampReach(DeckOptions.SnapReach(v)), s => Pct(s.DeckBreakReach));
        return r;
    }

    private static (int Min, int Max) Roll(Models.AppSettings s) => DeckOptions.BreakRange(s.DeckFlips, s.DeckFlipsFewest, s.DeckFlipsMost);

    /// <summary>WPF SetDeckRoll: one end past the other drags it along; an end the old single slider already rolls stays unset.</summary>
    private static void SetRoll(Models.AppSettings s, int? fewest, int? most)
    {
        var (lo, hi) = Roll(s);
        if (fewest is int f) { lo = DeckOptions.ClampRoll(f); hi = Math.Max(lo, hi); }
        if (most is int m) { hi = DeckOptions.ClampRoll(m); lo = Math.Min(lo, hi); }
        (s.DeckFlipsFewest, s.DeckFlipsMost) = DeckOptions.StoredEnds(s.DeckFlips, lo, hi);
    }

    /// <summary>The Whirlpool on the spiral page (WPF SpiralFeatureControl.Pool.cs, 7.4 box): four rows shown,
    /// the spiral, its hold, moving pops and the four drags behind a More fold that opens itself when one is off its default.</summary>
    public static SuperOptionRows Whirlpool()
    {
        var r = new SuperOptionRows(SuperEffect.Vortex, "PoolRows");
        r.Slider("super_pool_size", WhirlpoolOptions.SizeMin, WhirlpoolOptions.SizeMax, 5,
            s => s.WhirlpoolSize, (s, v) => s.WhirlpoolSize = WhirlpoolOptions.ClampSize(Snap5(v)), s => Pct(s.WhirlpoolSize));
        r.Slider("super_pool_smallest", WhirlpoolOptions.SmallestMin, WhirlpoolOptions.SmallestMax, 5,
            s => s.WhirlpoolSmallest, (s, v) => s.WhirlpoolSmallest = WhirlpoolOptions.SnapSmallest(v), s => Pct(s.WhirlpoolSmallest));
        r.Toggle("super_pool_centred", s => s.WhirlpoolStayCentred, (s, v) => s.WhirlpoolStayCentred = v);
        r.Toggle("super_pool_restless", s => s.WhirlpoolRestless, (s, v) => s.WhirlpoolRestless = v);
        var fold = r.Fold(FoldedRowsChanged);
        r.Combo("super_pool_spiral", PoolSpirals, s => s.WhirlpoolSpiral, (s, v) => s.WhirlpoolSpiral = v, into: fold);
        var every = r.Slider("super_pool_every", WhirlpoolOptions.HoldMin, WhirlpoolOptions.HoldMax, 1,
            s => s.WhirlpoolNewEvery, (s, v) => s.WhirlpoolNewEvery = WhirlpoolOptions.ClampHold(R(v)),
            s => Loc.GetF("super_pool_every_value", s.WhirlpoolNewEvery), into: fold);
        r.Toggle("super_moving_pops", s => s.MovingEffectsPop, (s, v) => s.MovingEffectsPop = v, into: fold);
        r.Heading("super_pool_drags", into: fold);
        r.Toggle("super_pool_drags_fog", s => s.WhirlpoolDragsFog, (s, v) => s.WhirlpoolDragsFog = v, tip: false, into: fold);
        r.Toggle("super_pool_drags_pictures", s => s.WhirlpoolDragsPictures, (s, v) => s.WhirlpoolDragsPictures = v, tip: false, into: fold);
        r.Toggle("super_pool_drags_bubbles", s => s.WhirlpoolDragsBubbles, (s, v) => s.WhirlpoolDragsBubbles = v, tip: false, into: fold);
        r.Toggle("super_pool_drags_words", s => s.WhirlpoolDragsWords, (s, v) => s.WhirlpoolDragsWords = v, tip: false, into: fold);
        // "New spiral every" only means something on "Always new": dimmed and locked while one spiral
        // is held (WPF DimPoolEvery, the SuperDim look). The value is kept.
        r.Refreshed += () =>
        {
            bool held = CoreSettings.Current.WhirlpoolSpiral.Length > 0;
            every.Opacity = held ? SuperDim.DimOpacity : 1;
            every.IsHitTestVisible = !held;
        };
        return r;
    }

    /// <summary>WPF PoolFoldedRowsChanged: a folded row away from its default opens the fold.</summary>
    public static bool FoldedRowsChanged(Models.AppSettings s) =>
        s.WhirlpoolSpiral.Length > 0
        || s.WhirlpoolNewEvery != WhirlpoolOptions.HoldDefault
        || s.MovingEffectsPop
        || !s.WhirlpoolDragsFog || !s.WhirlpoolDragsPictures || !s.WhirlpoolDragsBubbles || !s.WhirlpoolDragsWords;

    /// <summary>"Always new" first, then every saved Loom spiral with dials (never the user's own gif spiral).</summary>
    private static IEnumerable<(string, string)> PoolSpirals()
    {
        yield return (Loc.Get("super_pool_spiral_new"), "");
        List<(string Slug, string GifPath, string? ParamsJson)> saved;
        try { saved = ConditioningControlPanel.Services.Chaos.DtrhLoomStore.List(); } catch { yield break; }
        foreach (var (slug, _, json) in saved)
            if (LoomSpecJson.Parse(json) != null) yield return (slug.Replace('_', ' ').Replace('-', ' '), slug);
    }

    /// <summary>Afterglow on the subliminal page (WPF SubliminalFeatureControl.Afterglow.cs).</summary>
    public static SuperOptionRows Afterglow()
    {
        var r = new SuperOptionRows(SuperEffect.Afterglow, "AfterglowRows");
        r.Toggle("super_afterglow_also_base", s => s.AfterglowAlsoBase, (s, v) => s.AfterglowAlsoBase = v);
        r.Slider("super_afterglow_words", AfterglowLineOptions.WordsMin, AfterglowLineOptions.WordsMax, 1,
            s => s.AfterglowLineWords, (s, v) => s.AfterglowLineWords = AfterglowLineOptions.ClampWords(R(v)), s => s.AfterglowLineWords.ToString());
        r.Slider("super_afterglow_hold", AfterglowLineOptions.HoldMin, AfterglowLineOptions.HoldMax, AfterglowLineOptions.HoldStep,
            s => s.AfterglowLineHold, (s, v) => s.AfterglowLineHold = AfterglowLineOptions.ClampHold(v),
            s => Loc.GetF("super_opt_seconds", s.AfterglowLineHold.ToString("0.0")));
        r.Slider("super_afterglow_ghost", AfterglowLineOptions.GhostMin, AfterglowLineOptions.GhostMax, 1,
            s => s.AfterglowGhostOpacity, (s, v) => s.AfterglowGhostOpacity = AfterglowLineOptions.ClampGhost(R(v)), s => Pct(s.AfterglowGhostOpacity));
        return r;
    }

    /// <summary>Scrawl on the bouncing text page (WPF BouncingTextFeatureControl.Scrawl.cs), Stamp size included.</summary>
    public static SuperOptionRows Scrawl()
    {
        var r = new SuperOptionRows(SuperEffect.Scrawl, "ScrawlRows");
        r.Slider("super_scrawl_life", ScrawlOptions.StampSecondsMin, ScrawlOptions.StampSecondsMax, 1,
            s => s.ScrawlStampSeconds, (s, v) => s.ScrawlStampSeconds = ScrawlOptions.ClampStampSeconds(R(v)),
            s => Loc.GetF("super_opt_seconds", s.ScrawlStampSeconds));
        r.Slider("super_scrawl_cap", ScrawlOptions.StampCapMin, ScrawlOptions.StampCapMax, 1,
            s => s.ScrawlStampCap, (s, v) => s.ScrawlStampCap = ScrawlOptions.ClampStampCap(R(v)), s => s.ScrawlStampCap.ToString());
        r.Slider("super_scrawl_size", ScrawlOptions.StampSizeMin, ScrawlOptions.StampSizeMax, ScrawlOptions.StampSizeStep,
            s => s.ScrawlStampSize, (s, v) => s.ScrawlStampSize = ScrawlOptions.ClampStampSize(R(v)), s => Pct(s.ScrawlStampSize), large: 25);
        r.Slider("super_scrawl_slam", ScrawlOptions.SlamMin, ScrawlOptions.SlamMax, ScrawlOptions.SlamStep,
            s => s.ScrawlSlam, (s, v) => s.ScrawlSlam = ScrawlOptions.ClampSlam(R(v)), s => PctOrOff(s.ScrawlSlam));
        r.Toggle("super_moving_pops", s => s.MovingEffectsPop, (s, v) => s.MovingEffectsPop = v);
        return r;
    }

    /// <summary>Lights Down on the video page (WPF VideoFeatureControl.LightsDown.cs). Folded until its host
    /// registers in <see cref="SuperBox.HostedOnThisHead"/>.</summary>
    public static SuperOptionRows LightsDown()
    {
        var r = new SuperOptionRows(SuperEffect.LightsDown, "LightsDownRows");
        r.Slider("super_lights_darkness", LightsDownOptions.DarknessMin, LightsDownOptions.DarknessMax, 5,
            s => s.LightsDownDarkness, (s, v) => s.LightsDownDarkness = LightsDownOptions.ClampDarkness(LightsDownOptions.Snap(v)), s => Pct(s.LightsDownDarkness));
        r.Slider("super_lights_dim_speed", LightsDownOptions.DimSpeedMin, LightsDownOptions.DimSpeedMax, 5,
            s => s.LightsDownDimSpeed, (s, v) => s.LightsDownDimSpeed = LightsDownOptions.ClampDimSpeed(LightsDownOptions.Snap(v)), s => Pct(s.LightsDownDimSpeed));
        r.Slider("super_lights_return_speed", LightsDownOptions.ReturnSpeedMin, LightsDownOptions.ReturnSpeedMax, 5,
            s => s.LightsDownReturnSpeed, (s, v) => s.LightsDownReturnSpeed = LightsDownOptions.ClampReturnSpeed(LightsDownOptions.Snap(v)), s => Pct(s.LightsDownReturnSpeed));
        r.Toggle("super_lights_follow", s => s.LightsDownFollow, (s, v) => s.LightsDownFollow = v);
        return r;
    }

    /// <summary>Undertow on the Brain Drain page (WPF BrainDrainFeatureControl.Undertow.cs). Owner call
    /// 2026-11-25: Undertow is not hosted on this head, so these stay folded behind "coming soon".</summary>
    public static SuperOptionRows Undertow()
    {
        var r = new SuperOptionRows(SuperEffect.Undertow, "UndertowRows");
        r.Slider("super_undertow_size", UndertowOptions.SizeMin, UndertowOptions.SizeMax, 5,
            s => s.UndertowLensSize, (s, v) => s.UndertowLensSize = UndertowOptions.ClampSize(UndertowOptions.Snap(v)), s => Pct(s.UndertowLensSize));
        r.Slider("super_undertow_shrink", UndertowOptions.ShrinkMin, UndertowOptions.ShrinkMax, 5,
            s => s.UndertowShrinkSpeed, (s, v) => s.UndertowShrinkSpeed = UndertowOptions.ClampShrink(UndertowOptions.Snap(v)), s => Pct(s.UndertowShrinkSpeed));
        r.Slider("super_undertow_widen", UndertowOptions.WidenMin, UndertowOptions.WidenMax, 5,
            s => s.UndertowClickWiden, (s, v) => s.UndertowClickWiden = UndertowOptions.ClampWiden(UndertowOptions.Snap(v)), s => Pct(s.UndertowClickWiden));
        return r;
    }
}
