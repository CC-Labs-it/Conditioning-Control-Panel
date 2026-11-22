using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.PrizeCounter;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.SuperCore;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The real account equip behind the Studio look rows (owned = grants, worn = /v2/equip family looks), over a
/// fake HTTP handler: adopt, an owed push wins, the answer is the truth, flags off, fog2, signed out, the local cache.</summary>
[Collection("LookWardrobe")]
public class AccountLookStoreTests
{
    private const string A = "acct-a";
    private const string Base = "https://example.test";

    /// <summary>A server a test scripts by hand; every request is kept.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Path, JObject? Body)> Calls = new();
        public Func<HttpRequestMessage, JObject?, HttpResponseMessage> Answer = (_, _) => Json(404, new JObject());
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            JObject? body = null;
            if (req.Content != null) body = JObject.Parse(await req.Content.ReadAsStringAsync(ct));
            Calls.Add((req.Method, req.RequestUri!.AbsolutePath, body));
            return Answer(req, body);
        }
        public int Count(HttpMethod m) => Calls.Count(c => c.Method == m);
    }

    private static HttpResponseMessage Json(int status, JObject o)
        => new((HttpStatusCode)status) { Content = new StringContent(o.ToString(), Encoding.UTF8, "application/json") };

    private static JObject Looks(string bubble = "fx.bubble.soap", string card = "fx.card.plain", bool mix = false, string? fog2 = null)
        => new() { ["bubble"] = bubble, ["card"] = card, ["behind"] = "fx.behind.none", ["word"] = "fx.word.classic", ["mix"] = mix, ["fog2"] = fog2 };

    private static JObject EquipReply(JObject? looks)
    {
        var eq = new JObject { ["chess"] = null, ["updatedAt"] = "2026-11-17T12:00:00.000Z" };
        if (looks != null) eq["looks"] = looks;
        return new JObject { ["ok"] = true, ["equip"] = eq };
    }

    private sealed class Rig
    {
        public readonly FakeServer Server = new();
        public string? Account = A;
        public bool Offline;
        public readonly HashSet<string> Grants = new(StringComparer.Ordinal);
        public bool GrantsKnown = true;
        public readonly List<string> Raised = new();
        public DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        public readonly string Path;
        public Rig(string? path = null) { Path = path ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccp-looks-" + Guid.NewGuid().ToString("N"), AccountLookStore.FileName); }
        public AccountLookStore Make()
        {
            var s = new AccountLookStore(new HttpClient(Server), () => Account,
                () => Account == null || Offline ? null : (Account, "tok"),
                g => GrantsKnown ? Grants.Contains(g) : null, Path, () => Now, Base);
            s.Raise = Raised.Add;
            return s;
        }
        public void Own(params string[] ids) { foreach (var id in ids) Grants.Add("cos." + id); }
    }

    private static T WithStore<T>(ILookStore store, Func<T> body)
    {
        var was = LookWardrobe.Store;
        LookWardrobe.Store = store;
        try { return body(); } finally { LookWardrobe.Store = was; }
    }

    [Fact]
    public async Task A_pull_adopts_the_servers_looks_and_raises_what_changed()
    {
        var rig = new Rig();
        rig.Own("fx.bubble.heart", FogLooks.Twotone);
        rig.Server.Answer = (req, _) => Json(200, EquipReply(Looks("fx.bubble.heart", mix: true, fog2: FogLooks.Sky)));
        var s = rig.Make();
        await s.PullOnceAsync();
        Assert.Equal("fx.bubble.heart", s.Worn("bubble"));
        Assert.Equal(FogLooks.Twotone, s.Worn("mix"));
        Assert.Equal(FogLooks.Sky, s.Worn("fog2"));
        Assert.Contains("bubble", rig.Raised);
        Assert.Contains("mix", rig.Raised);
        Assert.Equal(HttpMethod.Get, rig.Server.Calls.Single().Method);
        Assert.Equal("/v2/equip", rig.Server.Calls[0].Path);
        // once per account per launch
        await s.PullOnceAsync();
        Assert.Single(rig.Server.Calls);
        // the next launch, offline, before any grants land: the cache wears the same and knows what was owned
        s.OnGrantsChanged();
        var rig2 = new Rig(rig.Path) { Offline = true, GrantsKnown = false };
        var s2 = rig2.Make();
        Assert.Equal("fx.bubble.heart", s2.Worn("bubble"));
        Assert.True(s2.Owns("fx.bubble.heart"));
        Assert.False(s2.Owns("fx.bubble.disco"));
        Assert.Empty(rig2.Server.Calls);
    }

    [Fact]
    public async Task An_owed_push_beats_adoption_and_settles_on_the_answer()
    {
        var rig = new Rig { Offline = true };
        rig.Own("fx.bubble.heart", "fx.card.lace");
        var s = rig.Make();
        Assert.True(s.Equip("bubble", "fx.bubble.heart"));           // offline: lands here, owed
        Assert.Equal("fx.bubble.heart", s.Worn("bubble"));
        Assert.Empty(rig.Server.Calls);

        // back online: the server still holds an older pick; the pull pushes instead of adopting it
        rig.Offline = false;
        rig.Server.Answer = (req, body) => req.Method == HttpMethod.Put
            ? Json(200, EquipReply((JObject)body!["value"]!.DeepClone()))
            : Json(200, EquipReply(Looks("fx.bubble.soap", "fx.card.lace")));
        await s.PullOnceAsync();
        var put = rig.Server.Calls.Single();
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.Equal("looks", (string?)put.Body!["family"]);
        Assert.Equal(A, (string?)put.Body["unified_id"]);
        Assert.Equal("fx.bubble.heart", (string?)put.Body["value"]!["bubble"]);
        Assert.Equal("fx.bubble.heart", s.Worn("bubble"));

        // a counter state with another account's equip is ignored; with this account's and nothing owed it is adopted
        var body2 = new JObject { ["ok"] = true, ["equip"] = Looks("fx.bubble.soap", "fx.card.lace") };
        s.FromCounter(A, body2, null);
        Assert.Equal("fx.card.lace", s.Worn("card"));
        Assert.Equal("fx.bubble.soap", s.Worn("bubble"));
    }

    [Fact]
    public async Task A_counter_reply_with_a_pick_owed_here_keeps_the_pick_and_shows_it_to_the_page()
    {
        var rig = new Rig { Offline = true };
        rig.Own("fx.bubble.heart", "fx.word.chalk");
        var s = rig.Make();
        s.Equip("bubble", "fx.bubble.heart");
        rig.Offline = false;
        rig.Server.Answer = (req, body) => Json(200, EquipReply((JObject)body!["value"]!.DeepClone()));
        // the sheet wore Chalk; the server's answer has the older bubble
        var state = new JObject { ["ok"] = true, ["equip"] = Looks("fx.bubble.soap") };
        state["equip"]!["word"] = "fx.word.chalk";
        s.FromCounter(A, state, "word");
        Assert.Equal("fx.word.chalk", s.Worn("word"));                 // the slot the sheet wrote comes from the answer
        Assert.Equal("fx.bubble.heart", s.Worn("bubble"));             // the owed pick stays
        Assert.Equal("fx.bubble.heart", (string?)state["equip"]!["bubble"]);   // and the page is shown it
        await s.PushAsync();
        Assert.Contains(rig.Server.Calls, c => c.Method == HttpMethod.Put && (string?)c.Body!["value"]!["bubble"] == "fx.bubble.heart");
    }

    [Fact]
    public async Task An_unowned_id_comes_back_as_the_old_value_and_the_answer_wins()
    {
        var rig = new Rig();
        rig.Own("fx.bubble.disco");                                     // the grants here think so; the server does not
        rig.Server.Answer = (req, _) => Json(200, EquipReply(Looks("fx.bubble.soap")));
        var s = rig.Make();
        Assert.True(s.Equip("bubble", "fx.bubble.disco"));
        await s.PushAsync();
        Assert.Equal("fx.bubble.soap", s.Worn("bubble"));
        Assert.Contains("bubble", rig.Raised);
        // settled: a later pull is a GET, not a push
        await s.PullOnceAsync();
        Assert.Equal(HttpMethod.Get, rig.Server.Calls[^1].Method);
    }

    [Fact]
    public async Task Flags_off_falls_back_to_free_only_quietly_and_backs_off()
    {
        var rig = new Rig();
        rig.Own("fx.bubble.heart");
        rig.Server.Answer = (req, _) => req.Method == HttpMethod.Get
            ? Json(200, EquipReply(null))                                    // SHOP_LOOKS off: no looks block
            : Json(200, new JObject { ["ok"] = false, ["reason"] = "unavailable" });
        var s = rig.Make();
        await s.PullOnceAsync();
        Assert.Null(s.Worn("bubble"));                                      // nothing adopted, nothing started
        int before = rig.Server.Calls.Count;
        Assert.True(s.Equip("bubble", "fx.bubble.heart"));                 // still wearable here (owned)
        await s.PushAsync();
        Assert.Equal(before, rig.Server.Calls.Count);                       // backing off: nothing sent
        rig.Now += AccountLookStore.OffBackoff + TimeSpan.FromMinutes(1);
        await s.PushAsync();
        Assert.Equal(HttpMethod.Put, rig.Server.Calls[^1].Method);          // asked again after the back-off
        int after = rig.Server.Calls.Count;
        await s.PushAsync(); await s.PushAsync();
        Assert.Equal(after, rig.Server.Calls.Count);                        // unavailable: back off again, no storm

        // a 404 (an old server) is the same answer
        var rig404 = new Rig();
        var s404 = rig404.Make();
        await s404.PullOnceAsync();
        s404.Equip("bubble", null);
        await s404.PushAsync();
        Assert.Single(rig404.Server.Calls);

        // nothing sold, nothing granted: the wardrobe shows only the free rows
        var none = new Rig();
        none.Server.Answer = (req, _) => Json(200, EquipReply(null));
        var sn = none.Make();
        await sn.PullOnceAsync();
        WithStore(sn, () =>
        {
            Assert.All(LookWardrobe.Choices("bubble").Skip(1), c => Assert.False(c.Owned));
            Assert.False(LookWardrobe.Wear("bubble", "fx.bubble.heart"));
            Assert.Equal("fx.bubble.soap", LookWardrobe.Worn("bubble"));
            return 0;
        });
    }

    [Fact]
    public void Fog2_is_a_palette_value_worn_only_with_two_tone_on()
    {
        static bool None(string _) => false;
        static bool Two(string id) => id == FogLooks.Twotone;
        var d = LookSet.Default;
        Assert.Equal("not_owned", LookEquipRules.Apply(d, "fog2", FogLooks.Sky, None).Refusal);
        Assert.Equal("not_worn", LookEquipRules.Apply(d, "fog2", FogLooks.Sky, Two).Refusal);
        var on = LookEquipRules.Apply(d, "mix", FogLooks.Twotone, Two).Next!;
        Assert.True(on.Mix);
        Assert.Equal(FogLooks.DefaultSecond, on.Get("fog2"));
        foreach (var (id, _) in FogLooks.Palette) Assert.Equal(id, LookEquipRules.Apply(on, "fog2", id, Two).Next!.Get("fog2"));
        Assert.Equal("bad_input", LookEquipRules.Apply(on, "fog2", FogLooks.Twotone, Two).Refusal);
        Assert.Equal("bad_input", LookEquipRules.Apply(on, "fog2", "fx.fog.teal", Two).Refusal);
        var off = LookEquipRules.Apply(on with { Fog2 = FogLooks.Peach }, "mix", null, Two).Next!;
        Assert.False(off.Mix);
        Assert.Null(off.Fog2);                                              // cleared when two-tone comes off
        Assert.Null(off.Get("fog2"));
        Assert.False(LookEquipRules.Apply(on, "fog", null, Two).Next!.Mix);  // slot fog null is the same switch
        Assert.Equal("bad_input", LookEquipRules.Apply(d, "fog", "fx.fog.lilac", Two).Refusal);   // no fog colour rows
        Assert.Equal("not_owned", LookEquipRules.Apply(d, "mix", FogLooks.Twotone, None).Refusal);
        // two-tone no longer owned: the clamp takes the mix and fog2 off
        Assert.False(LookEquipRules.Clamp(on, None).Mix);
        // the wire: fog2 null while off, a palette colour while on, no fog key
        var j = LookEquipRules.ToJson(on);
        Assert.Null(j["fog"]);
        Assert.Equal(FogLooks.DefaultSecond, (string?)j["fog2"]);
        Assert.Equal(JTokenType.Null, LookEquipRules.ToJson(off)["fog2"]!.Type);
        Assert.Equal(FogLooks.DefaultSecond, LookEquipRules.Read(new JObject { ["mix"] = true, ["fog2"] = "fx.fog.teal" })!.Fog2);
        Assert.Null(LookEquipRules.Read(new JObject { ["mix"] = false, ["fog2"] = FogLooks.Sky })!.Fog2);
        // the store says the same
        var rig = new Rig { Offline = true };
        rig.Own(FogLooks.Twotone);
        var s = rig.Make();
        Assert.False(s.Equip("fog2", FogLooks.Sky));
        Assert.True(s.Equip("mix", FogLooks.Twotone));
        Assert.True(s.Equip("fog2", FogLooks.Sky));
        Assert.Equal(FogLooks.Sky, s.Worn("fog2"));
        Assert.True(s.Equip("mix", null));
        Assert.Null(s.Worn("fog2"));
    }

    [Fact]
    public async Task Signed_out_is_free_only_and_sends_nothing()
    {
        var rig = new Rig { Account = null };
        rig.Own("fx.bubble.heart");
        var s = rig.Make();
        Assert.False(s.Owns("fx.bubble.heart"));
        Assert.Null(s.Worn("bubble"));
        Assert.False(s.Equip("bubble", "fx.bubble.heart"));
        Assert.True(s.Equip("bubble", null));
        await s.PullOnceAsync();
        await s.PushAsync();
        Assert.Empty(rig.Server.Calls);
        Assert.False(File.Exists(rig.Path));
        // another account's cache wears nothing for the one signed in now
        var a = new Rig { Offline = true };
        a.Own("fx.bubble.heart");
        var sa = a.Make();
        sa.Equip("bubble", "fx.bubble.heart");
        var b = new Rig(a.Path) { Account = "acct-b", Offline = true, GrantsKnown = false };
        var sb = b.Make();
        Assert.Null(sb.Worn("bubble"));
        Assert.False(sb.Owns("fx.bubble.heart"));
    }

    [Fact]
    public async Task The_relay_routes_the_sheets_wear_and_settles_the_store_on_its_answer()
    {
        var rig = new Rig();
        rig.Own("fx.bubble.heart");
        var store = rig.Make();
        var server = new FakeServer
        {
            Answer = (req, body) => Json(200, new JObject { ["ok"] = true, ["sp"] = 5, ["equip"] = Looks((string?)body!["itemId"] ?? "fx.bubble.soap") }),
        };
        var relay = new PrizeCounterRelay(new HttpClient(server), () => (A, "tok"), () => false, null, null) { Looks = store };
        var reply = await relay.RelayAsync(PrizeCounterRelay.OpEquip, new JObject { ["slot"] = "bubble", ["itemId"] = "fx.bubble.heart" }, null);
        Assert.True((bool)reply["ok"]!);
        var call = server.Calls.Single();
        Assert.Equal("/v2/shop/equip", call.Path);
        Assert.Equal("bubble", (string?)call.Body!["slot"]);
        Assert.Equal("fx.bubble.heart", (string?)call.Body["itemId"]);
        Assert.Equal(A, (string?)call.Body["unified_id"]);
        Assert.Equal("fx.bubble.heart", store.Worn("bubble"));              // the Studio sees the sheet's wear at once
        Assert.Contains("bubble", rig.Raised);
        // a bad shape never reaches the server
        Assert.Equal("bad_input", (string?)(await relay.RelayAsync(PrizeCounterRelay.OpEquip, new JObject { ["itemId"] = "x" }, null))["reason"]);
        Assert.Single(server.Calls);
        // the equip route refusing unavailable tells the store the server has no looks yet
        server.Answer = (_, _) => Json(200, new JObject { ["ok"] = false, ["reason"] = "unavailable" });
        await relay.RelayAsync(PrizeCounterRelay.OpEquip, new JObject { ["slot"] = "bubble", ["itemId"] = null }, null);
        int n = rig.Server.Calls.Count;
        store.Equip("bubble", null);
        await store.PushAsync();
        Assert.Equal(n, rig.Server.Calls.Count);
    }

    [Fact]
    public void The_cache_is_machine_local_never_a_setting_a_preset_or_backup()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "ConditioningControlPanel", "Resources"))) root = root.Parent;
        Assert.NotNull(root);
        var app = Path.Combine(root!.FullName, "ConditioningControlPanel");
        var settings = File.ReadAllText(Path.Combine(app, "Models", "AppSettings.cs"));
        Assert.DoesNotContain("looks-equip", settings);
        Assert.DoesNotContain("AccountLook", settings);
        Assert.DoesNotContain(ConditioningControlPanel.Services.SessionSettingsJournal.Fields, f => f.Contains("Look", StringComparison.OrdinalIgnoreCase));
        foreach (var f in Directory.GetFiles(Path.Combine(app, "Services"), "*.cs", SearchOption.AllDirectories))
        {
            if (f.EndsWith("AccountLookStore.cs", StringComparison.Ordinal)) continue;
            Assert.DoesNotContain(AccountLookStore.FileName, File.ReadAllText(f));
        }
        // the DEBUG preview never reaches it: the relay and the wiring stand down while the preview answers
        var store = File.ReadAllText(Path.Combine(app, "Services", "Prizes", "AccountLookStore.cs"));
        Assert.Equal(2, store.Split("ShopPreviewBackend.Enabled) return").Length - 1);
    }
}
