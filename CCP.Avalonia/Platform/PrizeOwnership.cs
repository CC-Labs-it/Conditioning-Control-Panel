using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Prizes;

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

        internal static event Action? Changed;
        private static readonly object Gate = new();
        private static readonly OwnershipService Owned = new(() => FriendsHead.Identity()?.UnifiedId, null, a => a());
        private static string? _account;
        static PrizeOwnership() => Owned.OwnershipChanged += (_, _) => Changed?.Invoke();
        internal static string[] Grants => Owned.Grants.ToArray();
        internal static bool Held(string id) => Owned.Grants.Contains(id);

        internal static void NoteAccount(string? account)
        {
            lock (Gate)
            {
                if (account == null || account != FriendsHead.Identity()?.UnifiedId || account == _account) return;
                _account = account;
                Owned.Clear();
            }
        }

        internal static void ApplyProfile(string? requested, string? answered, ConditioningControlPanel.Models.PrizesBlock? prizes, string source)
        {
            lock (Gate)
            {
                if (requested == null || requested != FriendsHead.Identity()?.UnifiedId) return;
                NoteAccount(requested);
                if (requested == answered && prizes?.Grants != null)
                    Owned.ApplySnapshot(requested, prizes.Revision, prizes.Grants);
            }
        }

        internal static void ApplySnapshot(string accountId, long revision, IEnumerable<string> grants)
        {
            lock (Gate)
            {
                if (accountId != FriendsHead.Identity()?.UnifiedId) return;
                NoteAccount(accountId);
                Owned.ApplySnapshot(accountId, revision, grants);
            }
        }

        internal static void ResetHeld()
        {
            lock (Gate) { _account = null; Owned.Clear(); }
        }

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
