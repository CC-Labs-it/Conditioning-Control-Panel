using System;
using System.Linq;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Lobby;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games;

internal sealed partial class GameWindow
{
    internal static bool JoinRace(string? room)
    {
        if (room != null && !RaceJoinLink.IsRoomId(room)) return false;
        if (!TierGate.DemandLab(Loc.Get("launcher_game_race_title"))) return false;
        return Launch(GlassroadId, room) != null;
    }

    private async Task InviteRaceFriend(JObject o)
    {
        var outcome = new SendOutcome(SendResult.Refused, null);
        var uid = (string?)o["friendUid"];
        var room = (string?)o["roomId"];
        try
        {
            var svc = FriendsHead.Service;
            if (svc != null && svc.Snapshot.Friends.Any(f => f.Id == uid) && RaceJoinLink.IsRoomId(room))
                outcome = await svc.InviteToRaceAsync(uid!, room!);
        }
        catch { outcome = new SendOutcome(SendResult.TryLater, null); }
        var key = outcome.Reason != null && InviteDestination.RaceReasons.Contains(outcome.Reason)
            ? "friends_race_" + outcome.Reason : global::ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.SendResultKey(outcome.Result);
        var note = Loc.Get(key);
        if (!_closing) Post(new { type = "invite-friend-reply", id = (string?)o["id"] ?? "",
            sent = global::ConditioningControlPanel.Controls.Friends.FriendsDrawerRules.IsGood(outcome.Result),
            note = string.IsNullOrEmpty(note) ? "" : char.ToUpperInvariant(note[0]) + note.Substring(1) });
    }
}
