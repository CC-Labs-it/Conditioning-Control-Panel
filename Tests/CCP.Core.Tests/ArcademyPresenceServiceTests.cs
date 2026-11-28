using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Campus presence, moved from WPF ArcademyPresenceService.cs: the emitter needs a consenting
/// rung and an identity, the snapshot pull only an online app, and a lowered rung owes one leave.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class ArcademyPresenceServiceTests
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly ConcurrentQueue<(HttpMethod Method, string? Token, JObject? Body)> Seen = new();
        public string Snapshot = "{\"v\":1,\"now\":1,\"students\":[]}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? null : JObject.Parse(await r.Content.ReadAsStringAsync(ct));
            Seen.Enqueue((r.Method, r.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null, body));
            var text = r.Method == HttpMethod.Get ? Snapshot : "{\"self\":\"me\"}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) };
        }

        public List<JObject> Posts() => Seen.Where(x => x.Method == HttpMethod.Post).Select(x => x.Body!).ToList();
        public int Gets => Seen.Count(x => x.Method == HttpMethod.Get);
    }

    private static void Until(Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < end) Thread.Sleep(10);
        Thread.Sleep(100);   // room for anything that should NOT have been sent
    }

    /// <summary>Runs <paramref name="body"/> with a fake proxy and the given rung and identity, then puts
    /// every seam back (identity first, so restoring the rung cannot post a revocation).</summary>
    private static void With(string share, bool signedIn, bool offline, Action<Fake> body)
    {
        var s = CoreSettings.Current;
        var secrets = new Dictionary<string, string?>();
        var (oldGet, oldSet, oldHttp) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, ArcademyPresenceService.Http);
        var (oldId, oldShare, oldOffline) = (s.UnifiedId, s.ArcademyPresenceShare, s.OfflineMode);
        var fake = new Fake();
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        ArcademyPresenceService.Http = new HttpClient(fake);
        try
        {
            s.ArcademyPresenceShare = share;
            s.UnifiedId = signedIn ? "u_1" : null;
            s.AuthToken = signedIn ? "tok" : null;
            s.OfflineMode = offline;
            body(fake);
        }
        finally
        {
            ArcademyPresenceService.Detach();
            s.UnifiedId = null; s.AuthToken = null;
            s.ArcademyPresenceShare = oldShare;
            (s.UnifiedId, s.OfflineMode) = (oldId, oldOffline);
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, ArcademyPresenceService.Http) = (oldGet, oldSet, oldHttp);
        }
    }

    [Fact]
    public void ConsentingSignedInCampusAnnouncesItsRoomsAndGrades()
    {
        With("anon", signedIn: true, offline: false, fake =>
        {
            var frames = new ConcurrentQueue<(string? Self, JObject? Snapshot)>();
            ArcademyPresenceService.Attach((self, snap) => frames.Enqueue((self, snap)));
            ArcademyPresenceService.NoteRoomEnter("daily_trigger");
            ArcademyPresenceService.NoteRoomEnter("not_a_room");
            ArcademyPresenceService.NoteClassEnd("instant_recall", "pass");
            ArcademyPresenceService.NoteClassEnd("sort", "s");
            Until(() => fake.Posts().Count >= 4 && frames.Any(f => f.Snapshot != null));
            var posts = fake.Posts();
            Assert.Equal(4, posts.Count);
            Assert.All(fake.Seen.Where(x => x.Method == HttpMethod.Post), x => Assert.Equal("tok", x.Token));
            // Five fields, no name, picture, time or position (WPF PostEventAsync).
            Assert.All(posts, p => Assert.Equal(new[] { "event", "room", "grade", "share", "unified_id" },
                p.Properties().Select(x => x.Name)));
            Assert.Contains(posts, p => (string?)p["event"] == "campus_enter" && (string?)p["share"] == "anon");
            Assert.Contains(posts, p => (string?)p["event"] == "room_enter" && (string?)p["room"] == "daily-trigger");
            Assert.Contains(posts, p => (string?)p["room"] == "instant-recall" && p["grade"]!.Type == JTokenType.Null);
            Assert.Contains(posts, p => (string?)p["room"] == "sort" && (string?)p["grade"] == "S");
            Assert.Contains(frames, f => f.Self == "me");

            ArcademyPresenceService.Detach();
            Assert.False(ArcademyPresenceService.Attached);
            Until(() => fake.Posts().Count >= 5);
            Assert.Equal("campus_leave", (string?)fake.Posts().Last()["event"]);
        });
    }

    [Theory]
    [InlineData("off", true)]
    [InlineData("discord", false)]
    public void OffOrSignedOutSendsNothingButStillWatches(string share, bool signedIn)
    {
        With(share, signedIn, offline: false, fake =>
        {
            JObject? snapshot = null;
            ArcademyPresenceService.Attach((_, snap) => snapshot ??= snap);
            ArcademyPresenceService.NoteRoomEnter("sort");
            ArcademyPresenceService.NoteClassEnd("sort", "A");
            Until(() => snapshot != null);
            Assert.NotNull(snapshot);
            Assert.Empty(fake.Posts());
            ArcademyPresenceService.Detach();
            Thread.Sleep(200);
            Assert.Empty(fake.Posts());
        });
    }

    [Fact]
    public void OfflineNeitherSendsNorWatches()
    {
        With("discord", signedIn: true, offline: true, fake =>
        {
            ArcademyPresenceService.Attach((_, _) => throw new InvalidOperationException("no frame offline"));
            ArcademyPresenceService.NoteRoomEnter("sort");
            Thread.Sleep(200);
            Assert.Empty(fake.Seen);
        });
    }

    [Fact]
    public void WrongSnapshotShapeIsNeverPushed()
    {
        With("off", signedIn: false, offline: false, fake =>
        {
            fake.Snapshot = "{\"v\":2,\"students\":[]}";
            var pushed = 0;
            ArcademyPresenceService.Attach((_, _) => Interlocked.Increment(ref pushed));
            Until(() => fake.Gets > 0);
            Assert.Equal(1, fake.Gets);
            Assert.Equal(0, pushed);
        });
    }

    /// <summary>WPF OnSettingChanged: only a lowered rung posts, and off posts the revocation.</summary>
    [Fact]
    public void LoweringTheRungPostsOneLeaveAndOffRevokes()
    {
        With("anon", signedIn: true, offline: false, fake =>
        {
            ArcademyPresenceService.Attach((_, _) => { });
            Until(() => fake.Posts().Count >= 1);
            var s = CoreSettings.Current;
            s.ArcademyPresenceShare = "discord";   // up: nothing owed
            s.ArcademyPresenceShare = "username";  // down: one leave at the new rung
            Until(() => fake.Posts().Count >= 2);
            s.ArcademyPresenceShare = "off";
            Until(() => fake.Posts().Count >= 3);
            var posts = fake.Posts();
            Assert.Equal(3, posts.Count);
            Assert.Contains(posts, p => (string?)p["event"] == "campus_leave" && (string?)p["share"] == "username");
            Assert.Contains(posts, p => (string?)p["event"] == "campus_leave" && (string?)p["share"] == "off");
        });
    }
}
