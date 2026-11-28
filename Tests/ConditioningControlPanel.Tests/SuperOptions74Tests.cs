using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.4 Super options round: the Whirlpool's "Smallest size" (ccp-bugs #1719) and "Restless pool"
/// (#1707), and the Deck's "Fewest flips" / "Most flips" (#1636). Defaults are the shipped
/// behaviour, clamps hold, presets carry them and every string is in the ten languages.
/// </summary>
public class SuperOptions74Tests
{
    private const double Dt = 1 / 30.0;

    private static Whirlpool Grown()
    {
        var p = new Whirlpool();
        for (double t = 0; t < 20; t += Dt) p.Step(Dt, 500, 400, 0, 1, 1080);
        Assert.Equal(1, p.Size, 6);
        return p;
    }

    private static void Move(Whirlpool p, double seconds, double speedPx = 800)
    {
        for (double t = 0; t < seconds; t += Dt) p.Step(Dt, 500, 400, speedPx, 1, 1080);
    }

    private static void Rest(Whirlpool p, double seconds)
    {
        for (double t = 0; t < seconds; t += Dt) p.Step(Dt, 500, 400, 0, 1, 1080);
    }

    // ---------------- Smallest size ----------------

    [Fact]
    public void Stock_pool_still_shrinks_to_nothing_while_the_mouse_moves()
    {
        var s = new AppSettings();
        Assert.Equal(0, s.WhirlpoolSmallest);
        Assert.False(s.WhirlpoolRestless);
        var p = Grown();
        p.Floor = WhirlpoolOptions.Floor(s.WhirlpoolSmallest);
        Move(p, 10);
        Assert.Equal(0, p.Size, 9);
    }

    [Fact]
    public void Smallest_size_keeps_the_pool_under_the_cursor_while_the_mouse_moves()
    {
        var p = Grown();
        p.Floor = WhirlpoolOptions.Floor(20);
        Move(p, 30);
        Assert.Equal(0.2, p.Size, 9);
        // Held up by its floor it is drawn, but it does not act.
        Assert.False(p.Live);
        // At rest it grows on to full as before, and acts again once past its floor.
        Rest(p, 20);
        Assert.Equal(1, p.Size, 6);
        Assert.True(p.Live);
    }

    /// <summary>Owner intent for #1719: "visible all the time", not "pulls all the time". A pool at
    /// its floor pulls nothing, swirls no fog, eats nothing and takes no words.</summary>
    [Theory]
    [InlineData(6)]
    [InlineData(20)]
    [InlineData(35)]
    [InlineData(50)]
    public void A_pool_held_up_by_its_floor_draws_but_does_not_act(int smallest)
    {
        var stage = new SuperStage { PoolOn = true, MovingPops = true };
        var p = stage.Pool;
        p.Floor = WhirlpoolOptions.Floor(smallest);
        Rest(p, 20);
        Move(p, 30);
        Assert.Equal(p.Floor, p.Size, 9);
        Assert.True(p.Size > Whirlpool.LiveSize || smallest == 6);
        Assert.False(p.Live);
        Assert.False(stage.PoolLive);
        Assert.False(stage.PoolTakesWords);
        var pull = stage.Pull(p.X + 5, p.Y, 1, PullKind.Bubble);
        Assert.False(pull.Inside);
        Assert.False(pull.Eaten);
        Assert.Equal(0, pull.Ax);
        Assert.Equal(0, p.Pull(p.X, p.Y).Ax);
        Assert.False(p.Pull(p.X, p.Y).Eaten);
    }

    [Fact]
    public void With_no_floor_the_live_rule_is_the_shipped_one()
    {
        var p = new Whirlpool();
        while (p.Size <= Whirlpool.LiveSize) p.Step(Dt, 500, 400, 0, 1, 1080);
        Assert.True(p.Live);
        Assert.True(p.Size < Whirlpool.LiveSize + 0.01);
    }

    [Fact]
    public void A_pool_acts_once_it_grows_a_margin_past_its_floor()
    {
        var p = new Whirlpool { Floor = 0.3 };
        Move(p, 30);
        Assert.False(p.Live);
        while (p.Size <= 0.3 + Whirlpool.FloorLiveMargin) p.Step(Dt, 500, 400, 0, 1, 1080);
        Assert.True(p.Live);
    }

