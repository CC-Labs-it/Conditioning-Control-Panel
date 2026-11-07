// PORTED from WPF MainWindow.SubscribeStar.cs. AccountSettingsSection owns the provider card
// and login/logout; App.axaml.cs owns the existing veil/vault refresh and Patreon.cs the celebration.
// WPF MainWindow.SubscribeStar.cs:28-48 and MainWindow.UiUpdates.cs:910-928:
// repaint the effective AI choice and refresh Programs. Takeover uses the base overlay
// in MainShellWindow.Patreon.cs, not the legacy locked/unlocked panels.
using System;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Windows;
public partial class MainShellWindow
{
    private bool _subscribeStarClosed;

    /// <summary>WPF InitializeSubscribeStarTab / OnSubscribeStarTierChanged: post to the UI thread,
    /// read the combined entitlement, and release the captured provider when the shell closes.</summary>
    private void InitializeSubscribeStarDependents()
    {
        var provider = Platform.AccountSeed.SubscribeStar;
        void Changed(object? sender, PatreonTier tier) => Dispatcher.UIThread.Post(() =>
        {
            if (!_subscribeStarClosed) RefreshSubscribeStarDependents();
        });
        if (provider != null) provider.TierChanged += Changed;
        // MainWindow_Loaded's UpdateUnlockablesVisibility paints hidden controls as well.
        Opened += (_, _) => RefreshSubscribeStarDependents();
        Closed += (_, _) =>
        {
            _subscribeStarClosed = true;
            if (provider != null) provider.TierChanged -= Changed;
        };
    }

    private void RefreshSubscribeStarDependents()
    {
        if (_subscribeStarClosed) return;
        try
        {
            var room = Named<CompanionTabView>("CompanionTab")?.FindControl<CompanionRoomView>("Room");
            if (room?.FindControl<AiPermissionsGrid>("PermissionsZone") is { } permissions)
            {
                // WPF #1307: paint the EFFECTIVE switch under the control's loading guard;
                // an entitlement read must never erase the user's saved choice.
                permissions.SyncFromSettings();
                permissions.ApplyTierGate();
            }
            RefreshProgramsTab(); // preserves the tab's own deferred rebuild while hidden (P07)
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "SubscribeStar tier refresh failed"); }
    }
}
