using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>One effect row the live server does not know yet (BRIEF "The preview wire").</summary>
internal sealed record PreviewEffect(string Id, string Slot, string Name, int Price,
    string? How = null, bool IsNew = false, bool Todo = false, bool Special = false);

/// <summary>An owned mark as the server body writes it.</summary>
internal sealed class PreviewOwned
{
    [JsonProperty("at")] public string At { get; set; } = "";
    [JsonProperty("paidSp")] public int PaidSp { get; set; }
}

internal sealed class PreviewLedgerLine
{
    [JsonProperty("t")] public string T { get; set; } = "";
    [JsonProperty("n")] public int N { get; set; }
}

internal sealed class PreviewReceipt
{
    [JsonProperty("idem")] public string Idem { get; set; } = "";
    [JsonProperty("itemId")] public string ItemId { get; set; } = "";
    [JsonProperty("paidSp")] public int PaidSp { get; set; }
    [JsonProperty("spBefore")] public int SpBefore { get; set; }
}

/// <summary>The whole preview, as kept in <c>shop-preview.json</c>. Its own wallet, never the real one.</summary>
internal sealed class ShopPreviewData
{
    [JsonProperty("v")] public int V { get; set; } = 1;
    [JsonProperty("sp")] public int Sp { get; set; }
    [JsonProperty("revision")] public long Revision { get; set; } = 1;
    [JsonProperty("owned")] public Dictionary<string, PreviewOwned> Owned { get; set; } = new(StringComparer.Ordinal);
    [JsonProperty("equip")] public Dictionary<string, string> Equip { get; set; } = new(StringComparer.Ordinal);
    [JsonProperty("mix")] public bool Mix { get; set; }
    /// <summary>The ISO week the gift was last opened in; null = never.</summary>
    [JsonProperty("giftWeek")] public string? GiftWeek { get; set; }
    /// <summary>Day 1 of the stamp card, yyyy-MM-dd.</summary>
    [JsonProperty("cardStart")] public string CardStart { get; set; } = "";
    [JsonProperty("stamped")] public List<int> Stamped { get; set; } = new();
    [JsonProperty("couponUsed")] public bool CouponUsed { get; set; }
    [JsonProperty("goal")] public string? Goal { get; set; }
    [JsonProperty("ledger")] public List<PreviewLedgerLine> Ledger { get; set; } = new();
    [JsonProperty("receipts")] public List<PreviewReceipt> Receipts { get; set; } = new();
}

/// <summary>A rule's answer: ok, or a refusal reason (the server's words where one fits).</summary>
internal readonly record struct PreviewOutcome(bool Ok, string? Reason, PreviewReceipt? Receipt = null)
{
    public static readonly PreviewOutcome Done = new(true, null);
    public static PreviewOutcome No(string reason) => new(false, reason);
}

/// <summary>
/// THE SHOP PREVIEW RULES (DEBUG preview, lane I): prices, owned, insufficient, the stamp card's coupon,
/// the weekly gift, one stamp a day, the goal, equip. Pure: no file, no network, no app state; every rule
/// takes the preview data and the time and changes only that data. <c>ShopPreviewBackend</c> keeps
/// the file and answers the page.
/// </summary>
internal static class ShopPreviewRules
{
    public const int StartSp = 410, GiftAmount = 25, CouponAt = 6, CardDays = 14, CouponPrice = 120;
    public const int LedgerMax = 6, ReceiptsMax = 20, SpCap = 9_999_999;
    public const string CatalogVersion = "preview-1";
    public const string TabEffects = "effects";
    public const string Twotone = "fx.fog.twotone";

    public static readonly string[] Everywhere = { "desktop", "web", "phone" };
    public static readonly string[] DesktopOnly = { "desktop" };