    /// <summary>Under a floor a restless pool that the mouse is feeding grows at its own (faster)
    /// pace; the floor clamp only ever lifts the slow rest pace, never caps the faster growth.</summary>
    [Fact]
    public void Under_a_floor_a_restless_pool_keeps_its_faster_growth()
    {
        var floored = new Whirlpool { Restless = true, Floor = 0.4 };
        var free = new Whirlpool { Restless = true };
        Move(floored, 1);
        Move(free, 1);
        Assert.Equal(free.Size, floored.Size, 9);
        Assert.True(floored.Size > 1.0 / Whirlpool.GrowSeconds * 2,
            $"one second of movement grew it only to {floored.Size}, the rest pace");
        // At rest under the floor it still climbs at the rest pace.
        var resting = new Whirlpool { Restless = true, Floor = 0.4 };
        Rest(resting, 1);
        Assert.Equal(1.0 / Whirlpool.GrowSeconds, resting.Size, 2);
    }

    [Fact]
    public void Under_the_floor_the_pool_fades_in_even_while_the_mouse_moves()
    {
        var p = new Whirlpool { Floor = 0.3 };
        p.Step(Dt, 500, 400, 800, 1, 1080);
        Assert.True(p.Size > 0 && p.Size < 0.01, "it creeps up, it never pops in at the floor");
        Move(p, 10);
        Assert.Equal(0.3, p.Size, 9);
        p.Reset();
        Assert.Equal(0, p.Size);
    }

    [Fact]
    public void Smallest_size_clamps_and_snaps()
    {
        Assert.Equal(0, WhirlpoolOptions.ClampSmallest(-10));
        Assert.Equal(WhirlpoolOptions.SmallestMax, WhirlpoolOptions.ClampSmallest(500));
        Assert.Equal(15, WhirlpoolOptions.SnapSmallest(13.4));
        Assert.Equal(WhirlpoolOptions.SmallestMax, WhirlpoolOptions.SnapSmallest(90));
        Assert.Equal(0.5, WhirlpoolOptions.Floor(999), 9);
        var s = new AppSettings { WhirlpoolSmallest = 200 };
        Assert.Equal(WhirlpoolOptions.SmallestMax, s.WhirlpoolSmallest);
    }

    // ---------------- Restless pool ----------------

    [Fact]
    public void Restless_pool_grows_while_the_mouse_moves_and_calms_at_rest()
    {
        var p = new Whirlpool { Restless = true };
        Rest(p, 20);
        Assert.Equal(0, p.Size, 9);
        Move(p, 3);
        double moved = p.Size;
        Assert.True(moved > 0.3, $"three seconds of movement should grow it, got {moved}");
        Move(p, 10);
        Assert.Equal(1, p.Size, 6);
        Rest(p, 3);
        Assert.True(p.Size < 1 && p.Size > 0.5);
        Rest(p, 20);
        Assert.Equal(0, p.Size, 9);
    }

    [Fact]
    public void Only_a_restless_pool_feeds_on_clicks()
    {
        var stock = new Whirlpool();
        stock.Kick();
        Assert.Equal(0, stock.Size);
        var p = new Whirlpool { Restless = true };
        p.Kick(); p.Kick();
        Assert.Equal(2 * Whirlpool.ClickKick, p.Size, 9);
        for (int i = 0; i < 100; i++) p.Kick();
        Assert.Equal(1, p.Size, 9);
    }

    [Fact]
    public void Restless_pool_keeps_its_floor_at_rest()
    {
        var p = new Whirlpool { Restless = true, Floor = 0.1 };
        Move(p, 20);
        Rest(p, 30);
        Assert.Equal(0.1, p.Size, 9);
    }

