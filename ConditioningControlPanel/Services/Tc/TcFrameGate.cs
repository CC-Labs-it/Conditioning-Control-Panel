using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>A page frame that passed <see cref="TcFrameGate"/>: the type plus only the fields the host
/// may act on, already checked. Fields that do not belong to the type stay null / zero.</summary>
internal sealed record TcPageFrame(string Type)
{
    public string? Where { get; init; }
    public string? Want { get; init; }
    public string? Save { get; init; }
    public string? Url { get; init; }
    public string? Reason { get; init; }
    public string? Ending { get; init; }
    public string? Tag { get; init; }
    public IReadOnlyList<string>? Niches { get; init; }
    public double Level { get; init; }
    /// <summary><c>haptic</c>: pulse length in milliseconds.</summary>
    public int Ms { get; init; }
    /// <summary><c>fuzz-ended</c>: run length in whole MINUTES (never milliseconds).</summary>
    public int Minutes { get; init; }
    public bool Value { get; init; }
    /// <summary><c>set-setting</c>: the page says this came from the player's own press (<c>ask:true</c>).</summary>
    public bool Ask { get; init; }
}

/// <summary>The gate's answer. <paramref name="Refusal"/> is a fixed code; for <c>save-offer</c> it is
/// the <c>save-result</c> reason the host answers with (<c>too-big</c>).</summary>
internal sealed record TcGateResult(bool Accepted, TcPageFrame? Frame, string? Refusal)
{
    public static TcGateResult Ok(TcPageFrame f) => new(true, f, null);
    public static TcGateResult No(string why) => new(false, null, why);
}

/// <summary>
/// Page to host, judged before the host acts (the page is untrusted). Only the types of
/// <c>fuzz/CONTRACT.md</c> v1 sections 1 and 5 plus the Contract v2 additions pass, each with its fixed
/// ids; everything else is refused. <c>open-url</c> is re-checked against the contract's allow list
/// here, never trusted from the page.
/// </summary>
internal static class TcFrameGate
{
    public const string RefuseShape = "shape";
    public const string RefuseType = "type";
    public const string RefuseField = "field";
    public const string RefuseTooBig = "too-big";
    public const string RefuseUrl = "url";

    public static readonly IReadOnlySet<string> NudgeWhere =
        new HashSet<string>(StringComparer.Ordinal) { "ending", "llama8", "transmission", "locked-tile", "pause", "save-sleep" };

    public static readonly IReadOnlySet<string> NudgeWant =
        new HashSet<string>(StringComparer.Ordinal) { "account", "download", "upgrade" };

    public static readonly IReadOnlySet<string> ExitReasons =
        new HashSet<string>(StringComparer.Ordinal) { "back", "key", "drop" };

    /// <summary>The eight theme niches (CONTRACT.md section 1, <c>scrolller-niches</c>).</summary>
    public static readonly IReadOnlySet<string> Niches =
        new HashSet<string>(StringComparer.Ordinal) { "trance", "bimbo", "drone", "sissy", "goon", "chastity", "censored", "infection" };

    /// <summary>Hosts any https path may open (CONTRACT.md section 1, <c>open-url</c>).</summary>
    public static readonly IReadOnlySet<string> OpenUrlHosts =
        new HashSet<string>(StringComparer.Ordinal) { "cclabs.app", "www.cclabs.app", "app.cclabs.app" };

    /// <summary>The credit and the server invite: exact strings only.</summary>
    public static readonly IReadOnlySet<string> OpenUrlExact = new HashSet<string>(StringComparer.Ordinal)
    {
        "https://discord.gg/YxVAMt4qaZ",
        "https://neal.fun/",
        "https://neal.fun/stimulation-clicker/",
    };

    /// <summary>Frames with no fields the host reads.</summary>
    private static readonly HashSet<string> Bare = new(StringComparer.Ordinal)
    {
        "ready", "heartbeat", "exit-done", "save-ask", "expo-fetch", "expo-cancel", "tab",
        "scrolller-release", "scrolller-play", "scrolller-view",
        "own-media-list",
    };

    /// <summary>A fixed id: lower-case letters, digits, '_' and '-', at most 40.</summary>
    private static readonly Regex Id = new("^[a-z0-9_-]{1,40}$", RegexOptions.CultureInvariant);

    public const double MaxHapticLevel = 1.0;
    public const int MaxHapticMs = 3000;
    public const int MaxLogChars = 500;

