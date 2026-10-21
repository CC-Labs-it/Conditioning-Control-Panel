using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>The account's live friend link (CONTRACT.md "Links"): 7 days, 25 uses, a claim makes
/// the pair friends at once. <see cref="Url"/> is always built from <see cref="Token"/>.</summary>
public sealed record FriendLink(string Token, DateTimeOffset ExpiresAt, int UsesLeft)
{
    public string Url => FriendsLinkRules.UrlFor(Token);
}

/// <summary>What a <c>claim</c> came back with. A blocked pair, an expired, revoked or unknown
/// token all read <see cref="Unusable"/> (the server never says "blocked").</summary>
public enum ClaimResult { Friends, Already, Self, Full, Unusable, TryLater }

/// <summary>
/// Everything about friend links that is not drawing or wire: the token grammar, reading a pasted
/// link, when a cached link is still good, and what Copy link puts on the clipboard when there is
/// no link to be had. Pure, so the suite holds it without a window or a server.
/// </summary>
public static class FriendsLinkRules
{
    /// <summary>The same alphabet as friend codes: no 0, 1, I or O.</summary>
    public const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    public const int TokenLength = 12;

    public const string UrlPrefix = "https://app.cclabs.app/f/";

    /// <summary>A cached link this close to its end is fetched again rather than handed out.</summary>
    public static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

    public static string UrlFor(string token) => UrlPrefix + token;

    public static bool IsToken(string? s)
    {
        if (s == null || s.Length != TokenLength) return false;
        foreach (var c in s) if (Alphabet.IndexOf(c) < 0) return false;
        return true;
    }

    /// <summary>The token in a pasted <c>https://app.cclabs.app/f/&lt;token&gt;</c> (any case, with or
    /// without the scheme, a trailing slash, query or fragment) or a bare token. Upper case. Null
    /// for anything else, a friend code included.</summary>
    public static string? ParseToken(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim().ToUpperInvariant();
        foreach (var head in new[] { "HTTPS://", "HTTP://" })
            if (s.StartsWith(head, StringComparison.Ordinal)) { s = s.Substring(head.Length); break; }
        if (s.StartsWith("WWW.", StringComparison.Ordinal)) s = s.Substring(4);
        const string host = "APP.CCLABS.APP/F/";
        if (s.StartsWith(host, StringComparison.Ordinal))
        {
            s = s.Substring(host.Length);
            int cut = s.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
        }
        else if (s.Contains('/')) return null;
        return IsToken(s) ? s : null;
    }

