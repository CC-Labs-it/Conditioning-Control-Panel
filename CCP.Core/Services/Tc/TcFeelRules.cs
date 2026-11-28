using System;
using ConditioningControlPanel.Models;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>
/// The pure half of T&amp;C's toy pulses and live settings (CONTRACT.md sections 1 and 2): which app setting
/// becomes which <c>setting</c> key, the frame itself, and the pulse length a toy gets. The window part is
/// <c>TcHostService.Feel.cs</c>.
/// </summary>
internal static class TcFeelRules
{
    public const string KeyGentle = "gentle";
    public const string KeyMotion = "motion";
    public const string KeyConsent = TcMediaRules.SettingKey;

    /// <summary>The Avalonia host's station on the shared Back Room pulse director.</summary>
    public const string Station = "tc";

    /// <summary>The <c>setting</c> key an <see cref="AppSettings"/> property change moves, or null for every
    /// other property. The picture gate reads four of them, so each of those re-sends the consent key.</summary>
    public static string? KeyFor(string? propertyName) => propertyName switch
    {
        nameof(AppSettings.GentleVisuals) => KeyGentle,
        nameof(AppSettings.MotionLevel) => KeyMotion,
        nameof(AppSettings.RemoteMediaConsented) or nameof(AppSettings.FypOnlineConsented)
            or nameof(AppSettings.MediaSource) or nameof(AppSettings.OfflineMode) => KeyConsent,
        _ => null,
    };

    public static JObject SettingFrame(string key, JToken value)
        => new() { ["type"] = "setting", ["key"] = key, ["value"] = value };

    /// <summary>Buttplug commands carry about a second of latency: a short pulse is stretched (as Just Drop
    /// does), never past the gate's ceiling.</summary>
    public static int DurationFor(int ms, bool buttplug)
        => Math.Clamp(buttplug ? ms * 2 : ms, 0, TcFrameGate.MaxHapticMs);

    /// <summary>A pulse plays only with a level above zero and a length above zero.</summary>
    public static bool Plays(double level, int ms) => level > 0 && ms > 0;
}
