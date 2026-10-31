using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Back Room prize ownership on this head. WPF asks <c>PrizeGrants.IsGranted</c>, backed by
    /// OwnershipService snapshots from BackRoomApi, provider validation and profile sync.
    /// The other source is WPF's DEBUG-only <c>CCP_PRIZE_GRANTS</c> override:
    /// same patterns (exact id, <c>prefix.*</c>, <c>*</c>), and a Release build never reads it.
    /// Parsing is Core's, so an empty id never matches and a pattern holding whitespace or a
    /// misplaced <c>*</c> is dropped.
    /// </summary>
    internal static class PrizeOwnership
    {
        /// <summary>Core prize ids, shared with the WPF ownership gate.</summary>
        internal const string FlashDriftBounce = PrizeGrants.FlashDriftBounce;
        internal const string FlashPendulum = PrizeGrants.FlashPendulum;
        internal const string JackpotRemix = PrizeGrants.JackpotRemix;
        internal const string BubbleRain = PrizeGrants.BubbleRain;
        internal const string BubbleSpiralIn = PrizeGrants.BubbleSpiralIn;
        internal static bool OwnsFlashV2() => IsGranted(FlashDriftBounce) || IsGranted(FlashPendulum);

        /// <summary>The DEBUG override spec, read once; always null in Release.</summary>
        internal static readonly string? OverrideSpec = OwnershipService.ReadDebugOverride();

        /// <summary>Test seam: the owned-check every caller uses.</summary>
        internal static Func<string, bool> IsGranted = id => Matches(OverrideSpec, id) || Held(id);

        // Reuse Core's account/revision rules and change detection for every snapshot source.
        private static readonly OwnershipService HeldOwnership = new(
            () => FriendsHead.Identity()?.UnifiedId, overrideSpec: null, raise: action => action());

        static PrizeOwnership() => HeldOwnership.OwnershipChanged += (_, _) => Changed?.Invoke();

        /// <summary>WPF GrantsChanged: snapshot grants changed, possibly off the UI thread.</summary>
        internal static event Action? Changed;

        internal static bool Held(string id) => HeldOwnership.IsGranted(id);

        internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
            => HeldOwnership.ApplySnapshot(accountId, revision, grants);

        // WPF App.xaml.cs:529 and ProfileSyncService.cs:573-697,1874.
        // Configure before any restored provider validates or the first profile sync starts.
        internal static void Seed()
        {
            ProviderSubscription.PrizesSink = ApplyFeed;
            BackRoomApi.DefaultApplyPrizes = ApplySnapshot;
            CoreAccount.UnifiedIdentityChanged -= OnIdentityChanged;
            CoreAccount.UnifiedIdentityChanged += OnIdentityChanged;
        }

        private static void OnIdentityChanged(object? sender, EventArgs e) =>
            Dispatcher.UIThread.Post(() => { _feedAccount = null; HeldOwnership.Clear(); });

        private static string? _feedAccount;

        // Copy before queueing: a provider response can be changed or the player can sign out
        // before this UI job runs. Missing blocks keep ownership; explicit empty grants revoke.
        internal static void ApplyFeed(string? requestedFor, string? answeredFor, PrizesBlock? prizes, string source)
        {
            if (string.IsNullOrEmpty(requestedFor)) return;
            var grants = string.Equals(requestedFor, answeredFor, StringComparison.Ordinal)
                ? prizes?.Grants?.ToArray() : null;
            var revision = prizes?.Revision ?? 0;
            Dispatcher.UIThread.Post(() =>
            {
                if (!string.Equals(FriendsHead.Identity()?.UnifiedId, requestedFor, StringComparison.Ordinal)) return;
                if (_feedAccount != null && !string.Equals(_feedAccount, requestedFor, StringComparison.Ordinal))
                    HeldOwnership.Clear();
                _feedAccount = requestedFor;
                if (grants != null) ApplySnapshot(requestedFor, revision, grants);
            });
        }

        /// <summary>Test cleanup (P02): forget every held snapshot.</summary>
        internal static void ResetHeld() { _feedAccount = null; HeldOwnership.Clear(); }

        /// <summary>WPF V2Badges.FlashOwned: any flashes v2 prize (both motions or Jackpot Remix).</summary>
        internal static bool FlashV2Owned() =>
            IsGranted(FlashDriftBounce) || IsGranted(FlashPendulum) || IsGranted(JackpotRemix);

        /// <summary>WPF V2Badges.BubbleOwned: either ambient bubble motion.</summary>
        internal static bool BubbleV2Owned() => IsGranted(BubbleRain) || IsGranted(BubbleSpiralIn);

        /// <summary>Core OwnershipService's override rules over one comma list.</summary>
        internal static bool Matches(string? spec, string id) =>
            !string.IsNullOrEmpty(id) && OwnershipService.MatchesOverride(OwnershipService.ParseOverride(spec), id);
    }
}