    /// <summary>
    /// Our link inside a pasted message ("add me https://app.cclabs.app/f/..." from a share sheet
    /// or a chat): the token of the first whole word that is itself our link, else null. A word
    /// must carry a slash, so a bare twelve-letter word in the message never reads as a token, and
    /// <see cref="ParseToken"/> still refuses every other host.
    /// </summary>
    public static string? LinkInText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw.Trim(WordWrappers);
            if (word.IndexOf('/') >= 0 && ParseToken(word) is { } t) return t;
        }
        return null;
    }

    // Brackets, quotes and sentence punctuation around a link in a message, in any order.
    private static readonly char[] WordWrappers = { '<', '>', '(', ')', '[', ']', '"', '\'', '.', ',', ';', ':', '!', '?' };

    /// <summary>What the add box shows for what was typed or pasted, and whether it is a link
    /// token rather than a friend code. A whole link becomes its token; up to five characters (or
    /// a pasted <c>CCP-</c> code) stay a code; six to twelve from the alphabet are a token on its way.
    /// <paramref name="oneKeystroke"/>: the text grew by one character, so a dashless <c>CCP</c> code
    /// is not read into it (a token that starts with C, C, P must stay typeable by hand).</summary>
    public static (string Text, bool IsToken) BoxText(string? typed, bool oneKeystroke = false)
    {
        if (string.IsNullOrEmpty(typed)) return ("", false);
        if (ParseToken(typed) is { } t) return (t, true);
        if (LinkInText(typed) is { } inText) return (inText, true);
        var s = typed.Trim().ToUpperInvariant();
        if (s.StartsWith("CCP-", StringComparison.Ordinal)) return (Controls.Friends.FriendsDrawerRules.NormaliseCode(s), false);
        if (s.Contains('/') || s.Contains(':'))
        {
            // Only our own host gives up a token on its way; a link from anywhere else is not a claim.
            foreach (var head in new[] { "HTTPS://", "HTTP://" })
                if (s.StartsWith(head, StringComparison.Ordinal)) { s = s.Substring(head.Length); break; }
            if (s.StartsWith("WWW.", StringComparison.Ordinal)) s = s.Substring(4);
            const string host = "APP.CCLABS.APP/F/";
            s = s.StartsWith(host, StringComparison.Ordinal) ? s.Substring(host.Length) : "";
        }
        var sb = new StringBuilder(TokenLength);
        foreach (var c in s)
        {
            if (Alphabet.IndexOf(c) < 0) continue;
            sb.Append(c);
            if (sb.Length == TokenLength) break;
        }
        var f = sb.ToString();
        if (!oneKeystroke && f.Length == 8 && f.StartsWith("CCP", StringComparison.Ordinal)) return (f.Substring(3), false);
        return f.Length <= Controls.Friends.FriendsDrawerRules.CodeLength ? (f, false) : (f, true);
    }

    /// <summary>A link worth handing out: uses left and not within <see cref="ExpiryMargin"/> of its end.</summary>
    public static bool IsUsable(FriendLink? link, DateTimeOffset now)
        => link != null && link.UsesLeft > 0 && now < link.ExpiresAt - ExpiryMargin;

    /// <summary>A link from the server this close to its end (or spent) is swapped for a fresh one
    /// before Copy link hands it out: nobody gets a link that dies before the friend opens it.</summary>
    public static readonly TimeSpan HandOutMargin = TimeSpan.FromHours(1);

    public static bool WorthHandingOut(FriendLink? link, DateTimeOffset now)
        => link != null && link.UsesLeft > 0 && now < link.ExpiresAt - HandOutMargin;

    /// <summary>The note after Copy link, worded from the link itself: whole days left (an hour of
    /// slack, so a link minted a moment ago reads 7), hours under a day, and the uses left.</summary>
    public static (string Key, int Left, int Uses) CopiedNote(FriendLink link, DateTimeOffset now)
    {
        var left = link.ExpiresAt - now;
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        int days = (int)Math.Floor((left + TimeSpan.FromHours(1)).TotalDays);
        if (days >= 1) return ("friends_link_copied_left", days, link.UsesLeft);
        return ("friends_link_copied_left_hours", Math.Max(1, (int)Math.Floor(left.TotalHours)), link.UsesLeft);
    }

    /// <summary>What Copy link puts on the clipboard: the link when there is one, else the code
    /// (and the drawer says so), else nothing.</summary>
    public static (string? Text, bool IsLink) CopyChoice(FriendLink? link, string? code)
    {
        if (link != null) return (link.Url, true);
        return string.IsNullOrEmpty(code) ? (null, false) : (code, false);
    }

    /// <summary>A <c>link</c> reply, or null when it does not fit the contract.</summary>
    public static FriendLink? ParseLink(JObject? o)
    {
        if (o == null || o.Value<bool?>("ok") != true) return null;
        var token = (o["token"]?.Type == JTokenType.String ? (string?)o["token"] : null)?.Trim().ToUpperInvariant();
        if (!IsToken(token)) return null;
        // The wire reads dates as strings (FriendsApi.ReadSettings); a default JObject.Parse makes them Dates.
        DateTimeOffset exp;
        var et = o["expires_at"];
        if (et?.Type == JTokenType.Date)
        {
            var dt = et.Value<DateTime>();
            exp = new DateTimeOffset(DateTime.SpecifyKind(dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt, DateTimeKind.Utc));
        }
        else if (et?.Type != JTokenType.String
            || !DateTimeOffset.TryParse((string?)et, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out exp))
            return null;
        int uses = o["uses_left"]?.Type == JTokenType.Integer ? o.Value<int>("uses_left") : 0;
        return new FriendLink(token!, exp, Math.Max(0, uses));
    }

    public static ClaimResult ClaimFromWire(string? status) => status switch
    {
        "friends" => ClaimResult.Friends,
        "already" => ClaimResult.Already,
        "self" => ClaimResult.Self,
        "full" => ClaimResult.Full,
        "unusable" or "bad_input" or "not_found" or "refused" or "blocked" => ClaimResult.Unusable,
        _ => ClaimResult.TryLater,
    };

    public static string ClaimResultKey(ClaimResult r) => r switch
    {
        ClaimResult.Friends => "friends_add_accepted",
        ClaimResult.Already => "friends_add_already",
        ClaimResult.Self => "friends_link_self",
        ClaimResult.Full => "friends_link_full",
        ClaimResult.Unusable => "friends_link_unusable",
        _ => "friends_add_try_later",
    };

    public static bool IsGood(ClaimResult r) => r == ClaimResult.Friends;
}

/// <summary>The live link, remembered per account until it is near its end, so Copy link costs one
/// call a week rather than one a press. Not thread safe: the service touches it on the UI thread.</summary>
public sealed class FriendLinkCache
{
    private string? _account;
    private FriendLink? _link;

    public FriendLink? Get(string? account, DateTimeOffset now)
        => account != null && account == _account && FriendsLinkRules.IsUsable(_link, now) ? _link : null;

    public void Put(string account, FriendLink link) { _account = account; _link = link; }

    public void Clear() { _account = null; _link = null; }
}
