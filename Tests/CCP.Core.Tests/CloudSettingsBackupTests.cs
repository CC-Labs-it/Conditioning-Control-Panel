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
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core <see cref="CloudSettingsBackup"/> on the wire (WPF ProfileSyncService.cs:4636-4890 endpoints and
/// bodies), plus the cross-OS rule: absolute paths never leave this machine and a restore keeps this machine's.</summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class CloudSettingsBackupTests : IDisposable
{
    private const string Url = "https://codebambi-proxy.vercel.app";
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly Fake _fake = new();

    public CloudSettingsBackupTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
    }

    public void Dispose()
    {
        V2AuthService.MergedRecovery = null;
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
    }

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<(string Url, string? Token, string Body)> Seen = new();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{\"success\":true}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Seen.Add((r.RequestUri!.AbsoluteUri, r.Headers.TryGetValues("X-Auth-Token", out var t) ? t.Single() : null,
                await r.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    private AppSettings Signed()
    {
        var s = new AppSettings { UnifiedId = "u_abc", AuthToken = "TOK" };
        s.SpiralPath = "/home/me/spiral.gif";
        s.MindWipeAudioPath = "/home/me/wipe.mp3";
        s.BlinkTrainerFolders = new List<string> { "/home/me/blink" };
        s.DeeperRecentFiles = new List<string> { "/home/me/deep.json" };
        s.CustomAssetsPath = "/home/me/assets";
        s.DisabledAssetPaths = new HashSet<string> { "images/a.png" };
        s.AssetPresets = new List<AssetPreset> { new() { Name = "p" } };
        return s;
    }

    private static string Pack(string json)
    {
        using var output = new MemoryStream();
        using (var gz = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(output.ToArray());
    }

    private static JObject Unpack(string base64)
    {
        using var input = new MemoryStream(Convert.FromBase64String(base64));
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        return JObject.Parse(reader.ReadToEnd());
    }

    [Fact]
    public async Task BackupUploadsNoAbsolutePathNoExcludedPropertyAndKeepsTheAssetLists()
    {
        var s = Signed();
        s.OpenRouterApiKey = "sk-secret";
        s.FlashEnabled = false;

        Assert.True(await new CloudSettingsBackup(_fake).BackupAsync(s));

        var (url, token, body) = Assert.Single(_fake.Seen);
        Assert.Equal(Url + "/v2/user/backup-settings", url);
        Assert.Equal("TOK", token);
        var req = JObject.Parse(body);
        Assert.Equal("u_abc", (string?)req["unified_id"]);
        Assert.NotNull(req["app_version"]);
        var sent = Unpack((string)req["settings_data"]!);
        var keys = sent.Properties().Select(p => p.Name).ToList();
        foreach (var path in CloudSettingsBackup.AbsolutePathFields) Assert.DoesNotContain(path, keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(keys, k => CloudSettingsBackup.IsExcludedFromBackup(k));
        Assert.DoesNotContain("OpenRouterApiKey", keys);
        Assert.DoesNotContain("/home/me", sent.ToString());
        Assert.False((bool)sent["FlashEnabled"]!);
        Assert.Contains("images/a.png", sent["DisabledAssetPaths"]!.Values<string>());
        Assert.Single(sent["AssetPresets"]!);
    }

    [Fact]
    public async Task AWindowsBackupRestoredHereKeepsThisMachinesPathsAndProgression()
    {
        // What frozen WPF uploads: its own absolute paths, its own level.
        var windows = new AppSettings
        {
            SpiralPath = @"C:\Users\me\spiral.gif", MindWipeAudioPath = @"C:\wipe.mp3", WallpaperSourceFolder = @"D:\walls",
            BlinkTrainerFolders = new List<string> { @"C:\blink" }, DeeperRecentFiles = new List<string> { @"C:\d.json" },
            DeeperLastDirectory = @"C:\deep", PlayerLevel = 99, FlashEnabled = false,
            DisabledAssetPaths = new HashSet<string> { "videos/w.mp4" },
        };
        _fake.Body = new JObject
        {
            ["backup"] = new JObject { ["settings_data"] = Pack(JsonConvert.SerializeObject(windows)), ["app_version"] = "6.11.0" },
        }.ToString();
        var here = Signed();
        here.PlayerLevel = 5;
        here.WallpaperSourceFolder = "/home/me/walls";

        var restored = await new CloudSettingsBackup(_fake).DownloadAsync(here);

        Assert.NotNull(restored);
        var (url, token, body) = Assert.Single(_fake.Seen);
        Assert.Equal(Url + "/v2/user/settings-backup", url);
        Assert.Equal("TOK", token);
        Assert.Equal("u_abc", (string?)JObject.Parse(body)["unified_id"]);
        foreach (var name in CloudSettingsBackup.AbsolutePathFields)
        {
            var p = typeof(AppSettings).GetProperty(name)!;
            Assert.Equal(JsonConvert.SerializeObject(p.GetValue(here)), JsonConvert.SerializeObject(p.GetValue(restored)));
        }
        Assert.Equal(5, restored!.PlayerLevel);
        Assert.Equal("u_abc", restored.UnifiedId);
        Assert.False(restored.FlashEnabled);                       // the backup's own settings do come back
        Assert.Contains("videos/w.mp4", restored.DisabledAssetPaths); // relative asset lists travel
        Assert.NotSame(here.BlinkTrainerFolders, restored.BlinkTrainerFolders);
    }

    [Fact]
    public async Task OverBudgetTrimsTheAssetListsFirstAndSkipsWhenStillTooBig()
    {
        var s = Signed();
        s.DisabledAssetPaths = new HashSet<string>(Enumerable.Range(0, 60_000).Select(_ => Guid.NewGuid() + ".png"));
        Assert.True(await new CloudSettingsBackup(_fake).BackupAsync(s));
        var data = (string)JObject.Parse(_fake.Seen.Single().Body)["settings_data"]!;
        Assert.True(data.Length <= SettingsBackupBudget.MaxEncodedBytes);
        Assert.Null(Unpack(data)["DisabledAssetPaths"]);
        Assert.NotNull(Unpack(data)["AssetPresets"]);

        _fake.Seen.Clear();
        s.BannerSeenIds = Enumerable.Range(0, 60_000).Select(_ => Guid.NewGuid().ToString()).ToList();   // not trimmable
        Assert.False(await new CloudSettingsBackup(_fake).BackupAsync(s));
        Assert.Empty(_fake.Seen);
    }

    [Fact]
    public async Task NoTokenNoAccountOrOfflineSendsNothing()
    {
        var c = new CloudSettingsBackup(_fake);
        var s = Signed(); s.AuthToken = null;
        Assert.False(await c.BackupAsync(s));
        s = Signed(); s.UnifiedId = null;
        Assert.False(await c.BackupAsync(s));
        Assert.Null(await c.DownloadAsync(s));
        s = Signed(); s.OfflineMode = true;
        Assert.False(await c.BackupAsync(s));
        Assert.Empty(_fake.Seen);
    }

    [Fact]
    public async Task A409GoesToContractDAndOtherRefusalsFail()
    {
        var merged = 0;
        _fake.Status = HttpStatusCode.Conflict;
        _fake.Body = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_new\"}";
        var c = new CloudSettingsBackup(_fake);
        Assert.False(await c.BackupAsync(Signed()));   // unseeded: an ordinary failure
        V2AuthService.MergedRecovery = (r, _) => { merged++; return Task.FromResult((int)r.StatusCode == 409); };
        Assert.False(await c.BackupAsync(Signed()));
        Assert.Null(await c.DownloadAsync(Signed()));
        Assert.Equal(2, merged);

        _fake.Status = HttpStatusCode.Unauthorized;
        Assert.False(await c.BackupAsync(Signed()));
        Assert.Null(await c.GetInfoAsync(Signed()));
    }

    [Fact]
    public async Task StatusReadsTheBackupsDateAndVersion()
    {
        _fake.Body = "{\"backup\":{\"backed_up_at\":\"2026-10-01T12:30:00Z\",\"app_version\":\"6.11.0\",\"size_bytes\":10}}";
        var info = await new CloudSettingsBackup(_fake).GetInfoAsync(Signed());
        Assert.Equal("6.11.0", info!.Value.AppVersion);
        Assert.Equal(new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc), info.Value.BackedUpAt!.Value.ToUniversalTime());
        _fake.Body = "{\"backup\":null}";
        Assert.Null(await new CloudSettingsBackup(_fake).GetInfoAsync(Signed()));
    }
}