    [Fact]
    public void Centred_says_where_restless_says_when()
    {
        // Centred stock pool: the mouse counts as resting, exactly as before.
        Assert.Equal((960.0, 540.0, 0.0), WhirlpoolOptions.Anchor(true, false, 10, 20, 800, 960, 540));
        Assert.Equal(WhirlpoolOptions.Anchor(true, 10, 20, 800, 960, 540), WhirlpoolOptions.Anchor(true, false, 10, 20, 800, 960, 540));
        // Centred restless pool: stays in the middle and reads the real speed.
        Assert.Equal((960.0, 540.0, 800.0), WhirlpoolOptions.Anchor(true, true, 10, 20, 800, 960, 540));
        // Under the cursor both read the cursor.
        Assert.Equal((10.0, 20.0, 800.0), WhirlpoolOptions.Anchor(false, true, 10, 20, 800, 960, 540));
        Assert.Equal((10.0, 20.0, 800.0), WhirlpoolOptions.Anchor(false, false, 10, 20, 800, 960, 540));
    }

    // ---------------- Restless pool: clicks ----------------

    [Fact]
    public void A_click_is_one_kick_held_or_tapped_and_an_old_press_never_kicks()
    {
        // The first poll after a pause only clears the "pressed since" bit.
        Assert.False(ConditioningControlPanel.Services.Super.SuperStageHost.ClickPressed(true, true, false, firstPoll: true));
        // Rising edge of the held bit.
        Assert.True(ConditioningControlPanel.Services.Super.SuperStageHost.ClickPressed(true, true, false, false));
        // Still held: no second kick.
        Assert.False(ConditioningControlPanel.Services.Super.SuperStageHost.ClickPressed(true, false, true, false));
        // A tap shorter than one step: up again by the poll, but pressed since the last one.
        Assert.True(ConditioningControlPanel.Services.Super.SuperStageHost.ClickPressed(false, true, false, false));
        Assert.False(ConditioningControlPanel.Services.Super.SuperStageHost.ClickPressed(false, false, false, false));
    }

    // ---------------- Deck: fewest / most flips ----------------

    [Fact]
    public void Deck_roll_with_no_ends_set_is_the_old_single_slider()
    {
        var s = new AppSettings();
        Assert.Equal(DeckOptions.RollUnset, s.DeckFlipsFewest);
        Assert.Equal(DeckOptions.RollUnset, s.DeckFlipsMost);
        Assert.Equal((3, 5), DeckOptions.BreakRange(s.DeckFlips, s.DeckFlipsFewest, s.DeckFlipsMost));
        for (int f = DeckOptions.FlipsMin; f <= DeckOptions.FlipsMax; f++)
            Assert.Equal(DeckOptions.BreakRange(f), DeckOptions.BreakRange(f, 0, 0));
    }

    [Fact]
    public void Deck_roll_takes_the_players_ends_clamped_and_never_crossed()
    {
        Assert.Equal((2, 7), DeckOptions.BreakRange(4, 2, 7));
        Assert.Equal((1, 9), DeckOptions.BreakRange(4, 1, 50));
        Assert.Equal((3, 9), DeckOptions.BreakRange(4, -5, 50));     // below 1 reads as unset
        Assert.Equal((6, 6), DeckOptions.BreakRange(4, 6, 2));       // a low end above the high end lifts it
        Assert.Equal((3, 8), DeckOptions.BreakRange(4, 0, 8));       // one end set, the other follows the old slider
        Assert.Equal((5, 5), DeckOptions.BreakRange(4, 5, 0));
        var s = new AppSettings { DeckFlipsFewest = -3, DeckFlipsMost = 40 };
        Assert.Equal(DeckOptions.RollUnset, s.DeckFlipsFewest);
        Assert.Equal(DeckOptions.RollMax, s.DeckFlipsMost);

        var rng = new Random(11);
        var seen = new HashSet<int>();
        for (int i = 0; i < 400; i++)
        {
            var (lo, hi) = DeckOptions.BreakRange(4, 2, 7);
            var c = FlashFlick(lo, hi, rng);
            Assert.InRange(c, 2, 7);
            seen.Add(c);
        }
        Assert.Equal(6, seen.Count);
    }

