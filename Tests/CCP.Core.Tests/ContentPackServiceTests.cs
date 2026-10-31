using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Packs;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The network half of WPF ContentPackService on Core, against a fake HTTP handler (never the
/// live server): manifest + built-in fallback, the Patreon signed-URL install with Range resume and
/// the encrypt step, rate limits, the V2 external link and the local zip install.
/// </summary>
public sealed class ContentPackServiceTests : IDisposable
{
    private const string Base = "http://127.0.0.1:9";
    private const string Cdn = "https://cdn.test/pack.zip";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-packsvc-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();
    private readonly FakeHandler _h = new();

    public ContentPackServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private (ContentPackService Svc, ContentPackStore Store) New(string? baseUrl = Base, string? token = "pat",
        (string, string)? identity = null, string? discordToken = null)
    {
        var store = new ContentPackStore(() => _root, () => _settings, () => { }, tempDir: () => Path.Combine(_root, ".temp"));
        var svc = new ContentPackService(store, new HttpClient(_h), () => baseUrl, () => _settings, () => { },
            () => token, () => identity, _ => Task.CompletedTask, () => Path.Combine(_root, "previews"), () => discordToken);
        return (svc, store);
    }

    private static byte[] Zip(params (string Path, string Body)[] entries)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
            foreach (var (p, b) in entries)
            {
                using var s = z.CreateEntry(p, CompressionLevel.NoCompression).Open();
                var bytes = Encoding.UTF8.GetBytes(b);
                s.Write(bytes, 0, bytes.Length);
            }
        return ms.ToArray();
    }

    private void ServeSignedUrl() => _h.On("POST", Base + "/pack/download-url",
        _ => Json(HttpStatusCode.OK, new { success = true, downloadUrl = Cdn, rateLimit = new { remaining = 4 } }));

    private static HttpResponseMessage Json(HttpStatusCode code, object body) =>
        new(code) { Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Manifest_FromServer_StampsInstalledState()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "Pack One", ("a.png", "image"));
        _h.On("GET", Base + "/packs/manifest", _ => Json(HttpStatusCode.OK,
            new { version = "2", packs = new[] { new { id = "p1", name = "Pack One" }, new { id = "p2", name = "Two" } } }));
        var (svc, _) = New();

        var packs = await svc.GetAvailablePacksAsync();

        Assert.Equal(new[] { "p1", "p2" }, packs.Select(p => p.Id));
        Assert.True(packs[0].IsDownloaded);
        Assert.True(packs[0].IsActive);   // orphans register active, as on WPF
        Assert.False(packs[1].IsDownloaded);
    }

    [Fact]
    public async Task Manifest_Unreachable_FallsBackToBuiltIn()
    {
        _h.On("GET", Base + "/packs/manifest", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (svc, _) = New();
        var packs = await svc.GetAvailablePacksAsync();
        Assert.Equal(new[] { "basic-bimbo-starter", "enhanced-bimbodoll-video" }, packs.Select(p => p.Id));
    }

    [Fact]
    public async Task NoBaseUrl_OrOffline_SendsNothing()
    {
        var (svc, _) = New(baseUrl: null);
        Assert.Equal(2, (await svc.GetAvailablePacksAsync()).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.InstallPackAsync(new ContentPack { Id = "x" }));

        _settings.OfflineMode = true;
        var (svc2, _) = New();
        Assert.Equal(2, (await svc2.GetAvailablePacksAsync()).Count);
        Assert.Null(await svc2.GetFullPackStatusAsync());
        Assert.Empty(_h.Requests);
    }

    [Fact]
    public async Task Install_SignedUrl_DownloadsEncryptsAndRegisters()
    {
        ServeSignedUrl();
        var zip = Zip(("Pack/images/a.png", "AAA"), ("Pack/images/b.txt", new string('s', 2000)), ("Pack/videos/v.mp4", "VVV"));
        _h.On("GET", Cdn, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
        var (svc, store) = New();
        var pack = new ContentPack { Id = "p9", Name = "Nine", SizeBytes = zip.Length };
        var completed = 0;
        svc.PackDownloadCompleted += (_, _) => completed++;

        await svc.InstallPackAsync(pack);

        var post = _h.Requests.First(r => r.Method == "POST");
        Assert.Equal("Bearer pat", post.Authorization);
        Assert.Contains("\"packId\":\"p9\"", post.Body);
        Assert.True(store.IsPackInstalled("p9"));
        Assert.Contains("p9", _settings.InstalledPackIds);
        Assert.Equal(new[] { "a.png" }, store.GetPackFiles("p9", "image").Select(f => f.OriginalName));
        Assert.Single(store.GetPackFiles("p9", "video"));
        using (var s = store.GetPackFileStream("p9", store.GetPackFiles("p9", "image")[0])!)
            Assert.Equal("AAA", Encoding.UTF8.GetString(s.ToArray()));
        Assert.True(pack.IsDownloaded);
        Assert.False(pack.IsDownloading);
        Assert.Equal(1, completed);
        Assert.Empty(Directory.GetFiles(store.PacksFolder, ".*_temp.zip"));
        Assert.Empty(Directory.GetDirectories(store.PacksFolder, ".*_extract"));
    }

    [Fact]
    public async Task Install_WindowsZipWithBackslashEntries_FindsImagesOnEveryOs()
    {
        ServeSignedUrl();
        var zip = Zip(("Pack\\images\\a.png", new string('A', 1500)));
        _h.On("GET", Cdn, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
        var (svc, store) = New();
        await svc.InstallPackAsync(new ContentPack { Id = "w1", Name = "Win" });
        Assert.Equal(new[] { "a.png" }, store.GetPackFiles("w1", "image").Select(f => f.OriginalName));
    }

    [Fact]
    public async Task Install_BadSignature_RejectedAndUserPacksKept()
    {
        // The CDN refuses a bad or expired signed URL (403). WPF ContentPackService.cs:449 retries it like any
        // HttpRequestException, then fails: nothing new is registered and the user's installed pack is untouched.
        var (keptGuid, _) = ContentPackStoreTests.WritePack(_root, "mine", "Mine", ("a.png", "image"));
        ServeSignedUrl();
        _h.On("GET", Cdn, _ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var (svc, store) = New();
        var failed = 0;
        svc.PackInstallFailed += (_, _) => failed++;
        var foldersBefore = Directory.GetDirectories(store.PacksFolder).Length;

        await Assert.ThrowsAsync<HttpRequestException>(() => svc.InstallPackAsync(new ContentPack { Id = "bad", Name = "Bad" }));

        Assert.Equal(1, failed);
        Assert.Equal(10, _h.Requests.Count(r => r.Url == Cdn));
        Assert.False(store.IsPackInstalled("bad"));
        Assert.DoesNotContain("bad", _settings.InstalledPackIds ?? new List<string>());
        Assert.True(store.IsPackInstalled("mine"));
        Assert.True(Directory.Exists(Path.Combine(_root, ".packs", keptGuid)));
        Assert.Equal(foldersBefore, Directory.GetDirectories(store.PacksFolder).Length);
        Assert.Empty(Directory.GetFiles(store.PacksFolder, ".*_temp.zip"));
    }

    [Fact]
    public async Task Install_EmptyBody_IsAFailedDownload()
    {
        ServeSignedUrl();
        _h.On("GET", Cdn, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[10]) });
        var (svc, store) = New();
        await Assert.ThrowsAsync<IOException>(() => svc.InstallPackAsync(new ContentPack { Id = "e1", Name = "Empty" }));
        Assert.False(store.IsPackInstalled("e1"));
    }

    [Fact]
    public async Task Install_DroppedConnection_ResumesWithRange()
    {
        ServeSignedUrl();
        var zip = Zip(("images/a.png", new string('x', 4000)));
        var cut = zip.Length / 2;
        var calls = 0;
        _h.On("GET", Cdn, req =>
        {
            calls++;
            if (calls == 1)
            {
                var c = new StreamContent(new BreakingStream(zip, cut));
                c.Headers.ContentLength = zip.Length;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = c };
            }
            Assert.Equal(cut, req.Headers.Range!.Ranges.Single().From);
            var rest = new ByteArrayContent(zip.Skip(cut).ToArray());
            rest.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(cut, zip.Length - 1, zip.Length);
            return new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = rest };
        });
        var (svc, store) = New();
        var statuses = new List<string>();
        svc.PackInstallStatus += (_, e) => statuses.Add(e.Status);

        await svc.InstallPackAsync(new ContentPack { Id = "r1", Name = "Resume" });

        Assert.Equal(2, calls);
        Assert.Contains(statuses, s => s.StartsWith("Connection lost at", StringComparison.Ordinal));
        Assert.True(store.IsPackInstalled("r1"));
    }

    [Fact]
    public async Task Install_RateLimited_RaisesEventWithResetTime()
    {
        _h.On("POST", Base + "/pack/download-url", _ => Json(HttpStatusCode.TooManyRequests,
            new { error = "rate", message = "Daily limit reached", resetTime = "2026-10-10T12:00:00Z" }));
        var (svc, store) = New();
        (string Msg, DateTime Reset)? seen = null;
        svc.RateLimitExceeded += (_, e) => seen = (e.Message, e.ResetTime);

        await Assert.ThrowsAsync<PackRateLimitException>(() => svc.InstallPackAsync(new ContentPack { Id = "p1" }));

        Assert.Equal("Daily limit reached", seen!.Value.Msg);
        Assert.Equal(new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc), seen.Value.Reset.ToUniversalTime());
        Assert.False(store.IsPackInstalled("p1"));
    }

    [Fact]
    public async Task Install_NoToken_Or401_AsksForSignIn()
    {
        var (svc, _) = New(token: null);
        var asked = 0;
        svc.AuthenticationRequired += (_, _) => asked++;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.InstallPackAsync(new ContentPack { Id = "p1" }));
        Assert.Empty(_h.Requests);   // no token: nothing is sent

        _h.On("POST", Base + "/pack/download-url", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (svc2, _) = New();
        svc2.AuthenticationRequired += (_, _) => asked++;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc2.InstallPackAsync(new ContentPack { Id = "p1" }));
        Assert.Equal(2, asked);
    }

    [Fact]
    public async Task ExternalLink_UsesV2Door()
    {
        _h.On("POST", Base + "/pack/download-url", _ => Json(HttpStatusCode.OK, new { downloadUrl = "https://mega.test/x" }));
        var (svc, _) = New(identity: ("u-1", "tok"));
        Assert.Equal("https://mega.test/x", await svc.GetExternalPackDownloadUrlAsync("ext"));
        var req = _h.Requests.Single();
        Assert.Equal("tok", req.AuthToken);
        Assert.Null(req.Authorization);
        Assert.Contains("\"unified_id\":\"u-1\"", req.Body);

        var (signedOut, _) = New(identity: null);
        string? msg = null;
        signedOut.AuthenticationRequired += (_, m) => msg = m;
        Assert.Null(await signedOut.GetExternalPackDownloadUrlAsync("ext"));
        Assert.NotNull(msg);
    }

    [Fact]
    public async Task PackStatus_ReadsServerShape()
    {
        _h.On("GET", Base + "/pack/status", _ => Json(HttpStatusCode.OK,
            new { userId = "u", dailyLimit = 5, packs = new Dictionary<string, object> { ["p1"] = new { canDownload = true, downloadsRemaining = 3 } } }));
        var (svc, _) = New();
        var status = await svc.GetFullPackStatusAsync();
        Assert.Equal(5, status!.DailyLimit);
        Assert.Equal(3, status.Packs!["p1"].DownloadsRemaining);
    }

    // WPF ContentPackService.cs:831-884. Each provider has its own bearer and endpoint.
    [Theory]
    [InlineData("absent")]
    [InlineData("refused")]
    [InlineData("throws")]
    [InlineData("malformed")]
    public async Task PackStatus_FallsBackToDiscord(string failure)
    {
        _h.On("GET", Base + "/pack/status", _ => failure switch
        {
            "throws" => throw new HttpRequestException("connection lost"),
            "malformed" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{") },
            _ => new HttpResponseMessage(HttpStatusCode.Unauthorized),
        });
        _h.On("GET", Base + "/discord/pack/status", _ => Json(HttpStatusCode.OK,
            new { dailyLimit = 7, packs = new Dictionary<string, object> { ["d"] = new { downloadsRemaining = 2 } } }));
        var (svc, _) = New(token: failure == "absent" ? null : "pat", discordToken: "discord");

        var status = await svc.GetFullPackStatusAsync();

        Assert.Equal(7, status!.DailyLimit);
        Assert.Equal(2, status.Packs!["d"].DownloadsRemaining);
        Assert.Equal(failure == "absent" ? 1 : 2, _h.Requests.Count);
        Assert.Equal("Bearer discord", _h.Requests.Last().Authorization);
        Assert.EndsWith("/discord/pack/status", _h.Requests.Last().Url);
        if (failure != "absent") Assert.Equal("Bearer pat", _h.Requests.First().Authorization);
    }

    [Theory]
    [InlineData("{\"dailyLimit\":5}", 5)]
    [InlineData("null", null)]
    public async Task PackStatus_PatreonSuccessDoesNotAskDiscord(string body, int? limit)
    {
        _h.On("GET", Base + "/pack/status", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        var (svc, _) = New(discordToken: "discord");
        Assert.Equal(limit, (await svc.GetFullPackStatusAsync())?.DailyLimit);
        Assert.Single(_h.Requests);
    }

    [Fact]
    public async Task PackStatus_SignedOutOfflineAndFailedDiscordReturnNull()
    {
        var (signedOut, _) = New(token: null);
        Assert.Null(await signedOut.GetFullPackStatusAsync());
        var (sandbox, _) = New(baseUrl: null, discordToken: "discord");
        Assert.Null(await sandbox.GetFullPackStatusAsync());
        _settings.OfflineMode = true;
        var (svc, _) = New(discordToken: "discord");
        Assert.Null(await svc.GetFullPackStatusAsync());
        Assert.Empty(_h.Requests);
        _settings.OfflineMode = false;
        Assert.Null(await svc.GetPackDownloadStatusAsync()); // both endpoints refuse
        Assert.Equal(2, _h.Requests.Count);
    }

    [Fact]
    public async Task LocalZip_Installs_AndKeepsUsersZip_RefusesNonPack()
    {
        var zipPath = Path.Combine(_root, "mine.zip");
        File.WriteAllBytes(zipPath, Zip(("images/a.jpg", "J")));
        var (svc, store) = New(baseUrl: null);
        await svc.InstallPackFromLocalZipAsync(new ContentPack { Id = "z1", Name = "Zip" }, zipPath);
        Assert.True(store.IsPackInstalled("z1"));
        Assert.True(File.Exists(zipPath));
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.InstallPackFromLocalZipAsync(new ContentPack { Id = "z1" }, zipPath));

        var junk = Path.Combine(_root, "junk.zip");
        File.WriteAllBytes(junk, Zip(("readme.txt", "no")));
        var before = Directory.GetDirectories(store.PacksFolder).Length;
        await Assert.ThrowsAsync<InvalidDataException>(() => svc.InstallPackFromLocalZipAsync(new ContentPack { Id = "z2" }, junk));
        Assert.Equal(before, Directory.GetDirectories(store.PacksFolder).Length);
        Assert.Empty(_h.Requests);
    }

    [Fact]
    public void Previews_PickIsCachedPerPack_ClearKeepsThePack()
    {
        var files = Enumerable.Range(0, 14).Select(i => ($"i{i}.png", "image")).ToArray();
        var (guid, _) = ContentPackStoreTests.WritePack(_root, "pv", "Previews", files);
        var (svc, store) = New();

        var first = svc.GetPackPreviewBytes("pv", 10).Select(Encoding.UTF8.GetString).ToList();
        var second = svc.GetPackPreviewBytes("pv", 10).Select(Encoding.UTF8.GetString).ToList();

        Assert.Equal(10, first.Count);
        Assert.Equal(first.OrderBy(x => x), second.OrderBy(x => x));
        var cache = Path.Combine(_root, ".packs", guid, ContentPackService.PreviewCacheFileName);
        Assert.True(File.Exists(cache));
        svc.ClearPreviewCache("pv");
        Assert.False(File.Exists(cache));
        Assert.Equal(14, store.GetPackFiles("pv", "image").Count);   // only the pick goes, never the pack
        Assert.Empty(svc.GetPackPreviewBytes("missing"));
    }

    [Fact]
    public async Task UrlPreviews_CachedOnDisk_SecondReadSendsNothing()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
        _h.On("GET", "https://img.test/abc123", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
        var (svc, _) = New();
        var urls = new[] { "https://img.test/abc123", "http://plain.test/no.png" };

        Assert.Single(await svc.GetPreviewBytesFromUrlsAsync("p1", urls));
        Assert.Single(await svc.GetPreviewBytesFromUrlsAsync("p1", urls));
        Assert.Single(_h.Requests);
        Assert.True(File.Exists(Path.Combine(_root, "previews", "p1", "abc123.png")));
    }

    [Fact]
    public async Task UrlPreviews_SandboxOrOffline_ServesCacheSendsNothing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "previews", "p1"));
        File.WriteAllBytes(Path.Combine(_root, "previews", "p1", "cached.png"), new byte[] { 7 });
        var (svc, _) = New(baseUrl: null);

        var got = await svc.GetPreviewBytesFromUrlsAsync("p1", new[] { "https://img.test/cached.png", "https://img.test/fresh" });

        Assert.Single(got);
        Assert.Empty(_h.Requests);
    }

    // ---------------------------------------------------------------------------------------------

    private sealed record Seen(string Method, string Url, string? Authorization, string? AuthToken, string Body);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _routes = new();
        public List<Seen> Requests { get; } = new();

        public void On(string method, string url, Func<HttpRequestMessage, HttpResponseMessage> reply) => _routes[method + " " + url] = reply;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(new Seen(request.Method.Method, request.RequestUri!.ToString(),
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("X-Auth-Token", out var t) ? t.Single() : null, body));
            return _routes.TryGetValue(request.Method.Method + " " + request.RequestUri, out var r)
                ? r(request)
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    /// <summary>Hands out the first <c>cut</c> bytes, then drops the connection.</summary>
    private sealed class BreakingStream(byte[] data, int cut) : MemoryStream(data, 0, cut)
    {
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, count) is > 0 and var n ? n : throw new IOException("connection reset");
        public override int Read(Span<byte> buffer) { var a = new byte[buffer.Length]; var n = Read(a, 0, a.Length); a.AsSpan(0, n).CopyTo(buffer); return n; }
    }
}
