// PORTED from ConditioningControlPanel/Services/Account/EntitlementTierSync.cs (126 lines): the live half of
// the entitlement tier. A tier bought on the site unlocks without a sign-in, rises only (Core
// EntitlementTierRule decides; a lower server tier is logged and never applied).
// Feeds on this head: the focus refresh (OnAppFocused, MainShellWindow Activated) and the profile load
// (AccountSeed.LoadProfileCoreAsync, WPF ProfileSyncService read-before-write :1109, via Raised).
// The heartbeat's second reading (WPF ProfileSyncService :751) comes through SyncPush.HeartbeatTierRead.

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    internal static class EntitlementTierSync
    {
        /// <summary>WPF FocusCheckGap: one focus refresh per 20 s at most.</summary>
        public static readonly TimeSpan FocusCheckGap = TimeSpan.FromSeconds(20);

        /// <summary>A rise landed (tier 1 or 2), raised on the UI thread before the providers' TierChanged.</summary>
        public static event Action<int>? TierRaised;

        private static DateTime _lastFocusCheckUtc = DateTime.MinValue;
        private static int _focusInFlight;
        private static int? _lastLowerLogged;

        private static bool SignedInWithV2(AppSettings s)
            => !string.IsNullOrEmpty(s.UnifiedId) && !string.IsNullOrEmpty(s.AuthToken);

        /// <summary>WPF Offer: a server reading of the folded tier, from any thread.</summary>
        public static void Offer(int? serverTier, string source)
        {
            if (serverTier is null) return;
            Dispatcher.UIThread.Post(() => ApplyOnUi(serverTier, source));
        }

        private static void ApplyOnUi(int? serverTier, string source)
        {
            try
            {
                var s = CoreSettings.Current;
                if (!SignedInWithV2(s)) return;
                switch (EntitlementTierRule.Decide(s.PatreonTier, serverTier))
                {
                    case EntitlementVerdict.Rise:
                        var tier = serverTier!.Value;
                        Log.Information("[Entitlement] {Source}: server tier {Server} above stored {Local}, unlocking now",
                            source, tier, s.PatreonTier);
                        EntitlementTierRule.ApplyRise(s, tier, DateTime.UtcNow);
                        CoreSettings.Save();
                        RaisedOnUi(tier);
                        break;
                    case EntitlementVerdict.Lower:
                        if (_lastLowerLogged != serverTier)
                        {
                            _lastLowerLogged = serverTier;
                            Log.Information("[Entitlement] {Source}: server tier {Server} below stored {Local}; keeping the grace window",
                                source, serverTier, s.PatreonTier);
                        }
                        break;
                }
            }
            catch (Exception ex) { Log.Warning(ex, "[Entitlement] apply failed ({Source})", source); }
        }

        /// <summary>The event half of a rise whose settings half (ApplyRise + save) a caller already did.</summary>
        public static void Raised(int tier) => Dispatcher.UIThread.Post(() => RaisedOnUi(tier));

        private static void RaisedOnUi(int tier)
        {
            _lastLowerLogged = null;
            try { TierRaised?.Invoke(tier); }
            catch (Exception ex) { Log.Warning(ex, "[Entitlement] TierRaised handler failed"); }
            AccountSeed.Patreon?.NotifyEntitlementRaised(tier >= 2 ? PatreonTier.Level2 : PatreonTier.Level1);
        }

        /// <summary>WPF OnAppFocused: the user clicked back in (perhaps from the checkout tab); re-read the tier.</summary>
        public static void OnAppFocused()
        {
            try
            {
                var s = CoreSettings.Current;
                if (!SignedInWithV2(s) || s.OfflineMode) return;
                var now = DateTime.UtcNow;
                if (now - _lastFocusCheckUtc < FocusCheckGap) return;
                if (Interlocked.Exchange(ref _focusInFlight, 1) == 1) return;
                _lastFocusCheckUtc = now;
                var unifiedId = s.UnifiedId!;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var node = await AccountSeed.NewV2().GetUserProfileNodeAsync(unifiedId);
                        Offer(EntitlementTierRule.ParseTier(node?["effective_tier"]), "focus");
                    }
                    catch (Exception ex) { Log.Debug("[Entitlement] focus refresh failed: {Error}", ex.Message); }
                    finally { Interlocked.Exchange(ref _focusInFlight, 0); }
                });
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _focusInFlight, 0);
                Log.Debug("[Entitlement] focus hook failed: {Error}", ex.Message);
            }
        }

        internal static void ResetForTests()
        {
            _lastFocusCheckUtc = DateTime.MinValue;
            _lastLowerLogged = null;
        }
    }
}
