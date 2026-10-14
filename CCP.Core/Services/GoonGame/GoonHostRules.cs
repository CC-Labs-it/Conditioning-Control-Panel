using System;

// The Goon host's pure rules, moved out of the WPF GoonHostService (which stays the WebView2
// host). Every head that hosts the page asks these, so the gates and the watchdog verdict have
// one copy. App.Patreon / App.UnifiedUserId became CoreAccount, which WPF seeds from the same.

namespace ConditioningControlPanel.Services.GoonGame
{
    internal static class GoonHostRules
    {
        /// <summary>Seconds of "beats arriving, frame counter frozen, page says it is visible"
        /// before the page is treated as visually frozen. Was 10 s until ccp-bugs #1326 (a busy
        /// machine closed a live match twice at 11 s); 20 s is still far short of how long the
        /// owner sat in front of a dead picture on 2026-08-04.</summary>
        internal const double PaintStallSeconds = 20;

        /// <summary>The heartbeat watch's own tick interval. A tick that arrives much later than
        /// this means the UI thread itself was stalled, see <see cref="PaintStallVerdict"/>.</summary>
        internal const double WatchIntervalSeconds = 5;

        /// <summary>Slack on <see cref="WatchIntervalSeconds"/> before a late tick counts as a UI
        /// stall, and the UI-stall age (VideoDiag.UiStallMs on WPF) that counts as one too.</summary>
        internal const double UiStallSlackSeconds = 3;

        internal enum PaintStallCall { Healthy, UiStalled, Stalled }

        /// <summary>Pure decision for the paint-stall watchdog (ccp-bugs #1326).
        /// <paramref name="frozenSeconds"/> is how long the page's frame counter has not moved,
        /// <paramref name="tickGapSeconds"/> how long since the watch last ticked, and
        /// <paramref name="uiStallMs"/> the app's own UI-thread stall age. A late tick or a stalled
        /// UI thread means the page's heartbeats were stuck behind us in the dispatcher queue, so
        /// the frozen figure is not evidence about the page: it is never a stall, the clock
        /// restarts. Only a frozen counter seen by a healthy UI thread past
        /// <see cref="PaintStallSeconds"/> is one.</summary>
        internal static PaintStallCall PaintStallVerdict(double frozenSeconds, double tickGapSeconds, long uiStallMs)
        {
            if (tickGapSeconds > WatchIntervalSeconds + UiStallSlackSeconds
                || uiStallMs > UiStallSlackSeconds * 1000)
                return PaintStallCall.UiStalled;
            return frozenSeconds > PaintStallSeconds ? PaintStallCall.Stalled : PaintStallCall.Healthy;
        }

        /// <summary>Is the opponent's niche pool fetched here? Only an explicit "off" for the Goon
        /// Game declines; never set means joining a Scrolller game is the opt-in.</summary>
        internal static bool PeerFetchAllowed(bool? goonMediaOnline) => goonMediaOnline != false;

        /// <summary>May the page send its OWN media to the opponent? TIER 1+ (any paid tier or
        /// whitelist), re-gated 2026-08-06.
        ///
        /// Sending was free-for-every-seat for one day (owner call 2026-08-04): the free model
        /// fixed the "invited free player throws blanks" bug from the first play-test, but left
        /// an un-gated seat able to push arbitrary media at an opponent, and abuse won over
        /// generosity. The blank-attack lesson survives as UI instead: the lobby transfer row
        /// names the perk (S.lobby.transferOff) rather than greying out in silence. The server's
        /// <c>media_send</c> verdict on /invite and /join computes the same bar
        /// (computeEffectiveTier &gt;= 1), so the two verdicts agree; voice notes ride this same
        /// cap. Consent still gates the lane per-match (the lobby checkbox both sides must
        /// tick), and receiving was never gated.</summary>
        internal static bool TransferAllowed() => CoreAccount.HasPremiumAccess;

        /// <summary>May the page MINT a room? ANY PATRON (owner call 2026-09-24): a paying tier
        /// or the whitelist, the same bar as sending (<see cref="TransferAllowed"/>). The server
        /// enforces it at <c>/v2/goon/invite</c> (403 <c>no_host_access</c> below
        /// <c>computeEffectiveTier &gt;= 1</c>) and this is the same verdict computed locally, so
        /// the title screen can lock Host instead of routing the player to a refusal. The page
        /// reads this with <c>=== true</c>, so a host that predates the flag leaves Host enabled
        /// and falls back to the server's answer.</summary>
        internal static bool HostingAllowed() => CoreAccount.HasPremiumAccess;

        /// <summary>May the page JOIN a room? Every SIGNED-IN account (owner call 2026-09-24),
        /// free included. The server's <c>/v2/goon/join</c> answers 401 <c>signin</c> without an
        /// account; this is only the local read of "is there one". Practice never asks.</summary>
        internal static bool JoiningAllowed() => !string.IsNullOrEmpty(CoreAccount.UnifiedUserId);

        /// <summary>A Discord snowflake is digits and nothing else. Applied to the SERVER's value on
        /// arrival and again on the way to the shell, because that string is about to become part
        /// of a shell-executed URL.</summary>
        internal static bool IsSnowflake(string? id)
        {
            if (string.IsNullOrEmpty(id) || id!.Length > 20) return false;
            foreach (var c in id) if (c < '0' || c > '9') return false;
            return true;
        }

        /// <summary>Trim + hard-cap a page-supplied string before it rides an outgoing request body.</summary>
        internal static string SafeShort(string? v, int max)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            var t = v!.Trim();
            return t.Length > max ? t.Substring(0, max) : t;
        }
    }
}
