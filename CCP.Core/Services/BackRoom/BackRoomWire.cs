using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.BackRoom;

// WPF BackRoomHostService.cs:46,58,156,171,723.
public static class BackRoomWire
{
    public static object GatesWire(AppSettings? s) => new {
        flash = true, subliminal = true, spiral = true, brainDrain = true,
        tunnel = s?.BackRoomTunnel ?? false, melt = s?.BackRoomMelt ?? false
    };
    public static string IntensityChoiceWire(AppSettings? s) =>
        (s?.BackRoomFxIntensity ?? BackRoomFxIntensity.Normal).ToString().ToLowerInvariant();
    public static string IntensityWire(AppSettings? s, MotionLevel motion) =>
        motion == MotionLevel.Full ? IntensityChoiceWire(s) : "calm";
    public static string EffectiveMediaSource(AppSettings? s)
    {
        var chosen = s?.BackRoomMediaSource ?? "auto";
        var resolved = chosen == "auto" ? (s?.MediaSource ?? "local") : chosen;
        return resolved is "online" or "mixed" && !(s?.HasRemoteMediaConsent ?? false) ? "local" : resolved;
    }
    public static object MediaWire(AppSettings? s) => new {
        source = s?.BackRoomMediaSource ?? "auto", effective = EffectiveMediaSource(s),
        subs = (s?.BackRoomMediaSubs ?? new List<string>()).ToArray(),
        off = (s?.BackRoomMediaSubsOff ?? new List<string>()).ToArray(),
        cap = AppSettings.BackRoomMediaSubCap, consented = s?.HasRemoteMediaConsent ?? false,
        ratio = s?.RemoteMediaRatio ?? 30
    };
}