    /// <summary>The effect rows in shelf order (prices from the passed mockup).</summary>
    public static readonly IReadOnlyList<PreviewEffect> Effects = new PreviewEffect[]
    {
        new("fx.bubble.soap", "bubble", "Soap", 0, "the default"),
        new("fx.bubble.rose", "bubble", "Rose", 120, "tint"),
        new("fx.bubble.mint", "bubble", "Mint", 120, "tint"),
        new("fx.bubble.lilac", "bubble", "Lilac", 120, "tint"),
        new("fx.bubble.marble", "bubble", "Marble", 360, IsNew: true),
        new("fx.bubble.heart", "bubble", "Heart", 360, IsNew: true),
        new("fx.bubble.champagne", "bubble", "Champagne", 360),
        new("fx.bubble.spiral", "bubble", "Spiral", 730, IsNew: true),
        new("fx.bubble.disco", "bubble", "Disco", 730),
        new("fx.bubble.frost", "bubble", "Frost", 360),
        new("fx.bubble.gum", "bubble", "Bubblegum", 360, Todo: true),
        new("fx.bubble.latex", "bubble", "Latex", 730, Todo: true),
        new("fx.bubble.oil", "bubble", "Oil slick", 730, Todo: true),
        new("fx.card.plain", "card", "Plain", 0),
        new("fx.card.ink", "card", "Ink", 120),
        new("fx.card.check", "card", "Check", 120),
        new("fx.card.lace", "card", "Lace", 360),
        new("fx.behind.none", "behind", "Nothing", 0),
        new("fx.behind.whisper", "behind", "Whisper", 360, "a new word each flip", IsNew: true),
        new("fx.fog.pink", "fog", "Pink", 0),
        new("fx.fog.lilac", "fog", "Lilac", 120),
        new("fx.fog.mint", "fog", "Mint", 120),
        new("fx.fog.amber", "fog", "Amber", 360),
        new(Twotone, "fog", "Two-tone", 730, "two colours swirl", Special: true),
        new("fx.word.classic", "word", "Classic", 0, "fades"),
        new("fx.word.type", "word", "Typed", 120, "typewriter"),
        new("fx.word.chalk", "word", "Chalk", 360, "written on"),
        new("fx.word.drift", "word", "Drift", 360, "letters rise"),
        new("fx.word.echo", "word", "Echo", 360, "ghost copies"),
        new("fx.word.marquee", "word", "Marquee", 730, "lights up"),
    };

    public static PreviewEffect? Effect(string? id) => id == null ? null : Effects.FirstOrDefault(e => e.Id == id);

    public static string IsoWeek(DateTimeOffset now)
    {
        var d = now.DateTime;
        return string.Create(CultureInfo.InvariantCulture, $"{ISOWeek.GetYear(d):D4}-W{ISOWeek.GetWeekOfYear(d):D2}");
    }

    private static string Day(DateTimeOffset now) => now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Stamp(DateTimeOffset now) => now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>A first preview: 410 Sparkles, every free row owned and worn, the sample's own owned rows,
    /// the gift ready, stamps 1, 2 and 4 done on day 5, the Spiral as the goal, a short ledger.</summary>
    public static ShopPreviewData Seed(DateTimeOffset now, JObject? sample)
    {
        var d = new ShopPreviewData { Sp = StartSp, CardStart = Day(now.AddDays(-4)), Stamped = new() { 1, 2, 4 }, Goal = "fx.bubble.spiral" };
        foreach (var e in Effects.Where(e => e.Price == 0)) d.Owned[e.Id] = new PreviewOwned { At = Stamp(now), PaidSp = 0 };
        foreach (var row in SampleRows(sample))
            if (row["owned"] is JObject o && row["id"]?.Type == JTokenType.String)
                d.Owned[(string)row["id"]!] = new PreviewOwned { At = (string?)o["at"] ?? Stamp(now), PaidSp = o.Value<int?>("paidSp") ?? 0 };
        foreach (var slot in Prizes.WornLooks.EffectSlots) d.Equip[slot] = Prizes.WornLooks.DefaultOf(slot)!;
        d.Equip[Prizes.WornLooks.Fog2] = Prizes.WornLooks.DefaultOf(Prizes.WornLooks.Fog2)!;
        d.Ledger.Add(new PreviewLedgerLine { T = "Won at chess", N = 20 });
        d.Ledger.Add(new PreviewLedgerLine { T = "Session finished", N = 12 });
        d.Ledger.Add(new PreviewLedgerLine { T = "Rubber duck", N = -120 });
        return d;
    }

    private static IEnumerable<JObject> SampleRows(JObject? sample)
        => sample?["tabs"] is JArray tabs ? tabs.SelectTokens("[*].shelves[*].rows[*]").OfType<JObject>() : Enumerable.Empty<JObject>();

