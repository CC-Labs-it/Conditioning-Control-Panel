using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The live half of <see cref="EntitlementTierRule"/>: takes an <c>effective_tier</c> reading
    /// from the heartbeat, the read-before-write profile read or a focus refresh, and on a rise
    /// stamps the grace windows, saves, and tells the app. Buy on the site, click back into the
    /// app, unlocked about a second later.
    /// </summary>
    public static class EntitlementTierSync
    {
        /// <summary>Minimum gap between two focus refreshes.</summary>
        public static readonly TimeSpan FocusCheckGap = TimeSpan.FromSeconds(20);

        /// <summary>Raised on the UI thread, BEFORE TierChanged, with the new tier (1 or 2).</summary>
        public static event Action<int>? TierRaised;

        /// <summary>Raised on the UI thread after a reading the lapse rows care about was applied
        /// (a Lower verdict stored, or cleared again).</summary>
        public static event Action? ReadingApplied;

        /// <summary>
        /// True once the server has answered with a tier THIS run. The lapse row and the lapse
        /// greeting wait for it: a remembered lower reading from days ago says nothing about
        /// someone who renewed on the web since, and telling a paying subscriber their plan
        /// closed is the one mistake this feature must not make (review 2026-10-13).
        /// </summary>
        public static bool ServerAnsweredThisRun { get; internal set; }

        private static DateTime _lastFocusCheckUtc = DateTime.MinValue;
        private static int _focusInFlight;
        private static int? _lastLowerLogged;

        /// <summary>Only for V2 accounts: a unified id and a token to prove it.</summary>
        private static bool SignedInWithV2(AppSettings? s)
            => s != null && !string.IsNullOrEmpty(s.UnifiedId) && !string.IsNullOrEmpty(s.AuthToken);

        /// <summary>Offer a server reading. Safe from any thread; never throws.</summary>
        public static void Offer(int? serverTier, string source)
        {
            try
            {
                if (serverTier is null) return;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,
                    new Action(() => ApplyOnUi(serverTier, source)));
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("Entitlement offer ({Source}) skipped: {Error}", source, ex.Message);
            }
        }

        private static void ApplyOnUi(int? serverTier, string source)
        {
            try
            {
                var settings = App.Settings?.Current;
                if (!SignedInWithV2(settings)) return;
                if (serverTier.HasValue) ServerAnsweredThisRun = true;

                var verdict = EntitlementTierRule.Decide(settings!.PatreonTier, serverTier);

                // The lapse rows' only input from here (ConversionRules.DecideLapse): a lower
                // reading is remembered, an agreeing or higher one forgets it. Never an access input.
                var seen = EntitlementTierRule.ServerTierSeenAfter(verdict, serverTier, settings.ServerTierSeen);
                if (seen != settings.ServerTierSeen)
                {
                    settings.ServerTierSeen = seen;
                    if (verdict != EntitlementVerdict.Rise) App.Settings?.Save(); // a rise saves below
                    try { ReadingApplied?.Invoke(); }
                    catch (Exception ex) { App.Logger?.Debug("[Entitlement] ReadingApplied handler failed: {Error}", ex.Message); }
                }

                switch (verdict)
                {
                    case EntitlementVerdict.Rise:
                        var tier = serverTier!.Value;
                        App.Logger?.Information("[Entitlement] {Source}: server tier {Server} above stored {Local}, unlocking now",
                            source, tier, settings.PatreonTier);
                        EntitlementTierRule.ApplyRise(settings, tier, DateTime.UtcNow);
                        App.Settings?.Save();
                        _lastLowerLogged = null;
                        CheckoutWatch.Disarm();
                        try { TierRaised?.Invoke(tier); }
                        catch (Exception ex) { App.Logger?.Warning(ex, "[Entitlement] TierRaised handler failed"); }
                        App.Patreon?.NotifyEntitlementRaised(tier >= 2 ? PatreonTier.Level2 : PatreonTier.Level1);
                        break;

                    case EntitlementVerdict.Lower:
                        // Recorded, never applied (see EntitlementVerdict.Lower). Logged once per value.
                        if (_lastLowerLogged != serverTier)
                        {
                            _lastLowerLogged = serverTier;
                            App.Logger?.Information("[Entitlement] {Source}: server tier {Server} below stored {Local}; keeping the grace window",
                                source, serverTier, settings.PatreonTier);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "[Entitlement] apply failed ({Source})", source);
            }
        }

        /// <summary>Raised on the UI thread after the profile's conversion fields changed
        /// (patreon_member, paid_until, post_grants).</summary>
        public static event Action? ExtrasApplied;

        /// <summary>Stores the profile's conversion fields (brief C2) on the UI thread. Never throws.</summary>
        public static void OfferExtras(Startup.Conversion.ProfileExtrasReading? reading)
        {
            try
            {
                if (reading == null) return;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                {
                    try
                    {
                        var settings = App.Settings?.Current;
                        if (!SignedInWithV2(settings) || !Startup.Conversion.ProfileExtras.Apply(settings!, reading)) return;
                        App.Settings?.Save();
                        App.DailyFree?.NotifyInputsChanged();
                        ExtrasApplied?.Invoke();
                    }
                    catch (Exception ex) { App.Logger?.Debug("[Entitlement] extras apply failed: {Error}", ex.Message); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("[Entitlement] extras offer skipped: {Error}", ex.Message); }
        }

        /// <summary>
        /// Stores the server's Patreon reading (patreon_is_active + patreon_confirmed_at) on the UI
        /// thread: a fresh confirmation re-stamps the 14-day grace, whatever the tier verdict said
        /// (a lapsed patron whose stored tier equals the server's never re-stamped before). Never throws.
        /// </summary>
        public static void OfferPatreon(PatreonActiveReading? reading, string source)
        {
            try
            {
                if (reading == null) return;
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted) return;
                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal, new Action(() =>
                {
                    try
                    {
                        var settings = App.Settings?.Current;
                        if (!SignedInWithV2(settings)) return;
                        var before = EffectiveTierNow();
                        if (!PatreonActiveRule.Restamp(settings!, reading, DateTime.UtcNow)) return;
                        App.Logger?.Information("[Entitlement] {Source}: Patreon confirmed active at {At:u}, grace now {Until:u}",
                            source, reading.ConfirmedAtUtc, settings!.PatreonPremiumValidUntil);
                        App.Settings?.Save();
                        // Access came back (a lapsed grace): the same refresh a rise gets.
                        var after = EffectiveTierNow();
                        if (after > before)
                        {
                            CheckoutWatch.Disarm();
                            App.Patreon?.NotifyEntitlementRaised(after >= 2 ? PatreonTier.Level2 : PatreonTier.Level1);
                        }
                    }
                    catch (Exception ex) { App.Logger?.Debug("[Entitlement] patreon reading failed: {Error}", ex.Message); }
                }));
            }
            catch (Exception ex) { App.Logger?.Debug("[Entitlement] patreon offer skipped: {Error}", ex.Message); }
        }

        /// <summary>
        /// The main window came to the front. At most once per <see cref="FocusCheckGap"/>, read
        /// the profile off the UI thread and offer its tier. Fire and forget.
        /// </summary>
        public static void OnAppFocused()
        {
            try
            {
                var settings = App.Settings?.Current;
                if (!SignedInWithV2(settings) || settings!.OfflineMode) return;
                var now = DateTime.UtcNow;
                if (now - _lastFocusCheckUtc < FocusCheckGap) return;
                _lastFocusCheckUtc = now;
                _ = RefreshAsync("focus");
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Entitlement] focus hook failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Any app window came to the front (Application.Activated). Within
        /// <see cref="CheckoutWatch.WatchFor"/> of a checkout link, re-read the server tier (and
        /// re-validate a Patreon sign-in), at most once per <see cref="CheckoutWatch.Gap"/>.
        /// </summary>
        public static void OnAppActivatedAfterCheckout()
        {
            try
            {
                if (App.Settings?.Current?.OfflineMode == true || !CheckoutWatch.TakeCheck(DateTime.UtcNow)) return;
                App.Logger?.Debug("[Entitlement] back from a checkout: re-reading the tier");
                _lastFocusCheckUtc = DateTime.UtcNow;
                _ = RefreshAsync("checkout");
                var patreon = App.Patreon;
                if (patreon?.IsAuthenticated == true)
                    _ = Task.Run(async () =>
                    {
                        try { await patreon.ValidateSubscriptionAsync(forceRefresh: true); }
                        catch (Exception ex) { App.Logger?.Debug("[Entitlement] checkout validate failed: {Error}", ex.Message); }
                    });
            }
            catch (Exception ex) { App.Logger?.Debug("[Entitlement] checkout hook failed: {Error}", ex.Message); }
        }

        /// <summary>
        /// Reads the profile off the UI thread and offers every reading on it. True when the
        /// server answered. One read at a time (a second caller gets false at once).
        /// </summary>
        internal static async Task<bool> RefreshAsync(string source)
        {
            var settings = App.Settings?.Current;
            if (!SignedInWithV2(settings) || settings!.OfflineMode) return false;
            if (Interlocked.Exchange(ref _focusInFlight, 1) == 1) return false;
            var unifiedId = settings.UnifiedId!;
            try
            {
                return await Task.Run(async () =>
                {
                    var node = await new V2AuthService().GetUserProfileNodeAsync(unifiedId);
                    if (node == null) return false;
                    Offer(EntitlementTierRule.ParseTier(node["effective_tier"]), source);
                    OfferPatreon(PatreonActiveRule.Parse(node), source);
                    OfferExtras(Startup.Conversion.ProfileExtras.Parse(node));
                    return true;
                });
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[Entitlement] {Source} refresh failed: {Error}", source, ex.Message);
                return false;
            }
            finally
            {
                Interlocked.Exchange(ref _focusInFlight, 0);
            }
        }

        /// <summary>Effective tier right now: 2 Prime, 1 Basic (an invite week is not a plan), else 0.</summary>
        public static int EffectiveTierNow()
        {
            var p = App.Patreon;
            if (p == null) return 0;
            if (p.HasLabAccess) return 2;
            return p.HasPremiumAccess && !p.IsInviteWeekOnly ? 1 : 0;
        }

        /// <summary>
        /// The "I just subscribed" button: re-reads the server profile and re-validates a Patreon
        /// sign-in now, waits for the readings to land, and says what it found.
        /// </summary>
        public static async Task<JustSubscribedOutcome> CheckNowAsync()
        {
            var before = EffectiveTierNow();
            var settings = App.Settings?.Current;
            var patreon = App.Patreon;
            var signedIn = SignedInWithV2(settings) || patreon?.IsAuthenticated == true;
            var reached = false;
            try
            {
                if (SignedInWithV2(settings) && !settings!.OfflineMode)
                {
                    _lastFocusCheckUtc = DateTime.UtcNow;
                    reached = await RefreshAsync("just-subscribed");
                }
                if (patreon?.IsAuthenticated == true)
                {
                    try { await patreon.ValidateSubscriptionAsync(forceRefresh: true); reached = true; }
                    catch (Exception ex) { App.Logger?.Debug("[Entitlement] just-subscribed validate failed: {Error}", ex.Message); }
                }
                // The readings land through BeginInvoke at Normal: one more Normal pass runs after them.
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null && !dispatcher.HasShutdownStarted)
                    await dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (Exception ex) { App.Logger?.Debug("[Entitlement] just-subscribed check failed: {Error}", ex.Message); }
            var outcome = JustSubscribedRule.Decide(signedIn, reached, before, EffectiveTierNow());
            App.Logger?.Information("[Entitlement] just-subscribed check: {Outcome}", outcome);
            return outcome;
        }
    }
}
