using System;

namespace ConditioningControlPanel.Services.UI
{
    /// <summary>
    /// The panel's one way to say "this lives in the launcher now" (declutter wave 2,
    /// 2026-11-15). Games left the Play page for the launcher's Games door; tools go to its Tools
    /// door. Callers never reach for the launcher themselves: they call <see cref="Open"/> with a
    /// door name and this seam decides what opens.
    ///
    /// <para><see cref="OpenLauncherDoor"/> is wired by the launcher side (the main session sets
    /// it to the launcher's door API when the branches merge). Until it is, the fallback does what
    /// the panel's "Back to CC Labs" button does: the panel tucks away and the launcher comes up
    /// (refused during Lockdown, like the button).</para>
    /// </summary>
    public static class PanelDoorLink
    {
        public const string Games = "games";
        public const string Tools = "tools";

        /// <summary>Shows the launcher opened on a door: "games" or "tools". Null = not wired.</summary>
        public static Action<string>? OpenLauncherDoor { get; set; }

        /// <summary>What runs while <see cref="OpenLauncherDoor"/> is null, or when it throws.</summary>
        internal static Action Fallback { get; set; } = DefaultFallback;

        /// <summary>"games" or "tools" (case and spaces forgiven); anything else is Games.</summary>
        public static string Normalize(string? door) =>
            string.Equals(door?.Trim(), Tools, StringComparison.OrdinalIgnoreCase) ? Tools : Games;

        /// <summary>True while Lockdown runs. Swappable so tests need no app.</summary>
        internal static Func<bool> IsLockedDown { get; set; } = () => App.Lockdown?.IsActive == true;

        /// <summary>Open the launcher on a door. Never throws. During Lockdown it does nothing and
        /// answers false: the launcher's way back counts as an escape attempt (AGENTS.md rule 6),
        /// and a Drop-day card or a palette row is not the user trying to leave.</summary>
        public static bool Open(string? door)
        {
            var key = Normalize(door);
            if (Locked()) return false;
            var hook = OpenLauncherDoor;
            if (hook != null)
            {
                try { hook(key); return true; }
                catch (Exception ex) { App.Logger?.Warning(ex, "PanelDoorLink: door {Door} hook failed", key); }
            }
            try { Fallback(); }
            catch (Exception ex) { App.Logger?.Warning(ex, "PanelDoorLink: fallback for {Door} failed", key); }
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
            catch (Exception ex) { App.Logger?.Warning(ex, "PanelDoorLink: Lockdown check failed"); return true; }
        }

        /// <summary>Presses the panel's title-bar "Back to CC Labs" button (<c>BtnBackToLauncher</c>),
        /// so the fallback is exactly that button: same handler, same Lockdown refusal, and a
        /// disabled button (a running Lab session) stays a no-op. No launcher type is named here.</summary>
        private static void DefaultFallback()
        {
            var button = App.MainWindowRef?.BtnBackToLauncher;
            if (button == null || !button.IsEnabled) return;
            button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, button));
        }
    }
}
