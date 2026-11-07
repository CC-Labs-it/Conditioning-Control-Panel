using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>LIVE, the adapter: reads <see cref="App.Lobby"/>'s snapshot (kept fresh by the
    /// panel's own lease while it is on screen) and joins through each game's own door.</summary>
    public sealed class LiveProvider : BillboardProviderBase
    {
        private bool _hooked;

        public override string Id => "live";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            var lobby = App.Lobby;
            if (lobby == null) return Array.Empty<BillboardCardSpec>();
            if (!_hooked) { lobby.Changed += _ => RaiseChanged(); _hooked = true; }
            var card = LiveCards.Decide(lobby.Snapshot, Loc);
            return card == null ? Array.Empty<BillboardCardSpec>() : new[] { card };
        });

        public override void Invoke(string actionTarget)
        {
            if (!LiveCards.TryParseTarget(actionTarget, out var game, out var key)) return;
            try
            {
                if (BackRoomApi.AppIdentity() == null) { App.MainWindowRef?.OpenUnifiedLoginDialog(); return; }
                if (game == LobbyGame.Chess) PieceByPieceHostService.JoinOpenTable(key);
                else GoonHostService.Launch(duckMainWindow: true, joinCode: key);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Billboard] live join {Game} failed", game); }
        }
    }
}
