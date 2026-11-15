using System;
using Serilog;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The panel's one way to say "this lives in the launcher now" (declutter wave 2,
    /// 2026-11-15). Games left the Play page for the launcher's Games door; tools go to its Tools
    /// door. Callers never reach for the launcher themselves: they call <see cref="Open"/> with a
    /// door name and this seam decides what opens.
    ///
    /// <para>Pure on purpose (no UI types) so both heads share it. The head injects the Lockdown
    /// check (<see cref="IsLockedDown"/>) and the <see cref="Fallback"/>, which does what the
    /// panel's "Back to CC Labs" button does: the panel tucks away and the launcher comes up.
    /// <see cref="OpenLauncherDoor"/> is wired by the launcher side when it can open a door page
    /// directly; until then the fallback runs.</para>
    /// </summary>
    public static class PanelDoorLink
    {
        public const string Games = "games";
        public const string Tools = "tools";

        /// <summary>Shows the launcher opened on a door: "games" or "tools". Null = not wired.</summary>
        public static Action<string>? OpenLauncherDoor { get; set; }

        /// <summary>What runs while <see cref="OpenLauncherDoor"/> is null, or when it throws: the
        /// head's "Back to CC Labs" button. Null = the head has not wired it (nothing opens).</summary>
        public static Action? Fallback { get; set; }

        /// <summary>True while Lockdown runs. Injected by the head; a throwing check counts as locked.</summary>
        public static Func<bool> IsLockedDown { get; set; } = () => false;

        /// <summary>"games" or "tools" (case and spaces forgiven); anything else is Games.</summary>
        public static string Normalize(string? door) =>
            string.Equals(door?.Trim(), Tools, StringComparison.OrdinalIgnoreCase) ? Tools : Games;

        /// <summary>Open the launcher on a door. Never throws. During Lockdown it does nothing and
        /// answers false: the launcher's way back counts as an escape attempt (AGENTS.md rule 6),
        /// and a Drop-day card or a palette row is not the user trying to leave. False too when
        /// neither the hook nor the fallback is wired.</summary>
        public static bool Open(string? door)
        {
            var key = Normalize(door);
            bool locked;
            try { locked = IsLockedDown(); }
            catch (Exception ex) { Log.Warning(ex, "PanelDoorLink: Lockdown check failed"); locked = true; }
            if (locked) return false;
            var hook = OpenLauncherDoor;
            if (hook != null)
            {
                try { hook(key); return true; }
                catch (Exception ex) { Log.Warning(ex, "PanelDoorLink: door {Door} hook failed", key); }
            }
            var fallback = Fallback;
            if (fallback == null) { Log.Debug("PanelDoorLink: door {Door} has no fallback wired", key); return false; }
            try { fallback(); }
            catch (Exception ex) { Log.Warning(ex, "PanelDoorLink: fallback for {Door} failed", key); }
            return true;
        }
    }
}
