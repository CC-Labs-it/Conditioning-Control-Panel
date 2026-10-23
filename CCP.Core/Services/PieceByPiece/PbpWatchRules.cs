using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.PieceByPiece.Media;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.PieceByPiece;

/// <summary>
/// Chess spectating, host side. Pure, so the wire checks are testable without a window.
/// <list type="bullet">
/// <item>host -&gt; page <c>pbp:settings</c> carries <c>letPeopleWatch</c> (bool, every boot),
/// the stands' effects choice <c>watchEffects</c> (bool) + <c>watchAmount</c>
/// (<see cref="Amounts"/>), <c>mediaAvailable</c> (bool, see <see cref="PbpMediaRules.MediaAvailable"/>)
/// and, on a board opened straight into a spectate, <c>spectateMatchId</c> (once).</item>
/// <item>page -&gt; host <c>{ type: 'pbp:setting', key, value }</c> when one of the page's
/// host-kept toggles changes: <c>letPeopleWatch</c> (bool), <c>watchEffects</c> (bool),
/// <c>watchAmount</c> (string, one of <see cref="Amounts"/>). Any other key or a value of the
/// wrong type is ignored: the page cannot write arbitrary settings through this frame.</item>
/// </list>
/// Spectate v2 (2026-10-14): the PUBLIC watch routes (tape chunks, catch-up snapshots, reaction
/// buckets) are CDN-cached and must not vary by caller, so the host relays them WITHOUT the auth
/// token (<see cref="IsPublicWatchPath"/>).
/// </summary>
internal static class PbpWatchRules
{
    public const string SettingFrame = "pbp:setting";
    public const string LetPeopleWatchKey = "letPeopleWatch";
    public const string WatchEffectsKey = "watchEffects";
    public const string WatchAmountKey = "watchAmount";

    /// <summary>The game's amount dial (game/preferences.js AMOUNTS), shared by the stands.</summary>
    public static readonly string[] Amounts = { "less", "normal", "more" };
    public const string DefaultAmount = "normal";

    /// <summary>An amount the page knows, else the default.</summary>
    public static string CleanAmount(string? amount) =>
        amount != null && Amounts.Contains(amount, StringComparer.Ordinal) ? amount : DefaultAmount;

    /// <summary>The server's match id: <c>m_</c> then hex (16 today; 8 to 32 accepted).</summary>
    public static bool IsMatchId(string? id) =>
        id != null && id.Length >= 10 && id.Length <= 34 && id.StartsWith("m_", StringComparison.Ordinal)
        && id.Skip(2).All(Uri.IsHexDigit);

    /// <summary>Read a <c>pbp:setting</c> frame. True only for a key this host knows with a
    /// value of the right type (and, for the amount, a known value).</summary>
    public static bool TryRead(JObject? o, out string key, out JToken value)
    {
        key = "";
        value = JValue.CreateNull();
        if (o == null || (string?)o["type"] != SettingFrame) return false;
        if (o["key"]?.Type != JTokenType.String) return false;
        var k = (string?)o["key"];
        var v = o["value"];
        if (v == null) return false;
        switch (k)
        {
            case LetPeopleWatchKey:
            case WatchEffectsKey:
                if (v.Type != JTokenType.Boolean) return false;
                break;
            case WatchAmountKey:
                if (v.Type != JTokenType.String || !Amounts.Contains((string?)v, StringComparer.Ordinal)) return false;
                break;
            default:
                return false;
        }
        key = k!;
        value = v;
        return true;
    }

    /// <summary>Store a read setting. Returns true when it changed something (worth a save).</summary>
    public static bool Apply(AppSettings? s, string key, JToken? value)
    {
        if (s == null || value == null) return false;
        switch (key)
        {
            case LetPeopleWatchKey:
                if (value.Type != JTokenType.Boolean) return false;
                bool watch = (bool)value;
                if (s.PbpLetPeopleWatch == watch) return false;
                s.PbpLetPeopleWatch = watch;
                return true;
            case WatchEffectsKey:
                if (value.Type != JTokenType.Boolean) return false;
                bool fx = (bool)value;
                if (s.PbpWatchEffects == fx) return false;
                s.PbpWatchEffects = fx;
                return true;
            case WatchAmountKey:
                if (value.Type != JTokenType.String) return false;
                var a = (string?)value;
                if (a == null || !Amounts.Contains(a, StringComparer.Ordinal)) return false;
                if (s.PbpWatchAmount == a) return false;
                s.PbpWatchAmount = a;
                return true;
            default:
                return false;
        }
    }