    public static TcGateResult Check(JToken? token)
    {
        if (token is not JObject o) return TcGateResult.No(RefuseShape);
        var type = Str(o, "type");
        if (type == null) return TcGateResult.No(RefuseShape);
        if (Bare.Contains(type)) return TcGateResult.Ok(new TcPageFrame(type));

        switch (type)
        {
            case "log":
            {
                var msg = Str(o, "msg") ?? "";
                var level = Str(o, "level");
                return TcGateResult.Ok(new TcPageFrame(type)
                {
                    Reason = level is "debug" or "info" or "warn" or "error" ? level : "info",
                    Save = msg.Length > MaxLogChars ? msg[..MaxLogChars] : msg,
                });
            }
            case "boot-error":
            {
                var msg = Str(o, "msg") ?? "";
                return TcGateResult.Ok(new TcPageFrame(type) { Save = msg.Length > MaxLogChars ? msg[..MaxLogChars] : msg });
            }
            case "exit":
            {
                var r = Str(o, "reason");
                return TcGateResult.Ok(new TcPageFrame(type) { Reason = r != null && ExitReasons.Contains(r) ? r : "back" });
            }
            case "nudge":
            {
                var where = Str(o, "where");
                var want = Str(o, "want");
                if (where == null || want == null || !NudgeWhere.Contains(where) || !NudgeWant.Contains(want))
                    return TcGateResult.No(RefuseField);
                return TcGateResult.Ok(new TcPageFrame(type) { Where = where, Want = want });
            }
            case "save-offer":
            {
                if (o["save"] is not JValue { Type: JTokenType.String } v) return TcGateResult.No(RefuseField);
                var save = (string)v!;
                if (save.Length == 0) return TcGateResult.No(RefuseField);
                if (save.Length > TcRules.MaxSaveChars || Encoding.UTF8.GetByteCount(save) > TcRules.MaxSaveBytes)
                    return TcGateResult.No(RefuseTooBig);
                return TcGateResult.Ok(new TcPageFrame(type) { Save = save });
            }
            case "open-url":
            {
                var url = OpenUrl(Str(o, "url"));
                return url == null ? TcGateResult.No(RefuseUrl) : TcGateResult.Ok(new TcPageFrame(type) { Url = url });
            }
            case "set-setting":
            {
                if (Str(o, "key") != "media.remoteConsent" || o["value"] is not JValue { Type: JTokenType.Boolean } val)
                    return TcGateResult.No(RefuseField);
                return TcGateResult.Ok(new TcPageFrame(type)
                {
                    Value = (bool)val!,
                    Ask = o["ask"] is JValue { Type: JTokenType.Boolean } a && (bool)a!,
                });
            }
            case "scrolller-niches":
            {
                if (o["niches"] is not JArray arr) return TcGateResult.No(RefuseField);
                var list = new List<string>(2);
                foreach (var t in arr)
                {
                    // An unknown id reads as the general mix (CONTRACT.md): dropped here.
                    if (t is JValue { Type: JTokenType.String } s && Niches.Contains((string)s!) && !list.Contains((string)s!))
                        list.Add((string)s!);
                    if (list.Count == 2) break;
                }
                return TcGateResult.Ok(new TcPageFrame(type) { Niches = list });
            }
            case "haptic":
            {
                if (!TryNumber(o["level"], out var level) || !TryNumber(o["ms"], out var ms)) return TcGateResult.No(RefuseField);
                var tag = Str(o, "tag");
                return TcGateResult.Ok(new TcPageFrame(type)
                {
                    Level = Math.Clamp(level, 0, MaxHapticLevel),
                    Ms = (int)Math.Clamp(ms, 0, MaxHapticMs),
                    Tag = tag != null && Id.IsMatch(tag) ? tag : null,
                });
            }
            case "fuzz-ended":
            {
                var ending = Str(o, "ending");
                if (ending == null || !Id.IsMatch(ending)) return TcGateResult.No(RefuseField);
                if (!TryNumber(o["minutes"], out var minutes)) return TcGateResult.No(RefuseField);
                return TcGateResult.Ok(new TcPageFrame(type) { Ending = ending, Minutes = (int)Math.Clamp(minutes, 0, 24 * 60) });
            }
            default:
                return TcGateResult.No(RefuseType);
        }
    }

    /// <summary>The contract's link rule, host side: https on the three cclabs.app hosts (any path, no
    /// port, no user info), or one of the exact credit links. Returns the url to open, else null.</summary>
    public static string? OpenUrl(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length > 2048) return null;
        foreach (var c in raw)
            if (char.IsControl(c) || c == '"' || c == '\'' || c == '<' || c == '>' || c == '\\' || c == ' ') return null;
        if (OpenUrlExact.Contains(raw)) return raw;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0) return null;
        if (!raw.StartsWith("https://", StringComparison.Ordinal)) return null;
        return OpenUrlHosts.Contains(uri.Host) ? uri.AbsoluteUri : null;
    }

    private static string? Str(JObject o, string key)
        => o[key] is JValue { Type: JTokenType.String } v ? (string?)v : null;

    private static bool TryNumber(JToken? t, out double d)
    {
        d = 0;
        if (t is not JValue { Type: JTokenType.Integer or JTokenType.Float } v) return false;
        d = Convert.ToDouble(v.Value, System.Globalization.CultureInfo.InvariantCulture);
        return !double.IsNaN(d) && !double.IsInfinity(d);
    }
}
