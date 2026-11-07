// WPF Services/PieceByPiece/PieceByPieceHostService.Friends.cs:37-61, 127-161.
// Queue until identity, reuse one challenge, resolve on replacement, timeout or close.
using System;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
namespace ConditioningControlPanel.Avalonia.Views.Games;
internal sealed partial class GameWindow
{
    private bool _pbpFriendsReady;
    private JObject? _pbpFriendIntent;
    private TaskCompletionSource<string?>? _pbpChallenge;
    private string? _pbpChallengeFor;
    internal static Task<string?> ChallengePbpFriend(string friendId, TimeSpan timeout)
    {
        if (string.IsNullOrEmpty(friendId)) return Task.FromResult<string?>(null);
        var w = Launch(PbpId);
        if (w == null) return Task.FromResult<string?>(null);
        if (w._pbpChallenge is { Task.IsCompleted: false } live && w._pbpChallengeFor == friendId) return live.Task;
        w._pbpChallenge?.TrySetResult(null);
        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        w._pbpChallenge = tcs;
        w._pbpChallengeFor = friendId;
        w.IntendPbp(new() { ["type"] = "pbp:friend", ["mode"] = "challenge", ["friendId"] = friendId });
        _ = Task.Delay(timeout).ContinueWith(_ => tcs.TrySetResult(null), TaskScheduler.Default);
        return tcs.Task;
    }
    internal static void JoinPbpChallenge(string challengeId)
    {
        if (InviteDestination.IsChallengeId(challengeId))
            Launch(PbpId)?.IntendPbp(new() { ["type"] = "pbp:friend", ["mode"] = "accept", ["challengeId"] = challengeId });
    }
    private void IntendPbp(JObject intent)
    {
        _pbpFriendIntent = intent;
        if (_pbpFriendsReady && !_closing) PostPbpFriendIntent();
    }
    private void PostPbpFriendIntent()
    {
        if (_pbpFriendIntent is not { } intent) return;
        _pbpFriendIntent = null;
        Post(intent);
    }
    private void OnPbpFriendChallenge(JObject o)
    {
        var id = o["challengeId"]?.Type == JTokenType.String ? (string?)o["challengeId"] : null;
        _pbpChallenge?.TrySetResult(InviteDestination.IsChallengeId(id) ? id : null);
    }
}
