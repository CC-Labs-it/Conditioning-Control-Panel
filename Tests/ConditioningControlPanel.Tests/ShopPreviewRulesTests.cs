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
    public void The_coupon_takes_one_120_row_free_at_six_stamps()
    {
        var d = Fresh();
        Assert.Equal("unavailable", ShopPreviewRules.Buy(d, Sample(), "fx.card.ink", "c0", true, Now).Reason);
        d.Stamped.AddRange(new[] { 3, 5, 6 });
        Assert.True(ShopPreviewRules.CouponReady(d));
        Assert.Equal("unavailable", ShopPreviewRules.Buy(d, Sample(), "fx.card.lace", "c1", true, Now).Reason);   // 360
        var r = ShopPreviewRules.Buy(d, Sample(), "fx.card.ink", "c2", true, Now);
        Assert.True(r.Ok);
        Assert.Equal(0, r.Receipt!.PaidSp);
        Assert.Equal(410, d.Sp);
        Assert.True(d.CouponUsed);
        Assert.Equal("unavailable", ShopPreviewRules.Buy(d, Sample(), "fx.card.check", "c3", true, Now).Reason);
    }

    [Fact]
    public void The_gift_opens_once_a_week()
    {
        var d = Fresh();
        Assert.True(ShopPreviewRules.OpenGift(d, Now).Ok);
        Assert.Equal(435, d.Sp);
        Assert.Equal("Monday gift", d.Ledger[0].T);
        Assert.Equal("not_ready", ShopPreviewRules.OpenGift(d, Now.AddDays(5)).Reason);   // Sunday, same week
        Assert.True(ShopPreviewRules.OpenGift(d, Now.AddDays(6)).Ok);                      // Monday
        Assert.Equal(460, d.Sp);
    }

    [Fact]
    public void One_stamp_a_day_and_the_card_starts_again_after_day_14()
    {
        var d = Fresh();
        Assert.True(ShopPreviewRules.StampToday(d, Now).Ok);
        Assert.Equal(new[] { 1, 2, 4, 5 }, d.Stamped);
        Assert.Equal("stamped", ShopPreviewRules.StampToday(d, Now.AddHours(8)).Reason);
        Assert.True(ShopPreviewRules.StampToday(d, Now.AddDays(1)).Ok);
        Assert.Equal(1, ShopPreviewRules.CardDay(d, Now.AddDays(10)));   // day 15: a fresh card
        Assert.Empty(d.Stamped);
    }

    [Fact]
    public void The_goal_takes_a_row_not_owned_and_a_buy_clears_it()
    {
        var d = Fresh();
        Assert.Equal("owned", ShopPreviewRules.SetGoal(d, Sample(), "fx.bubble.soap").Reason);
        Assert.Equal("not_in_stock", ShopPreviewRules.SetGoal(d, Sample(), "nope").Reason);
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
        Assert.Equal("fx.fog.pink", d.Equip["fog"]);   // the first colour stays
        Assert.True(ShopPreviewRules.Equip(d, "fog2", "fx.fog.amber").Ok);   // any colour, owned or not
        Assert.Equal("bad_input", ShopPreviewRules.Equip(d, "fog2", ShopPreviewRules.Twotone).Reason);
        Assert.True(ShopPreviewRules.Equip(d, "mix", null).Ok);
        Assert.False(d.Mix);
        Assert.True(ShopPreviewRules.Equip(d, "mix", ShopPreviewRules.Twotone).Ok);
        Assert.True(d.Mix);
    }

    [Fact]
    public void Credits_grow_one_ledger_line_per_run_and_the_ledger_keeps_six()
    {
        var d = Fresh();
        ShopPreviewRules.Credit(d, 1, "Bubble pops");
        ShopPreviewRules.Credit(d, 1, "Bubble pops");
        ShopPreviewRules.Credit(d, 0, "Bubble pops");
        Assert.Equal(412, d.Sp);
        Assert.Equal(2, d.Ledger[0].N);
        for (int i = 0; i < 9; i++) ShopPreviewRules.Note(d, "x" + i, 1);
        Assert.Equal(ShopPreviewRules.LedgerMax, d.Ledger.Count);
        Assert.Equal("x8", d.Ledger[0].T);
    }
}
