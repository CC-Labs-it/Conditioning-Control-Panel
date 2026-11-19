using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.PrizeCounter;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The DEBUG shop preview (lane I, BRIEF "The preview wire"): the rules (prices, owned, insufficient,
/// coupon, gift once a week, one stamp a day, goal, equip) and the state body the page reads.
/// </summary>
public class ShopPreviewRulesTests
{
    // Tuesday 17 November 2026, noon. ISO week 2026-W47; the next Monday is the 23rd.
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static JObject Sample()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var path = Path.Combine(dir!.FullName, "ConditioningControlPanel", "Resources", "web", "prizecounter", "mock", "state.sample.json");
        return JObject.Parse(File.ReadAllText(path));
    }

    private static ShopPreviewData Fresh() => ShopPreviewRules.Seed(Now, Sample());

    private static JObject[] Rows(JObject state, string? tab = null) => ((JArray)state["tabs"]!).OfType<JObject>()
        .Where(t => tab == null || (string?)t["id"] == tab)
        .SelectMany(t => t.SelectTokens("shelves[*].rows[*]").OfType<JObject>()).ToArray();

    [Fact]
    public void A_first_preview_is_the_mockup_s_morning()
    {
        var d = Fresh();
        Assert.Equal(410, d.Sp);
        foreach (var e in ShopPreviewRules.Effects) Assert.Equal(e.Price == 0, d.Owned.ContainsKey(e.Id));
        Assert.True(d.Owned.ContainsKey("rain.dress.rose-flare"));   // the sample's own owned rows stay owned
        Assert.True(ShopPreviewRules.GiftReady(d, Now));
        Assert.Equal(5, ShopPreviewRules.CardDay(d, Now));
        Assert.Equal(new[] { 1, 2, 4 }, d.Stamped);
        Assert.Equal("fx.bubble.spiral", d.Goal);
        Assert.Equal(3, d.Ledger.Count);
        Assert.Equal("fx.bubble.soap", d.Equip["bubble"]);
        Assert.Equal("fx.fog.lilac", d.Equip["fog2"]);
        Assert.False(d.Mix);
    }

    [Fact]
    public void The_state_puts_effects_first_and_marks_every_row_with_its_places()
    {
        var s = ShopPreviewRules.BuildState(Fresh(), Sample(), Now);
        Assert.True((bool)s["ok"]!);
        Assert.True((bool)s["preview"]!);
        Assert.Equal(410, (int)s["sp"]!);
        Assert.Equal("2026-W47", (string?)s["week"]);
        var tabs = ((JArray)s["tabs"]!).Select(t => (string?)t["id"]).ToArray();
        Assert.Equal("effects", tabs[0]);
        Assert.Contains("kart", tabs);
        Assert.Contains("backroom", tabs);
        foreach (var row in Rows(s)) Assert.NotNull(row["places"]);
        foreach (var row in Rows(s, "effects"))
        {
            Assert.Equal(new[] { "desktop" }, row["places"]!.Values<string>().ToArray());
            Assert.Contains((string?)row["slot"], WornLooks.EffectSlots);
            Assert.False(string.IsNullOrEmpty((string?)row["name"]));
        }
        Assert.Equal(new[] { "desktop" }, Rows(s, "desk")[0]["places"]!.Values<string>().ToArray());
        Assert.Equal(3, Rows(s, "kart")[0]["places"]!.Count());
        Assert.Equal(5, ((JArray)s["tabs"]![0]!["shelves"]!).Count);   // one shelf per slot
    }

    [Theory]
    [InlineData("fx.bubble.soap", 0)]
    [InlineData("fx.card.ink", 120)]
    [InlineData("fx.bubble.champagne", 360)]
    [InlineData("fx.bubble.frost", 360)]
    [InlineData("fx.bubble.spiral", 730)]
    [InlineData("fx.card.lace", 360)]
    [InlineData("fx.behind.whisper", 360)]
    [InlineData("fx.fog.twotone", 730)]
    [InlineData("fx.word.type", 120)]
    [InlineData("fx.word.marquee", 730)]
    public void Prices_follow_the_wire(string id, int price)
    {
        var row = Rows(ShopPreviewRules.BuildState(Fresh(), Sample(), Now), "effects").Single(r => (string?)r["id"] == id);
        Assert.Equal(price, (int)row["priceSp"]!);
        Assert.Equal(price == 0, row["owned"]!.Type == JTokenType.Object);
    }

    [Fact]
    public void Not_out_yet_new_and_special_rows_carry_their_flags()
    {
        var s = ShopPreviewRules.BuildState(Fresh(), Sample(), Now);
        var fx = Rows(s, "effects").ToDictionary(r => (string)r["id"]!);
        foreach (var id in new[] { "fx.bubble.gum", "fx.bubble.latex", "fx.bubble.oil" })
        {
            Assert.True((bool)fx[id]["todo"]!);
            Assert.Equal("soon", (string?)fx[id]["sale"]);
        }
        Assert.Null(fx["fx.bubble.frost"]["todo"]);
        Assert.True((bool)fx["fx.fog.twotone"]["special"]!);
        Assert.Equal(new[] { "fx.bubble.marble", "fx.bubble.heart", "fx.bubble.spiral", "fx.behind.whisper" },
            s["fresh"]!.Values<string>().ToArray());
        Assert.All(s["fresh"]!.Values<string>(), id => Assert.True((bool)fx[id!]["isNew"]!));
        var equip = (JObject)s["equip"]!;
        Assert.Equal(new[] { "bubble", "card", "behind", "fog", "word", "fog2", "mix" }, equip.Properties().Select(p => p.Name).ToArray());
        Assert.False((bool)equip["mix"]!);
        var stamps = (JObject)s["stamps"]!;
        Assert.Equal(5, (int)stamps["day"]!);
        Assert.Equal(6, (int)stamps["couponAt"]!);
        Assert.False((bool)stamps["couponReady"]!);
        var gift = (JObject)s["gift"]!;
        Assert.True((bool)gift["ready"]!);
        Assert.Equal(25, (int)gift["amount"]!);
        Assert.StartsWith("2026-11-23T00:00:00", (string?)gift["next"]);
    }

    [Fact]
    public void A_buy_pays_the_price_once_and_owns_the_row()
    {
        var d = Fresh();
        var r = ShopPreviewRules.Buy(d, Sample(), "fx.bubble.champagne", "i-1", false, Now);
        Assert.True(r.Ok);
        Assert.Equal(360, r.Receipt!.PaidSp);
        Assert.Equal(410, r.Receipt.SpBefore);
        Assert.Equal(50, d.Sp);
        Assert.True(d.Owned.ContainsKey("fx.bubble.champagne"));
        Assert.Equal(-360, d.Ledger[0].N);
        // The same idem again is the same receipt, never a second charge.
        Assert.True(ShopPreviewRules.Buy(d, Sample(), "fx.bubble.champagne", "i-1", false, Now).Ok);
        Assert.Equal(50, d.Sp);
        Assert.Equal("idem_mismatch", ShopPreviewRules.Buy(d, Sample(), "fx.card.ink", "i-1", false, Now).Reason);
        Assert.Equal("owned", ShopPreviewRules.Buy(d, Sample(), "fx.bubble.champagne", "i-2", false, Now).Reason);
        Assert.Equal("insufficient", ShopPreviewRules.Buy(d, Sample(), "fx.card.ink", "i-3", false, Now).Reason);
        Assert.Equal(50, d.Sp);
    }

    [Fact]
    public void Real_rows_buy_too_and_unknown_or_unreleased_rows_do_not()
    {
        var d = Fresh();
        Assert.True(ShopPreviewRules.Buy(d, Sample(), "rain.acc.silver-drops", "a", false, Now).Ok);
        Assert.Equal(290, d.Sp);
        Assert.Equal("not_in_stock", ShopPreviewRules.Buy(d, Sample(), "fx.bubble.gum", "b", false, Now).Reason);
        Assert.Equal("not_in_stock", ShopPreviewRules.Buy(d, Sample(), "rt_demo", "c", false, Now).Reason);   // sale soon
        Assert.Equal("not_in_stock", ShopPreviewRules.Buy(d, Sample(), "nope", "d", false, Now).Reason);
        Assert.Equal("bad_input", ShopPreviewRules.Buy(d, Sample(), "fx.card.ink", "", false, Now).Reason);
        var s = ShopPreviewRules.BuildState(d, Sample(), Now);
        Assert.Contains("cos.rain.acc.silver-drops", s["prizes"]!["grants"]!.Values<string>());
    }

    [Fact]
    public void The_coupon_takes_one_tier_S_row_free_at_six_stamps()
    {
        var d = Fresh();
        Assert.Equal("not_ready", ShopPreviewRules.Buy(d, Sample(), "fx.card.ink", "c0", true, Now).Reason);
        d.Stamped.AddRange(new[] { 3, 5, 6 });
        Assert.True(ShopPreviewRules.CouponReady(d));
        Assert.Equal("unavailable", ShopPreviewRules.Buy(d, Sample(), "fx.card.lace", "c1", true, Now).Reason);   // tier M
        var r = ShopPreviewRules.Buy(d, Sample(), "fx.card.ink", "c2", true, Now);
        Assert.True(r.Ok);
        Assert.Equal(0, r.Receipt!.PaidSp);
        Assert.Equal(410, d.Sp);
        Assert.True(d.CouponUsed);
        Assert.Equal(("coupon", "fx.card.ink", 0), (d.Ledger[0].Code, d.Ledger[0].Id, d.Ledger[0].N));
        Assert.Equal("not_ready", ShopPreviewRules.Buy(d, Sample(), "fx.card.check", "c3", true, Now).Reason);
        // The coupon follows the tier letter: every effect row it takes is the cheapest priced tier.
        Assert.All(ShopPreviewRules.Effects.Where(e => e.Price > 0 && ShopPreviewRules.TierOf(e.Price) == ShopPreviewRules.CouponTier),
            e => Assert.Equal(ShopPreviewRules.Effects.Where(x => x.Price > 0).Min(x => x.Price), e.Price));
        // After the fog colours and bubble tints left, the coupon still has effect rows to take (2026-11-17).
        Assert.Equal(new[] { "fx.card.ink", "fx.card.check", "fx.word.type" },
            ShopPreviewRules.Effects.Where(e => !e.Todo && ShopPreviewRules.TierOf(e.Price) == ShopPreviewRules.CouponTier).Select(e => e.Id).ToArray());
    }

    [Fact]
    public void The_gift_opens_once_a_week()
    {
        var d = Fresh();
        Assert.True(ShopPreviewRules.OpenGift(d, Now).Ok);
        Assert.Equal(435, d.Sp);
        Assert.Equal(("gift", 25), (d.Ledger[0].Code, d.Ledger[0].N));
        Assert.Null(d.Ledger[0].T);
        Assert.Equal("not_ready", ShopPreviewRules.OpenGift(d, Now.AddDays(5)).Reason);   // Sunday, same week
        Assert.True(ShopPreviewRules.OpenGift(d, Now.AddDays(6)).Ok);                      // Monday
        Assert.Equal(460, d.Sp);
    }

    [Fact]
    public void One_stamp_a_day_one_card_per_account_and_done_after_day_14()
    {
        var d = Fresh();   // day 5, stamps 1, 2, 4
        Assert.True(ShopPreviewRules.StampToday(d, Now).Ok);
        Assert.Equal(new[] { 1, 2, 4, 5 }, d.Stamped);
        Assert.Equal("stamped", ShopPreviewRules.StampToday(d, Now.AddHours(8)).Reason);
        Assert.True(ShopPreviewRules.StampToday(d, Now.AddDays(1)).Ok);    // day 6
        Assert.True(ShopPreviewRules.StampToday(d, Now.AddDays(3)).Ok);    // day 8; day 7 stays empty
        Assert.Equal(new[] { 1, 2, 4, 5, 6, 8 }, d.Stamped);
        Assert.True(ShopPreviewRules.CouponReady(d));

        Assert.Equal(14, ShopPreviewRules.CardDay(d, Now.AddDays(9)));
        Assert.False(ShopPreviewRules.CardDone(d, Now.AddDays(9)));
        var day15 = Now.AddDays(10);
        Assert.True(ShopPreviewRules.CardDone(d, day15));
        Assert.Equal(14, ShopPreviewRules.CardDay(d, day15));
        Assert.Equal("done", ShopPreviewRules.StampToday(d, day15).Reason);
        Assert.Equal("done", ShopPreviewRules.StampToday(d, Now.AddDays(60)).Reason);   // never a second card
        Assert.Equal(new[] { 1, 2, 4, 5, 6, 8 }, d.Stamped);

        var s = (JObject)ShopPreviewRules.BuildState(d, Sample(), day15)["stamps"]!;
        Assert.True((bool)s["done"]!);
        Assert.True((bool)s["couponReady"]!);                             // an earned coupon outlives the card
        Assert.True(ShopPreviewRules.Buy(d, Sample(), "fx.word.type", "late", true, Now.AddDays(20)).Ok);
        s = (JObject)ShopPreviewRules.BuildState(d, Sample(), Now.AddDays(20))["stamps"]!;
        Assert.Equal((true, false, true), ((bool)s["done"]!, (bool)s["couponReady"]!, (bool)s["couponUsed"]!));
    }

    [Fact]
    public void The_first_stamp_is_day_1_and_before_it_there_is_no_card()
    {
        var d = Fresh();
        d.CardStart = "";
        d.Stamped.Clear();
        var s = (JObject)ShopPreviewRules.BuildState(d, Sample(), Now)["stamps"]!;
        Assert.Equal((1, 0, false), ((int)s["day"]!, ((JArray)s["stamped"]!).Count, (bool)s["done"]!));
        Assert.Equal(1, ShopPreviewRules.CardDay(d, Now.AddDays(30)));   // no stamp yet: still day 1, never done
        Assert.True(ShopPreviewRules.StampToday(d, Now).Ok);
        Assert.Equal("2026-11-17", d.CardStart);
        Assert.Equal(new[] { 1 }, d.Stamped);
        Assert.Equal("stamped", ShopPreviewRules.StampToday(d, Now.AddDays(-1)).Reason);   // a clock behind day 1
        Assert.True(ShopPreviewRules.StampToday(d, Now.AddDays(13)).Ok);
        Assert.Equal(new[] { 1, 14 }, d.Stamped);
        Assert.Equal("done", ShopPreviewRules.StampToday(d, Now.AddDays(14)).Reason);
    }

    [Fact]
    public void An_old_rules_file_heals_into_the_one_card()
    {
        var d = Fresh();
        d.CardStart = "garbage";
        d.Stamped = null!;
        d.Ledger = new() { new PreviewLedgerLine { T = "Won at chess", N = 20 }, new PreviewLedgerLine { N = 3 } };
        d.Receipts = null!;
        ShopPreviewRules.Heal(d, Sample());
        Assert.Equal("", d.CardStart);
        Assert.Empty(d.Stamped);
        Assert.Single(d.Ledger);
        Assert.NotNull(d.Receipts);
        var line = (JObject)ShopPreviewRules.BuildState(d, Sample(), Now)["ledger"]![0]!;
        Assert.Equal("Won at chess", (string?)line["t"]);
        Assert.Null(line["code"]);

        d.CardStart = "2026-10-01";
        d.Stamped = new() { 3, 1, 1, 15, 0, 2 };
        ShopPreviewRules.Heal(d, Sample());
        Assert.Equal(new[] { 1, 2, 3 }, d.Stamped);
        Assert.True(ShopPreviewRules.CardDone(d, Now));
    }

    [Fact]
    public void The_goal_takes_a_row_not_owned_and_a_buy_clears_it()
    {
        var d = Fresh();
        Assert.Equal("owned", ShopPreviewRules.SetGoal(d, Sample(), "fx.bubble.soap").Reason);
        Assert.Equal("unavailable", ShopPreviewRules.SetGoal(d, Sample(), "nope").Reason);
        Assert.Equal("unavailable", ShopPreviewRules.SetGoal(d, Sample(), "fx.bubble.gum").Reason);   // not out yet
        Assert.True(ShopPreviewRules.SetGoal(d, Sample(), "fx.card.ink").Ok);
        Assert.Equal("fx.card.ink", d.Goal);
        Assert.True(ShopPreviewRules.Buy(d, Sample(), "fx.card.ink", "g", false, Now).Ok);
        Assert.Null(d.Goal);
        Assert.True(ShopPreviewRules.SetGoal(d, Sample(), "fx.card.lace").Ok);
        Assert.True(ShopPreviewRules.SetGoal(d, Sample(), null).Ok);
        Assert.Null(d.Goal);
    }

    [Fact]
    public void Equip_wears_only_owned_rows_of_the_slot()
    {
        var d = Fresh();
        Assert.Equal("not_owned", ShopPreviewRules.Equip(d, "bubble", "fx.bubble.champagne").Reason);
        d.Owned["fx.bubble.champagne"] = new PreviewOwned();
        Assert.Equal("bad_input", ShopPreviewRules.Equip(d, "card", "fx.bubble.champagne").Reason);
        Assert.Equal("bad_input", ShopPreviewRules.Equip(d, "hat", "fx.bubble.champagne").Reason);
        Assert.Equal("unavailable", ShopPreviewRules.Equip(d, "bubble", "fx.bubble.gum").Reason);
        Assert.True(ShopPreviewRules.Equip(d, "bubble", "fx.bubble.champagne").Ok);
        Assert.Equal("fx.bubble.champagne", d.Equip["bubble"]);
        Assert.True(ShopPreviewRules.Equip(d, "bubble", null).Ok);
        Assert.Equal("fx.bubble.soap", d.Equip["bubble"]);
    }

    [Fact]
    public void Two_tone_is_a_switch_with_its_own_second_colour()
    {
        var d = Fresh();
        Assert.Equal("not_owned", ShopPreviewRules.Equip(d, "fog", ShopPreviewRules.Twotone).Reason);
        Assert.Equal("not_owned", ShopPreviewRules.Equip(d, "fog2", "fx.fog.mint").Reason);
        d.Owned[ShopPreviewRules.Twotone] = new PreviewOwned();
        Assert.True(ShopPreviewRules.Equip(d, "fog", ShopPreviewRules.Twotone).Ok);
        Assert.True(d.Mix);
        Assert.Equal("fx.fog.pink", d.Equip["fog"]);   // colour one stays the user's own
        foreach (var (row, _) in ConditioningControlPanel.SuperCore.FogLooks.Palette)
            Assert.True(ShopPreviewRules.Equip(d, "fog2", row).Ok);           // every palette colour, free
        Assert.Equal("fx.fog.peach", d.Equip["fog2"]);
        Assert.Equal("bad_input", ShopPreviewRules.Equip(d, "fog2", ShopPreviewRules.Twotone).Reason);
        Assert.Equal("bad_input", ShopPreviewRules.Equip(d, "fog2", "fx.fog.teal").Reason);
        Assert.Equal("bad_input", ShopPreviewRules.Equip(d, "fog", "fx.fog.lilac").Reason);   // no fog colour rows
        Assert.True(ShopPreviewRules.Equip(d, "fog2", null).Ok);
        Assert.Equal("fx.fog.lilac", d.Equip["fog2"]);
        Assert.True(ShopPreviewRules.Equip(d, "mix", null).Ok);
        Assert.False(d.Mix);
        Assert.True(ShopPreviewRules.Equip(d, "mix", ShopPreviewRules.Twotone).Ok);
        Assert.True(d.Mix);
    }

    [Fact]
    public void Credits_grow_one_ledger_line_per_run_and_the_ledger_keeps_six()
    {
        var d = Fresh();
        ShopPreviewRules.Credit(d, 1, ShopPreviewRules.CodePops, Now);
        ShopPreviewRules.Credit(d, 1, ShopPreviewRules.CodePops, Now);
        ShopPreviewRules.Credit(d, 0, ShopPreviewRules.CodePops, Now);
        Assert.Equal(412, d.Sp);
        Assert.Equal((ShopPreviewRules.CodePops, 2), (d.Ledger[0].Code, d.Ledger[0].N));
        for (int i = 0; i < 9; i++) ShopPreviewRules.Note(d, "x" + i, 1, Now);
        Assert.Equal(ShopPreviewRules.LedgerMax, d.Ledger.Count);
        Assert.Equal("x8", d.Ledger[0].Code);
        // On the wire every line is a code with its time, never English words.
        var buy = ShopPreviewRules.Buy(d, Sample(), "fx.bubble.heart", "led", false, Now);
        Assert.True(buy.Ok);
        var line = (JObject)ShopPreviewRules.BuildState(d, Sample(), Now)["ledger"]![0]!;
        Assert.Equal(new[] { "code", "n", "at", "id" }, line.Properties().Select(p => p.Name).ToArray());
        Assert.Equal(("buy", -360, "fx.bubble.heart"), ((string?)line["code"], (int)line["n"]!, (string?)line["id"]));
    }
}
