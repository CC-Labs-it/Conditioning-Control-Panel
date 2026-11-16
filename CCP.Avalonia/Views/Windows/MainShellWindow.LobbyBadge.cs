// The Social rail badge's own lobby lease (WPF ConditioningControlPanel/MainWindow/MainWindow.Lobby.cs:30-71):
// held while the panel is on screen, released when it hides to the tray, minimises or closes, so the
// "N open" pill keeps moving after the launcher hides and stops its own poll.

using System;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Builds the badge's poll on first show (the same wire set as the Lobby page).</summary>
        internal static Func<LobbyPoll> CreateLobbyBadgePoll = AvailableSubjectsTabView.BuildLobby;
        private LobbyPoll? _lobbyBadgePoll;
        private IDisposable? _lobbyBadgeLease;

        /// <summary>WPF MainWindow.Lobby.cs:39 LobbyBadgeWanted.</summary>
        internal static bool LobbyBadgeWanted(bool visible, WindowState state) =>
            visible && state != WindowState.Minimized;

        /// <summary>WPF HookLobbyBadge (:43): follow visibility and window state, release on close.</summary>
        private void HookLobbyBadge()
        {
            PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty || e.Property == WindowStateProperty) SyncLobbyBadgeLease();
            };
            Closed += (_, _) =>
            {
                _lobbyBadgeLease?.Dispose();
                _lobbyBadgeLease = null;
                if (_lobbyBadgePoll == null) return;
                _lobbyBadgePoll.Changed -= OnLobbyBadgeChanged;
                _lobbyBadgePoll.Dispose();
                _lobbyBadgePoll = null;
            };
            SyncLobbyBadgeLease();
        }

        private void SyncLobbyBadgeLease()
        {
            try
            {
                if (LobbyBadgeWanted(IsVisible, WindowState))
                {
                    if (_lobbyBadgePoll == null)
                    {
                        _lobbyBadgePoll = CreateLobbyBadgePoll();
                        _lobbyBadgePoll.Changed += OnLobbyBadgeChanged;
                    }
                    _lobbyBadgeLease ??= _lobbyBadgePoll.Watch();
                }
                else
                {
                    _lobbyBadgeLease?.Dispose();
                    _lobbyBadgeLease = null;
                }
            }
            catch (Exception ex) { Log.Debug("Lobby badge lease: {E}", ex.Message); }
        }

        // WPF MainWindow.Lobby.cs:99: every round sets the rail badge.
        private void OnLobbyBadgeChanged(LobbySnapshot snapshot)
        {
            try { NavBadges.Set(NavSections.Social, snapshot.OpenCount); } catch { }
        }
    }
}