    /// <summary>Today's day on the stamp card (1 to 14). A card past day 14 starts again on day 1.</summary>
    public static int CardDay(ShopPreviewData d, DateTimeOffset now)
    {
        if (!DateTime.TryParseExact(d.CardStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start))
            start = now.Date;
        int day = (int)(now.Date - start.Date).TotalDays + 1;
        if (day > CardDays || day < 1)
        {
            d.CardStart = Day(now);
            d.Stamped.Clear();
            d.CouponUsed = false;
            day = 1;
        }
        return day;
    }

    public static bool CouponReady(ShopPreviewData d) => d.Stamped.Count >= CouponAt && !d.CouponUsed;

    public static bool GiftReady(ShopPreviewData d, DateTimeOffset now) => !string.Equals(d.GiftWeek, IsoWeek(now), StringComparison.Ordinal);

    /// <summary>The next Monday 00:00 after <paramref name="now"/>, in its offset.</summary>
    public static DateTimeOffset NextMonday(DateTimeOffset now)
    {
        int ahead = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (ahead == 0) ahead = 7;
        return new DateTimeOffset(now.Date.AddDays(ahead), now.Offset);
    }

    public static void Note(ShopPreviewData d, string text, int n)
    {
        d.Ledger.Insert(0, new PreviewLedgerLine { T = text, N = n });
        if (d.Ledger.Count > LedgerMax) d.Ledger.RemoveRange(LedgerMax, d.Ledger.Count - LedgerMax);
    }

    /// <summary>A gain the desktop banked, mirrored into the preview wallet. A run of the same source
    /// grows one ledger line instead of pushing the others out.</summary>
    public static void Credit(ShopPreviewData d, int amount, string text)
    {
        if (amount <= 0) return;
        d.Sp = (int)Math.Min(SpCap, (long)d.Sp + amount);
        if (d.Ledger.Count > 0 && d.Ledger[0].T == text && d.Ledger[0].N > 0) d.Ledger[0].N += amount;
        else Note(d, text, amount);
    }

    /// <summary>A row by id: an effect row, else a row of the bundled real tabs. Null = not in stock.</summary>
    public static (int Price, string Sale, string? Needs, string Name)? Row(JObject? sample, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (Effect(id) is { } e) return (e.Price, e.Todo ? "soon" : "on", null, e.Name);
        var row = SampleRows(sample).FirstOrDefault(r => (string?)r["id"] == id);
        if (row == null || row["priceSp"] is not JValue { Type: JTokenType.Integer } p) return null;
        return (p.Value<int>(), (string?)row["sale"] ?? "on", (string?)row["needs"], NameOf(id));
    }

    /// <summary>A plain name for a ledger line from a real row id (the page names real rows itself).</summary>
    internal static string NameOf(string id)
    {
        var last = id.Split('.').Last().Replace('-', ' ').Replace('_', ' ').Trim();
        return last.Length == 0 ? id : char.ToUpperInvariant(last[0]) + last[1..];
    }

    /// <summary>A buy. Same idem again = the same receipt. A coupon buy takes any 120 row free, once per card.</summary>
    public static PreviewOutcome Buy(ShopPreviewData d, JObject? sample, string? itemId, string? idem, bool coupon, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(idem) || string.IsNullOrEmpty(itemId)) return PreviewOutcome.No("bad_input");
        var replay = d.Receipts.FirstOrDefault(r => r.Idem == idem);
        if (replay != null) return replay.ItemId == itemId ? new(true, null, replay) : PreviewOutcome.No("idem_mismatch");
        if (Row(sample, itemId) is not { } row || row.Sale != "on") return PreviewOutcome.No("not_in_stock");
        if (d.Owned.ContainsKey(itemId)) return PreviewOutcome.No("owned");
        if (row.Needs == "discord") return PreviewOutcome.No("discord_required");
        CardDay(d, now);
        if (coupon && (!CouponReady(d) || row.Price != CouponPrice)) return PreviewOutcome.No("unavailable");
        int price = coupon ? 0 : row.Price;
        if (d.Sp < price) return PreviewOutcome.No("insufficient");

