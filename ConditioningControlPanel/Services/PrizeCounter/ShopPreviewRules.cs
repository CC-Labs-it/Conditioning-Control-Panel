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

/// <summary>One ledger line. The server sends fixed codes, never words (CCP-Server shop-ledger.js):
/// <c>buy</c> and <c>coupon</c> name their row in <c>id</c>, <c>gift</c> is the Monday gift. The preview also
/// mirrors desktop gains as <c>level</c>, <c>pops</c> and <c>play</c>. <c>t</c> is only an older preview
/// file's English line, read back as it was.</summary>
internal sealed class PreviewLedgerLine
{
    [JsonProperty("code", NullValueHandling = NullValueHandling.Ignore)] public string? Code { get; set; }
    [JsonProperty("n")] public int N { get; set; }
    [JsonProperty("at", NullValueHandling = NullValueHandling.Ignore)] public string? At { get; set; }
    [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)] public string? Id { get; set; }
    [JsonProperty("t", NullValueHandling = NullValueHandling.Ignore)] public string? T { get; set; }
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
    /// <summary>Day 1 of the stamp card (the day of the first stamp), yyyy-MM-dd; empty = no stamp yet.</summary>
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
/// <para>THE STAMP CARD follows the server (CCP-Server shop-stamps.js, owner 2026-11-17): ONE card per
/// account. Day 1 is the day of the first stamp, the card covers 14 days, one stamp a day, a missed day
/// stays empty. At 6 stamps the coupon buys one row of tier S (the cheapest priced tier: it follows the
/// tier, never a number). From day 15 the card is <c>done</c> for good: stamping answers <c>done</c>, no
/// new card, an earned unused coupon still works.</para>
/// </summary>
internal static class ShopPreviewRules
{
    public const int StartSp = 410, GiftAmount = 25, CouponAt = 6, CardDays = 14;
    /// <summary>The tier a coupon buys (server COUPON_TIER).</summary>
    public const string CouponTier = "S";
    /// <summary>Ledger codes: the server's three, then the preview's mirrored desktop gains.</summary>
    public const string CodeBuy = "buy", CodeCoupon = "coupon", CodeGift = "gift", CodeLevel = "level", CodePops = "pops", CodePlay = "play";
    public const int LedgerMax = 6, ReceiptsMax = 20, SpCap = 9_999_999;
    public const string CatalogVersion = "preview-1";
    public const string TabEffects = "effects";
    public const string Twotone = SuperCore.FogLooks.Twotone;

    public static readonly string[] Everywhere = { "desktop", "web", "phone" };
    public static readonly string[] DesktopOnly = { "desktop" };

    /// <summary>The effect rows in shelf order (prices from the passed mockup).</summary>
    public static readonly IReadOnlyList<PreviewEffect> Effects = new PreviewEffect[]
    {
        new("fx.bubble.soap", "bubble", "Soap", 0, "the default"),
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
        // The fog shelf sells ONE thing (owner 2026-11-17): the fog already wears the user's own filter colour, so
        // the single colours are gone; Two-tone adds a free second colour (slot fog2, FogLooks.Palette).
        new(Twotone, "fog", "Two-tone", 730, "your colour and a second swirl", Special: true),
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
        d.Ledger.Add(new PreviewLedgerLine { Code = CodePlay, N = 20, At = Stamp(now.AddHours(-1)) });
        d.Ledger.Add(new PreviewLedgerLine { Code = CodePops, N = 12, At = Stamp(now.AddHours(-3)) });
        d.Ledger.Add(new PreviewLedgerLine { Code = CodeBuy, N = -120, Id = "desk.toy.rubber-duck", At = Stamp(now.AddDays(-1)) });
        if (Row(sample, "desk.toy.rubber-duck") != null) d.Owned["desk.toy.rubber-duck"] = new PreviewOwned { At = Stamp(now.AddDays(-1)), PaidSp = 120 };
        return d;
    }

