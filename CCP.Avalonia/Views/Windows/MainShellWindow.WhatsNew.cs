using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Platform;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF MainWindow.Marquee.cs:296: stamp a fresh profile silently; upgrades wait on the ladder.
        internal static void OfferWhatsNewIfNeeded(AppSettings settings, string version, Action save, Func<Task> show)
        {
            if (string.IsNullOrEmpty(settings.LastSeenVersion))
            {
                settings.LastSeenVersion = version;
                save();
                return;
            }
            if (settings.LastSeenVersion == version) return;
            StartupLadder.EnqueueModal("whats-new", 30, async () =>
            {
                await show();
                settings.LastSeenVersion = version;
                save();
            });
        }
    }
}
