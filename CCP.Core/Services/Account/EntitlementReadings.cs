using System;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>What one server tier reading did to the settings.</summary>
    public readonly record struct EntitlementReadingResult(EntitlementVerdict Verdict, bool Closed, bool Rose, bool Dirty);

    /// <summary>
    /// One head's running state for <see cref="EntitlementTierRule.ApplyReading"/>: the lower reading
    /// waiting for its second look, per account. The settings half of WPF EntitlementTierSync.ApplyOnUi,
    /// for a head without it. The head saves, repaints and marshals; nothing here touches a window.
    /// </summary>
    public sealed class EntitlementReadings
    {
        private string? _pendingUid;
        private GracePending? _pending;

        /// <param name="held">True while a session or a game owns the screen: a close waits for it.</param>
        public EntitlementReadingResult Apply(AppSettings settings, int? serverTier, Func<bool> held, DateTime nowUtc)
        {
            // Only a V2 account with a token (WPF EntitlementTierSync.SignedInWithV2).
            if (serverTier is null || string.IsNullOrEmpty(settings.UnifiedId) || string.IsNullOrEmpty(settings.AuthToken))
                return new EntitlementReadingResult(EntitlementVerdict.NoSignal, false, false, false);

            var verdict = EntitlementTierRule.Decide(settings.PatreonTier, serverTier);

            // A different account starts its own confirmation.
            if (_pendingUid != settings.UnifiedId)
            {
                _pendingUid = settings.UnifiedId;
                _pending = null;
            }
            var outcome = EntitlementTierRule.ApplyReading(settings, verdict, serverTier, _pending, held, nowUtc);
            _pending = outcome.Pending;

            // A close moved the stored tier to the reading, so it is no longer lower.
            var seen = EntitlementTierRule.ServerTierSeenAfter(outcome.Closed ? EntitlementVerdict.Same : verdict, serverTier, settings.ServerTierSeen);
            var seenMoved = seen != settings.ServerTierSeen;
            if (seenMoved) settings.ServerTierSeen = seen;

            var rose = EntitlementTierRule.ApplyRise(settings, serverTier, nowUtc);
            return new EntitlementReadingResult(verdict, outcome.Closed, rose, outcome.Dirty || seenMoved || rose);
        }
    }
}