    /// <summary>A preview file from an older catalogue (the Rose, Mint and Lilac bubble tints are gone, and the
    /// single fog colours: a worn fog colour goes back to the user's own, fog2 keeps a palette colour):
    /// a worn row that is no longer a row of its slot puts back the slot's free row, and a goal that is no
    /// longer in stock is cleared. Owned marks for gone rows stay in the file and do nothing.
    /// A file from the old stamp rules (a card that restarted after day 14) reads as the one card: missing
    /// lists come back empty, stamps outside days 1 to 14 or twice are dropped, an unreadable day 1 means no
    /// card yet. Old English ledger lines stay as <c>t</c>; a line with neither code nor words goes.</summary>
    public static void Heal(ShopPreviewData d, JObject? sample)
    {
        d.Owned ??= new(StringComparer.Ordinal);
        d.Equip ??= new(StringComparer.Ordinal);
        d.Receipts ??= new();
        d.Ledger = (d.Ledger ?? new()).Where(l => l != null && (!string.IsNullOrEmpty(l.Code) || !string.IsNullOrEmpty(l.T))).Take(LedgerMax).ToList();
        d.Stamped = (d.Stamped ?? new()).Where(n => n >= 1 && n <= CardDays).Distinct().OrderBy(n => n).ToList();
        if (Start(d) == null) { d.CardStart = ""; d.Stamped.Clear(); }
        foreach (var slot in Prizes.WornLooks.EffectSlots.Append(Prizes.WornLooks.Fog2))
        {
            bool kept = d.Equip.TryGetValue(slot, out var id) && (slot == Prizes.WornLooks.Fog2
                ? SuperCore.FogLooks.IsPalette(id)                          // a second colour still on the palette
                : Effect(id) is { Special: false } e && e.Slot == slot);
            if (!kept) d.Equip[slot] = Prizes.WornLooks.DefaultOf(slot)!;
        }
        if (d.Goal != null && Row(sample, d.Goal) == null) d.Goal = null;
    }

    private static IEnumerable<JObject> SampleRows(JObject? sample)
        => sample?["tabs"] is JArray tabs ? tabs.SelectTokens("[*].shelves[*].rows[*]").OfType<JObject>() : Enumerable.Empty<JObject>();

    /// <summary>Day 1 of the card, or null before the first stamp.</summary>
    private static DateTime? Start(ShopPreviewData d)
        => DateTime.TryParseExact(d.CardStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var s) ? s.Date : null;

    /// <summary>The card's raw day number for <paramref name="now"/> (1 before the first stamp; past 14 once over).</summary>
    private static int RawDay(ShopPreviewData d, DateTimeOffset now)
        => Start(d) is { } s ? (int)(now.Date - s).TotalDays + 1 : 1;

    /// <summary>Today's day on the one stamp card, 1 to 14 (day 14 stays shown once the card is over). Reads only.</summary>
    public static int CardDay(ShopPreviewData d, DateTimeOffset now) => Math.Clamp(RawDay(d, now), 1, CardDays);

    /// <summary>The card is over: day 15 and later. For good, there is no second card.</summary>
    public static bool CardDone(ShopPreviewData d, DateTimeOffset now) => RawDay(d, now) > CardDays;

    public static bool CouponReady(ShopPreviewData d) => d.Stamped.Count >= CouponAt && !d.CouponUsed;

    public static bool GiftReady(ShopPreviewData d, DateTimeOffset now) => !string.Equals(d.GiftWeek, IsoWeek(now), StringComparison.Ordinal);

