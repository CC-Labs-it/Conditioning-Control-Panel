using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Companion;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// 7.2 lane L9: Rain's look follows the account through /v2/equip family rain (72-PR-LANES-BRIEF contract 3),
/// fail-soft while the route is missing, and a look with no pack on this PC is re-baked when the dressing
/// room opens.
/// </summary>
public sealed class RainLookSyncTests
{
    private const string Dress = "{\"rev\":1,\"items\":{\"dress\":\"rose-flare\"},\"colors\":{}}";
    private const string Hair = "{\"rev\":1,\"items\":{\"hair\":\"copper-lob\"},\"colors\":{}}";

    private static string N(string look) => RainLookCodec.Normalize(look);

    private sealed class FakeHandler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Url, string? Token, string? Body)> Seen = new();
        public Func<HttpRequestMessage, string?, HttpResponseMessage> Answer = (_, _) => new HttpResponseMessage(HttpStatusCode.NotFound);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var body = r.Content == null ? null : await r.Content.ReadAsStringAsync(ct);
            Seen.Add((r.Method, r.RequestUri!.ToString(), r.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null, body));
            return Answer(r, body);
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static string Equip(string? rainLook, string updatedAt) =>
        "{\"ok\":true,\"equip\":{\"chess\":null,\"desk\":null,\"rain\":" + (rainLook ?? "null") + ",\"updatedAt\":\"" + updatedAt + "\"}}";

    /// <summary>A fake PC: its settings, and what the sync did to them.</summary>
    private sealed class Pc
    {
        public string Look = "";
        public bool Owed;
        public DateTime? Agreed;
        public readonly List<string> Adopted = new();
        public string? Account = "uid-1";
        public readonly FakeHandler Http = new();

        public RainLookSync Sync() => new(new HttpClient(Http), new RainLookSync.Hooks
        {
            Identity = () => Account == null ? null : (Account, "tok-1"),
            Local = () => (Look, Owed, Agreed),
            Agreed = (look, at) => { if (N(look) == N(Look)) Owed = false; Agreed = at; },
            Adopt = (look, at) => { Look = look; Owed = false; Agreed = at; Adopted.Add(look); },
            BaseUrl = "https://proxy.test",
        });
    }

    // ------------------------------------------------------------------ pure

    [Fact]
    public void ReadEquip_reads_the_rain_copy_and_tells_never_set_from_default()
    {
        var set = RainLookSync.ReadEquip(Equip(Dress, "2026-10-30T10:00:00Z"))!.Value;
        Assert.True(set.Present);
        Assert.Equal(N(Dress), set.Look);
        Assert.Equal(new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc), set.UpdatedUtc);

        var none = RainLookSync.ReadEquip(Equip(null, "2026-10-30T10:00:00Z"))!.Value;
        Assert.False(none.Present);

        var dflt = RainLookSync.ReadEquip(Equip("{\"rev\":1,\"items\":{},\"colors\":{}}", "2026-10-30T10:00:00Z"))!.Value;
        Assert.True(dflt.Present);
        Assert.Equal("", dflt.Look);

        Assert.Null(RainLookSync.ReadEquip("{\"ok\":false,\"reason\":\"signed_out\"}"));
        Assert.Null(RainLookSync.ReadEquip("<html>404</html>"));
        Assert.Null(RainLookSync.ReadEquip(""));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1761818400000).UtcDateTime,
            RainLookSync.ReadEquip("{\"ok\":true,\"equip\":{\"rain\":null,\"updatedAt\":1761818400000}}")!.Value.UpdatedUtc);
    }

    [Fact]
    public void Decide_pushes_what_is_owed_adopts_what_is_newer_and_seeds_an_empty_server()
    {
        var t1 = new DateTime(2026, 10, 30, 9, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddHours(1);
        var server = new RainEquipCopy(true, N(Hair), t2);

        Assert.Equal(RainSyncStep.Push, RainLookSync.Decide(Dress, pushOwed: true, t1, server));          // owed always goes up first
        Assert.Equal(RainSyncStep.Adopt, RainLookSync.Decide(Dress, false, t1, server));                  // newer than our agreement
        Assert.Equal(RainSyncStep.Adopt, RainLookSync.Decide(Dress, false, null, server));                // never agreed: the account wins
        Assert.Equal(RainSyncStep.Push, RainLookSync.Decide(Dress, false, t2.AddMinutes(1), server));     // older than our agreement
        Assert.Equal(RainSyncStep.None, RainLookSync.Decide(Hair, false, t1, server));                    // already the same
        Assert.Equal(RainSyncStep.Push, RainLookSync.Decide(Dress, false, null, new RainEquipCopy(false, "", t2)));  // seed
        Assert.Equal(RainSyncStep.None, RainLookSync.Decide("", false, null, new RainEquipCopy(false, "", t2)));     // nothing to seed
    }

    [Fact]
    public void The_put_body_is_the_contract_and_a_default_look_is_still_an_object()
    {
        var body = RainLookSync.PutBody("uid-1", Dress);
        Assert.Equal(new[] { "unified_id", "family", "value" }, body.Properties().Select(p => p.Name));
        Assert.Equal("rain", (string?)body["family"]);
        Assert.Equal(N(Dress), RainLookCodec.Normalize(body["value"]!.ToString()));
        var dflt = RainLookSync.PutBody("uid-1", "");
        Assert.Equal(JTokenType.Object, dflt["value"]!.Type);
        Assert.Equal(RainLookCodec.Rev, (int)dflt["value"]!["rev"]!);
    }

    // ------------------------------------------------------------------ the run

    [Fact]
    public async Task A_missing_route_is_soft_the_look_and_the_owed_push_stay()
    {
        var pc = new Pc { Look = Dress, Owed = true };
        await pc.Sync().SyncAsync();                       // PUT -> 404
        Assert.Equal(Dress, pc.Look);
        Assert.True(pc.Owed);

        pc.Owed = false;
        await pc.Sync().SyncAsync();                       // GET -> 404
        Assert.Equal(Dress, pc.Look);
        Assert.Empty(pc.Adopted);
        Assert.Null(pc.Agreed);

        pc.Http.Answer = (_, _) => throw new HttpRequestException("offline");
        await pc.Sync().SyncAsync();
        Assert.Equal(Dress, pc.Look);
    }

    [Fact]
    public async Task An_owed_push_goes_up_with_the_token_door_and_clears_on_the_receipt()
    {
        var pc = new Pc { Look = Dress, Owed = true };
        pc.Http.Answer = (_, body) => Json(Equip(JObject.Parse(body!)["value"]!.ToString(), "2026-10-30T11:00:00Z"));
        await pc.Sync().SyncAsync();
        var put = Assert.Single(pc.Http.Seen);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("https://proxy.test/v2/equip", put.Url);
        Assert.Equal("tok-1", put.Token);
        Assert.Equal("uid-1", (string?)JObject.Parse(put.Body!)["unified_id"]);
        Assert.False(pc.Owed);
        Assert.Equal(new DateTime(2026, 10, 30, 11, 0, 0, DateTimeKind.Utc), pc.Agreed);
        Assert.Empty(pc.Adopted);
    }

    /// <summary>The real server (CCP-Server equip.js clampRain) stores and answers Rain's default look as
    /// <c>rain: null</c>. A default pushed from one PC must settle there and reach the other PC.</summary>
    [Fact]
    public async Task A_default_look_settles_and_spreads_although_the_server_answers_it_as_null()
    {
        // PC A had a dress agreed at 10:00, then chose the default: the PUT answer is rain null.
        var a = new Pc { Look = "", Owed = true, Agreed = new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc) };
        a.Http.Answer = (_, _) => Json(Equip(null, "2026-10-30T11:00:00Z"));
        await a.Sync().SyncAsync();
        Assert.False(a.Owed);                               // settled, not owed forever
        Assert.Empty(a.Adopted);
        Assert.Equal(new DateTime(2026, 10, 30, 11, 0, 0, DateTimeKind.Utc), a.Agreed);

        // PC B still wears the dress it agreed at 10:00: the newer null copy is adopted, never overwritten.
        var b = new Pc { Look = Dress, Agreed = new DateTime(2026, 10, 30, 10, 0, 0, DateTimeKind.Utc) };
        b.Http.Answer = (_, _) => Json(Equip(null, "2026-10-30T11:00:00Z"));
        await b.Sync().SyncAsync();
        Assert.Equal(new[] { "" }, b.Adopted);
        Assert.All(b.Http.Seen, s => Assert.Equal(HttpMethod.Get, s.Method));

        // A server copy that was never set still gets seeded from a PC that never agreed.
        Assert.Equal(RainSyncStep.Push, RainLookSync.Decide(Dress, false, null, new RainEquipCopy(false, "", new DateTime(2026, 10, 30, 11, 0, 0, DateTimeKind.Utc))));
        Assert.Equal(RainSyncStep.Push, RainLookSync.Decide(Dress, false, new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc),
            new RainEquipCopy(false, "", new DateTime(2026, 10, 30, 11, 0, 0, DateTimeKind.Utc))));
    }

    [Fact]
    public async Task A_push_the_clamp_empties_to_null_is_adopted_as_the_default()
    {
        var pc = new Pc { Look = Dress, Owed = true };
        pc.Http.Answer = (_, _) => Json(Equip(null, "2026-10-30T11:00:00Z"));
        await pc.Sync().SyncAsync();
        Assert.Equal(new[] { "" }, pc.Adopted);
        Assert.False(pc.Owed);
    }

    [Fact]
    public async Task A_server_clamp_on_push_is_adopted()
    {
        var pc = new Pc { Look = Dress, Owed = true };
        pc.Http.Answer = (_, _) => Json(Equip("{\"rev\":1,\"items\":{},\"colors\":{}}", "2026-10-30T11:00:00Z"));
        await pc.Sync().SyncAsync();
        Assert.Equal(new[] { "" }, pc.Adopted);
        Assert.False(pc.Owed);
    }

    [Fact]
    public async Task A_newer_server_look_is_adopted_and_an_equal_one_only_stamps()
    {
        var pc = new Pc { Look = Dress, Agreed = new DateTime(2026, 10, 29, 0, 0, 0, DateTimeKind.Utc) };
        pc.Http.Answer = (r, _) => Json(Equip(Hair, "2026-10-30T12:00:00Z"));
        await pc.Sync().SyncAsync();
        var get = Assert.Single(pc.Http.Seen);
        Assert.Equal(HttpMethod.Get, get.Method);
        Assert.Equal("https://proxy.test/v2/equip?unified_id=uid-1", get.Url);
        Assert.Equal(new[] { N(Hair) }, pc.Adopted);
        Assert.Equal(new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc), pc.Agreed);

        await pc.Sync().SyncAsync();                       // same look now: no second adopt, no push
        Assert.Single(pc.Adopted);
        Assert.All(pc.Http.Seen, s => Assert.Equal(HttpMethod.Get, s.Method));
    }

    [Fact]
    public async Task An_empty_server_is_seeded_from_this_pc()
    {
        var pc = new Pc { Look = Dress };
        pc.Http.Answer = (r, body) => r.Method == HttpMethod.Get
            ? Json(Equip(null, "2026-10-30T12:00:00Z"))
            : Json(Equip(JObject.Parse(body!)["value"]!.ToString(), "2026-10-30T12:00:05Z"));
        await pc.Sync().SyncAsync();
        Assert.Equal(new[] { HttpMethod.Get, HttpMethod.Put }, pc.Http.Seen.Select(s => s.Method));
        Assert.Empty(pc.Adopted);
        Assert.Equal(new DateTime(2026, 10, 30, 12, 0, 5, DateTimeKind.Utc), pc.Agreed);
    }

    [Fact]
    public async Task A_reply_for_an_account_that_signed_out_meanwhile_is_dropped()
    {
        var pc = new Pc { Look = Dress };
        pc.Http.Answer = (_, _) => { pc.Account = null; return Json(Equip(Hair, "2026-10-30T12:00:00Z")); };
        await pc.Sync().SyncAsync();
        Assert.Empty(pc.Adopted);
        Assert.Equal(Dress, pc.Look);

        var signedOut = new Pc { Look = Dress, Owed = true, Account = null };
        await signedOut.Sync().SyncAsync();
        Assert.Empty(signedOut.Http.Seen);
    }

    // ------------------------------------------------------------------ re-bake on adopt

    [Fact]
    public void An_adopted_look_without_a_pack_here_plays_the_embedded_clips_and_owes_a_bake()
    {
        var hash = RainLookHash.Compute(Dress);
        Assert.Equal("", RainLookSync.PackFor(Dress, _ => false));
        Assert.Equal(hash, RainLookSync.PackFor(Dress, h => h == hash));
        Assert.Equal("", RainLookSync.PackFor("", _ => true));

        Assert.True(RainLookSync.RebakeOwed(Dress, "", _ => false));
        Assert.True(RainLookSync.RebakeOwed(Dress, RainLookHash.Compute(Hair), _ => true));   // a stale pack from a restore
        Assert.True(RainLookSync.RebakeOwed(Dress, hash, _ => false));                         // the pack went missing
        Assert.False(RainLookSync.RebakeOwed(Dress, hash, h => h == hash));
        Assert.False(RainLookSync.RebakeOwed("", "", _ => false));                              // the default look needs no pack
    }

    [Fact]
    public void The_ready_frame_bakes_a_look_that_owes_one_and_nothing_else()
    {
        var posted = new List<JObject>();
        int commits = 0;
        var dir = Path.Combine(Path.GetTempPath(), "ccp-rainsync-" + Guid.NewGuid().ToString("N"));
        try
        {
            RainApplyController Controller() => new(new RainApplyController.Hooks
            {
                Post = m => posted.Add(JObject.FromObject(m)),
                Store = () => new RainPackStore(RainPackStore.RootFor(dir), () => null,
                    name => File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "rain_emotes", name))),
                Access = () => new RainItemAccess(LoadManifest(), new RainGrantSet(new[] { "cos.rain.dress.rose-flare" }), new Lvl()),
                Clips = () => new[] { "idle" },
                Commit = (_, _) => commits++,
            });
            var init = new JObject { ["type"] = "init" };
            var apply = Controller();
            RainWardrobeHostService.Route(JObject.FromObject(new { type = "rain-ready" }), apply, m => posted.Add(JObject.FromObject(m)),
                () => init, () => { }, () => N(Dress));
            Assert.Equal(new[] { "init", "rain-bake" }, posted.Select(p => (string?)p["type"]));
            Assert.Equal(RainLookHash.Compute(N(Dress)), (string?)posted[1]["lookHash"]);
            Assert.True(apply.IsBaking);
            apply.Abort();

            posted.Clear();
            RainWardrobeHostService.Route(JObject.FromObject(new { type = "rain-ready" }), Controller(), m => posted.Add(JObject.FromObject(m)),
                () => init, () => { }, () => null);
            Assert.Equal(new[] { "init" }, posted.Select(p => (string?)p["type"]));
            Assert.Equal(0, commits);
        }
        finally { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } }
    }

    [Fact]
    public void Sync_state_is_machine_local()
    {
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.RainLookPushOwed)));
        Assert.True(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.RainLookAgreedUtc)));
        Assert.False(ProfileSyncService.IsExcludedFromBackup(nameof(AppSettings.RainLook)));   // the look itself rides the backup
        var at = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
        var current = new AppSettings { RainLookPushOwed = true, RainLookAgreedUtc = at };
        var restored = new AppSettings();
        ProfileSyncService.PreserveLocalOnlyFields(current, restored);
        Assert.True(restored.RainLookPushOwed);
        Assert.Equal(at, restored.RainLookAgreedUtc);
    }

    private sealed class Lvl : IRainProgressSource
    {
        public int Level { get; init; } = 1;
        public IReadOnlyCollection<string> UnlockedAchievements { get; init; } = Array.Empty<string>();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static RainItemManifest LoadManifest() =>
        RainItemManifest.Load(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Resources", "web", "rain-wardrobe", RainItemManifest.FileName));
}
