using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Invites;

/// <summary>What happened to one of the inviter's codes.</summary>
public enum InviteNewsKind
{
    /// <summary>A friend redeemed the code: their free week has started.</summary>
    Claimed,
    /// <summary>A friend the account invited paid for a month.</summary>
    Subscribed,
}

/// <summary>One piece of invite news. <see cref="Name"/> is null when the server gave none.</summary>
public sealed record InviteNews(InviteNewsKind Kind, string Key, string? Name);

/// <summary>
/// THE INVITE NEWS, the pure half: which "someone used your code" notices a <c>/v2/invites/mine</c>
/// read owes, judged against <c>AppSettings.InboxStamps</c> so each is told once.
///
/// <para>A code that reads <c>trying</c> or <c>converted</c> and has no stamp yet is a claim. A
/// lifetime converted count above the remembered one is a subscription; the first read of an
/// account only remembers the count, since those friends subscribed before this notice existed.
/// Stamps for codes the month no longer lists are dropped, so the map stays a few entries long.</para>
/// </summary>
public static class InviteClaimNotice
{
    private const string CodePrefix = "invite:code:";
    private const string TotalPrefix = "invite:total:";

    /// <summary>
    /// The news this read owes, and the stamps updated to match. Returns nothing (and touches
    /// nothing) for a read that is not a subscriber's reply.
    /// </summary>
    public static IReadOnlyList<InviteNews> Decide(InviteMine? mine, string? unifiedId, IDictionary<string, string>? stamps, out bool stampsChanged)
    {
        stampsChanged = false;
        var news = new List<InviteNews>();
        if (mine == null || !mine.Reachable || stamps == null || string.IsNullOrEmpty(unifiedId)) return news;

        var slots = mine.Snapshot?.Slots;
        if (slots != null)
        {
            var listed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var slot in slots)
            {
                if (slot == null || string.IsNullOrEmpty(slot.Code)) continue;
                var key = CodePrefix + slot.Code;
                listed.Add(key);
                if (slot.State == InviteSlotState.Open) continue;
                if (stamps.ContainsKey(key)) continue;
                stamps[key] = "1";
                stampsChanged = true;
                news.Add(new InviteNews(InviteNewsKind.Claimed, key, Clean(slot.InviteeName)));
            }
            // Only a reply that lists codes may forget old ones (a refusal lists none).
            if (slots.Count > 0)
            {
                foreach (var stale in stamps.Keys.Where(k => k.StartsWith(CodePrefix, StringComparison.Ordinal) && !listed.Contains(k)).ToArray())
                {
                    stamps.Remove(stale);
                    stampsChanged = true;
                }
            }
        }

        var totalKey = TotalPrefix + unifiedId;
        int total = Math.Max(0, mine.Snapshot?.ConvertedTotal ?? mine.ConvertedTotal);
        bool known = stamps.TryGetValue(totalKey, out var raw) && int.TryParse(raw, out _);
        int seen = known ? int.Parse(raw!) : 0;
        if (!known || total != seen)
        {
            stamps[totalKey] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
            stampsChanged = true;
        }
        if (known && total > seen)
        {
            // Name the friend when this month's list shows exactly who it was.
            var converted = slots?.Where(s => s != null && s.State == InviteSlotState.Converted).ToArray();
            var name = converted is { Length: 1 } ? Clean(converted[0].InviteeName) : null;
            news.Add(new InviteNews(InviteNewsKind.Subscribed, totalKey + ":" + total, name));
        }
        return news;
    }

    private static string? Clean(string? name)
    {
        var s = name?.Trim();
        return string.IsNullOrEmpty(s) ? null : s;
    }
}
