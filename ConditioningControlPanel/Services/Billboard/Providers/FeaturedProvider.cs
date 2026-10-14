using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Services.PieceByPiece;

namespace ConditioningControlPanel.Services.Billboard.Providers
{
    /// <summary>
    /// FEATURED MATCH, the pure half (2026-10-14): the one chess match the server features right
    /// now, as a Live card. "White vs Black", the move count, "N watching" when anyone is, and a
    /// Watch button that opens the match as a spectator. A pinned match reads "featured match", a
    /// rotated one "live now". No match, no card.
    /// </summary>
    public static class FeaturedCards
    {
        /// <summary>One stable id, so a snooze quiets featured matches as a whole.</summary>
        public const string CardId = "live.featured";

        public const string WatchPrefix = "watch:";

        /// <summary>The art: the chess game's own feature picture.</summary>
        public const string Art = "features/piecebypiece.png";

        public static BillboardCardSpec? Decide(PbpFeaturedReply? reply, Func<string, string> loc)
        {
            var m = reply?.Match;
            if (m == null || !PbpWatchRules.IsMatchId(m.Id)) return null;
            var line = m.Watchers > 0
                ? CardText.F(loc, "billboard_card_featured_line_watching", m.Moves, m.Watchers)
                : CardText.F(loc, "lobby_moves", m.Moves);
            return new BillboardCardSpec(CardId, BillboardCardKind.Live, 3,
                Eyebrow(reply!.Pinned, loc),
                CardText.F(loc, "lobby_row_versus", m.White, m.Black),
                line, CardHues.Live, CardArt.Poster, Art,
                new BillboardAction(BillboardActionKind.Callback, WatchPrefix + m.Id, loc("lobby_btn_watch")));
        }

        public static string Eyebrow(bool pinned, Func<string, string> loc) =>
            loc(pinned ? "billboard_card_featured_pinned" : "billboard_card_featured_rotating");

        /// <summary>Reads a Callback target back to a match id. False for anything this provider did not issue.</summary>
        public static bool TryParseTarget(string? target, out string matchId)
        {
            matchId = string.Empty;
            if (target == null || !target.StartsWith(WatchPrefix, StringComparison.Ordinal)) return false;
            var id = target.Substring(WatchPrefix.Length);
            if (!PbpWatchRules.IsMatchId(id)) return false;
            matchId = id;
            return true;
        }
    }

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
