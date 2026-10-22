using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>FEATURED MATCH, the adapter: reads <see cref="PbpFeaturedFeed"/> (polled only while
    /// the board is on screen, MainWindow.DashboardBillboard) and watches through the Lobby's door.</summary>
    public sealed class FeaturedProvider : BillboardProviderBase
    {
        private bool _hooked;

        public override string Id => "featured";

        public override IEnumerable<BillboardCardSpec> Current(BillboardContext context) => Safe(() =>
        {
            if (!_hooked) { PbpFeaturedFeed.Changed += RaiseChanged; _hooked = true; }
            var card = FeaturedCards.Decide(PbpFeaturedFeed.Current, Loc);
            return card == null ? Array.Empty<BillboardCardSpec>() : new[] { card };
        });

        /// <summary>Same gate as the Lobby's Watch: signed out (or chess shut) asks to sign in.</summary>
        public override void Invoke(string actionTarget)
        {
            if (!FeaturedCards.TryParseTarget(actionTarget, out var matchId)) return;
            try
            {
                var gates = MainWindow.CurrentLobbyGates();
                if (!gates.SignedIn || !gates.CanJoin(LobbyGame.Chess)) { App.MainWindowRef?.OpenUnifiedLoginDialog(); return; }
                PieceByPieceHostService.HostSpectate(matchId);
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "[Billboard] featured watch failed"); }
        }
    }
}
