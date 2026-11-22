using System;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>What the watchdog does on a tick.</summary>
internal enum TcWatchVerdict { Keep, Close }

/// <summary>
/// The pure half of <see cref="TcHostService"/>'s window: the init frame (CONTRACT.md section 2 + 3.1,
/// Contract v2 caps from <see cref="TcRules.Caps"/>), the host frames it sends, the watchdog and the
/// close handshake timings. No I/O and no App statics.
/// </summary>
internal static class TcHostRules
{
    /// <summary>Before the page's ready: close when nothing at all came for this long.</summary>
    public static readonly TimeSpan BootLimit = TimeSpan.FromSeconds(45);

    /// <summary>After ready: the page beats every 5 s; this much silence closes the window. Generous on
    /// purpose: a heavy scene load may hold the page's thread, and the contract has no ping.</summary>
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromSeconds(90);

    /// <summary>The page answers <c>close</c> with <c>exit-done</c> within this (CONTRACT.md section 2).</summary>
    public static readonly TimeSpan CloseAnswer = TimeSpan.FromMilliseconds(300);

    /// <summary>The host force-closes this long after it asked, answer or not.</summary>
    public static readonly TimeSpan CloseForce = TimeSpan.FromMilliseconds(800);

    public const string ReasonPanic = "panic";
    public const string ReasonFocus = "focus";

    public static TcWatchVerdict Watch(bool ready, TimeSpan sinceAnyMessage, TimeSpan sinceHeartbeat)
        => ready
            ? (sinceHeartbeat >= SilenceLimit ? TcWatchVerdict.Close : TcWatchVerdict.Keep)
            : (sinceAnyMessage >= BootLimit ? TcWatchVerdict.Close : TcWatchVerdict.Keep);

    /// <summary>A graceful close (the page's own exit, or a host <c>close</c>) ends on <c>exit-done</c> or
    /// at <see cref="CloseForce"/>, whichever comes first.</summary>
    public static bool CloseNow(bool exitDone, TimeSpan sinceAsked) => exitDone || sinceAsked >= CloseForce;

    /// <summary>The active mod as the page's <c>mod</c> id; community mods read as default.</summary>
    public static string ModWire(string? activeModId) => activeModId switch
    {
        BuiltInMods.BambiSleepId => "bambi",
        BuiltInMods.SissyHypnoId => "sissy",
        BuiltInMods.DronificationId => "drone",
        BuiltInMods.LockedId => "locked",
        _ => "default",
    };

    public static string MotionWire(MotionLevel level) => level switch
    {
        MotionLevel.Reduced => "reduced",
        MotionLevel.Off => "off",
        _ => "full",
    };

    /// <summary>
    /// The boot frame. <paramref name="v2Caps"/> comes from <see cref="TcRules.Caps"/>; the v1 caps that
    /// later slices light up (pictures, Sparkles, toys) are sent off here, so the page draws none of
    /// them yet. Words are the Circe recorded bank for every mod (never synthetic speech).
    /// </summary>
    public static JObject InitFrame(string? lang, MotionLevel motion, bool gentle, string? activeModId, JObject v2Caps)
    {
        var caps = (JObject)v2Caps.DeepClone();
        caps["scrolller"] = new JObject { ["consent"] = false };
        caps["sparkles"] = false;
        caps["words"] = new JObject { ["bank"] = "circe" };
        caps["toys"] = false;
        caps["toysConnected"] = false;
        return new JObject
        {
            ["type"] = "init",
            ["protocol"] = TcRules.Protocol,
            ["lang"] = string.IsNullOrWhiteSpace(lang) ? "en" : lang,
            ["reduced"] = motion != MotionLevel.Full,
            ["motion"] = MotionWire(motion),
            ["gentle"] = gentle,
            ["caps"] = caps,
            ["mod"] = ModWire(activeModId),
        };
    }

    public static JObject Suspend(bool on, string reason)
        => new() { ["type"] = "suspend", ["on"] = on, ["reason"] = reason is ReasonPanic or ReasonFocus or "arcade" ? reason : ReasonFocus };

    public static JObject Close(string reason) => new() { ["type"] = "close", ["reason"] = reason };
}
