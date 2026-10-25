// PORTED from ConditioningControlPanel/MainWindow/MainWindow.CompanionFx.cs (95 lines).
//
// The WPF file has no ambient effect left: the hero disc's breathe belongs to
// CompanionHeroCard.StartAmbientLoop (parked by CompanionRoomView off the tab's own visibility) and
// the connect sheen was deleted with the AI-brain card it swept. What remains, and is ported here:
//   * the tab's visibility hook - leaving writes the two drawers' open/closed state, entering
//     restores it (WPF restores once at startup, InitializePatreonTab -> RestoreCompanionSectionStates,
//     MainWindow.Patreon.cs:1266; here on every show, same result since each hide writes first) and offers an upgrader
//     the Awareness v2 consent dialog (WPF EnsureAwarenessV2Consent, MainShellWindow.CompanionRoom.cs);
//   * the mod repaint - CoreMods.ModChanged re-reads the roster (WPF UpdateCompanionCardsUI). The
//     hero re-reads its own name/portrait off the same event (CompanionHeroCard.OnModChanged).
//   * per-card assigned-prompt names and tooltips refresh through that same roster path,
//     using Core InstalledCommunityPrompts (also WPF's installed-name reader).
// WPF's IsIncomingTab guard is not needed: this head's ShowTab has no fade-out re-show.

using System;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Models;
using Avalonia.Controls;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _companionFxInitialized;
        /// <summary>WPF restores the drawers at load; here that waits for the first show, so until
        /// then the room holds defaults and persisting them would overwrite the saved states.</summary>
        private bool _companionDrawersRestored;

        /// <summary>WPF MainWindow.CompanionFx.cs:32, called from CompanionTabView on a real
        /// visibility edge of the tab (the room itself stays collapsed).</summary>
        internal void OnCompanionTabVisibilityChanged(bool visible, CompanionRoomView room)
        {
            try
            {
                var map = CoreSettings.Current?.CompanionSectionOpen;
                if (!visible)
                {
                    // Per-toggle would be a settings save on every click of a rarely-opened drawer.
                    if (map != null && _companionDrawersRestored) { room.PersistDrawerStates(map); CoreSettings.Save(); }
                }
                else
                {
                    if (map != null) room.RestoreDrawerStates(map);
                    _companionDrawersRestored = true;
                }

                InitializeCompanionFx(room);

                if (visible) EnsureAwarenessV2Consent();
            }
            catch (Exception ex) { Log.Debug("OnCompanionTabVisibilityChanged: {E}", ex.Message); }
        }

        /// <summary>WPF MainWindow.Patreon.cs:1546 RevealCompanionWorkshopCell: under v2 the room is
        /// collapsed, so each Workshop cell opens the Companion page that hosts it.</summary>
        internal void RevealCompanionWorkshopCell(string? cellKey)
        {
            if (CompanionPageForWorkshopCell(cellKey) is { } page) { ShowTab(page); return; }
            try { RoomView()?.RevealWorkshop(cellKey); }
            catch (Exception ex) { Log.Debug("RevealCompanionWorkshopCell: {E}", ex.Message); }
        }

        /// <summary>WPF MainWindow.Patreon.cs:1561: the page that hosts a Workshop cell (null = none;
        /// the Awareness cell lives on the Awareness tab).</summary>
        internal static string? CompanionPageForWorkshopCell(string? cellKey) => cellKey switch
        {
            null => "personality",
            CompanionRoomAnchors.WorkshopRosterCell => "personality",
            CompanionRoomAnchors.WorkshopBehaviorCell => "companionai",
            CompanionRoomAnchors.WorkshopTriggersCell => "companionai",
            CompanionRoomAnchors.WorkshopCommunityCell => "personality",
            CompanionRoomAnchors.WorkshopLibraryCell => "companionlinks",
            _ => null,
        };

        /// <summary>WPF WindowChrome.cs:176: quitting while still on the tab is not a hide edge.</summary>
        private void PersistCompanionDrawerStatesOnExit()
        {
            var map = CoreSettings.Current?.CompanionSectionOpen;
            var room = Named<Tabs.CompanionTabView>("CompanionTab")?.FindControl<CompanionRoomView>("Room");
            if (map == null || room == null || !_companionDrawersRestored) return;
            room.PersistDrawerStates(map);
            CoreSettings.Save();
        }

        private void InitializeCompanionFx(CompanionRoomView room)
        {
            if (_companionFxInitialized) return;
            _companionFxInitialized = true;
            void OnModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(room.RefreshRoster);
            CoreMods.ModChanged += OnModChanged;
            // A static event must not pin a closed shell (P41).
            Closed += (_, _) => CoreMods.ModChanged -= OnModChanged;
        }
    }
}
