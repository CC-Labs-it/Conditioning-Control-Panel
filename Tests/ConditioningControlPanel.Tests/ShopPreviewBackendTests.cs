using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.BubbleCore;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.PrizeCounter;
using ConditioningControlPanel.Services.Prizes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The DEBUG shop preview backend: every op over the preview file, the file across restarts,
/// and the worn looks it feeds (lane I, BRIEF "The preview wire").</summary>
[Collection("LookWardrobe")]   // both raise WornLooks.Changed: never in parallel
public class ShopPreviewBackendTests
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

    private static string TempFile() => Path.Combine(Path.GetTempPath(), "ccp-shop-preview-" + Guid.NewGuid().ToString("N"), ShopPreviewBackend.FileName);

    [Fact]
    public void The_backend_answers_every_op_and_keeps_the_file()
    {
        var path = TempFile();
        try
        {
            var b = new ShopPreviewBackend(path, Sample(), () => Now);
            Assert.True((bool)b.Handle("shop-state", null, null)["preview"]!);
            Assert.True(File.Exists(path));
            var receipt = b.Handle("shop-buy", new JObject { ["itemId"] = "fx.bubble.champagne" }, "x1");
            Assert.True((bool)receipt["ok"]!);
            Assert.Equal(360, (int)receipt["paidSp"]!);
            Assert.Equal(50, (int)receipt["sp"]!);
            Assert.Equal("fx.bubble.champagne", (string?)receipt["itemId"]);
            Assert.NotNull(receipt["tabs"]);   // a fresh state rides with the receipt
            var poor = b.Handle("shop-buy", new JObject { ["itemId"] = "fx.card.lace" }, "x2");
            Assert.Equal("insufficient", (string?)poor["reason"]);
            Assert.Equal(50, (int)poor["sp"]!);
            Assert.Equal("fx.bubble.champagne", (string?)b.Handle("shop-equip", new JObject { ["slot"] = "bubble", ["itemId"] = "fx.bubble.champagne" }, null)["equip"]!["bubble"]);
            Assert.True((bool)b.Handle("shop-gift", new JObject(), null)["ok"]!);
            Assert.True((bool)b.Handle("shop-stamp", new JObject(), null)["ok"]!);
            Assert.Null(b.Handle("shop-goal", new JObject { ["itemId"] = null }, null)["goal"]!.Value<string>());
            Assert.Equal("bad_input", (string?)b.Handle("shop-wat", null, null)["reason"]);

            // A new backend over the same file reads the same preview.
            var again = new ShopPreviewBackend(path, Sample(), () => Now);
            Assert.Equal(75, again.Sp);
            Assert.Equal("fx.bubble.champagne", again.Worn("bubble"));
            Assert.Equal(new[] { 1, 2, 4, 5 }, again.Handle("shop-state", null, null)["stamps"]!["stamped"]!.Values<int>().ToArray());
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void A_broken_file_starts_a_fresh_preview()
    {
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ not json");
            var b = new ShopPreviewBackend(path, Sample(), () => Now);
            Assert.Equal(ShopPreviewRules.StartSp, b.Sp);
            Assert.Equal(ShopPreviewRules.StartSp, JsonConvert.DeserializeObject<ShopPreviewData>(File.ReadAllText(path))!.Sp);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void Worn_looks_read_the_preview_and_say_when_something_changes()
    {
        var path = TempFile();
        var was = WornLooks.Source;
        string? heard = null;
        void Listen(string slot) => heard ??= slot;
        try
        {
            WornLooks.Source = null;
            Assert.Equal("fx.bubble.soap", WornLooks.Get(WornLooks.Bubble));
            Assert.Equal("fx.fog.pink", WornLooks.Get(WornLooks.Fog));
            Assert.False(WornLooks.MixOn);

            var b = new ShopPreviewBackend(path, Sample(), () => Now);
            b.Credit(400, ShopPreviewRules.CodeLevel);
            b.Handle("shop-buy", new JObject { ["itemId"] = "fx.bubble.champagne" }, "w1");
            WornLooks.Source = b.Worn;
            WornLooks.Changed += Listen;
            b.Handle("shop-equip", new JObject { ["slot"] = "bubble", ["itemId"] = "fx.bubble.champagne" }, null);
            Assert.Equal("bubble", heard);
            Assert.Equal("fx.bubble.champagne", WornLooks.Get(WornLooks.Bubble));
            Assert.Equal(BubbleSkinIds.Champagne, WornLooks.BubbleSkin());
            Assert.Equal("fx.card.plain", WornLooks.Get(WornLooks.Card));
        }
        finally
        {
            WornLooks.Changed -= Listen;
            WornLooks.Source = was;
            try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { }
        }
    }

    [Fact]
    public void A_file_that_owns_and_wears_the_gone_fog_colours_loads_cleanly()
    {
        // The single fog colours left the shop (owner, 2026-11-17): only Two-tone is sold.
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ \"v\": 1, \"sp\": 500, \"cardStart\": \"2026-11-13\", \"stamped\": [1], \"mix\": true,"
                + " \"owned\": { \"fx.fog.pink\": { \"at\": \"x\", \"paidSp\": 0 }, \"fx.fog.amber\": { \"at\": \"x\", \"paidSp\": 360 },"
                + " \"fx.fog.twotone\": { \"at\": \"x\", \"paidSp\": 730 } },"
                + " \"equip\": { \"fog\": \"fx.fog.amber\", \"fog2\": \"fx.fog.mint\" }, \"goal\": \"fx.fog.lilac\" }");
            var b = new ShopPreviewBackend(path, Sample(), () => Now);
            Assert.Equal("fx.fog.pink", b.Worn("fog"));            // the worn colour falls back to the user's own
            Assert.Equal("fx.fog.mint", b.Worn("fog2"));           // still a palette colour: kept
            Assert.Equal(ShopPreviewRules.Twotone, b.Worn("mix"));
            var s = b.Handle("shop-state", null, null);
            Assert.Null(s["goal"]!.Value<string>());
            var fog = s.SelectTokens("tabs[0].shelves[*].rows[*]").Where(r => (string?)r["slot"] == "fog").Select(r => (string?)r["id"]).ToArray();
            Assert.Equal(new[] { ShopPreviewRules.Twotone }, fog);
            Assert.Equal("not_in_stock", (string?)b.Handle("shop-buy", new JObject { ["itemId"] = "fx.fog.lilac" }, "f1")["reason"]);
            Assert.Equal("fx.fog.sky", (string?)b.Handle("shop-equip", new JObject { ["slot"] = "fog2", ["itemId"] = "fx.fog.sky" }, null)["equip"]!["fog2"]);
            Assert.Equal("bad_input", (string?)b.Handle("shop-equip", new JObject { ["slot"] = "fog", ["itemId"] = "fx.fog.amber" }, null)["reason"]);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void A_file_that_still_wears_a_gone_tint_falls_back_to_soap()
    {
        // The Rose, Mint and Lilac bubble tints were taken out of the shop (owner, 2026-11-17).
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ \"v\": 1, \"sp\": 90, \"cardStart\": \"2026-11-13\", \"stamped\": [1],"
                + " \"owned\": { \"fx.bubble.soap\": { \"at\": \"x\", \"paidSp\": 0 }, \"fx.bubble.rose\": { \"at\": \"x\", \"paidSp\": 120 } },"
                + " \"equip\": { \"bubble\": \"fx.bubble.rose\", \"fog\": \"fx.fog.mint\", \"fog2\": \"fx.bubble.lilac\" },"
                + " \"goal\": \"fx.bubble.mint\" }");
            var b = new ShopPreviewBackend(path, Sample(), () => Now);
            Assert.Equal(90, b.Sp);
            Assert.Equal("fx.bubble.soap", b.Worn("bubble"));
            Assert.Equal("fx.fog.pink", b.Worn("fog"));            // fog colours are not sold any more: back to the user's own
            Assert.Equal("fx.fog.lilac", b.Worn("fog2"));
            Assert.Equal("fx.card.plain", b.Worn("card"));
            var s = b.Handle("shop-state", null, null);
            Assert.Equal("fx.bubble.soap", (string?)s["equip"]!["bubble"]);
            Assert.Null(s["goal"]!.Value<string>());
            Assert.DoesNotContain("cos.fx.bubble.rose", s["prizes"]!["grants"]!.Values<string>());
            Assert.DoesNotContain(s.SelectTokens("tabs[*].shelves[*].rows[*].id").Values<string>(), id => id!.StartsWith("fx.bubble.rose"));
            Assert.Equal("not_in_stock", (string?)b.Handle("shop-buy", new JObject { ["itemId"] = "fx.bubble.lilac" }, "t1")["reason"]);
            Assert.Equal("bad_input", (string?)b.Handle("shop-equip", new JObject { ["slot"] = "bubble", ["itemId"] = "fx.bubble.mint" }, null)["reason"]);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }

    [Fact]
    public void Desktop_credits_get_a_ledger_code_never_words()
    {
        Assert.Equal("level", ShopPreviewBackend.CreditCode(SparklePointSource.LevelUp));
        Assert.Equal("pops", ShopPreviewBackend.CreditCode(SparklePointSource.BubbleMilestone));
        Assert.Equal("play", ShopPreviewBackend.CreditCode(SparklePointSource.Place));
    }

    [Fact]
    public void A_file_from_the_old_stamp_rules_loads_as_the_one_card()
    {
        // Written by the earlier preview: English ledger lines, a card that would have restarted after day 14.
        var path = TempFile();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{ \"v\": 1, \"sp\": 300, \"cardStart\": \"2026-10-20\", \"stamped\": [1, 2, 3, 4, 5, 6, 6, 19],"
                + " \"couponUsed\": false, \"ledger\": [ { \"t\": \"Monday gift\", \"n\": 25 }, { \"t\": \"Rubber duck\", \"n\": -120 } ],"
                + " \"receipts\": null, \"equip\": null, \"owned\": null }");
            var b = new ShopPreviewBackend(path, Sample(), () => Now);
            var s = b.Handle("shop-state", null, null);
            Assert.True((bool)s["ok"]!);
            var stamps = (JObject)s["stamps"]!;
            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, stamps["stamped"]!.Values<int>().ToArray());
            Assert.Equal((14, true, true), ((int)stamps["day"]!, (bool)stamps["done"]!, (bool)stamps["couponReady"]!));
            Assert.Equal("Monday gift", (string?)s["ledger"]![0]!["t"]);
            Assert.Equal("done", (string?)b.Handle("shop-stamp", new JObject(), null)["reason"]);
            var claim = b.Handle("shop-buy", new JObject { ["itemId"] = "fx.card.ink", ["coupon"] = true }, "old-1");
            Assert.True((bool)claim["ok"]!);
            Assert.Equal("coupon", (string?)claim["ledger"]![0]!["code"]);
            Assert.Equal("not_ready", (string?)b.Handle("shop-buy", new JObject { ["itemId"] = "fx.card.check", ["coupon"] = true }, "old-2")["reason"]);

            File.WriteAllText(path, "{ \"v\": 1, \"sp\": 5, \"stamped\": null, \"ledger\": null }");
            var bare = new ShopPreviewBackend(path, Sample(), () => Now).Handle("shop-state", null, null);
            Assert.Equal((1, false), ((int)bare["stamps"]!["day"]!, (bool)bare["stamps"]!["done"]!));
            Assert.Empty((JArray)bare["ledger"]!);
        }
        finally { try { Directory.Delete(Path.GetDirectoryName(path)!, true); } catch { } }
    }
}
