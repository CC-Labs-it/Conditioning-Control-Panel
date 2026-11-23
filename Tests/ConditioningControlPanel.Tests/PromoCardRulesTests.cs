using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConditioningControlPanel.Services.Announcements;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The client's copy of the promo limits (handoff-1123/BF-PROMO-BRIEF.md): a promo that breaks any
/// one of them is dropped whole, so the old AnnouncementPopup shows instead of half a card.
/// </summary>
public class PromoCardRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly long EndsMs = new DateTimeOffset(2026, 11, 30, 23, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static JsonObject Sample() => JsonNode.Parse($$"""
        {
          "layout": "tc-paper",
          "tag": "BLACK FRIDAY",
          "hook": "Whirlpool, meet Steam.",
          "code": "BF50",
          "rows": [ { "label": "50% off", "sub": "Basic or Prime" }, { "label": "Prime half price", "sub": "first month or year" } ],
          "stickers": [ "https://cclabs.app/promo/bf50/effects.jpg", "https://cdn.cclabs.app/promo/bf50/effects-b.jpg" ],
          "ends_at": {{EndsMs}},
          "cta": { "label": "Half off on Patreon", "url": "https://www.patreon.com/CodeBambi" },
          "extra": "dropped"
        }
        """)!.AsObject();

    private static PromoCard? Read(JsonNode? node, long? expiresAt = null)
    {
        if (node == null) return PromoCardRules.TryRead(null, Now, expiresAt);
        using var doc = JsonDocument.Parse(node.ToJsonString());
        return PromoCardRules.TryRead(doc.RootElement.Clone(), Now, expiresAt);
    }

    private static PromoCard? With(Action<JsonObject> change)
    {
        var o = Sample();
        change(o);
        return Read(o);
    }

    [Fact]
    public void The_contract_sample_reads_whole()
    {
        var p = Read(Sample());
        Assert.NotNull(p);
        Assert.Equal("BLACK FRIDAY", p!.Tag);
        Assert.Equal("Whirlpool, meet Steam.", p.Hook);
        Assert.Equal("BF50", p.Code);
        Assert.Equal(2, p.Rows.Count);
        Assert.Equal("first month or year", p.Rows[1].Sub);
        Assert.Equal(2, p.Stickers.Count);
        Assert.Equal(EndsMs, p.EndsAt!.Value.ToUnixTimeMilliseconds());
        Assert.Equal("https://www.patreon.com/CodeBambi", p.Cta!.Url.AbsoluteUri);
        Assert.False(p.HasEnded(Now));
        Assert.True(p.HasEnded(DateTimeOffset.FromUnixTimeMilliseconds(EndsMs)));
    }

    [Fact]
    public void Absent_or_not_an_object_is_no_promo()
    {
        Assert.Null(Read(null));
        Assert.Null(Read(JsonValue.Create("tc-paper")));
        Assert.Null(Read(new JsonArray()));
    }

    [Fact]
    public void Tag_hook_sub_and_stickers_are_optional()
    {
        var p = With(o =>
        {
            o.Remove("tag"); o.Remove("hook"); o.Remove("stickers");
            o["rows"] = new JsonArray(new JsonObject { ["label"] = "50% off" });
        });
        Assert.NotNull(p);
        Assert.Null(p!.Tag);
        Assert.Null(p.Hook);
        Assert.Empty(p.Stickers);
        Assert.Null(p.Rows[0].Sub);
    }

    public static TheoryData<string> Breaks() => new()
    {
        "layout other", "layout missing", "tag long", "hook long", "tag number",
        "code short", "code long", "code lower", "code symbol", "code number",
        "rows none", "rows missing", "rows four", "row label long", "row sub long", "row label missing", "rows object",
        "stickers four", "sticker http", "sticker host", "sticker lookalike host", "sticker long", "sticker quote", "sticker port", "sticker userinfo", "sticker not string",
        "ends past", "ends now", "ends string",
        "cta not object", "cta label long", "cta label missing", "cta http", "cta host", "cta javascript", "cta space",
    };

    [Theory]
    [MemberData(nameof(Breaks))]
    public void Any_broken_limit_drops_the_whole_promo(string what)
    {
        var p = With(o =>
        {
            JsonObject Row(int i) => o["rows"]![i]!.AsObject();
            JsonObject Cta() => o["cta"]!.AsObject();
            switch (what)
            {
                case "layout other": o["layout"] = "dark"; break;
                case "layout missing": o.Remove("layout"); break;
                case "tag long": o["tag"] = new string('T', 25); break;
                case "hook long": o["hook"] = new string('h', 61); break;
                case "tag number": o["tag"] = 5; break;
                case "code short": o["code"] = "BF"; break;
                case "code long": o["code"] = new string('B', 17); break;
                case "code lower": o["code"] = "bf50"; break;
                case "code symbol": o["code"] = "BF-50"; break;
                case "code number": o["code"] = 50; break;
                case "rows none": o["rows"] = new JsonArray(); break;
                case "rows missing": o.Remove("rows"); break;
                case "rows four":
                    o["rows"] = new JsonArray(new JsonObject { ["label"] = "a" }, new JsonObject { ["label"] = "b" },
                        new JsonObject { ["label"] = "c" }, new JsonObject { ["label"] = "d" });
                    break;
                case "row label long": Row(0)["label"] = new string('x', 25); break;
                case "row sub long": Row(0)["sub"] = new string('x', 33); break;
                case "row label missing": Row(0).Remove("label"); break;
                case "rows object": o["rows"] = new JsonObject(); break;
                case "stickers four":
                    o["stickers"] = new JsonArray("https://cclabs.app/a.jpg", "https://cclabs.app/b.jpg", "https://cclabs.app/c.jpg", "https://cclabs.app/d.jpg");
                    break;
                case "sticker http": o["stickers"] = new JsonArray("http://cclabs.app/a.jpg"); break;
                case "sticker host": o["stickers"] = new JsonArray("https://example.com/a.jpg"); break;
                case "sticker lookalike host": o["stickers"] = new JsonArray("https://cclabs.app.evil.com/a.jpg"); break;
                case "sticker long": o["stickers"] = new JsonArray("https://cclabs.app/" + new string('a', 490)); break;
                case "sticker quote": o["stickers"] = new JsonArray("https://cclabs.app/a\".jpg"); break;
                case "sticker port": o["stickers"] = new JsonArray("https://cclabs.app:8443/a.jpg"); break;
                case "sticker userinfo": o["stickers"] = new JsonArray("https://me@cclabs.app/a.jpg"); break;
                case "sticker not string": o["stickers"] = new JsonArray(5); break;
                case "ends past": o["ends_at"] = Now.AddMinutes(-1).ToUnixTimeMilliseconds(); break;
                case "ends now": o["ends_at"] = Now.ToUnixTimeMilliseconds(); break;
                case "ends string": o["ends_at"] = EndsMs.ToString(); break;
                case "cta not object": o["cta"] = "https://www.patreon.com/CodeBambi"; break;
                case "cta label long": Cta()["label"] = new string('c', 29); break;
                case "cta label missing": Cta().Remove("label"); break;
                case "cta http": Cta()["url"] = "http://www.patreon.com/CodeBambi"; break;
                case "cta host": Cta()["url"] = "https://patreon.com.evil.com/x"; break;
                case "cta javascript": Cta()["url"] = "javascript:alert(1)"; break;
                case "cta space": Cta()["url"] = "https://www.patreon.com/Code Bambi"; break;
                default: throw new ArgumentException(what);
            }
        });
        Assert.Null(p);
    }

    [Fact]
    public void Only_layout_and_rows_are_required()
    {
        var p = Read(new JsonObject { ["layout"] = "tc-paper", ["rows"] = new JsonArray(new JsonObject { ["label"] = "50% off" }) });
        Assert.NotNull(p);
        Assert.Null(p!.Code);
        Assert.Null(p.Cta);
        Assert.Null(p.EndsAt);
        Assert.False(p.HasEnded(Now.AddYears(5)));
    }

    [Theory]
    [InlineData("code")]
    [InlineData("cta")]
    [InlineData("ends_at")]
    public void A_missing_or_null_optional_field_hides_its_part_and_keeps_the_card(string field)
    {
        var missing = With(o => o.Remove(field));
        var nulled = With(o => o[field] = null);
        foreach (var p in new[] { missing, nulled })
        {
            Assert.NotNull(p);
            Assert.Equal(2, p!.Rows.Count);
            Assert.True(field != "code" || p.Code == null);
            Assert.True(field != "cta" || p.Cta == null);
            Assert.True(field != "ends_at" || p.EndsAt == null);
        }
    }

    [Fact]
    public void Every_allowed_host_passes()
    {
        foreach (var host in PromoCardRules.StickerHosts)
            Assert.NotNull(PromoCardRules.SafeHttps($"https://{host}/p.jpg", PromoCardRules.StickerHosts));
        foreach (var host in PromoCardRules.CtaHosts)
            Assert.NotNull(PromoCardRules.SafeHttps($"https://{host}/CodeBambi", PromoCardRules.CtaHosts));
        Assert.NotNull(PromoCardRules.SafeHttps("https://WWW.Patreon.com/CodeBambi", PromoCardRules.CtaHosts));
    }

    [Fact]
    public void Ends_after_the_announcement_expires_is_dropped_when_the_reply_says_so()
    {
        Assert.Null(Read(Sample(), expiresAt: EndsMs - 1));
        Assert.NotNull(Read(Sample(), expiresAt: EndsMs));
        Assert.NotNull(Read(Sample(), expiresAt: null));
    }

    [Theory]
    [InlineData(2 * 24 * 60 + 4 * 60, "2d 4h")]
    [InlineData(4 * 60 + 12, "4h 12m")]
    [InlineData(12, "12m")]
    [InlineData(60, "1h 0m")]
    [InlineData(24 * 60, "1d 0h")]
    public void The_countdown_shows_two_units_at_most(int minutes, string expected)
    {
        Assert.Equal(expected, PromoCountdown.Format(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void The_last_minute_reads_one_and_the_end_reads_nothing()
    {
        Assert.Equal("1m", PromoCountdown.Format(TimeSpan.FromSeconds(20)));
        Assert.Equal("", PromoCountdown.Format(TimeSpan.Zero));
        Assert.Equal("", PromoCountdown.Format(TimeSpan.FromSeconds(-5)));
    }
}
