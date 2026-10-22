using System.Linq;
using ConditioningControlPanel.Services.PieceByPiece;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    private string? _featuredSpectate;

    // WPF HostSpectate: a fresh board consumes its target in settings, an existing ready board
    // receives a friend intent. Games already belong to PanicSurfaces' "games" funnel.
    internal static GameWindow? HostSpectate(string matchId)
    {
        if (!PbpWatchRules.IsMatchId(matchId)) return null;
        GameWindow? live;
        lock (Open) live = Open.FirstOrDefault(w => w.Spec.Id == PbpId);
        if (live != null)
        {
            live._featuredSpectate = matchId;
            if (live.IsReady) live.SendFeaturedSpectate();
            live.Activate();
            return live;
        }
        var window = new GameWindow(Games[PbpId]) { _featuredSpectate = matchId };
        window.Load(Server());
        window.Show();
        return window;
    }

    private void SendFeaturedSpectate()
    {
        if (_featuredSpectate is not { } id) return;
        _featuredSpectate = null;
        Post(new JObject { ["type"] = "pbp:friend", ["mode"] = "spectate", ["matchId"] = id });
    }
}
