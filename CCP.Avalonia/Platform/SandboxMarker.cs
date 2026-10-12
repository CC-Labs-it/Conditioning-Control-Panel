using System;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// A CCP_USERDATA_DIR sandbox (tests, kc live checks) must never be mistaken for the real app: SandboxNet refuses
    /// every remote host there, so sign-in and everything online "does not work" by design. Every window title gets
    /// <see cref="Prefix"/>; the shell's title bar shows a SANDBOX badge (MainShellWindow.axaml, TxtSandboxBadge).
    /// </summary>
    internal static class SandboxMarker
    {
        internal const string Prefix = "[SANDBOX] ";
        private static bool _installed;

        /// <summary>The title rule is on while true. The app's own startup turns it on; test hosts are sandboxes too
        /// but never start the app, so their window-title assertions are untouched unless a test opts in.</summary>
        internal static bool Enabled;

        /// <summary>True when CorePaths honoured CCP_USERDATA_DIR (it installs the network guard as it does).</summary>
        public static bool Active
        {
            get { _ = CorePaths.UserData; return SandboxNet.Active; }
        }

        internal static void Install()
        {
            if (!Active) return;
            Enabled = true;
            if (_installed) return;
            _installed = true;
            Window.TitleProperty.Changed.AddClassHandler<Window>((w, e) =>
            {
                if (Enabled && e.NewValue is string t && !t.StartsWith(Prefix, StringComparison.Ordinal)) w.Title = Prefix + t;
            });
        }
    }
}
