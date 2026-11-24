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
            if (Locked()) return false;
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

        /// <summary>The line shown when Lockdown refuses a door: the launcher veil's own words
        /// ("Lockdown is on. The launcher waits."), so no new copy.</summary>
        public const string LockdownLineKey = "launcher_lockdown_veil";

        /// <summary>True when <paramref name="tabKey"/> opens a launcher door (the Games pill,
        /// "play") and Lockdown refuses it right now. ccp-bugs #1701: the pill must not ask
        /// "Open Games?" for a trip that cannot happen; it says why instead.</summary>
        public static bool RefusedNow(string? tabKey) =>
            NavSections.LauncherDoorFor(tabKey) != null && Locked();

        private static bool Locked()
        {
            try { return IsLockedDown(); }
            catch (Exception ex) { Log.Warning(ex, "PanelDoorLink: Lockdown check failed"); return true; }
        }
    }
}
