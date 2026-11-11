using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Controls.Friends;

namespace ConditioningControlPanel.Services.Friends;

/// <summary>Friends frames for the Racing Thoughts room.</summary>
public static class GlassroadInviteRules
{
    public sealed record FriendRow(string uid, string name);

    public static IReadOnlyList<FriendRow> FriendRows(IEnumerable<Friend>? friends) =>
        (friends ?? Array.Empty<Friend>())
            .Where(f => f != null && f.Online && !string.IsNullOrEmpty(f.Id) && !string.IsNullOrWhiteSpace(f.Name))
            .Take(50).Select(f => new FriendRow(f.Id, f.Name)).ToArray();

    public static bool CanInvite(IEnumerable<Friend>? friends, string? uid, string? room) =>
        !string.IsNullOrEmpty(uid) && InviteDestination.IsRoomId(room)
        && friends != null && friends.Any(f => f != null && f.Id == uid);

    public static string NoteKey(SendOutcome outcome) =>
        outcome.Reason != null && InviteDestination.RaceReasons.Contains(outcome.Reason)
            ? "friends_race_" + outcome.Reason : FriendsDrawerRules.SendResultKey(outcome.Result);

    public static bool IsGood(SendOutcome outcome) => FriendsDrawerRules.IsGood(outcome.Result);
    public static string Sentence(string text) => string.IsNullOrEmpty(text) ? "" : char.ToUpperInvariant(text[0]) + text.Substring(1);
}
