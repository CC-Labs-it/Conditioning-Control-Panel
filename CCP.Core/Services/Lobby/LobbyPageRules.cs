using System;

namespace ConditioningControlPanel.Services.Lobby;

/// <summary>WPF AvailableSubjectsTabView's responsive Lobby and row presentation rules.</summary>
public static class LobbyPageRules
{
    public const double NarrowWidth = 900;
    public static bool IsNarrowWidth(double width) => width > 0 && width < NarrowWidth;
    public static int Column(int index) => Math.Clamp(index, 0, 2);
    public static string LabelKey(LobbyGame game) => game switch
    {
        LobbyGame.Chess => "lobby_game_chess", LobbyGame.Goon => "lobby_game_goon", _ => "lobby_game_remote"
    };
    public static string Id(LobbyRow row) => row.Game + "|" + (row.Key ?? row.HostName + "|" + row.OpponentName) + "|" + row.State;
    public static string Title(LobbyRow row, Func<string, string> loc) =>
        row.State == LobbyRowState.Playing && !string.IsNullOrEmpty(row.OpponentName)
            ? string.Format(loc("lobby_row_versus"), row.HostName, row.OpponentName) : row.HostName;
    /// <summary>The card's second line: game label, then the settings summary when there is one.</summary>
    public static string Line(string gameLabel, string summary) => summary.Length > 0 ? gameLabel + " · " + summary : gameLabel;
    public static bool HasButton(LobbyRow row) => row.CanJoin || row.CanWatch;
    /// <summary>The join would be refused by the game's gate: the button shows with a lock.</summary>
    public static bool Locked(LobbyRow row, LobbyGates gates) => row.CanJoin && !gates.CanJoin(row.Game);
    public static double ButtonOpacity(bool locked) => locked ? 0.55 : 1.0;
}
