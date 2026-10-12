// PORTED (seam) from ConditioningControlPanel/MainWindow/MainWindow.Leash.cs:95-108 (LeashBlocksGames,
// PresentLeashGateFromLauncher; WPF 7.1.5). The launcher asks this before a game tile opens
// (LauncherHost.cs:360-366). No Leash service on this head yet, so nothing seeds the providers and the
// gate never blocks; when the service lands it sets them (GateDue != null / CheckLeashGate).

using System;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>True while a leash punishment is due (WPF LeashLocator.Service()?.GateDue != null).</summary>
        internal static Func<bool>? LeashGateDueProvider;

        /// <summary>Brings the leash gate up on the panel (WPF CheckLeashGate after clearing the snooze).</summary>
        internal static Action<MainShellWindow>? PresentLeashGateProvider;

        /// <summary>True while a punishment stands between a launcher tile and its game.</summary>
        internal static bool LeashBlocksGames
        {
            get { try { return LeashGateDueProvider?.Invoke() == true; } catch { return false; } }
        }

        /// <summary>The launcher's way in: a tile press is a deliberate "I want to play", so the gate comes up now.</summary>
        internal void PresentLeashGateFromLauncher()
        {
            try { PresentLeashGateProvider?.Invoke(this); }
            catch (Exception ex) { Serilog.Log.Debug("Leash gate from launcher failed: {E}", ex.Message); }
        }
    }
}