    /// <summary>What the settings frame says about watching. Missing settings = the default (on).</summary>
    public static bool LetPeopleWatch(AppSettings? s) => s?.PbpLetPeopleWatch ?? true;

    /// <summary>The stands' Distraction effects. Missing settings = the default (on, owner 2026-10-14).</summary>
    public static bool WatchEffects(AppSettings? s) => s?.PbpWatchEffects ?? true;

    /// <summary>The stands' amount dial. Missing settings = <see cref="DefaultAmount"/>.</summary>
    public static string WatchAmount(AppSettings? s) => CleanAmount(s?.PbpWatchAmount);

    private static readonly string[] PublicLeaves = { "upto", "chunk", "reactions" };

    /// <summary>
    /// A PUBLIC spectate route: <c>GET /v2/pbp/watch/{matchId}/upto|chunk|reactions/{n}</c>, n
    /// decimal digits. These answers are immutable (or 60 s) CDN-cached and identical for every
    /// caller, so the host sends them with no <c>X-Auth-Token</c>: a request that carries a token
    /// must never be what fills or varies a shared cache entry. Everything else under
    /// <c>/v2/pbp/</c> (the join heartbeat <c>/watch/{id}</c>, reacting, posting fx, every seat
    /// route) keeps the token.
    /// <para>Matched the way the server's router matches (Express defaults): case-insensitive, one
    /// trailing slash allowed, query string and fragment ignored. Any path the server would route to
    /// a public handler therefore never carries the token, whatever its spelling.</para>
    /// </summary>
    public static bool IsPublicWatchPath(string? method, string? path)
    {
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) || path == null) return false;
        int cut = path.IndexOfAny(new[] { '?', '#' });
        if (cut >= 0) path = path.Substring(0, cut);
        if (path.EndsWith("/", StringComparison.Ordinal)) path = path.Substring(0, path.Length - 1);
        const string prefix = "/v2/pbp/watch/";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var parts = path.Substring(prefix.Length).Split('/');
        if (parts.Length != 3) return false;
        var id = parts[0];
        if (!(id.Length >= 2 && (id[0] == 'm' || id[0] == 'M') && id[1] == '_' && IsMatchId("m_" + id.Substring(2)))) return false;
        if (!PublicLeaves.Contains(parts[1], StringComparer.OrdinalIgnoreCase)) return false;
        var n = parts[2];
        return n.Length >= 1 && n.Length <= 12 && n.All(c => c >= '0' && c <= '9');
    }

    /// <summary>
    /// The one host -&gt; page <c>pbp:settings</c> frame, built here so its field names are tested
    /// (the page reads exactly these): <c>type, videoHoldSec, reducedMotion, whispers,
    /// letPeopleWatch, watchEffects, watchAmount, mediaAvailable, spectateMatchId</c>
    /// (null unless the board opened straight into a spectate).
    /// </summary>
    public static JObject SettingsFrame(int videoHoldSec, bool reducedMotion,
        System.Collections.Generic.IEnumerable<string>? whispers, AppSettings? s, bool mediaAvailable,
        string? spectateMatchId, double gentleIntroSpeed = 0)
        => new()
        {
            ["type"] = "pbp:settings",
            ["videoHoldSec"] = videoHoldSec,
            ["reducedMotion"] = reducedMotion,
            ["whispers"] = new JArray((whispers ?? Array.Empty<string>()).Cast<object>().ToArray()),
            ["letPeopleWatch"] = LetPeopleWatch(s),
            ["watchEffects"] = WatchEffects(s),
            ["watchAmount"] = WatchAmount(s),
            ["mediaAvailable"] = mediaAvailable,
            ["spectateMatchId"] = spectateMatchId != null ? new JValue(spectateMatchId) : JValue.CreateNull(),
            // Gentle visuals (owner, 2026-10-14): the clockwork intros still play, slower. 0 = no gentle
            // override (the page follows reducedMotion as before).
            ["gentleIntroSpeed"] = gentleIntroSpeed,
        };

    /// <summary>Gentle visuals and the chess intros: when the ONLY thing holding motion below Full is
    /// gentle visuals, the intros play their full clockwork at the gentle speed, floored at 0.5 so a
    /// 3 s intro never drags past 6 s. 0 when gentle is off or the player reduced motion themselves.</summary>
    public static double GentleIntroSpeed(bool gentleOn, MotionLevel levelWithoutGentle, double gentleSpeed)
        => gentleOn && levelWithoutGentle == MotionLevel.Full
            ? Math.Clamp(double.IsNaN(gentleSpeed) ? 0.5 : gentleSpeed, 0.5, 1.0)
            : 0;
}
