using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    private sealed class RaceFriends : IFriendsService
    {
        public readonly List<(string, string)> Invites = new();
        public readonly List<ReceiptReport> Receipts = new();
        public void ReportReceipt(ReceiptReport report) => Receipts.Add(report);
        public SendOutcome Outcome = new(SendResult.Sent, null);
        public TaskCompletionSource<SendOutcome>? Pending;
        public bool Available => true;
        public FriendsSnapshot Snapshot { get; set; } = FriendsSnapshot.Empty;
        public bool PresenceShared { get; set; }
        public event Action<FriendsSnapshot>? SnapshotChanged { add { } remove { } }
        public event Action<InboxItem>? Delivered { add { } remove { } }
        public event Action<SendKind, Friend>? Sent { add { } remove { } }
        public event Action<FriendRequest>? RequestArrived { add { } remove { } }
        public event Action<string>? RequestGone { add { } remove { } }
        public Task<SendOutcome> InviteToRaceAsync(string id, string room) { Invites.Add((id, room)); return Pending?.Task ?? Task.FromResult(Outcome); }
        public Task RefreshAsync() => Task.CompletedTask;
        public Task<SendResult> PokeAsync(string a, string b) => throw new NotSupportedException();
        public Task<SendResult> InviteAsync(string a, string b, string? c) => throw new NotSupportedException();
        public Task<SendResult> SendWatchAsync(string a, WatchRef b) => throw new NotSupportedException();
        public Task<AddResult> AddByCodeAsync(string a) => throw new NotSupportedException();
        public Task<ActResult> AcceptAsync(string a) => throw new NotSupportedException();
        public Task<ActResult> DeclineAsync(string a) => throw new NotSupportedException();
        public Task<ActResult> CancelRequestAsync(string a) => throw new NotSupportedException();
        public Task<ActResult> RemoveAsync(string a) => throw new NotSupportedException();
        public Task<ActResult> BlockAsync(string a) => throw new NotSupportedException();
        public Task<ActResult> UnblockAsync(string a) => throw new NotSupportedException();
        public Task<ActResult> SetSquelchAsync(string a, bool b) => throw new NotSupportedException();
        public Task<ActResult> ReportAsync(string a, string b) => throw new NotSupportedException();
        public void SetActivity(PresenceActivity a) { }
        public void SetDrawerOpen(bool a) { }
    }

    // WPF GlassroadRules.cs:42,71-95; GlassroadHostService.cs:225-232,314-343.
    [Fact]
    public void Glassroad_FriendsBridge_ListsAndInvitesOnlyKnownFriends() => RunGlassroad("1", (_, _, _) =>
    {
        var old = FriendsHead.Service;
        var fake = new RaceFriends();
        var friends = new[] {
            new Friend("u1", "Mia", null, 0, true, FriendPresence.None, false),
            new Friend("u2", "Offline", null, 0, false, FriendPresence.None, false),
            new Friend("u3", " ", null, 0, true, FriendPresence.None, false),
        };
        fake.Snapshot = FriendsSnapshot.Empty with { Friends = friends };
        FriendsHead.Service = fake;
        try
        {
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            bool onUi = true;
            w.Posted += json => { onUi &= Dispatcher.UIThread.CheckAccess(); sent.Add(JObject.Parse(json)); };
            w.Web.OnNavigationCompleted(w.PageUrl);
            Assert.True((bool)sent.Last()["caps"]!["friendInvite"]!);
            w.Web.OnWebMessage("{\"type\":\"friends-list\",\"id\":\"list\"}");
            var list = sent.Single(o => (string?)o["id"] == "list");
            var row = Assert.Single((JArray)list["friends"]!);
            Assert.Equal("u1", (string?)row["uid"]);
            Assert.Equal("Mia", (string?)row["name"]);
            w.Web.OnWebMessage("{\"type\":\"invite-friend\",\"id\":\"ok\",\"friendUid\":\"u1\",\"roomId\":\"abc123def456\"}");
            Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(("u1", "abc123def456"), Assert.Single(fake.Invites));
            Assert.True((bool)sent.Single(o => (string?)o["id"] == "ok")["sent"]!);
            fake.Outcome = new SendOutcome(SendResult.Refused, null) { Reason = "room_full" };
            w.Web.OnWebMessage("{\"type\":\"invite-friend\",\"id\":\"full\",\"friendUid\":\"u1\",\"roomId\":\"abc123def456\"}");
            var full = sent.Single(o => (string?)o["id"] == "full");
            Assert.False((bool)full["sent"]!);
            Assert.Equal(ConditioningControlPanel.Services.Glassroad.GlassroadRules.Sentence(Loc.Get("friends_race_room_full")), (string?)full["note"]);
            w.Web.OnWebMessage("{\"type\":\"invite-friend\",\"id\":\"bad\",\"friendUid\":\"stranger\",\"roomId\":\"bad\"}");
            Assert.False((bool)sent.Single(o => (string?)o["id"] == "bad")["sent"]!);
            w.Web.OnWebMessage("{\"type\":\"invite-friend\",\"id\":\"badroom\",\"friendUid\":\"u1\",\"roomId\":\"bad\"}");
            w.Web.OnWebMessage("{\"type\":\"invite-friend\",\"id\":\"unknown\",\"friendUid\":\"stranger\",\"roomId\":\"abc123def456\"}");
            Assert.False((bool)sent.Single(o => (string?)o["id"] == "badroom")["sent"]!);
            Assert.False((bool)sent.Single(o => (string?)o["id"] == "unknown")["sent"]!);
            Assert.Equal(2, fake.Invites.Count);
            fake.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            w.Web.OnWebMessage("{\"type\":\"invite-friend\",\"id\":\"late\",\"friendUid\":\"u1\",\"roomId\":\"abc123def456\"}");
            Task.Run(() => fake.Pending.SetResult(new(SendResult.Sent, null))).Wait();
            Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(5)));
            Dispatcher.UIThread.RunJobs();
            Assert.True((bool)sent.Single(o => (string?)o["id"] == "late")["sent"]!);
            Assert.True(onUi);
        }
        finally { FriendsHead.Service = old; }
    });

    // WPF GlassroadRules.cs:143-195; GlassroadHostService.cs:253-266; SparklesClaimService.cs:94-146.
    [Fact]
    public void Glassroad_EligibleResult_ClaimsRetriesAndDeliversReceipt() => RunGlassroad("1", (_, _, fake) =>
    {
        var s = CoreSettings.Current;
        var (oldId, oldToken, oldSp, oldOffline) = (s.UnifiedId, s.AuthToken, s.SkillPoints, s.OfflineMode);
        var oldUrl = Environment.GetEnvironmentVariable(V2PurchaseHead.EnvVar);
        try
        {
            (s.UnifiedId, s.AuthToken, s.SkillPoints, s.OfflineMode) = ("discord:12", "tok", 100, false);
            Environment.SetEnvironmentVariable(V2PurchaseHead.EnvVar, "http://127.0.0.1:8123");
            fake.Replies.Enqueue((202, "{\"pending\":true,\"retryAfterMs\":1}"));
            fake.Replies.Enqueue((200, "{\"ok\":true,\"receipt\":{\"result\":{\"kind\":\"place\",\"place\":2,\"of\":8},\"paid\":20,\"credited\":20,\"sp\":120}}"));
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.Web.OnNavigationCompleted(w.PageUrl);
            w.Web.OnWebMessage("{\"type\":\"race-result\",\"finished\":true,\"flagged\":true,\"laps\":3,\"timeS\":90,\"place\":2,\"of\":8}");
            Assert.Empty(fake.Seen);
            w.Web.OnWebMessage("{\"type\":\"race-result\",\"finished\":true,\"mode\":\"online\",\"laps\":3,\"fullLaps\":3,\"timeS\":90,\"place\":2,\"of\":8,\"roomId\":\"abc123def456\"}");
            Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, fake.Seen.Count);
            Assert.All(fake.Seen, x => Assert.Equal("http://127.0.0.1:8123/v2/sparkles/claim", x.Url));
            var claim = JObject.Parse(fake.Seen[0].Body);
            Assert.Equal(fake.Seen[0].Body, fake.Seen[1].Body);
            Assert.Equal("discord:12", (string?)claim["unified_id"]);
            Assert.False(string.IsNullOrWhiteSpace((string?)claim["claimId"]));
            Assert.NotNull(fake.Seen[0].Token);
            Assert.Equal("rt3d", (string?)claim["game"]);
            Assert.Equal("online", (string?)claim["mode"]);
            Assert.Equal(90000, (int)claim["durationMs"]!);
            Assert.Equal("abc123def456", (string?)claim["proof"]!["roomId"]);
            Assert.Equal(120, s.SkillPoints);
            var slip = sent.Single(o => (string?)o["type"] == "sparkles-receipt");
            Assert.Equal(20, (int)slip["credited"]!);
            Assert.Equal(120, (int)slip["sp"]!);
            Assert.Contains(Loc.GetF("Sparkles_Slip_Place", 2, 8), ((JArray)slip["lines"]!).Select(x => (string?)x));
            // A credit cannot lower the local wallet, a shadow receipt is silent, and a late reply
            // for the old account cannot credit the new one (WPF SparklesClaimService.cs:65,175).
            string race = "{\"type\":\"race-result\",\"finished\":true,\"laps\":3,\"timeS\":90,\"place\":1,\"of\":4}";
            fake.Replies.Enqueue((200, "{\"ok\":true,\"receipt\":{\"credited\":5,\"sp\":80}}"));
            w.Web.OnWebMessage(race);
            Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(120, s.SkillPoints);
            Assert.Equal(120, (int)sent.Last(o => (string?)o["type"] == "sparkles-receipt")["sp"]!);
            int slips = sent.Count(o => (string?)o["type"] == "sparkles-receipt");
            fake.Replies.Enqueue((200, "{\"ok\":true,\"receipt\":{\"credited\":5,\"sp\":200,\"shadow\":true}}"));
            w.Web.OnWebMessage(race);
            Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(120, s.SkillPoints);
            Assert.Equal(slips, sent.Count(o => (string?)o["type"] == "sparkles-receipt"));
            fake.ReplyGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            fake.RequestSeen.Reset();
            fake.Replies.Enqueue((200, "{\"ok\":true,\"receipt\":{\"credited\":5,\"sp\":200}}"));
            w.Web.OnWebMessage(race);
            Assert.True(fake.RequestSeen.Wait(TimeSpan.FromSeconds(10)));
            s.UnifiedId = "another-account";
            fake.ReplyGate.TrySetResult(true);
            Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(10)));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(120, s.SkillPoints);
        }
        finally
        {
            fake.ReplyGate?.TrySetResult(true);
            (s.UnifiedId, s.AuthToken, s.SkillPoints, s.OfflineMode) = (oldId, oldToken, oldSp, oldOffline);
            Environment.SetEnvironmentVariable(V2PurchaseHead.EnvVar, oldUrl);
        }
    });

    // WPF FriendsLanding.cs:523-537,565-569 and GlassroadHostService.cs:76-97.
    [Fact]
    public void RaceInvite_GatesAndJoinsNewAndExistingWindow() => RunGlassroad(null, (_, _, _) =>
    {
        var oldService = FriendsLanding.Service;
        var fake = new RaceFriends();
        bool lab = false;
        CoreEntitlement.HasLabProvider = () => lab;
        CoreEntitlement.ShowDeniedHandler = _ => { };
        FriendsLanding.Service = () => fake;
        FriendsSeen.Shared.Clear();
        InboxItem Invite(string id, string? room) => new(id, SendKind.Invite, "u1", "Mia", null, null,
            InviteDestination.Race, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(5)) { Room = room };
        try
        {
            var denied = Invite("0000000000000011", "abc123def456");
            Assert.True(FriendsLanding.CanJoin(denied));
            Assert.Contains(Loc.Get("friends_land_dest_race"), FriendsLanding.KnockLine(denied));
            FriendsLanding.OnKnockDone(denied, KnockOutcome.Go);
            Assert.Contains(fake.Receipts, r => r.State == ReceiptState.Declined);
            Assert.False(GameWindow.IsAnyOpen());
            lab = true;
            FriendsLanding.OnKnockDone(Invite("0000000000000012", "abc123def456"), KnockOutcome.Go);
            var game = Assert.Single(GameWindow.OpenWindowsForTests());
            Assert.True(game.PageUrl!.Query.Contains("&join=abc123def456", StringComparison.Ordinal));
            Assert.Contains(fake.Receipts, r => r.State == ReceiptState.Joined);
            var sent = new List<JObject>();
            game.Posted += json => sent.Add(JObject.Parse(json));
            FriendsLanding.OnKnockDone(Invite("0000000000000013", "abc123def457"), KnockOutcome.Go);
            Assert.DoesNotContain(sent, x => (string?)x["type"] == "join-room");
            game.HandleMessage("{\"type\":\"ready\"}");
            Assert.Equal("abc123def457", (string?)sent.Single(x => (string?)x["type"] == "join-room")["roomId"]);
            FriendsLanding.OnKnockDone(Invite("0000000000000014", "abc123def458"), KnockOutcome.Go);
            Assert.Equal("abc123def458", (string?)sent.Last()["roomId"]);
            Assert.Same(game, Assert.Single(GameWindow.OpenWindowsForTests()));
        }
        finally { FriendsLanding.Service = oldService; FriendsSeen.Shared.Clear(); }
    });
}