    /// <summary>The next Monday 00:00 after <paramref name="now"/>, in its offset.</summary>
    public static DateTimeOffset NextMonday(DateTimeOffset now)
    {
        int ahead = ((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7;
        if (ahead == 0) ahead = 7;
        return new DateTimeOffset(now.Date.AddDays(ahead), now.Offset);
    }

    /// <summary>A ledger line by code (never words), newest first, at most six.</summary>
    public static void Note(ShopPreviewData d, string code, int n, DateTimeOffset now, string? id = null)
    {
        d.Ledger.Insert(0, new PreviewLedgerLine { Code = code, N = n, Id = id, At = Stamp(now) });
        if (d.Ledger.Count > LedgerMax) d.Ledger.RemoveRange(LedgerMax, d.Ledger.Count - LedgerMax);
    }

    /// <summary>A gain the desktop banked, mirrored into the preview wallet. A run of the same code
    /// grows one ledger line instead of pushing the others out.</summary>
    public static void Credit(ShopPreviewData d, int amount, string code, DateTimeOffset now)
    {
        if (amount <= 0) return;
        d.Sp = (int)Math.Min(SpCap, (long)d.Sp + amount);
        if (d.Ledger.Count > 0 && d.Ledger[0].Code == code && d.Ledger[0].N > 0) { d.Ledger[0].N += amount; d.Ledger[0].At = Stamp(now); }
        else Note(d, code, amount, now);
    }

    /// <summary>An effect row's tier letter from its price (the server's S, M, L); a free row has none.</summary>
    internal static string? TierOf(int price) => price >= 730 ? "L" : price >= 360 ? "M" : price > 0 ? "S" : null;

    /// <summary>A row by id: an effect row, else a row of the bundled real tabs. Null = not in stock.</summary>
    public static (int Price, string Sale, string? Needs, string? Tier)? Row(JObject? sample, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (Effect(id) is { } e) return (e.Price, e.Todo ? "soon" : "on", null, TierOf(e.Price));
        var row = SampleRows(sample).FirstOrDefault(r => (string?)r["id"] == id);
        if (row == null || row["priceSp"] is not JValue { Type: JTokenType.Integer } p) return null;
        return (p.Value<int>(), (string?)row["sale"] ?? "on", (string?)row["needs"], row["tier"]?.Type == JTokenType.String ? (string?)row["tier"] : null);
    }

    /// <summary>A buy. Same idem again = the same receipt. A coupon buy takes one tier S row free, once per account.</summary>
    public static PreviewOutcome Buy(ShopPreviewData d, JObject? sample, string? itemId, string? idem, bool coupon, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(idem) || string.IsNullOrEmpty(itemId)) return PreviewOutcome.No("bad_input");
        var replay = d.Receipts.FirstOrDefault(r => r.Idem == idem);
        if (replay != null) return replay.ItemId == itemId ? new(true, null, replay) : PreviewOutcome.No("idem_mismatch");
        if (Row(sample, itemId) is not { } row || row.Sale != "on") return PreviewOutcome.No("not_in_stock");
        if (d.Owned.ContainsKey(itemId)) return PreviewOutcome.No("owned");
        if (row.Needs == "discord") return PreviewOutcome.No("discord_required");
        if (coupon && row.Tier != CouponTier) return PreviewOutcome.No("unavailable");
        if (coupon && !CouponReady(d)) return PreviewOutcome.No("not_ready");
        int price = coupon ? 0 : row.Price;
        if (d.Sp < price) return PreviewOutcome.No("insufficient");

        var receipt = new PreviewReceipt { Idem = idem, ItemId = itemId, PaidSp = price, SpBefore = d.Sp };
        d.Sp -= price;
        d.Owned[itemId] = new PreviewOwned { At = Stamp(now), PaidSp = price };
        d.Revision++;
        if (coupon) d.CouponUsed = true;
        if (d.Goal == itemId) d.Goal = null;
        Note(d, coupon ? CodeCoupon : CodeBuy, -price, now, itemId);
        d.Receipts.Insert(0, receipt);
        if (d.Receipts.Count > ReceiptsMax) d.Receipts.RemoveRange(ReceiptsMax, d.Receipts.Count - ReceiptsMax);
        return new(true, null, receipt);
    }

    /// <summary>
    /// Wear or take off. Slots bubble, card, behind, fog, word take an owned row of that slot (null = the
    /// slot's free row). Two-tone is a switch, not a colour: <c>fog</c> or <c>mix</c> with its id turns the
    /// mix on, <c>mix</c> with null turns it off. <c>fog2</c> is the mix's second colour: a palette colour
    /// (FogLooks.Palette, never a shop row), free once Two-tone is owned; null puts back lilac.
    /// </summary>
    public static PreviewOutcome Equip(ShopPreviewData d, string? slot, string? itemId)
    {
        if (slot == Prizes.WornLooks.Fog2)
        {
            if (itemId != null && !SuperCore.FogLooks.IsPalette(itemId)) return PreviewOutcome.No("bad_input");
            if (!d.Owned.ContainsKey(Twotone)) return PreviewOutcome.No("not_owned");
            d.Equip[slot] = itemId ?? Prizes.WornLooks.DefaultOf(slot)!;
            return PreviewOutcome.Done;
        }
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
        Note(d, CodeGift, GiftAmount, now);
        d.Sp = (int)Math.Min(SpCap, (long)d.Sp + GiftAmount);
        return PreviewOutcome.Done;
    }

    /// <summary>One stamp a day. The first stamp makes today day 1; after day 14 the card answers done.</summary>
    public static PreviewOutcome StampToday(ShopPreviewData d, DateTimeOffset now)
    {
        if (Start(d) == null) { d.CardStart = Day(now); d.Stamped.Clear(); }
        int raw = RawDay(d, now);
        if (raw > CardDays) return PreviewOutcome.No("done");
        if (raw < 1 || d.Stamped.Contains(raw)) return PreviewOutcome.No("stamped");   // a clock behind day 1 stamps nothing
        d.Stamped.Add(raw);
        d.Stamped.Sort();
        return PreviewOutcome.Done;
    }

    /// <summary>Save for an item (null clears). Unknown or unsold ids answer unavailable, owned ones owned (as the server).</summary>
    public static PreviewOutcome SetGoal(ShopPreviewData d, JObject? sample, string? itemId)
    {
        if (itemId == null) { d.Goal = null; return PreviewOutcome.Done; }
        if (Row(sample, itemId) is not { Sale: "on" }) return PreviewOutcome.No("unavailable");
        if (d.Owned.ContainsKey(itemId)) return PreviewOutcome.No("owned");
        d.Goal = itemId;
        return PreviewOutcome.Done;
    }

    /// <summary>Where a real row shows: the desk and Rain live in the desktop app, the rest everywhere.
    /// Chess finishes are desktop only until website + phone support for them lands (then drop the chess.finish. line).</summary>
    public static string[] PlacesOf(string id)
        => id.StartsWith("desk.", StringComparison.Ordinal) || id.StartsWith("rain.", StringComparison.Ordinal)
            || id.StartsWith("chess.finish.", StringComparison.Ordinal) ? DesktopOnly : Everywhere;

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
                    ["id"] = e.Id, ["tier"] = TierOf(e.Price), ["priceSp"] = e.Price,
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
                ["couponReady"] = CouponReady(d), ["couponUsed"] = d.CouponUsed, ["done"] = CardDone(d, now),
            },
            ["goal"] = d.Goal,
            ["ledger"] = new JArray(d.Ledger.Take(LedgerMax).Select(LedgerJson)),
        };
    }

    /// <summary>A ledger line on the wire: <c>{ code, n, at, id? }</c> as the server sends it; an older
    /// preview file's English line goes as <c>{ t, n }</c> (the page reads it only as a fallback).</summary>
    private static JObject LedgerJson(PreviewLedgerLine l)
    {
        if (string.IsNullOrEmpty(l.Code)) return new JObject { ["t"] = l.T, ["n"] = l.N };
        var o = new JObject { ["code"] = l.Code, ["n"] = l.N, ["at"] = l.At };
        if (!string.IsNullOrEmpty(l.Id)) o["id"] = l.Id;
        return o;
    }
}
