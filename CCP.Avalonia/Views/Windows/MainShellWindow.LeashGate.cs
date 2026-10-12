// PORTED (seam) from ConditioningControlPanel/MainWindow/MainWindow.Leash.cs:95-108 (LeashBlocksGames,
// PresentLeashGateFromLauncher; WPF 7.1.5). The launcher asks this before a game tile opens
// (LauncherHost.cs:360-366). Platform/LeashHead seeds LeashGateDueProvider (GateDue != null). The gate
// card that PresentLeashGateProvider brings up is a later unit (LeashGateCard); until it is set a tile
// is never held, because the player could neither do the punishment nor cut the leash from the panel.

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
            get { try { return PresentLeashGateProvider != null && LeashGateDueProvider?.Invoke() == true; } catch { return false; } }
        }

        /// <summary>The launcher's way in: a tile press is a deliberate "I want to play", so the gate comes up now.</summary>
        internal void PresentLeashGateFromLauncher()
        {
            try { PresentLeashGateProvider?.Invoke(this); }
            catch (Exception ex) { Serilog.Log.Debug("Leash gate from launcher failed: {E}", ex.Message); }
        }
    }
}
