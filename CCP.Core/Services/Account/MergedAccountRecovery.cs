using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The wire shape of a merged-account refusal (split-accounts contract, item B/D):
    /// <c>HTTP 409 {"error":"merged","canonical_unified_id":"u_..."}</c>. Pure parsing, no app
    /// state, so it is unit-testable on its own.
    /// </summary>
    internal static class MergedAccountResponse
    {
        public const int ConflictStatus = 409;
        public const string MergedError = "merged";

        /// <summary>
        /// Conservative shape check for a unified id: the server mints <c>u_</c> plus a base36
        /// timestamp plus 12 hex chars (server.js generateUnifiedId), so lower-case alphanumerics
        /// after the prefix. Bounded so a hostile body cannot hand us a megabyte "id".
        /// </summary>
        private static readonly Regex UnifiedIdPattern = new("^u_[a-z0-9]{6,64}$", RegexOptions.Compiled);

        public static bool IsValidUnifiedId(string? id) =>
            !string.IsNullOrEmpty(id) && UnifiedIdPattern.IsMatch(id);

        /// <summary>
        /// True when <paramref name="statusCode"/> + <paramref name="body"/> is a merged-account
        /// refusal carrying a well-formed canonical id. Any other 409 (and every non-409) is false,
        /// so callers keep their existing handling for those.
        /// </summary>
        public static bool TryParse(int statusCode, string? body, out string? canonicalUnifiedId)
        {
            canonicalUnifiedId = null;
            if (statusCode != ConflictStatus || string.IsNullOrWhiteSpace(body)) return false;

            JObject obj;
            try { obj = JObject.Parse(body); }
            catch { return false; }

            if (!string.Equals(obj["error"]?.Type == JTokenType.String ? obj["error"]!.Value<string>() : null,
                    MergedError, StringComparison.Ordinal))
                return false;

            var canonical = obj["canonical_unified_id"]?.Type == JTokenType.String
                ? obj["canonical_unified_id"]!.Value<string>()
                : null;
            if (!IsValidUnifiedId(canonical)) return false;

            canonicalUnifiedId = canonical;
            return true;
        }
    }

    internal enum MergedSwapDecision
    {
        /// <summary>Adopt the canonical id.</summary>
        Swap,
        /// <summary>The canonical id is missing or malformed.</summary>
        IgnoreInvalidId,
        /// <summary>The app already holds the canonical id; nothing to do.</summary>
        IgnoreSameId,
        /// <summary>This session already swapped to that canonical once; never loop.</summary>
        IgnoreAlreadySwapped,
    }

    /// <summary>
    /// Loop guard for the merged swap: at most one swap per app session per canonical id, and
    /// never a swap onto the id already held. Pure state, no app references, thread-safe.
    /// </summary>
    internal sealed class MergedSwapPolicy
    {
        private readonly object _gate = new();
        private readonly HashSet<string> _swappedTo = new(StringComparer.Ordinal);

        public MergedSwapDecision Decide(string? currentUnifiedId, string? canonicalUnifiedId)
        {
            if (!MergedAccountResponse.IsValidUnifiedId(canonicalUnifiedId)) return MergedSwapDecision.IgnoreInvalidId;
            if (string.Equals(currentUnifiedId, canonicalUnifiedId, StringComparison.Ordinal)) return MergedSwapDecision.IgnoreSameId;
            lock (_gate)
            {
                if (_swappedTo.Contains(canonicalUnifiedId!)) return MergedSwapDecision.IgnoreAlreadySwapped;
            }
            return MergedSwapDecision.Swap;
        }

        /// <summary>Records the swap. Returns false if it was already recorded (a concurrent winner).</summary>
        public bool MarkSwapped(string canonicalUnifiedId)
        {
            lock (_gate) return _swappedTo.Add(canonicalUnifiedId);
        }

        public int SwapCount { get { lock (_gate) return _swappedTo.Count; } }
    }

    /// <summary>
    /// Contract D, desktop side. One handler for every authenticated V2 request path that can be
    /// answered with <c>409 merged</c> after the account this desktop was signed into has been
    /// merged into another (the record it keeps writing to is now a tombstone).
    ///
    /// <para>On a merged refusal: stop the sync/heartbeat timers, adopt
    /// <c>canonical_unified_id</c> as the stored unified id, drop the auth token (it belonged to the
    /// tombstone and the canonical never issued it), persist, then re-run the existing provider
    /// sign-in so the canonical mints a fresh token. The identity step is Core's; what only a head
    /// can do (stop its sync, re-run its providers, reload, repaint, offer the sign-in) comes in
    /// through the seams below (WPF seeds them in App startup). Unseeded seams are skipped, so the
    /// identity still moves and the app reads as signed out of the tombstone, which is the state a
    /// lapsed session leaves.</para>
    ///
    /// <para>Single-flight: several timers can receive the same 409 in the same second. The first
    /// caller claims the canonical id in <see cref="MergedSwapPolicy"/> (an atomic add) and the
    /// swap runs ONCE, on a detached task; the rest see
    /// <see cref="MergedSwapDecision.IgnoreAlreadySwapped"/> (or
    /// <see cref="MergedSwapDecision.IgnoreSameId"/> once the swap has landed) and return. The
    /// loop guard is per app session per canonical id, so a canonical that is itself merged later
    /// can still be followed once, but the same hop can never repeat.</para>
    ///
    /// <para>Detached on purpose: some of the doors this is wired into are called from inside
    /// <c>ProfileSyncService.HandleUnauthorizedAsync</c>, which holds a non-reentrant recovery
    /// gate. The swap reloads the profile, and that reload can 401 and want the same gate. Running
    /// the swap inline there would deadlock; running it on its own task cannot.</para>
    /// </summary>
    public static class MergedAccountRecovery
    {
        private static MergedSwapPolicy _policy = new();
        private static readonly SemaphoreSlim SwapGate = new(1, 1);

        /// <summary>Stop anything that writes to the old id (heartbeat, nudge) and forget the loaded profile.</summary>
        public static volatile Action? StopSync;
        /// <summary>Re-stamp the local quest file for the canonical id (same person, merged account).</summary>
        public static volatile Action<string>? StampQuests;
        /// <summary>Re-run the provider sign-in; true when an auth token for the canonical is in hand.</summary>
        public static volatile Func<Task<bool>>? Reauthenticate;
        /// <summary>Read the canonical's profile and restart the heartbeat.</summary>
        public static volatile Func<Task>? ReloadProfile;
        /// <summary>The swap ended: (canonical, reauthed). The head repaints, and offers the sign-in when false.</summary>
        public static volatile Action<string, bool>? Finished;

        /// <summary>Tests: the running swap, a clean claim ledger and a settings instance.</summary>
        internal static Task LastSwap { get; private set; } = Task.CompletedTask;
        internal static Func<Models.AppSettings>? SettingsForTest;
        internal static void ResetForTest() { _policy = new MergedSwapPolicy(); SettingsForTest = null; }
        private static Models.AppSettings Settings => SettingsForTest?.Invoke() ?? CoreSettings.Current;

        /// <summary>
        /// Checks <paramref name="response"/> for a merged refusal and starts the swap if it is one.
        /// Returns true when the response WAS a merged 409 (whether the swap was started or the
        /// guard ignored it): the caller should stop treating the request as an ordinary failure
        /// and not retry it with the old id. Returns false for every other response, untouched.
        ///
        /// <para>Pass <paramref name="body"/> when the caller has already read it; otherwise the
        /// body is read here (only on a 409, so the common path costs nothing).</para>
        /// </summary>
        public static async Task<bool> TryHandleAsync(HttpResponseMessage? response, string? body = null)
        {
            if (response == null) return false;
            var status = (int)response.StatusCode;
            if (status != MergedAccountResponse.ConflictStatus) return false;

            if (body == null)
            {
                try { body = await response.Content.ReadAsStringAsync(); }
                catch { return false; }
            }
            return TryHandle(status, body);
        }

        /// <summary>
        /// Status + body form for callers that read the body themselves. Synchronous: it parses,
        /// decides, claims the canonical id and schedules the swap; nothing here waits on the
        /// network.
        /// </summary>
        public static bool TryHandle(int statusCode, string? body)
        {
            if (!MergedAccountResponse.TryParse(statusCode, body, out var canonical)) return false;

            var current = Settings.UnifiedId ?? CoreAccount.UnifiedUserId;
            var decision = _policy.Decide(current, canonical);
            if (decision == MergedSwapDecision.Swap && !_policy.MarkSwapped(canonical!))
                decision = MergedSwapDecision.IgnoreAlreadySwapped;   // lost the claim to a concurrent caller

            if (decision != MergedSwapDecision.Swap)
            {
                Log.Debug("[MergedAccount] 409 merged for {Current} -> {Canonical} ignored ({Decision})",
                    current, canonical, decision);
                return true;
            }

            LastSwap = Task.Run(() => RunSwapAsync(current, canonical!));
            return true;
        }

        private static async Task RunSwapAsync(string? fromUnifiedId, string canonical)
        {
            // Serialises successive swaps (a canonical that is itself merged later); the claim in
            // TryHandle already guarantees each canonical runs at most once.
            await SwapGate.WaitAsync();
            try
            {
                await SwapAsync(fromUnifiedId, canonical);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[MergedAccount] swap to {Canonical} failed", canonical);
            }
            finally
            {
                SwapGate.Release();
            }
        }

        private static async Task SwapAsync(string? fromUnifiedId, string canonical)
        {
            Log.Information("[MergedAccount] account {From} was merged into {Canonical}; adopting the canonical id and re-signing in",
                fromUnifiedId ?? "(none)", canonical);

            // 1. Timers first, so nothing else writes to the tombstone while we swap. The sync must
            //    also READ the canonical before it PUSHes (same reason as a logout).
            try { StopSync?.Invoke(); }
            catch (Exception ex) { Log.Debug("[MergedAccount] stopping sync failed: {Error}", ex.Message); }

            // 2. Identity: canonical id in, tombstone's token out. Persisted without a cloud backup
            //    (the backup door is one of the ones that just refused us).
            CoreAccount.UnifiedUserId = canonical;
            var settings = Settings;
            settings.UnifiedId = canonical;
            settings.AuthToken = null;
            if (SettingsForTest == null) CoreSettings.Save(suppressCloudBackup: true);

            // The local quest file is stamped with the owning id and is RESET by EnsureOwnedBy on
            // the next profile load when the stamp differs. Same human, merged account: carry it.
            try { StampQuests?.Invoke(canonical); }
            catch (Exception ex) { Log.Debug("[MergedAccount] quest owner restamp failed: {Error}", ex.Message); }

            // 3. Re-run the provider sign-in. The OAuth doors land on the canonical and mint its token.
            bool reauthed = false;
            try { if (Reauthenticate is { } reauth) reauthed = await reauth(); }
            catch (Exception ex) { Log.Warning(ex, "[MergedAccount] provider re-auth threw"); }

            if (reauthed)
            {
                var landed = Settings.UnifiedId;
                if (!string.Equals(landed, canonical, StringComparison.Ordinal))
                    Log.Warning("[MergedAccount] provider re-auth answered {Landed}, expected canonical {Canonical}; keeping the server's answer",
                        landed, canonical);
                else
                    Log.Information("[MergedAccount] re-signed in on {Canonical}", canonical);

                try { if (ReloadProfile is { } reload) await reload(); }
                catch (Exception ex) { Log.Warning(ex, "[MergedAccount] profile reload after swap failed"); }
            }
            else
            {
                // 4. No provider credential (device-code / email session, or the provider's own token
                //    has lapsed): signed out of the tombstone, holding the canonical id with no token.
                Log.Information("[MergedAccount] no provider credential to re-sign in with; offering the sign-in prompt for {Canonical}", canonical);
            }

            try { Finished?.Invoke(canonical, reauthed); }
            catch (Exception ex) { Log.Debug("[MergedAccount] finish hook failed: {Error}", ex.Message); }
        }
    }
}
