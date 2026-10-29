using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// What ducking_recovery.json must hold, and which live audio session a stored entry belongs to
    /// (sleepysana, 2026-10-20: "when i close the app, my sliders on my computer are messed up").
    ///
    /// Two holes, both leaving another app at the ducked volume, which Windows remembers per app:
    /// <list type="bullet">
    /// <item>An unduck that could not reach an app (its session gone or silent) parks the original
    /// volume for a 3 minute retry, then deleted the recovery file anyway. Closing the app inside that
    /// window, or the window running out, lost the volume for good. The file now holds every
    /// original not yet handed back: the live ducks AND the parked ones (<see cref="ToPersist"/>).</item>
    /// <item>The launch-time recovery matched by PID only, so an app that restarted since (a new PID)
    /// was never restored, and a recycled PID could stamp one app's volume onto another. It now
    /// matches the way the retry already did: same PID and same name, else the same name
    /// (<see cref="Match"/>).</item>
    /// </list>
    /// Pure, pinned by DuckRecoveryRuleTests. In Core since 7.2: the Avalonia head journals its own ducks
    /// with it (Platform/DuckRecovery.cs); the stored value is a volume (WPF) or a stream record (pactl).
    /// </summary>
    public static class DuckRecoveryRule
    {
        /// <summary>The originals to keep on disk: live ducks win over parked entries for one PID.</summary>
        public static Dictionary<int, T> ToPersist<T>(IReadOnlyDictionary<int, T> ducked, IReadOnlyDictionary<int, T> parked)
        {
            var all = new Dictionary<int, T>();
            foreach (var kv in parked) all[kv.Key] = kv.Value;
            foreach (var kv in ducked) all[kv.Key] = kv.Value;
            return all;
        }

        /// <summary>
        /// The live PID a stored entry restores, or null. <paramref name="live"/> maps each live
        /// session's PID to its process name (empty when unknown). Our own process never matches.
        /// </summary>
        public static int? Match(int storedPid, string? storedName, IReadOnlyDictionary<int, string> live, int ownPid)
        {
            var known = !string.IsNullOrEmpty(storedName);
            if (storedPid != ownPid && storedPid != 0 && live.TryGetValue(storedPid, out var nameNow))
            {
                // Same PID: the same app unless both names are known and differ (a recycled PID).
                if (!known || string.IsNullOrEmpty(nameNow) || string.Equals(nameNow, storedName, StringComparison.OrdinalIgnoreCase))
                    return storedPid;
            }
            if (!known) return null;
            foreach (var kv in live)
            {
                if (kv.Key == ownPid || kv.Key == 0) continue;
                if (string.Equals(kv.Value, storedName, StringComparison.OrdinalIgnoreCase)) return kv.Key;
            }
            return null;
        }
    }
}
