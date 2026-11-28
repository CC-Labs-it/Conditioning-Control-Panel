using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>
/// The pure half of the T&amp;C save carry (Contract v2 <c>save-offer</c> / <c>save-ask</c>, server routes
/// <c>GET</c> and <c>PUT /v2/tc/save</c>, CCP-Server <c>proxy/tc-save.js</c>). The host builds the path and
/// the body itself (never from the page), and maps every answer to the contract's frames. Nothing is
/// written to disk and the save never touches Sparkles.
/// </summary>
internal static class TcSaveRules
{
    public const string Path = "/v2/tc/save";
    public const string From = "desktop";

    public const string ReasonSignIn = "sign-in";
    public const string ReasonTooBig = "too-big";
    public const string ReasonOffline = "offline";
    public const string ReasonRefused = "refused";
    public const string ReasonNone = "none";

    /// <summary>The desktop door for a read: the account in the query, the token in a header.</summary>
    public static string GetPath(string unifiedId) => Path + "?unified_id=" + Uri.EscapeDataString(unifiedId);

    /// <summary>The desktop door for a write: <c>{unified_id, save, from:'desktop'}</c>.</summary>
    public static string PutBody(string unifiedId, string save)
        => new JObject { ["unified_id"] = unifiedId, ["save"] = save, ["from"] = From }.ToString(Formatting.None);

    public static bool SignedIn(string? unifiedId, string? token)
        => !string.IsNullOrWhiteSpace(unifiedId) && !string.IsNullOrWhiteSpace(token);

    /// <summary><c>caps.saveSlot</c>: a signed-in account with Offline mode off (Offline mode disables every
    /// network feature, the same way this host's pictures and Expo stand down).</summary>
    public static bool SlotOpen(string? unifiedId, string? token, bool offlineMode)
        => !offlineMode && SignedIn(unifiedId, token);

    /// <summary>The answer a save call gets before any request is built, or null when it may go out:
    /// Offline mode = <c>offline</c> (no request leaves), no account = <c>sign-in</c>.</summary>
    public static JObject? RefuseBeforeSend(string? unifiedId, string? token, bool offlineMode)
    {
        if (offlineMode) return Result(false, ReasonOffline);
        if (!SignedIn(unifiedId, token)) return Result(false, ReasonSignIn);
        return null;
    }

    public static JObject Result(bool ok, string? reason = null)
    {
        var o = new JObject { ["type"] = "save-result", ["ok"] = ok };
        if (!ok) o["reason"] = reason ?? ReasonRefused;
        return o;
    }

    /// <summary>A transport answer that is not a 200 with a body: no answer or a server error = offline,
    /// 401 = sign in, anything else (409 merged, 400, 429) = refused. Null for a 200.</summary>
    private static string? TransportReason(int status) => status switch
    {
        0 => ReasonOffline,
        >= 500 => ReasonOffline,
        401 => ReasonSignIn,
        200 => null,
        _ => ReasonRefused,
    };

    private static JObject? Parse(string? body)
    {
        try { return string.IsNullOrWhiteSpace(body) ? null : JToken.Parse(body) as JObject; }
        catch (JsonException) { return null; }
    }

    private static string KnownReason(JObject o)
        => (string?)o["reason"] switch
        {
            ReasonTooBig => ReasonTooBig,
            ReasonNone => ReasonNone,
            _ => ReasonRefused,
        };

    /// <summary><c>PUT</c> answer -&gt; <c>save-result</c>.</summary>
    public static JObject MapPut(int status, string? body)
    {
        if (TransportReason(status) is { } why) return Result(false, why);
        var o = Parse(body);
        if (o == null) return Result(false, ReasonRefused);
        return o["ok"] is JValue { Type: JTokenType.Boolean } ok && (bool)ok! ? Result(true) : Result(false, KnownReason(o));
    }

    /// <summary><c>GET</c> answer -&gt; <c>save-incoming {save, savedAt, from}</c>, or <c>save-result</c> with
    /// <c>none</c> / a refusal. A slot that does not look like a save the page can read is refused.</summary>
    public static JObject MapGet(int status, string? body)
    {
        if (TransportReason(status) is { } why) return Result(false, why);
        var o = Parse(body);
        if (o == null) return Result(false, ReasonRefused);
        if (!(o["ok"] is JValue { Type: JTokenType.Boolean } ok && (bool)ok!)) return Result(false, KnownReason(o));
        var save = o["save"] is JValue { Type: JTokenType.String } s ? (string?)s : null;
        var from = (string?)o["from"];
        if (string.IsNullOrEmpty(save) || save.Length > TcRules.MaxSaveChars
            || o["savedAt"] is not JValue { Type: JTokenType.Integer or JTokenType.Float } at
            || from is not ("web" or "desktop"))
            return Result(false, ReasonRefused);
        return new JObject
        {
            ["type"] = "save-incoming",
            ["save"] = save,
            ["savedAt"] = Convert.ToInt64(at.Value, System.Globalization.CultureInfo.InvariantCulture),
            ["from"] = from,
        };
    }
}
