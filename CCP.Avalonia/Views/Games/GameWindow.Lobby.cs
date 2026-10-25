using System.Linq;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private JObject? _lobbyIntent;
    private string? _spectateInit;

    // WPF PieceByPieceHostService.Friends: fresh boards receive intents after identity;
    // a fresh spectate target belongs in settings instead. Reuse the panic-registered game host.
    internal static GameWindow? OpenChessLobby(string mode, string? key = null)
    {
        if (mode == "join" && !(key is { Length: > 2 and <= 64 } && key.StartsWith("p_", System.StringComparison.Ordinal)
            && key.Skip(2).All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'))) return null;
        if (mode == "spectate" && !PbpWatchRules.IsMatchId(key)) return null;
        if (mode != "join" && mode != "spectate" && mode != "host") return null;
        var window = Launch(PbpId);
        if (window == null) return null;
        if (mode == "spectate" && !window._pbpSettingsPosted)
        {
            window._lobbyIntent = null;
            window._spectateInit = key;
        }
        else
        {
            window._spectateInit = null;
            window._lobbyIntent = new JObject { ["type"] = "pbp:friend", ["mode"] = mode };
            if (mode == "join") window._lobbyIntent["target"] = key;
            if (mode == "spectate") window._lobbyIntent["matchId"] = key;
            if (window._pbpIdentityPosted) window.PostLobbyIntent();
        }
        return window;
    }

    private void PostLobbyIntent()
    {
        if (_lobbyIntent is not { } intent) return;
        _lobbyIntent = null;
        Post(intent);
    }
}
