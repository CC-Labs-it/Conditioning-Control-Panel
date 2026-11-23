using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.Announcements
{
    /// <summary>One offer row of a promo card: a pill with a label and a smaller tag beside it.</summary>
    public sealed record PromoRow(string Label, string? Sub);

    /// <summary>The promo card's one button: a label and the page it opens.</summary>
    public sealed record PromoCta(string Label, Uri Url);

    /// <summary>
    /// The <c>promo</c> object of <c>GET /config/announcement</c> once it has passed every limit
    /// in <see cref="PromoCardRules"/>. A build that reads one draws the paper promo window; any
    /// reply without one (or with one that fails a single limit) keeps the old AnnouncementPopup.
    /// Wire contract: handoff-1123/BF-PROMO-BRIEF.md.
    /// </summary>
    public sealed record PromoCard(
        string Layout,
        string? Tag,
        string? Hook,
        string Code,
        IReadOnlyList<PromoRow> Rows,
        IReadOnlyList<Uri> Stickers,
        DateTimeOffset EndsAt,
        PromoCta Cta);

    /// <summary>
    /// Reads and re-checks the promo object the server sends. The server validates on POST too;
    /// this is the client's own copy of the same limits, because a card that breaks one of them
    /// would draw badly or open a page we never meant to open. Any failure drops the WHOLE promo
    /// (the caller then shows the old popup), never a half card. Pure: no WPF, no I/O.
    /// </summary>
    public static class PromoCardRules
    {
        public const string Layout = "tc-paper";
        public const int MaxTag = 24;
        public const int MaxHook = 60;
        public const int MinCode = 3;
        public const int MaxCode = 16;
        public const int MinRows = 1;
        public const int MaxRows = 3;
        public const int MaxRowLabel = 24;
        public const int MaxRowSub = 32;
        public const int MaxStickers = 3;
        public const int MaxUrl = 500;
        public const int MaxCtaLabel = 28;

        private static readonly Regex CodePattern = new("^[A-Z0-9]+$", RegexOptions.CultureInvariant);

        /// <summary>Hosts a sticker picture may come from.</summary>
        public static readonly IReadOnlyList<string> StickerHosts = new[] { "cclabs.app", "app.cclabs.app", "cdn.cclabs.app" };

        /// <summary>Hosts the button may open.</summary>
        public static readonly IReadOnlyList<string> CtaHosts = new[] { "www.patreon.com", "patreon.com", "cclabs.app", "app.cclabs.app" };

        /// <summary>
        /// The promo, or null when it is absent or breaks any limit. <paramref name="expiresAt"/> is
        /// the announcement's own end (epoch ms) when the reply carries one; the server keeps it to
        /// itself today, so null skips that one check.
        /// </summary>
        public static PromoCard? TryRead(JsonElement? promo, DateTimeOffset now, long? expiresAt = null)
        {
            try
            {
                if (promo is not { ValueKind: JsonValueKind.Object } p) return null;

                if (Str(p, "layout") != Layout) return null;

                var tag = OptionalText(p, "tag", MaxTag, out var ok);
                if (!ok) return null;
                var hook = OptionalText(p, "hook", MaxHook, out ok);
                if (!ok) return null;

                var code = Str(p, "code");
                if (code == null || code.Length < MinCode || code.Length > MaxCode || !CodePattern.IsMatch(code)) return null;

                if (!p.TryGetProperty("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array) return null;
                var rows = new List<PromoRow>();
                foreach (var r in rowsEl.EnumerateArray())
                {
                    if (r.ValueKind != JsonValueKind.Object) return null;
                    var label = Str(r, "label");
                    if (!IsShortText(label, MaxRowLabel)) return null;
                    var sub = OptionalText(r, "sub", MaxRowSub, out ok);
                    if (!ok) return null;
                    rows.Add(new PromoRow(label!.Trim(), sub));
                }
                if (rows.Count < MinRows || rows.Count > MaxRows) return null;

                var stickers = new List<Uri>();
                if (p.TryGetProperty("stickers", out var stEl) && stEl.ValueKind != JsonValueKind.Null)
                {
                    if (stEl.ValueKind != JsonValueKind.Array) return null;
                    foreach (var s in stEl.EnumerateArray())
                    {
                        var uri = SafeHttps(s.ValueKind == JsonValueKind.String ? s.GetString() : null, StickerHosts);
                        if (uri == null) return null;
                        stickers.Add(uri);
                    }
                    if (stickers.Count > MaxStickers) return null;
                }

                if (!p.TryGetProperty("ends_at", out var endsEl) || endsEl.ValueKind != JsonValueKind.Number
                    || !endsEl.TryGetInt64(out var endsMs)) return null;
                DateTimeOffset endsAt;
                try { endsAt = DateTimeOffset.FromUnixTimeMilliseconds(endsMs); }
                catch (ArgumentOutOfRangeException) { return null; }
                if (endsAt <= now) return null;
                if (expiresAt is long exp && endsMs > exp) return null;

                if (!p.TryGetProperty("cta", out var ctaEl) || ctaEl.ValueKind != JsonValueKind.Object) return null;
                var ctaLabel = Str(ctaEl, "label");
                if (!IsShortText(ctaLabel, MaxCtaLabel)) return null;
                var ctaUrl = SafeHttps(Str(ctaEl, "url"), CtaHosts);
                if (ctaUrl == null) return null;

                return new PromoCard(Layout, tag, hook, code, rows, stickers, endsAt, new PromoCta(ctaLabel!.Trim(), ctaUrl));
            }
            catch (Exception)
            {
                // A shape we did not expect is a promo we do not draw.
                return null;
            }
        }

        /// <summary>
        /// An absolute https URL on one of <paramref name="hosts"/>, at most <see cref="MaxUrl"/>
        /// characters, with no quotes, spaces or control characters (it ends up on a command line
        /// when the browser launcher falls back to cmd). Null otherwise.
        /// </summary>
        public static Uri? SafeHttps(string? raw, IReadOnlyList<string> hosts)
        {
            if (string.IsNullOrEmpty(raw) || raw.Length > MaxUrl) return null;
            foreach (var c in raw)
                if (char.IsControl(c) || char.IsWhiteSpace(c) || c == '"' || c == '\'' || c == '`' || c == '<' || c == '>')
                    return null;
            if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttps) return null;
            if (!string.IsNullOrEmpty(uri.UserInfo)) return null;
            if (!uri.IsDefaultPort) return null;
            var host = uri.IdnHost.ToLowerInvariant();
            if (!hosts.Contains(host)) return null;
            return uri;
        }

        private static string? Str(JsonElement o, string name) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>Absent or null is fine (null back, ok); present must be a short single line.</summary>
        private static string? OptionalText(JsonElement o, string name, int max, out bool ok)
        {
            ok = true;
            if (!o.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
            if (v.ValueKind != JsonValueKind.String) { ok = false; return null; }
            var s = v.GetString();
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (!IsShortText(s, max)) { ok = false; return null; }
            return s.Trim();
        }

        private static bool IsShortText(string? s, int max)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var t = s.Trim();
            if (t.Length > max) return false;
            foreach (var c in t) if (char.IsControl(c)) return false;
            return true;
        }
    }

    /// <summary>The promo card's countdown: two units at most, the largest first.</summary>
    public static class PromoCountdown
    {
        public enum Unit { Days, Hours, Minutes }

        /// <summary>
        /// "2d 4h" from a day out, "4h 12m" inside a day, "12m" inside the hour. The last minute
        /// reads "1m" rather than "0m", so the card never claims it is over while it still runs.
        /// Empty once <paramref name="left"/> is zero or less.
        /// </summary>
        public static IReadOnlyList<(int Value, Unit Unit)> Parts(TimeSpan left)
        {
            if (left <= TimeSpan.Zero) return Array.Empty<(int, Unit)>();
            var totalMinutes = (long)Math.Ceiling(left.TotalMinutes);
            var days = (int)(totalMinutes / (24 * 60));
            var hours = (int)(totalMinutes / 60 % 24);
            var minutes = (int)(totalMinutes % 60);
            if (days > 0) return new[] { (days, Unit.Days), (hours, Unit.Hours) };
            if (hours > 0) return new[] { (hours, Unit.Hours), (minutes, Unit.Minutes) };
            return new[] { (minutes, Unit.Minutes) };
        }

        /// <summary>The same parts as short text ("2d 4h"), for logs and screen readers.</summary>
        public static string Format(TimeSpan left) =>
            string.Join(" ", Parts(left).Select(p => p.Value + (p.Unit switch { Unit.Days => "d", Unit.Hours => "h", _ => "m" })));
    }
}