        var receipt = new PreviewReceipt { Idem = idem, ItemId = itemId, PaidSp = price, SpBefore = d.Sp };
        d.Sp -= price;
        d.Owned[itemId] = new PreviewOwned { At = Stamp(now), PaidSp = price };
        d.Revision++;
        if (coupon) d.CouponUsed = true;
        if (d.Goal == itemId) d.Goal = null;
        Note(d, coupon ? "Stamp card: " + row.Name : row.Name, -price);
        d.Receipts.Insert(0, receipt);
        if (d.Receipts.Count > ReceiptsMax) d.Receipts.RemoveRange(ReceiptsMax, d.Receipts.Count - ReceiptsMax);
        return new(true, null, receipt);
    }

    /// <summary>
    /// Wear or take off. Slots bubble, card, behind, fog, word take an owned row of that slot (null = the
    /// slot's free row). Two-tone is a switch, not a colour: <c>fog</c> or <c>mix</c> with its id turns the
    /// mix on, <c>mix</c> with null turns it off. <c>fog2</c> is the mix's second colour: any fog colour,
    /// once Two-tone is owned (the mockup offers every colour there).
    /// </summary>
    public static PreviewOutcome Equip(ShopPreviewData d, string? slot, string? itemId)
    {
        var e = Effect(itemId);
        if (itemId != null && e == null) return PreviewOutcome.No("bad_input");
        if (e is { Todo: true }) return PreviewOutcome.No("unavailable");
        switch (slot)
        {
            case Prizes.WornLooks.Mix:
                if (itemId == null) { d.Mix = false; return PreviewOutcome.Done; }
                if (itemId != Twotone) return PreviewOutcome.No("bad_input");
                if (!d.Owned.ContainsKey(Twotone)) return PreviewOutcome.No("not_owned");
                d.Mix = true;
                return PreviewOutcome.Done;
            case Prizes.WornLooks.Fog2:
                if (e != null && (e.Slot != "fog" || e.Special)) return PreviewOutcome.No("bad_input");
                if (!d.Owned.ContainsKey(Twotone)) return PreviewOutcome.No("not_owned");
                d.Equip[slot] = itemId ?? Prizes.WornLooks.DefaultOf(slot)!;
                return PreviewOutcome.Done;
        }
        if (slot == null || !Prizes.WornLooks.EffectSlots.Contains(slot)) return PreviewOutcome.No("bad_input");
        if (e == null) { d.Equip[slot] = Prizes.WornLooks.DefaultOf(slot)!; return PreviewOutcome.Done; }
        if (e.Slot != slot) return PreviewOutcome.No("bad_input");
        if (!d.Owned.ContainsKey(e.Id)) return PreviewOutcome.No("not_owned");
        if (e.Special) d.Mix = true;
        else d.Equip[slot] = e.Id;
        return PreviewOutcome.Done;
    }

    /// <summary>The Monday gift: once per ISO week.</summary>
    public static PreviewOutcome OpenGift(ShopPreviewData d, DateTimeOffset now)
    {
        if (!GiftReady(d, now)) return PreviewOutcome.No("not_ready");
        d.GiftWeek = IsoWeek(now);
        Credit(d, GiftAmount, "Monday gift");
        return PreviewOutcome.Done;
    }

    /// <summary>One stamp a day, on today's day of the card.</summary>
    public static PreviewOutcome StampToday(ShopPreviewData d, DateTimeOffset now)
    {
        int day = CardDay(d, now);
        if (d.Stamped.Contains(day)) return PreviewOutcome.No("stamped");
        d.Stamped.Add(day);
        d.Stamped.Sort();
        return PreviewOutcome.Done;
    }

    /// <summary>Save for an item (null clears). Unknown ids and owned ones are refused.</summary>
    public static PreviewOutcome SetGoal(ShopPreviewData d, JObject? sample, string? itemId)
    {
        if (itemId == null) { d.Goal = null; return PreviewOutcome.Done; }
        if (Row(sample, itemId) == null) return PreviewOutcome.No("not_in_stock");
        if (d.Owned.ContainsKey(itemId)) return PreviewOutcome.No("owned");
        d.Goal = itemId;
        return PreviewOutcome.Done;
    }

    /// <summary>Where a real row shows: the desk and Rain live in the desktop app, the rest everywhere.</summary>
    public static string[] PlacesOf(string id)
        => id.StartsWith("desk.", StringComparison.Ordinal) || id.StartsWith("rain.", StringComparison.Ordinal) ? DesktopOnly : Everywhere;

    /// <summary>The <c>shop-state</c> body: the server's shape plus the preview wire's additions.</summary>
    public static JObject BuildState(ShopPreviewData d, JObject? sample, DateTimeOffset now)
    {
        int day = CardDay(d, now);
        var grants = new SortedSet<string>(StringComparer.Ordinal);
        JToken Owned(string id) => d.Owned.TryGetValue(id, out var o) ? new JObject { ["at"] = o.At, ["paidSp"] = o.PaidSp } : JValue.CreateNull();

        var shelves = new JArray();
        foreach (var slot in Prizes.WornLooks.EffectSlots)
        {
            var rows = new JArray();
            foreach (var e in Effects.Where(e => e.Slot == slot))
            {
                var grant = "cos." + e.Id;
                if (d.Owned.ContainsKey(e.Id)) grants.Add(grant);
                var row = new JObject
                {
                    ["id"] = e.Id, ["tier"] = e.Price >= 730 ? "L" : e.Price >= 360 ? "M" : "S", ["priceSp"] = e.Price,
                    ["grants"] = new JArray(grant), ["owned"] = Owned(e.Id), ["sale"] = e.Todo ? "soon" : "on",
                    ["slot"] = e.Slot, ["name"] = e.Name, ["places"] = new JArray(DesktopOnly),
                };
                if (e.How != null) row["how"] = e.How;
                if (e.IsNew) row["isNew"] = true;
                if (e.Todo) row["todo"] = true;
                if (e.Special) row["special"] = true;
                rows.Add(row);
            }
            shelves.Add(new JObject { ["rows"] = rows });
        }
        var tabs = new JArray(new JObject { ["id"] = TabEffects, ["shelves"] = shelves });
        if (sample?["tabs"] is JArray real)
        {
            var copy = (JArray)real.DeepClone();
            foreach (var row in copy.SelectTokens("[*].shelves[*].rows[*]").OfType<JObject>())
            {
                if (row["id"]?.Type != JTokenType.String) continue;
                var id = (string)row["id"]!;
                row["owned"] = Owned(id);
                row["places"] = new JArray(PlacesOf(id));
                if (d.Owned.ContainsKey(id) && row["grants"] is JArray g) foreach (var s in g.Values<string>()) if (s != null) grants.Add(s);
            }
            foreach (var t in copy) tabs.Add(t);
        }

        var equip = new JObject();
        foreach (var slot in Prizes.WornLooks.EffectSlots) equip[slot] = d.Equip.TryGetValue(slot, out var v) ? v : Prizes.WornLooks.DefaultOf(slot);
        equip[Prizes.WornLooks.Fog2] = d.Equip.TryGetValue(Prizes.WornLooks.Fog2, out var f2) ? f2 : Prizes.WornLooks.DefaultOf(Prizes.WornLooks.Fog2);
        equip[Prizes.WornLooks.Mix] = d.Mix;

        return new JObject
        {
            ["ok"] = true,
            ["preview"] = true,
            ["sp"] = d.Sp,
            ["catalogVersion"] = CatalogVersion,
            ["week"] = IsoWeek(now),
            ["tier"] = 2,
            ["prizes"] = new JObject { ["revision"] = d.Revision, ["grants"] = new JArray(grants) },
            ["tabs"] = tabs,
            ["fresh"] = new JArray(Effects.Where(e => e.IsNew).Select(e => e.Id)),
            ["equip"] = equip,
            ["gift"] = new JObject { ["ready"] = GiftReady(d, now), ["amount"] = GiftAmount, ["next"] = NextMonday(now).ToString("o", CultureInfo.InvariantCulture) },
            ["stamps"] = new JObject
            {
                ["day"] = day, ["stamped"] = new JArray(d.Stamped), ["couponAt"] = CouponAt,
                ["couponReady"] = CouponReady(d), ["couponUsed"] = d.CouponUsed,
            },
            ["goal"] = d.Goal,
            ["ledger"] = new JArray(d.Ledger.Take(LedgerMax).Select(l => new JObject { ["t"] = l.T, ["n"] = l.N })),
        };
    }
}
