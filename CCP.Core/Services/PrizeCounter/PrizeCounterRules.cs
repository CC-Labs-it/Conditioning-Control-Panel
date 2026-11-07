using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.PrizeCounter;

/// <summary>The Prize Counter host rules both heads share (moved out of WPF PrizeCounterHostService):
/// tabs, close reasons, the sign-in pin, the Rain V2 gate on server bodies and the motion wire.</summary>
public static class PrizeCounterRules
{
    public const string GameId = "prizecounter";

    /// <summary>Why the counter went, as an opener under it hears it (panic and app-exit never hand focus back).</summary>
    public const string CloseBack = "back", ClosePanic = "panic", CloseWear = "wear", CloseAppExit = "app-exit", CloseFailed = "failed";

    /// <summary>The counter's tabs, in the server's order (CONTRACT "Server body").</summary>
    public static readonly string[] Tabs = { "rain-clothes", "rain-hair", "kart", "chess", "profile", "desk", "prime", "backroom" };

    /// <summary>A door's tab as the page knows it: <c>rain</c> is the clothes tab, an unknown name is null.</summary>
    public static string? CounterTab(string? tab)
    {
        if (string.IsNullOrWhiteSpace(tab)) return null;
        var t = tab.Trim().ToLowerInvariant();
        return t == "rain" ? "rain-clothes" : Array.IndexOf(Tabs, t) >= 0 ? t : null;
    }

    /// <summary>An open counter asked again reloads only when the door names a different tab.</summary>
    public static bool ReopensOnTab(string? current, string? wanted)
        => wanted != null && !string.Equals(current, wanted, StringComparison.Ordinal);

    /// <summary>The page's own <c>close {reason}</c>: only <c>panic</c> is kept, anything else is back.</summary>
    public static string PageCloseReason(string? reason) => reason == ClosePanic ? ClosePanic : CloseBack;

    /// <summary>Somebody other than the pinned account is signed in now: pin them and load again.</summary>
    public static bool SignedInSince(string? pinned, string? now)
        => !string.IsNullOrEmpty(now) && !string.Equals(pinned, now, StringComparison.Ordinal);

    /// <summary>Each game under the counter hears the close once; one that throws cannot starve the rest.</summary>
    public static void NotifyOpeners(IEnumerable<Action<string>> openers, string reason)
    {
        foreach (var opener in openers)
            try { opener(reason); }
            catch (Exception ex) { Log.Warning("PrizeCounter: an opener's close hook threw: {E}", ex.Message); }
    }

    /// <summary>True for a Rain row id (<c>rain.*</c>) or a Rain tab id (<c>rain-*</c>).</summary>
    public static bool IsRainId(string? id)
        => id != null && (id.StartsWith("rain.", StringComparison.Ordinal) || id.StartsWith("rain-", StringComparison.Ordinal));

    /// <summary>Rain V2 gate off: the Rain tabs go and any other <c>rain.*</c> row becomes a hole (null keeps the
    /// slot layout), also in a refusal's fresh <c>state</c>. Gate on, or no tabs: untouched. Never throws.</summary>
    public static JObject? HideRain(JObject? body, bool rainOn)
    {
        if (rainOn || body == null) return body;
        var copy = (JObject)body.DeepClone();
        HideRainTabs(copy);
        if (copy["state"] is JObject state) HideRainTabs(state);
        return copy;
    }

    private static void HideRainTabs(JObject body)
    {
        if (body["tabs"] is not JArray tabs) return;
        foreach (var tab in tabs.OfType<JObject>().Where(t => IsRainId(t["id"]?.Type == JTokenType.String ? (string?)t["id"] : null)).ToList())
            tab.Remove();
        foreach (var rows in tabs.SelectTokens("[*].shelves[*].rows").OfType<JArray>())
            for (int i = 0; i < rows.Count; i++)
                if (rows[i] is JObject row && row["id"]?.Type == JTokenType.String && ((string?)row["id"])!.StartsWith("rain.", StringComparison.Ordinal))
                    rows[i] = JValue.CreateNull();
    }

    /// <summary>A Rain <c>shop-buy</c> with the gate off: the host answers <c>unavailable</c>, never relays it.</summary>
    public static bool RefusesBuy(string? op, JObject? body, bool rainOn)
        => !rainOn && op == PrizeCounterRelay.OpBuy
           && body?["itemId"]?.Type == JTokenType.String && ((string?)body["itemId"])!.StartsWith("rain.", StringComparison.Ordinal);

    /// <summary>The page's three levels. Gentle visuals caps motion at less, as it does for UI motion.</summary>
    public static string MotionWire(Models.MotionLevel m, bool gentle) => m switch
    {
        Models.MotionLevel.Off => "off",
        Models.MotionLevel.Reduced => "less",
        _ => gentle ? "less" : "full",
    };
}
