using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>Account-scoped server ownership from profile reads, sync, provider validation and purchase replies.
    /// Core OwnershipService rejects other accounts and older revisions. Nothing is persisted or uploaded.</summary>
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

        /// <summary>Any accepted snapshot for the signed-in account, changed or not: the chess page re-sends its grants.</summary>
        internal static event Action? GrantsChanged;

        // Reuse Core's account/revision rules and change detection for every snapshot source.
        private static readonly OwnershipService HeldOwnership = new(
            () => FriendsHead.Identity()?.UnifiedId, overrideSpec: null, raise: action => action());
        private static readonly object Gate = new();
        private static string? _account;

        static PrizeOwnership() => HeldOwnership.OwnershipChanged += (_, _) => Changed?.Invoke();

        /// <summary>WPF GrantsChanged: snapshot grants changed, possibly off the UI thread.</summary>
        internal static event Action? Changed;

        internal static bool Held(string id) => HeldOwnership.IsGranted(id);

        internal static string[] Grants => HeldOwnership.Grants.ToArray();

        internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
        {
            lock (Gate)
            {
                if (accountId != FriendsHead.Identity()?.UnifiedId) return;
                NoteAccount(accountId);
                HeldOwnership.ApplySnapshot(accountId, revision, grants);
            }
            GrantsChanged?.Invoke();
        }

        /// <summary>A newly signed-in account starts with nothing held, and the change is raised.</summary>
        internal static void NoteAccount(string? account)
        {
            lock (Gate)
            {
                if (account == null || account != FriendsHead.Identity()?.UnifiedId || account == _account) return;
                _account = account;
                HeldOwnership.Clear();
            }
        }

        /// <summary>Profile and sync replies carry the account's prize snapshot; only the asked account's answer lands.</summary>
        internal static void ApplyProfile(string? requested, string? answered, ConditioningControlPanel.Models.PrizesBlock? prizes, string source)
        {
            lock (Gate)
            {
                if (requested == null || requested != FriendsHead.Identity()?.UnifiedId) return;
                NoteAccount(requested);
                if (requested != answered || prizes?.Grants == null) return;
                HeldOwnership.ApplySnapshot(requested, prizes.Revision, prizes.Grants);
            }
            GrantsChanged?.Invoke();
        }

        /// <summary>The sync push's feed (WPF ProfileSyncService.PrizeFeed): the requested account's grants.</summary>
        internal static void ApplySyncSnapshot(string requestedFor, PrizesBlock? prizes)
        {
            if (prizes?.Grants != null) ApplySnapshot(requestedFor, prizes.Revision, prizes.Grants);
        }

        /// <summary>Provider validation replies: only an answer for the account that asked lands.</summary>
        internal static void ApplyValidateSnapshot(string? requestedFor, string? answeredFor, PrizesBlock? prizes, string source)
        {
            if (!string.IsNullOrEmpty(requestedFor) && requestedFor == answeredFor)
                ApplySyncSnapshot(requestedFor, prizes);
        }

        /// <summary>The current account's held grants, or null before any snapshot for it (cannot validate yet).</summary>
        internal static ISet<string>? KnownGrants()
        {
            var current = FriendsHead.Identity()?.UnifiedId;
            lock (Gate) return current != null && current == _account && HeldOwnership.Revision > 0
                ? new HashSet<string>(HeldOwnership.Grants, StringComparer.Ordinal) : null;
        }

        /// <summary>Logout and test cleanup: forget every held snapshot.</summary>
        internal static void ResetHeld() { lock (Gate) { _account = null; HeldOwnership.Clear(); } }

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
