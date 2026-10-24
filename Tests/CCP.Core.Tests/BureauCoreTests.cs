using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Bureau;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Bureau's shared rules (moved from WPF Services/Bureau): the evidence index over a head's pack
/// source, and the host's pure bridge rules. Runs in this assembly's CCP_USERDATA_DIR sandbox.
/// </summary>
public sealed class BureauCoreTests : IDisposable
{
    private readonly Func<IReadOnlyCollection<string>?> _packs = BureauIndexService.InstalledPacksProvider;
    private readonly Func<string, IEnumerable<PackFileEntry>> _files = BureauIndexService.ImageFilesProvider;
    private readonly Func<string, PackFileEntry, Stream?> _open = BureauIndexService.OpenFileProvider;

    public void Dispose()
    {
        BureauIndexService.InstalledPacksProvider = _packs;
        BureauIndexService.ImageFilesProvider = _files;
        BureauIndexService.OpenFileProvider = _open;
    }

    private static string Sha(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    private static PackFileEntry Entry(string name, string ext) =>
        new() { OriginalName = name, ObfuscatedName = Guid.NewGuid().ToString("N"), FileType = "image", Extension = ext };

    /// <summary>Two packs; the payload of each file is "packId/name". Counts every open.</summary>
    private static Dictionary<string, List<PackFileEntry>> Seed(out Func<int> opens, params (string Pack, string Name, string Ext)[] files)
    {
        var map = files.GroupBy(f => f.Pack).ToDictionary(g => g.Key, g => g.Select(f => Entry(f.Name, f.Ext)).ToList());
        var count = 0;
        opens = () => count;
        BureauIndexService.InstalledPacksProvider = () => map.Keys.ToList();
        BureauIndexService.ImageFilesProvider = id => map[id];
        BureauIndexService.OpenFileProvider = (id, e) =>
        {
            count++;
            return new MemoryStream(Encoding.UTF8.GetBytes(id + "/" + e.OriginalName));
        };
        return map;
    }

    [Fact]
    public async Task IndexMapsPlaintextHashesBackToTheirPackAndSkipsNonImages()
    {
        var a = "bureau-a-" + Guid.NewGuid().ToString("N");
        var b = "bureau-b-" + Guid.NewGuid().ToString("N");
        Seed(out _, (a, "one", ".png"), (a, "two", "GIF"), (a, "clip", "mp4"), (b, "three", "webp"));

        await BureauIndexService.RebuildAsync();

        Assert.True(BureauIndexService.IsReady);
        Assert.Equal(3, BureauIndexService.Total);
        Assert.True(BureauIndexService.TryResolve(Sha(a + "/two"), out var owner, out var entry));
        Assert.Equal(a, owner);
        Assert.Equal("two", entry!.OriginalName);
        Assert.True(BureauIndexService.TryResolve(Sha(b + "/three"), out owner, out _));
        Assert.Equal(b, owner);
        Assert.False(BureauIndexService.TryResolve(Sha(a + "/clip"), out _, out _));
        Assert.Equal(2, BureauIndexService.PackCounts[a]);
        Assert.Equal(1, BureauIndexService.PackCounts[b]);
    }

    [Fact]
    public async Task ARebuildHashesFromTheCacheWithoutReopeningPackFiles()
    {
        var a = "bureau-c-" + Guid.NewGuid().ToString("N");
        Seed(out var opens, (a, "one", "jpg"), (a, "two", "jpeg"));

        await BureauIndexService.RebuildAsync();
        Assert.Equal(2, opens());
        Assert.True(File.Exists(Path.Combine(CorePaths.UserData, "bureau_hash_index.json")));

        await BureauIndexService.RebuildAsync();
        Assert.Equal(2, opens());
        Assert.True(BureauIndexService.TryResolve(Sha(a + "/one"), out _, out _));
    }

    [Fact]
    public async Task NoPackServiceReportsReady()
    {
        BureauIndexService.InstalledPacksProvider = () => null;
        await BureauIndexService.RebuildAsync();
        Assert.True(BureauIndexService.IsReady);
    }

    [Theory]
    [InlineData(":3")]
    [InlineData("")]
    [InlineData("x:1")]
    public void MalformedTargetsAreRejected(string target) =>
        Assert.False(BureauHostRules.TryParseTarget(target, out _, out _));

    [Fact]
    public void ATargetIsA64CharHashAndAFrame()
    {
        var hash = new string('a', 64);
        Assert.True(BureauHostRules.TryParseTarget(hash + ":7", out var h, out var f));
        Assert.Equal(hash, h);
        Assert.Equal(7, f);
        Assert.False(BureauHostRules.TryParseTarget(hash + ":x", out _, out _));
        Assert.False(BureauHostRules.TryParseTarget(hash + "b:1", out _, out _));
    }

    [Fact]
    public async Task BatchCapsAtSixSkipsUndecodableAndScopesToTheLoadedPack()
    {
        var a = "bureau-d-" + Guid.NewGuid().ToString("N");
        var b = "bureau-e-" + Guid.NewGuid().ToString("N");
        Seed(out _, (a, "p1", "png"), (a, "p2", "png"), (b, "q1", "png"));
        await BureauIndexService.RebuildAsync();

        var undecodable = new string('f', 64);
        var all = Enumerable.Range(0, 10).Select(i => Sha(a + "/p1") + ":" + i).Prepend(undecodable + ":0");
        (string, int, int)? Decode(string hash, int frame) => hash == undecodable ? null : ("data:x", 4, 3);

        var batch = BureauHostRules.BuildBatch(new JArray(all), null, null, Decode);
        Assert.Equal(BureauHostRules.BatchDecodeCap, batch.Count);
        Assert.DoesNotContain(batch, i => ((string)i["target"]!).StartsWith(undecodable));
        Assert.Equal(new JArray(4, 3), batch[0]["dims"]);
        Assert.Null(batch[0]["boxes"]);

        var mixed = new JArray(Sha(a + "/p2") + ":0", Sha(b + "/q1") + ":0", undecodable.Replace('f', 'e') + ":0");
        var proposals = new JObject { [Sha(b + "/q1") + ":0"] = new JArray(new JObject { ["x"] = 1 }) };
        var scoped = BureauHostRules.BuildBatch(mixed, b, proposals, Decode);
        Assert.Single(scoped);
        Assert.Equal(Sha(b + "/q1") + ":0", (string)scoped[0]["target"]!);
        Assert.NotNull(scoped[0]["boxes"]);
    }

    [Fact]
    public void LockersHideVideoOnlyAndEmptyInstalledPacks()
    {
        Assert.Null(BureauHostRules.Locker("v", "Video", 0, installed: false, indexed: 0));
        Assert.Null(BureauHostRules.Locker("e", "Empty", 50, installed: true, indexed: 0));
        Assert.Equal(12, (int)BureauHostRules.Locker("i", "In", 50, installed: true, indexed: 12)!["images"]!);
        Assert.Equal(50, (int)BureauHostRules.Locker("o", "Out", 50, installed: false, indexed: 0)!["images"]!);
    }

    [Fact]
    public void DroppedZipMatchesPackNameOrIdOnLettersAndDigits()
    {
        Assert.True(BureauHostRules.ZipMatchesPack("/dl/Bambi Core Collection.zip", "Bambi Core Collection", "core"));
        Assert.True(BureauHostRules.ZipMatchesPack("/dl/bambi-core_v2.ZIP", "Other", "Bambi Core v2"));
        Assert.False(BureauHostRules.ZipMatchesPack("/dl/Bambi Core.zip", "Bambi Core Collection", "core2"));
    }

    [Fact]
    public void SubmitCarriesAmendAndShapesTheReply()
    {
        var page = new JObject { ["target"] = "t", ["dims"] = new JArray(1, 2), ["amend"] = true };
        var req = BureauHostRules.SubmitRequest("uid", page);
        Assert.Equal("uid", (string)req["unified_id"]!);
        Assert.Equal(new JArray(), req["boxes"]);
        Assert.True((bool)req["amend"]!);
        Assert.Null(BureauHostRules.SubmitRequest("uid", new JObject())["amend"]);

        var ok = BureauHostRules.SubmitReply("t", 200, new JObject { ["xp"] = 5 });
        Assert.Equal(("submit-result", true, 5), ((string)ok["type"]!, (bool)ok["ok"]!, (int)ok["xp"]!));
        var bad = BureauHostRules.SubmitReply("t", 429, null);
        Assert.Equal((false, 429, "server error 429"), ((bool)bad["ok"]!, (int)bad["code"]!, (string)bad["error"]!));
        Assert.Equal("slow down", (string)BureauHostRules.SubmitReply("t", 429, new JObject { ["error"] = "slow down" })["error"]!);
    }

    [Fact]
    public void RequestsSignWithTheAuthTokenAndOnlyAdminRoutesCarryTheAdminToken()
    {
        using var plain = BureauHostRules.BuildRequest(HttpMethod.Get, "/v2/bureau/stats", null, "tok", "adm", admin: false);
        Assert.Equal("https://codebambi-proxy.vercel.app/v2/bureau/stats", plain.RequestUri!.ToString());
        Assert.Equal("tok", plain.Headers.GetValues("X-Auth-Token").Single());
        Assert.False(plain.Headers.Contains("x-admin-token"));
        Assert.Null(plain.Content);

        using var admin = BureauHostRules.BuildRequest(HttpMethod.Post, "/admin/bureau/gold", new JObject { ["a"] = 1 }, null, "adm", admin: true);
        Assert.False(admin.Headers.Contains("X-Auth-Token"));
        Assert.Equal("adm", admin.Headers.GetValues("x-admin-token").Single());
        Assert.Equal("{\"a\":1}", admin.Content!.ReadAsStringAsync().Result);

        var next = BureauHostRules.NextRequest("uid", certifying: true);
        Assert.Equal((24, true), ((int)next["count"]!, (bool)next["certify"]!));
        Assert.Null(BureauHostRules.NextRequest("uid", certifying: false)["certify"]);
    }
}