    [Fact]
    public void Deck_slider_moves_store_only_the_ends_that_differ_from_the_old_slider()
    {
        // Stock settings (3..5): one touch of "Fewest" to 2 marks that end only; the other stays unset.
        var s = new AppSettings();
        (s.DeckFlipsFewest, s.DeckFlipsMost) = DeckOptions.StoredEnds(s.DeckFlips, 2, 5);
        Assert.Equal(2, s.DeckFlipsFewest);
        Assert.Equal(DeckOptions.RollUnset, s.DeckFlipsMost);

        // Back to 3: both ends are stock again, so nothing reads as changed against a fresh settings object.
        (s.DeckFlipsFewest, s.DeckFlipsMost) = DeckOptions.StoredEnds(s.DeckFlips, 3, 5);
        var stock = new AppSettings();
        Assert.Equal(stock.DeckFlipsFewest, s.DeckFlipsFewest);
        Assert.Equal(stock.DeckFlipsMost, s.DeckFlipsMost);

        // Whatever is stored, the deck rolls exactly what the sliders showed.
        for (int f = DeckOptions.FlipsMin; f <= DeckOptions.FlipsMax; f++)
            for (int lo = DeckOptions.RollMin; lo <= DeckOptions.RollMax; lo++)
                for (int hi = lo; hi <= DeckOptions.RollMax; hi++)
                {
                    var (sf, sm) = DeckOptions.StoredEnds(f, lo, hi);
                    Assert.Equal((lo, hi), DeckOptions.BreakRange(f, sf, sm));
                }

        // The page writes through that rule, never both raw ends.
        var page = File.ReadAllText(Path.Combine(Root(), "Features", "FlashFeatureControl.Deck.cs"));
        Assert.Contains("DeckOptions.StoredEnds(s.DeckFlips, lo, hi)", page);
        Assert.DoesNotContain("s.DeckFlipsFewest = lo;", page);
    }

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return Path.Combine(dir!.FullName, "ConditioningControlPanel");
    }

    private static int FlashFlick(int lo, int hi, Random rng)
        => Services.Flash.FlickerDeck.Create(0, rng, lo, hi).BreakAt;

    // ---------------- Presets and strings ----------------

    [Fact]
    public void Presets_carry_the_new_options_and_reset_them_when_missing()
    {
        foreach (var name in new[] { nameof(AppSettings.WhirlpoolSmallest), nameof(AppSettings.WhirlpoolRestless),
                     nameof(AppSettings.DeckFlipsFewest), nameof(AppSettings.DeckFlipsMost) })
            Assert.Contains(name, PresetCarry.Looks);

        var src = new AppSettings { WhirlpoolSmallest = 25, WhirlpoolRestless = true, DeckFlipsFewest = 2, DeckFlipsMost = 8 };
        var dst = new AppSettings();
        PresetCarry.Apply(dst, PresetCarry.Snapshot(src));
        Assert.Equal(25, dst.WhirlpoolSmallest);
        Assert.True(dst.WhirlpoolRestless);
        Assert.Equal(2, dst.DeckFlipsFewest);
        Assert.Equal(8, dst.DeckFlipsMost);

        // A preset from before these options (no names) puts the shipped behaviour back.
        PresetCarry.Apply(dst, new Dictionary<string, string>());
        Assert.Equal(0, dst.WhirlpoolSmallest);
        Assert.False(dst.WhirlpoolRestless);
        Assert.Equal(DeckOptions.RollUnset, dst.DeckFlipsFewest);
        Assert.Equal(DeckOptions.RollUnset, dst.DeckFlipsMost);
    }

    [Fact]
    public void Every_new_string_is_in_the_ten_languages()
    {
        var dir = SourceRoots.LanguagesDirectory;
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        var keys = new[] { "super_pool_smallest", "tooltip_super_pool_smallest", "super_pool_restless", "tooltip_super_pool_restless",
            "super_deck_fewest", "tooltip_super_deck_fewest", "super_deck_most", "tooltip_super_deck_most" };
        foreach (var f in files)
        {
            var bytes = File.ReadAllBytes(f);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{f} has a BOM");
            var json = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            foreach (var k in keys)
            {
                var v = (string?)json[k];
                Assert.False(string.IsNullOrWhiteSpace(v), $"{Path.GetFileName(f)} lacks {k}");
                Assert.DoesNotContain("—", v);
                Assert.DoesNotContain("–", v);
                Assert.DoesNotContain("!", v);
            }
        }
    }
}
