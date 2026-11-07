using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Avalonia.Views.Games;
internal sealed partial class GameWindow
{
    private JObject? _lobbyIntent;
    private string? _spectateInit;
    internal static GameWindow? OpenChessLobby(string mode, string? key = null)
    {
        if (mode == "join" && !PbpWatchRules.IsTableId(key)) return null;
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
